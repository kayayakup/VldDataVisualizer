using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VldDataVisualizer.Models
{
    public class VldErrorLog
    {
        public DateTime Timestamp { get; set; }
        public string Category { get; set; } // YEŞİL / SARI / KIRMIZI
        public string DeviceId { get; set; }
        public double Voltage { get; set; }
        public double Kilometer { get; set; }
        public int RepeatCount { get; set; }
        public int TrainCount { get; set; }
        public double AvgTrainSpeed { get; set; }
    }

}
