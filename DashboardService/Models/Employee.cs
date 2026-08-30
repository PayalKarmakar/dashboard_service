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

        public string ChamberName { get; set; } = string.Empty;

        public DateTime EntryTime { get; set; }

        // Allowed inside duration. Value comes from appsettings AlertSettings:AfterMinutes.
        public int TimeThresholdMinutes { get; set; } = 60;

        public int AttentionMinutes { get; set; } = 30;

        public int WarningRemainingMinutes { get; set; } = 10;

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
            }
        }

        public bool CanStopVoice => IsVoicePlaying;

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

                    return $"Exceeded {exceeded.Hours:00}:"
                         + $"{exceeded.Minutes:00}:"
                         + $"{exceeded.Seconds:00}";
                }

                return $"{RemainingTime.Hours:00}:"
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

                if (elapsedMinutes >= TimeThresholdMinutes)
                    return "Violation";

                if (elapsedMinutes >= TimeThresholdMinutes - WarningRemainingMinutes)
                    return "Warning";

                if (elapsedMinutes >= AttentionMinutes)
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