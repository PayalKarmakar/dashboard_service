using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DashboardService.Models
{
    public class SensorReading
    {
        public long ChamberId { get; set; }

        public decimal? Temperature { get; set; }

        public DateTime? TemperatureReadAt { get; set; }

        public decimal? Humidity { get; set; }

        public DateTime? HumidityReadAt { get; set; }

        public decimal? CO { get; set; }

        public DateTime? CoReadAt { get; set; }

        public decimal? CO2 { get; set; }

        public DateTime? Co2ReadAt { get; set; }

        public decimal? O2 { get; set; }

        public DateTime? O2ReadAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime RecordedAt { get; set; }
    }
}
