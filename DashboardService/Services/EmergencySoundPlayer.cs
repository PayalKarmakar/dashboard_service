using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace DashboardService.Services;

/// <summary>
/// Plays the bundled emergency sound (sound.mpeg) together with sensor voice alerts.
/// Falls back to a generated siren if the file is missing.
/// </summary>
internal static class EmergencySoundPlayer
{
    private static readonly object PlayLock = new();
    private static int _aliasSeq;
    private static string? _activeAlias;
    private static MediaPlayer? _activePlayer;
    private static string? _loopFilePath;
    private static volatile bool _playingUnderVoice;

    public static void StartUnderVoice()
    {
        _playingUnderVoice = true;
        string? loopFile = EnsureLoopFile();
        if (string.IsNullOrWhiteSpace(loopFile))
        {
            return;
        }

        if (TryStartMciLoop(loopFile))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        // Do not block the speech thread on the UI dispatcher — that can
        // swallow the whole announcement after login.
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_playingUnderVoice)
            {
                return;
            }

            try
            {
                StopPlayerOnUi();
                var player = new MediaPlayer { Volume = 0.35 };
                _activePlayer = player;
                player.MediaEnded += (_, _) => ReplayIfNeeded(player);
                player.MediaFailed += (_, _) =>
                {
                    System.Diagnostics.Debug.WriteLine("Emergency MediaPlayer failed; keeping voice.");
                };
                player.Open(new Uri(loopFile, UriKind.Absolute));
                player.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Emergency start failed: {ex.Message}");
            }
        }));
    }

    private static bool TryStartMciLoop(string loopFile)
    {
        string escaped = loopFile.Replace("'", "\\'", StringComparison.Ordinal);
        lock (PlayLock)
        {
            if (!string.IsNullOrEmpty(_activeAlias))
            {
                Mci($"stop {_activeAlias}");
                Mci($"close {_activeAlias}");
                _activeAlias = null;
            }

            string alias = $"srploop{Interlocked.Increment(ref _aliasSeq)}";
            bool opened =
                Mci($"open \"{escaped}\" type mpegvideo alias {alias}") == 0 ||
                Mci($"open \"{escaped}\" alias {alias}") == 0;

            if (!opened)
            {
                return false;
            }

            _activeAlias = alias;
            Mci($"setaudio {alias} volume to 350");
            if (Mci($"play {alias} repeat") != 0 && Mci($"play {alias}") != 0)
            {
                Mci($"close {alias}");
                _activeAlias = null;
                return false;
            }

            return true;
        }
    }

    public static void StopUnderVoice()
    {
        _playingUnderVoice = false;
        CancelActivePlayback();
    }

    private static string? EnsureLoopFile()
    {
        if (!string.IsNullOrWhiteSpace(_loopFilePath) && File.Exists(_loopFilePath))
        {
            return _loopFilePath;
        }

        string? sourcePath = ResolveSoundPath();
        string tempPath = Path.Combine(Path.GetTempPath(), "srp-sensor-alert.mp3");
        try
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                File.Copy(sourcePath, tempPath, overwrite: true);
            }
            else
            {
                tempPath = Path.Combine(Path.GetTempPath(), "srp-sensor-alert.wav");
                File.WriteAllBytes(tempPath, BuildSirenWav());
            }

            _loopFilePath = tempPath;
            return _loopFilePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emergency loop file failed: {ex.Message}");
            return null;
        }
    }

    private static void ReplayIfNeeded(MediaPlayer player)
    {
        if (!_playingUnderVoice)
        {
            return;
        }

        try
        {
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch
        {
        }
    }

    private static void StopPlayerOnUi()
    {
        try { _activePlayer?.Stop(); } catch { }
        try { _activePlayer?.Close(); } catch { }
        _activePlayer = null;
    }

    public static void Play(Func<bool>? shouldCancel = null)
    {
        try
        {
            string? sourcePath = ResolveSoundPath();
            if (!string.IsNullOrWhiteSpace(sourcePath) &&
                PlayCopiedMp3(sourcePath, shouldCancel))
            {
                return;
            }

            PlayGeneratedSiren(shouldCancel);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emergency sound failed: {ex.Message}");
        }
    }

    private static bool PlayCopiedMp3(string sourcePath, Func<bool>? shouldCancel)
    {
        // Copy to a short temp path. MCI fails on folders like "New folder (6)".
        string tempPath = Path.Combine(Path.GetTempPath(), $"srp-alert-{Guid.NewGuid():N}.mp3");
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: true);
            if (PlayWithMediaPlayer(tempPath, shouldCancel) ||
                PlayFileBlocking(tempPath, "mpegvideo", shouldCancel))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emergency mp3 playback failed: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static void PlayGeneratedSiren(Func<bool>? shouldCancel)
    {
        string wavPath = Path.Combine(Path.GetTempPath(), $"srp-siren-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(wavPath, BuildSirenWav());
            PlayFileBlocking(wavPath, "waveaudio", shouldCancel);
        }
        finally
        {
            try { File.Delete(wavPath); } catch { }
        }
    }

    public static void CancelActivePlayback()
    {
        _playingUnderVoice = false;
        lock (PlayLock)
        {
            if (!string.IsNullOrEmpty(_activeAlias))
            {
                Mci($"stop {_activeAlias}");
                Mci($"close {_activeAlias}");
                _activeAlias = null;
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _activePlayer = null;
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            try { _activePlayer?.Stop(); } catch { }
            try { _activePlayer?.Close(); } catch { }
            _activePlayer = null;
        }));
    }

    private static string? ResolveSoundPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "Assets", "sound.mpeg"),
            Path.Combine(baseDir, "sound.mpeg"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "sound.mpeg"),
            Path.Combine(AppContext.BaseDirectory, "sound.mpeg"),
        ];

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static byte[] BuildSirenWav()
    {
        const int sampleRate = 22050;
        const double durationSeconds = 1.8;
        const double beatSeconds = 0.3;
        const short amplitude = 22000;

        int sampleCount = (int)(sampleRate * durationSeconds);
        var samples = new short[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            double t = i / (double)sampleRate;
            bool highTone = ((int)(t / beatSeconds) % 2) == 0;
            double freq = highTone ? 980 : 620;
            double envelope = FadeEnvelope(i, sampleCount);
            samples[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * amplitude * envelope);
        }

        return WrapWav(samples, sampleRate);
    }

    private static double FadeEnvelope(int i, int sampleCount)
    {
        const int fade = 600;
        if (i < fade)
        {
            return i / (double)fade;
        }

        int remaining = sampleCount - i;
        if (remaining < fade)
        {
            return remaining / (double)fade;
        }

        return 1.0;
    }

    private static byte[] WrapWav(short[] samples, int sampleRate)
    {
        int dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        return stream.ToArray();
    }

    private static bool PlayWithMediaPlayer(string path, Func<bool>? shouldCancel)
    {
        if (Application.Current?.Dispatcher == null)
        {
            return false;
        }

        var opened = new ManualResetEventSlim(false);
        var ended = new ManualResetEventSlim(false);
        var failed = new ManualResetEventSlim(false);

        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var player = new MediaPlayer { Volume = 1.0 };
                _activePlayer = player;
                player.MediaOpened += (_, _) => opened.Set();
                player.MediaEnded += (_, _) => ended.Set();
                player.MediaFailed += (_, _) => failed.Set();
                player.Open(new Uri(path, UriKind.Absolute));
                player.Play();
            });

            int waited = 0;
            while (waited < 4000 && !opened.IsSet && !failed.IsSet && !ended.IsSet)
            {
                if (shouldCancel?.Invoke() == true)
                {
                    return false;
                }

                Thread.Sleep(50);
                waited += 50;
            }

            if (failed.IsSet || (!opened.IsSet && !ended.IsSet))
            {
                return false;
            }

            while (!ended.IsSet && !failed.IsSet)
            {
                if (shouldCancel?.Invoke() == true)
                {
                    return true;
                }

                Thread.Sleep(80);
            }

            return !failed.IsSet;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MediaPlayer emergency sound failed: {ex.Message}");
            return false;
        }
        finally
        {
            opened.Dispose();
            ended.Dispose();
            failed.Dispose();

            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try { _activePlayer?.Stop(); } catch { }
                    try { _activePlayer?.Close(); } catch { }
                    _activePlayer = null;
                });
            }
            catch
            {
            }
        }
    }

    private static bool PlayFileBlocking(string path, string mediaType, Func<bool>? shouldCancel)
    {
        string alias = $"srpsiren{Interlocked.Increment(ref _aliasSeq)}";
        string escaped = path.Replace("'", "\\'", StringComparison.Ordinal);

        lock (PlayLock)
        {
            _activeAlias = alias;
        }

        bool opened = false;
        try
        {
            opened =
                Mci($"open \"{escaped}\" type {mediaType} alias {alias}") == 0 ||
                Mci($"open \"{escaped}\" type mpegvideo alias {alias}") == 0 ||
                Mci($"open \"{escaped}\" alias {alias}") == 0;

            if (!opened)
            {
                return false;
            }

            if (Mci($"play {alias}") != 0)
            {
                return false;
            }

            bool heardPlaying = false;
            while (true)
            {
                if (shouldCancel?.Invoke() == true)
                {
                    Mci($"stop {alias}");
                    return heardPlaying;
                }

                var status = new StringBuilder(64);
                Mci($"status {alias} mode", status);
                string mode = status.ToString().Trim();

                if (mode.Equals("playing", StringComparison.OrdinalIgnoreCase))
                {
                    heardPlaying = true;
                }

                if (string.IsNullOrEmpty(mode) ||
                    mode.Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("paused", StringComparison.OrdinalIgnoreCase))
                {
                    return heardPlaying;
                }

                Thread.Sleep(50);
            }
        }
        finally
        {
            if (opened)
            {
                Mci($"close {alias}");
            }

            lock (PlayLock)
            {
                if (_activeAlias == alias)
                {
                    _activeAlias = null;
                }
            }
        }
    }

    private static int Mci(string command, StringBuilder? returnString = null)
    {
        return mciSendString(command, returnString, returnString?.Capacity ?? 0, IntPtr.Zero);
    }

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(
        string command,
        StringBuilder? returnString,
        int returnLength,
        IntPtr callback);
}
