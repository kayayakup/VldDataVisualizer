using System;
using System.Collections.Generic;

namespace VldDataVisualizer.Models
{
    public class VldErrorLog
    {
        public DateTime Timestamp { get; set; }
        public string Category { get; set; } // YEŞİL / SARI / KIRMIZI
        public int RepeatCount { get; set; }
        public List<AnomalyDevice> AffectedDevices { get; set; } = new List<AnomalyDevice>();
        public List<TrainInfoLog> AllTrains { get; set; } = new List<TrainInfoLog>();

        // Görüntüleme için formatlanmış özellikler
        public string DevicesDisplay => string.Join(", ", AffectedDevices.Select(d => $"{d.DeviceId} ({d.Voltage:N0}V)"));
        public string TrainPositionsDisplay => string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Position:N0}m"));
        public string TrainSpeedsDisplay => string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Speed:N0}km/h"));
        public string CategoryColor
        {
            get
            {
                return Category switch
                {
                    "YEŞİL" => "#4CAF50",
                    "SARI" => "#FFC107",
                    "KIRMIZI" => "#F44336",
                    _ => "#9E9E9E"
                };
            }
        }
    }

    public class AnomalyDevice
    {
        public string DeviceId { get; set; }
        public double Voltage { get; set; }
        public double Kilometer { get; set; }
        public double StartPosition { get; set; }
        public double EndPosition { get; set; }
    }

    public class TrainInfoLog
    {
        public int TrainId { get; set; }
        public string TrainName { get; set; }
        public double Position { get; set; } // metre
        public double Speed { get; set; } // km/h
        public string TrackType { get; set; }
        public string Status { get; set; }
    }
}