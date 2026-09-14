using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DashboardService.Services;

/// <summary>
/// Plays the bundled emergency sound (sound.mpeg) before sensor voice alerts.
/// Falls back to a generated siren if the file is missing.
/// </summary>
internal static class EmergencySoundPlayer
{
    private static readonly object PlayLock = new();
    private static int _aliasSeq;
    private static string? _activeAlias;

    public static void Play(Func<bool>? shouldCancel = null)
    {
        string? mpegPath = ResolveSoundPath();
        if (!string.IsNullOrWhiteSpace(mpegPath))
        {
            PlayFileBlocking(mpegPath, "mpegvideo", shouldCancel);
            return;
        }

        string wavPath = Path.Combine(Path.GetTempPath(), $"srp-siren-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(wavPath, BuildSirenWav());
            PlayFileBlocking(wavPath, "waveaudio", shouldCancel);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emergency siren failed: {ex.Message}");
        }
        finally
        {
            try { File.Delete(wavPath); } catch { }
        }
    }

    public static void CancelActivePlayback()
    {
        lock (PlayLock)
        {
            if (string.IsNullOrEmpty(_activeAlias))
            {
                return;
            }

            Mci($"stop {_activeAlias}");
            Mci($"close {_activeAlias}");
            _activeAlias = null;
        }
    }

    private static string? ResolveSoundPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "Assets", "sound.mpeg"),
            Path.Combine(baseDir, "sound.mpeg"),
            Path.Combine(baseDir, "..", "..", "..", "..", "sound.mpeg")
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

    private static void PlayFileBlocking(string path, string mediaType, Func<bool>? shouldCancel)
    {
        string alias = $"srpsiren{Interlocked.Increment(ref _aliasSeq)}";
        string escaped = path.Replace("'", "\\'", StringComparison.Ordinal);

        lock (PlayLock)
        {
            _activeAlias = alias;
        }

        try
        {
            if (Mci($"open \"{escaped}\" type {mediaType} alias {alias}") != 0 &&
                Mci($"open \"{escaped}\" type mpegvideo alias {alias}") != 0)
            {
                throw new InvalidOperationException("Failed to open emergency sound.");
            }

            Mci($"play {alias}");

            while (true)
            {
                if (shouldCancel?.Invoke() == true)
                {
                    Mci($"stop {alias}");
                    break;
                }

                var status = new StringBuilder(64);
                Mci($"status {alias} mode", status);
                string mode = status.ToString().Trim();

                if (string.IsNullOrEmpty(mode) ||
                    mode.Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("paused", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                Thread.Sleep(50);
            }
        }
        finally
        {
            Mci($"close {alias}");
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
