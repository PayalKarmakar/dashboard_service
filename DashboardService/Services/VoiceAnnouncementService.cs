using System.Collections.Concurrent;
using DashboardService.Models;

namespace DashboardService.Services;

public sealed class VoiceAnnouncementService : IDisposable
{
    private readonly ConcurrentDictionary<long, LoopingAnnouncement> _active = new();
    private readonly ConcurrentQueue<long> _roundRobin = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly object _speakLock = new();
    private readonly Thread _speechThread;
    private readonly Func<long, Task>? _markPlayedAsync;
    private readonly HashSet<long> _markedPlayed = new();
    private volatile bool _disposed;
    private volatile bool _cancelCurrentSpeech;
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

        ClearOneTimeQueue();

        foreach (long key in keys)
        {
            VoicePlayingChanged?.Invoke(key, false);
        }

        CancelSpeech();
        _workAvailable.Set();
    }

    /// <summary>
    /// Stops queued one-time (sensor) announcements and cancels if one is speaking now.
    /// Does not stop looping employee alerts.
    /// </summary>
    public void StopOneTimeAnnouncements()
    {
        ClearOneTimeQueue();

        lock (_speakLock)
        {
            if (_currentlySpeakingKey == null)
            {
                _cancelCurrentSpeech = true;
                IndianOnlineTts.CancelActivePlayback();
            }
        }

        _workAvailable.Set();
    }

    private void ClearOneTimeQueue()
    {
        while (_oneTimeAnnouncements.TryDequeue(out _))
        {
        }
    }

    private void CancelIfSpeaking(long transactionId)
    {
        lock (_speakLock)
        {
            if (_currentlySpeakingKey == transactionId)
            {
                _cancelCurrentSpeech = true;
                IndianOnlineTts.CancelActivePlayback();
            }
        }
    }

    private void CancelSpeech()
    {
        lock (_speakLock)
        {
            _cancelCurrentSpeech = true;
            IndianOnlineTts.CancelActivePlayback();
        }
    }

    private void SpeechWorkerLoop()
    {
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
                SpeakLine(oneTimeLine, null);
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
                    _cancelCurrentSpeech = false;
                }

                SpeakLine(line, transactionId);

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
                    _cancelCurrentSpeech = false;
                }
            }

            if (_active.ContainsKey(transactionId))
            {
                _roundRobin.Enqueue(transactionId);
                Thread.Sleep(250);
            }
        }
    }

    private void SpeakLine(VoiceAnnouncementLine line, long? transactionId)
    {
        try
        {
            // Always use online Indian TTS (en-IN / bn) — no local voice pack needed.
            string culture = string.IsNullOrWhiteSpace(line.Culture)
                ? AlertMessageService.CultureEnglishIndia
                : line.Culture;

            if (culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
                !culture.Equals(AlertMessageService.CultureEnglishIndia, StringComparison.OrdinalIgnoreCase))
            {
                culture = AlertMessageService.CultureEnglishIndia;
            }

            IndianOnlineTts.Speak(
                line.Message,
                culture,
                shouldCancel: () =>
                    _disposed ||
                    _cancelCurrentSpeech ||
                    (transactionId.HasValue && !_active.ContainsKey(transactionId.Value)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Voice line failed ({line.Culture}): {ex.Message}");
        }
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
        IndianOnlineTts.CancelActivePlayback();
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
