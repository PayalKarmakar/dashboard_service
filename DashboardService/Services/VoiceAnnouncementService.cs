using System.Collections.Concurrent;
using System.Speech.Synthesis;

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

    private readonly ConcurrentQueue<string> _sensorAnnouncements = new();

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

    /// <summary>
    /// Starts (or keeps) a continuous voice loop for this member until Stop / StopAll.
    /// </summary>
    public void StartLooping(long transactionId, string message, long? alertId = null)
    {
        if (transactionId <= 0 || string.IsNullOrWhiteSpace(message) || _disposed)
        {
            return;
        }

        string trimmed = message.Trim();
        bool added = false;

        _active.AddOrUpdate(
            transactionId,
            _ =>
            {
                added = true;
                return new LoopingAnnouncement(trimmed, alertId);
            },
            (_, existing) =>
            {
                existing.Message = trimmed;
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

    public void AnnounceSensorOnce(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _disposed)
        {
            return;
        }

        _sensorAnnouncements.Enqueue(message.Trim());

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

        System.Diagnostics.Debug.WriteLine("SPEECH WORKER STARTED");

        var voices = synthesizer.GetInstalledVoices();

        foreach (var voice in voices)
        {
            System.Diagnostics.Debug.WriteLine(
                $"VOICE: {voice.VoiceInfo.Name} | {voice.VoiceInfo.Culture}");
        }

        // TEMPORARY TEST
        synthesizer.SelectVoice("Microsoft Zira Desktop");

        System.Diagnostics.Debug.WriteLine("SELECTED VOICE: Microsoft Zira Desktop");

        synthesizer.Speak(
            "This is a direct Windows speech test.");

        System.Diagnostics.Debug.WriteLine("DIRECT SPEECH FINISHED");

        _synthesizer = synthesizer;

        ConfigureVoice(synthesizer);



        _synthesizer = synthesizer;
        ConfigureVoice(synthesizer);

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

            // EMPLOYEE LOOPING ANNOUNCEMENT

            if (!_roundRobin.TryDequeue(out long transactionId))
            {
                continue;
            }

            // SENSOR ONE-TIME ANNOUNCEMENT
            // =====================================

            if (_sensorAnnouncements.TryDequeue(out string? sensorMessage))
            {
                try
                {
                    lock (_speakLock)
                    {
                        _currentlySpeakingKey = null;
                    }

                    speakDone.Reset();

                    synthesizer.SpeakAsync(sensorMessage);

                    while (!speakDone.WaitOne(200))
                    {
                        if (_disposed)
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
                        $"Sensor voice announcement failed: {ex.Message}");
                }

                continue;
            }

            if (!_active.TryGetValue(transactionId, out LoopingAnnouncement? item))
            {
                continue;
            }

            try
            {
                lock (_speakLock)
                {
                    _currentlySpeakingKey = transactionId;
                }

                speakDone.Reset();
                synthesizer.SpeakAsync(item.Message);

                while (!speakDone.WaitOne(200))
                {
                    if (_disposed || !_active.ContainsKey(transactionId))
                    {
                        synthesizer.SpeakAsyncCancelAll();
                        speakDone.WaitOne(1000);
                        break;
                    }
                }

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

            // Keep looping this member until Stop is pressed.
            if (_active.ContainsKey(transactionId))
            {
                _roundRobin.Enqueue(transactionId);
                Thread.Sleep(250);
            }
        }

        _synthesizer = null;
    }

    private void ConfigureVoice(SpeechSynthesizer synthesizer)
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

        if (!string.IsNullOrWhiteSpace(settings.VoiceName))
        {
            selectedVoice = installedVoices.FirstOrDefault(v =>
                v.Name.Equals(settings.VoiceName, StringComparison.OrdinalIgnoreCase) ||
                v.Description.Contains(settings.VoiceName, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedVoice == null && !string.IsNullOrWhiteSpace(settings.VoiceCulture))
        {
            selectedVoice = installedVoices.FirstOrDefault(v =>
                v.Culture.Name.Equals(settings.VoiceCulture, StringComparison.OrdinalIgnoreCase));
        }

        selectedVoice ??= installedVoices.FirstOrDefault(v =>
            v.Culture.Name.Equals("en-IN", StringComparison.OrdinalIgnoreCase) ||
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

        _sensorAnnouncements.Clear();

        if (_speechThread.IsAlive)
        {
            _speechThread.Join(TimeSpan.FromSeconds(5));
        }

        _workAvailable.Dispose();
    }

    private sealed class LoopingAnnouncement
    {
        public LoopingAnnouncement(string message, long? alertId)
        {
            Message = message;
            AlertId = alertId;
        }

        public string Message { get; set; }

        public long? AlertId { get; set; }
    }
}
