using System.Collections.Concurrent;
using System.Speech.Synthesis;
using DashboardService.Models;

namespace DashboardService.Services;

public sealed class VoiceAnnouncementService : IDisposable
{
    private readonly ConfigurationService _configurationService = new();
    private readonly ConcurrentDictionary<long, LoopingAnnouncement> _active = new();
    private readonly ConcurrentQueue<long> _roundRobin = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly object _speakLock = new();
    private readonly Thread _speechThread;
    private readonly Func<long, Task>? _markPlayedAsync;
    private readonly HashSet<long> _markedPlayed = new();
    private SpeechSynthesizer? _synthesizer;
    private volatile bool _disposed;
    private long? _currentlySpeakingKey;

    private readonly ConcurrentQueue<VoiceAnnouncementLine> _oneTimeAnnouncements = new();

    public event Action<long, bool>? VoicePlayingChanged;

    public VoiceAnnouncementService(Func<long, Task>? markPlayedAsync = null)
    {
        _markPlayedAsync = markPlayedAsync;
        _speechThread = new Thread(SpeechWorkerLoop)
        {
            IsBackground = true,
            Name = "VoiceAnnouncementWorker"
        };
        _speechThread.SetApartmentState(ApartmentState.STA);
        _speechThread.Start();
    }

    public bool IsPlaying(long transactionId) => _active.ContainsKey(transactionId);

    public bool HasAnyPlaying => !_active.IsEmpty;

    public void StartLooping(
        long transactionId,
        IReadOnlyList<VoiceAnnouncementLine> lines,
        long? alertId = null)
    {
        if (transactionId <= 0 || _disposed)
        {
            return;
        }

        var validLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Message))
            .Select(line => new VoiceAnnouncementLine(line.Message.Trim(), line.Culture))
            .ToList();

        if (validLines.Count == 0)
        {
            return;
        }

        bool added = false;

        _active.AddOrUpdate(
            transactionId,
            _ =>
            {
                added = true;
                return new LoopingAnnouncement(validLines, alertId);
            },
            (_, existing) =>
            {
                existing.SetLines(validLines);
                if (alertId.HasValue)
                {
                    existing.AlertId = alertId;
                }

                return existing;
            });

        if (added)
        {
            _roundRobin.Enqueue(transactionId);
            VoicePlayingChanged?.Invoke(transactionId, true);
        }

        _workAvailable.Set();
    }

    public void StartLooping(long transactionId, string message, long? alertId = null)
    {
        StartLooping(
            transactionId,
            new[] { new VoiceAnnouncementLine(message) },
            alertId);
    }

    public void AnnounceOnce(string message, string culture = "en-IN")
    {
        if (string.IsNullOrWhiteSpace(message) || _disposed)
        {
            return;
        }

        _oneTimeAnnouncements.Enqueue(
            new VoiceAnnouncementLine(message.Trim(), culture));

        _workAvailable.Set();
    }

    public void AnnounceOnce(IEnumerable<VoiceAnnouncementLine> lines)
    {
        if (_disposed)
        {
            return;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Message))
            {
                continue;
            }

            _oneTimeAnnouncements.Enqueue(
                new VoiceAnnouncementLine(line.Message.Trim(), line.Culture));
        }

        _workAvailable.Set();
    }

    public void Stop(long transactionId)
    {
        if (!_active.TryRemove(transactionId, out _))
        {
            return;
        }

        VoicePlayingChanged?.Invoke(transactionId, false);
        CancelIfSpeaking(transactionId);
        _workAvailable.Set();
    }

    public void StopAll()
    {
        var keys = _active.Keys.ToList();
        _active.Clear();

        while (_roundRobin.TryDequeue(out _))
        {
        }

        foreach (long key in keys)
        {
            VoicePlayingChanged?.Invoke(key, false);
        }

        CancelSpeech();
        _workAvailable.Set();
    }

    private void CancelIfSpeaking(long transactionId)
    {
        lock (_speakLock)
        {
            if (_currentlySpeakingKey == transactionId)
            {
                try
                {
                    _synthesizer?.SpeakAsyncCancelAll();
                }
                catch
                {
                }
            }
        }
    }

    private void CancelSpeech()
    {
        lock (_speakLock)
        {
            try
            {
                _synthesizer?.SpeakAsyncCancelAll();
            }
            catch
            {
            }
        }
    }

    private void SpeechWorkerLoop()
    {
        using var synthesizer = new SpeechSynthesizer
        {
            Volume = 100
        };

        _synthesizer = synthesizer;
        ConfigureVoice(synthesizer, AlertMessageService.CultureEnglishIndia);

        using var speakDone = new AutoResetEvent(false);
        synthesizer.SpeakCompleted += (_, _) => speakDone.Set();

        while (!_disposed)
        {
            _workAvailable.WaitOne(TimeSpan.FromMilliseconds(400));

            if (_disposed)
            {
                break;
            }

            if (!_active.IsEmpty && _roundRobin.IsEmpty)
            {
                foreach (long key in _active.Keys)
                {
                    _roundRobin.Enqueue(key);
                }
            }

            if (_oneTimeAnnouncements.TryDequeue(out VoiceAnnouncementLine? oneTimeLine))
            {
                SpeakLine(synthesizer, speakDone, oneTimeLine, null);
                continue;
            }

            if (!_roundRobin.TryDequeue(out long transactionId))
            {
                continue;
            }

            if (!_active.TryGetValue(transactionId, out LoopingAnnouncement? item))
            {
                continue;
            }

            VoiceAnnouncementLine? line = item.GetNextLine();
            if (line == null)
            {
                continue;
            }

            try
            {
                lock (_speakLock)
                {
                    _currentlySpeakingKey = transactionId;
                }

                SpeakLine(synthesizer, speakDone, line, transactionId);

                if (item.AlertId is long alertId &&
                    _markPlayedAsync != null &&
                    _markedPlayed.Add(alertId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _markPlayedAsync(alertId);
                        }
                        catch
                        {
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Voice announcement failed: {ex.Message}");
            }
            finally
            {
                lock (_speakLock)
                {
                    _currentlySpeakingKey = null;
                }
            }

            if (_active.ContainsKey(transactionId))
            {
                _roundRobin.Enqueue(transactionId);
                Thread.Sleep(250);
            }
        }

        _synthesizer = null;
    }

    private void SpeakLine(
        SpeechSynthesizer synthesizer,
        AutoResetEvent speakDone,
        VoiceAnnouncementLine line,
        long? transactionId)
    {
        try
        {
            ConfigureVoice(synthesizer, line.Culture);
            speakDone.Reset();
            synthesizer.SpeakAsync(line.Message);

            while (!speakDone.WaitOne(200))
            {
                if (_disposed)
                {
                    synthesizer.SpeakAsyncCancelAll();
                    speakDone.WaitOne(1000);
                    break;
                }

                if (transactionId.HasValue && !_active.ContainsKey(transactionId.Value))
                {
                    synthesizer.SpeakAsyncCancelAll();
                    speakDone.WaitOne(1000);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Voice line failed ({line.Culture}): {ex.Message}");
        }
    }

    private void ConfigureVoice(SpeechSynthesizer synthesizer, string culture)
    {
        var settings = _configurationService.GetAlertSettings();
        synthesizer.Rate = Math.Clamp(settings.VoiceRate, -10, 10);

        var installedVoices = synthesizer
            .GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .ToList();

        if (installedVoices.Count == 0)
        {
            return;
        }

        VoiceInfo? selectedVoice = null;

        if (!string.IsNullOrWhiteSpace(settings.VoiceName) &&
            culture.Equals(AlertMessageService.CultureEnglishIndia, StringComparison.OrdinalIgnoreCase))
        {
            selectedVoice = installedVoices.FirstOrDefault(v =>
                v.Name.Equals(settings.VoiceName, StringComparison.OrdinalIgnoreCase) ||
                v.Description.Contains(settings.VoiceName, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedVoice == null && !string.IsNullOrWhiteSpace(culture))
        {
            selectedVoice = installedVoices.FirstOrDefault(v =>
                v.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedVoice == null &&
            culture.Equals(AlertMessageService.CultureBengaliIndia, StringComparison.OrdinalIgnoreCase))
        {
            selectedVoice = installedVoices.FirstOrDefault(v =>
                v.Culture.Name.StartsWith("bn", StringComparison.OrdinalIgnoreCase));
        }

        selectedVoice ??= installedVoices.FirstOrDefault(v =>
            v.Culture.Name.Equals(AlertMessageService.CultureEnglishIndia, StringComparison.OrdinalIgnoreCase) ||
            v.Description.Contains("India", StringComparison.OrdinalIgnoreCase));

        selectedVoice ??= installedVoices.FirstOrDefault(v =>
            v.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase));

        selectedVoice ??= installedVoices[0];

        synthesizer.SelectVoice(selectedVoice.Name);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _active.Clear();
        CancelSpeech();
        _workAvailable.Set();

        _oneTimeAnnouncements.Clear();

        if (_speechThread.IsAlive)
        {
            _speechThread.Join(TimeSpan.FromSeconds(5));
        }

        _workAvailable.Dispose();
    }

    private sealed class LoopingAnnouncement
    {
        private int _nextLineIndex;
        private IReadOnlyList<VoiceAnnouncementLine> _lines;

        public LoopingAnnouncement(IReadOnlyList<VoiceAnnouncementLine> lines, long? alertId)
        {
            _lines = lines;
            AlertId = alertId;
        }

        public long? AlertId { get; set; }

        public void SetLines(IReadOnlyList<VoiceAnnouncementLine> lines)
        {
            _lines = lines;
            _nextLineIndex = 0;
        }

        public VoiceAnnouncementLine? GetNextLine()
        {
            if (_lines.Count == 0)
            {
                return null;
            }

            VoiceAnnouncementLine line = _lines[_nextLineIndex % _lines.Count];
            _nextLineIndex = (_nextLineIndex + 1) % _lines.Count;
            return line;
        }
    }
}
