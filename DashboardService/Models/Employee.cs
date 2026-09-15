using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DashboardService.Models
{
    public class Employee : INotifyPropertyChanged
    {
        public long EmployeeId { get; set; }

        public long TransactionId { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public string CardUid { get; set; } = string.Empty;

        public string ChamberName { get; set; } = string.Empty;

        public long ChamberId { get; set; }

        public DateTime EntryTime { get; set; }

        public DateTime? ExitTime { get; set; }
        public TimeSpan? Duration { get; set; }

        public string DurationDisplay => Duration.HasValue ? Duration.Value.ToString(@"hh\:mm\:ss"): "-";

        // Allowed inside duration from the chamber TIME (MIN). Falls back to AfterMinutes.
        public int TimeThresholdMinutes { get; set; } = 60;

        public int AttentionMinutes { get; set; } = 30;

        public int WarningRemainingMinutes { get; set; } = 10;

        public int ViolationAfterMinutes { get; set; }

        public bool WarningAudioEnabled { get; set; } = true;

        public bool ViolationAudioEnabled { get; set; } = true;

        public int WarningMaxPlayCount { get; set; } = 1;

        public int ViolationMaxPlayCount { get; set; }

        public int ViolationRepeatAfterMinutes { get; set; } = 5;

        public int HalfTimeMinutes { get; set; }

        public bool HalfTimeAudioEnabled { get; set; } = true;

        public int HalfTimeMaxPlayCount { get; set; } = 1;

        public string HalfTimeMessage { get; set; } = string.Empty;

        public long? HalfTimeRuleId { get; set; }

        public string WarningMessage { get; set; } = string.Empty;

        public string ViolationMessage { get; set; } = string.Empty;

        public long? WarningRuleId { get; set; }

        public long? ViolationRuleId { get; set; }

        public Dictionary<string, int> AnnouncementCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public int GetAnnouncementCount(params string[] alertTypes)
        {
            int total = 0;
            foreach (string type in alertTypes)
            {
                if (AnnouncementCounts.TryGetValue(type, out int count))
                {
                    total += count;
                }
            }

            return total;
        }

        public bool AlertTriggered { get; set; }

        public DateTime? LastAnnouncementAt { get; set; }

        public HashSet<string> AnnouncedTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasAnnouncement(string alertType)
        {
            return AnnouncedTypes.Contains(alertType);
        }

        private bool _isVoicePlaying;

        public bool IsVoicePlaying
        {
            get => _isVoicePlaying;
            set
            {
                if (_isVoicePlaying == value)
                {
                    return;
                }

                _isVoicePlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStopVoice));
                OnPropertyChanged(nameof(ShowStopVoice));
                OnPropertyChanged(nameof(VoicePlayingLabel));
            }
        }

        public bool CanStopVoice => IsVoicePlaying;

        public string VoicePlayingLabel => IsVoicePlaying ? "Speaking" : string.Empty;

        // Show Stop for active voice, or any alert-status member so the column is usable.
        public bool ShowStopVoice =>
            IsVoicePlaying ||
            Status is "Attention" or "Warning" or "Violation";

        private TimeSpan _remainingTime;

        public TimeSpan RemainingTime
        {
            get => _remainingTime;
            set
            {
                _remainingTime = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Countdown));
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(ShowStopVoice));
            }
        }

        public string Countdown
        {
            get
            {
                if (RemainingTime.TotalSeconds <= 0)
                {
                    var exceeded = RemainingTime.Duration();
                    int totalHours = (int)exceeded.TotalHours;

                    return $"Exceeded {totalHours:00}:"
                         + $"{exceeded.Minutes:00}:"
                         + $"{exceeded.Seconds:00}";
                }

                int remainingHours = (int)RemainingTime.TotalHours;
                return $"{remainingHours:00}:"
                     + $"{RemainingTime.Minutes:00}:"
                     + $"{RemainingTime.Seconds:00}";
            }
        }

        public string Status
        {
            get
            {
                double elapsedMinutes =
                    (DateTime.Now - EntryTime).TotalMinutes;

                if (elapsedMinutes >= TimeThresholdMinutes + Math.Max(0, ViolationAfterMinutes))
                    return "Violation";

                if (WarningRemainingMinutes > 0
                    && TimeThresholdMinutes > WarningRemainingMinutes
                    && elapsedMinutes >= TimeThresholdMinutes - WarningRemainingMinutes)
                    return "Warning";

                if (HalfTimeMinutes > 0 && elapsedMinutes >= HalfTimeMinutes)
                    return "Attention";

                return "Inside";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}