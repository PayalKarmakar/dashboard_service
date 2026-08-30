using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DashboardService.Models
{
    public class ChamberDashboard
    {
        public long ChamberId { get; set; }

        public string ChamberCode { get; set; } = string.Empty;

        public string ChamberName { get; set; } = string.Empty;

        public int MemberCount { get; set; }

        public int MemberThreshold { get; set; }

        public int OccupancyPercentage
        {
            get
            {
                if (MemberThreshold <= 0)
                    return 0;

                return (int)((double)MemberCount / MemberThreshold * 100);
            }
        }
    }
}
