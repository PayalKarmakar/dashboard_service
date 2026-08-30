using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DashboardService.Models
{
    public class SensorReading
    {
        public decimal? Temperature { get; set; }
        public decimal? Humidity { get; set; }
        public decimal? CO { get; set; }
        public decimal? CO2 { get; set; }
        public decimal? O2 { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
