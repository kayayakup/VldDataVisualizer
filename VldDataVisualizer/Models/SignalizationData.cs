// Models/SignalizationData.cs
namespace VldDataVisualizer.Models
{
    public class SignalizationData
    {
        // Temel kimlik bilgileri
        public string SystemId { get; set; } = "ATS_SIGNALIZATION";
        public string SystemType { get; set; } = "CBTC-ATS";
        public DateTime Timestamp { get; set; }

        // Tren bilgileri
        public List<TrainInfo> ActiveTrains { get; set; } = new List<TrainInfo>();
        public int TotalActiveTrains => ActiveTrains.Count;

        // Sistem durumu
        public string SystemStatus { get; set; } = "NORMAL";
        public bool IsCommunicationActive { get; set; } = true;
        public List<string> SystemAlarms { get; set; } = new List<string>();

        // İstasyon bilgileri
        public List<StationInfo> Stations { get; set; } = new List<StationInfo>();
    }

    public class TrainInfo
    {
        public int TrainNumber { get; set; }
        public int TrainId { get; set; }
        public string TrainName { get; set; }
        public double Speed { get; set; } // km/s
        public int CurrentBlockId { get; set; }
        public double CurrentPosition { get; set; } // metre
        public int NextStationId { get; set; }
        public string NextStationName { get; set; }
        public double DistanceToNextStation { get; set; } // metre
        public string Status { get; set; } = "MOVING";
        public bool IsInService { get; set; } = true;
        public int PassengerCount { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }

    public class StationInfo
    {
        public int StationId { get; set; }
        public string StationName { get; set; }
        public List<ArrivingTrain> ArrivingTrains { get; set; } = new List<ArrivingTrain>();
        public int WaitingPassengers { get; set; }
        public string Status { get; set; } = "NORMAL";
    }

    public class ArrivingTrain
    {
        public int TrainId { get; set; }
        public string TrainName { get; set; }
        public int ArrivalMinutes { get; set; }
        public string Destination { get; set; }
        public bool WillStop { get; set; } = true;
    }
}