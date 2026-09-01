using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DashboardService.Models
{
    public class SensorAlertSettings
    {
        public bool VoiceEnabled { get; set; }

        public int RepeatAfterMinutes { get; set; }

        public int SensorViolationDbCheckIntervalSeconds { get; set; }

        public int CheckLiveSensorReadingInterval { get; set; }

        public string EnglishVoiceCulture { get; set; } = "en-IN";

        public string BengaliVoiceCulture { get; set; } = "bn-IN";
     }
    
}
