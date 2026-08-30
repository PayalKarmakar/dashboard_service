using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DashboardService.Models
{
    public class SensorViolation
    {
        public long SensorViolationsId { get; set; }
        public long ChamberId { get; set; }

        public string SensorModel { get; set; } = string.Empty;
        public string SensorType { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public string? Unit { get; set; }

        public string? CreationSeverity { get; set; }
        public string? FinalSeverity { get; set; }

        public string ThresholdType { get; set; } = string.Empty;
        public decimal ThresholdValue { get; set; }
        public decimal ActualValueAtStart { get; set; }

        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public long? DurationSeconds { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime? LastAnnouncedAt { get; set; }

        public string? LastAnnouncedSeverity { get; set; }
    }
}
