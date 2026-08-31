using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DashboardService.Services;

/// <summary>
/// Speaks with Indian-locale online TTS (en-IN / bn) — no Windows voice pack install required.
/// </summary>
internal static class IndianOnlineTts
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly object PlayLock = new();
    private static int _aliasSeq;
    private static string? _activeAlias;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://translate.google.com/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-IN,en;q=0.9,bn;q=0.8");
        return client;
    }

    public static void Speak(string message, string culture, Func<bool>? shouldCancel = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string tl = MapLanguage(culture);

        foreach (string chunk in SplitChunks(message.Trim(), maxChars: 160))
        {
            if (shouldCancel?.Invoke() == true)
            {
                return;
            }

            string? mp3Path = null;
            try
            {
                mp3Path = DownloadMp3(chunk, tl);
                PlayMp3Blocking(mp3Path, shouldCancel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Online Indian TTS failed, using local fallback: {ex.Message}");
                SpeakLocalFallback(chunk, shouldCancel);
            }
            finally
            {
                if (mp3Path != null)
                {
                    try { File.Delete(mp3Path); } catch { }
                }
            }
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

    private static void SpeakLocalFallback(string text, Func<bool>? shouldCancel)
    {
        if (shouldCancel?.Invoke() == true || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer
        {
            Volume = 100,
            Rate = -1
        };

        var voices = synthesizer.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .ToList();

        var indian = voices.FirstOrDefault(v =>
            v.Culture.Name.Equals("en-IN", StringComparison.OrdinalIgnoreCase) ||
            v.Name.Contains("India", StringComparison.OrdinalIgnoreCase) ||
            v.Description.Contains("India", StringComparison.OrdinalIgnoreCase));

        synthesizer.SelectVoice((indian ?? voices.FirstOrDefault())?.Name
            ?? synthesizer.Voice.Name);

        synthesizer.Speak(text);
    }

    private static string MapLanguage(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return "en-IN";
        }

        if (culture.StartsWith("bn", StringComparison.OrdinalIgnoreCase))
        {
            return "bn";
        }

        // Force Indian English accent for all English (and unknown) cultures.
        return "en-IN";
    }

    private static string DownloadMp3(string text, string tl)
    {
        string url =
            "https://translate.google.com/translate_tts" +
            $"?ie=UTF-8&client=tw-ob&tl={Uri.EscapeDataString(tl)}&q={Uri.EscapeDataString(text)}";

        byte[] bytes = Http.GetByteArrayAsync(url)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (bytes.Length < 100)
        {
            throw new InvalidOperationException("Indian TTS returned empty audio.");
        }

        string path = Path.Combine(Path.GetTempPath(), $"srp-indian-tts-{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void PlayMp3Blocking(string path, Func<bool>? shouldCancel)
    {
        string alias = $"srpvoice{Interlocked.Increment(ref _aliasSeq)}";
        string escaped = path.Replace("'", "\\'", StringComparison.Ordinal);

        lock (PlayLock)
        {
            _activeAlias = alias;
        }

        try
        {
            if (Mci($"open \"{escaped}\" type mpegvideo alias {alias}") != 0)
            {
                throw new InvalidOperationException("Failed to open TTS audio.");
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

                Thread.Sleep(80);
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

    private static IEnumerable<string> SplitChunks(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }

        var parts = Regex.Split(text, @"(?<=[\.!\?।])\s+")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (parts.Count == 0)
        {
            parts.Add(text);
        }

        var buffer = new StringBuilder();
        foreach (string part in parts)
        {
            string piece = part.Trim();
            if (piece.Length > maxChars)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                }

                for (int i = 0; i < piece.Length; i += maxChars)
                {
                    yield return piece.Substring(i, Math.Min(maxChars, piece.Length - i));
                }

                continue;
            }

            if (buffer.Length + piece.Length + 1 > maxChars && buffer.Length > 0)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
            }

            if (buffer.Length > 0)
            {
                buffer.Append(' ');
            }

            buffer.Append(piece);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
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
