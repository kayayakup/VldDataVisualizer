// Models/SignalizationData.cs - Düzeltilmiş
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VldDataVisualizer.Models
{
    public class SignalizationData
    {
        public string SystemId { get; set; } = "ATS_SIGNALIZATION";
        public string SystemType { get; set; } = "CBTC-ATS";
        public DateTime Timestamp { get; set; }

        public List<TrainInfo> ActiveTrains { get; set; } = new List<TrainInfo>();
        public int TotalActiveTrains => ActiveTrains.Count;

        public string SystemStatus { get; set; } = "NORMAL";
        public bool IsCommunicationActive { get; set; } = true;
        public List<string> SystemAlarms { get; set; } = new List<string>();

        public List<StationInfo> Stations { get; set; } = new List<StationInfo>();
        public List<TrackBlock> TrackBlocks { get; set; } = new List<TrackBlock>();
        public List<RouteInfo> ActiveRoutes { get; set; } = new List<RouteInfo>();
    }

    public class TrainInfo
    {
        public int TrainNumber { get; set; }
        public int TrainId { get; set; }
        public double TrainLength { get; set; } = 88; // metre

        // ÖNEMLI: TrackType ve Direction birbirine bağlı - rastgele OLMAMALI!
        // TrackType belirlendikten sonra Direction otomatik ayarlanmalı

        private string _trackType = string.Empty;
        public string TrackType
        {
            get => _trackType;
            set
            {
                _trackType = value;
                // TrackType değiştiğinde Direction'ı da otomatik ayarla
                if (_trackType == "UP")
                {
                    _direction = "NORTHBOUND";
                }
                else if (_trackType == "DOWN")
                {
                    _direction = "SOUTHBOUND";
                }
            }
        }

        private string _direction = string.Empty;
        public string Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                // Direction değiştiğinde TrackType'ı da otomatik ayarla
                if (_direction == "NORTHBOUND")
                {
                    _trackType = "UP";
                }
                else if (_direction == "SOUTHBOUND")
                {
                    _trackType = "DOWN";
                }
            }
        }

        private string _status = string.Empty;
        public string Status
        {
            get => _status ?? "MOVING";
            set => _status = value;
        }

        public string TrainName { get; set; } = string.Empty;
        public double Speed { get; set; }
        public int CurrentBlockId { get; set; }
        public double CurrentPosition { get; set; }
        public double PositionInBlock { get; set; }
        public int NextStationId { get; set; }
        public string NextStationName { get; set; } = string.Empty;
        public double DistanceToNextStation { get; set; }
        public bool IsInService { get; set; } = true;
        public int PassengerCount { get; set; }
        public DateTime LastUpdateTime { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }

        public double Heading { get; set; }

        // Yardımcı metod: Hat bilgisini string olarak döndür
        public string GetTrackDescription()
        {
            if (TrackType == "UP" && Direction == "NORTHBOUND")
                return "Üst Hat (Depo → Darıca Sahil)";
            else if (TrackType == "DOWN" && Direction == "SOUTHBOUND")
                return "Alt Hat (Darıca Sahil → Depo)";
            else
                return "Bilinmeyen Hat";
        }

        // Yardımcı metod: Hat tutarlılığını kontrol et
        public bool IsTrackConsistent()
        {
            if (TrackType == "UP" && Direction == "NORTHBOUND") return true;
            if (TrackType == "DOWN" && Direction == "SOUTHBOUND") return true;
            return false;
        }
    }

    public class StationInfo
    {
        public int StationId { get; set; }
        public string StationName { get; set; } = string.Empty;
        public List<ArrivingTrain> ArrivingTrains { get; set; } = new List<ArrivingTrain>();
        public int WaitingPassengers { get; set; }
        public string Status { get; set; } = "NORMAL";

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public double PlatformLength { get; set; }
        public int PlatformCount { get; set; }
        public double StationLength { get; set; }
    }

    public class ArrivingTrain
    {
        public int TrainId { get; set; }
        public string TrainName { get; set; } = string.Empty;
        public int ArrivalMinutes { get; set; }
        public string Destination { get; set; } = string.Empty;
        public bool WillStop { get; set; } = true;
        public double CurrentDistance { get; set; }
    }

    public class TrackBlock : INotifyPropertyChanged
    {
        private int _occupyingTrainId;
        private bool _isOccupied;
        private string _status;

        public int BlockId { get; set; }
        public string BlockName { get; set; } = string.Empty;
        public double StartPosition { get; set; }
        public double EndPosition { get; set; }
        public double BlockLength { get; set; }
        public double Gradient { get; set; }
        public double Curvature { get; set; }
        public int SpeedLimit { get; set; }
        public string BlockType { get; set; } = string.Empty;

        public bool IsOccupied
        {
            get => _isOccupied;
            set
            {
                if (_isOccupied != value)
                {
                    _isOccupied = value;
                    OnPropertyChanged();
                }
            }
        }

        public int OccupyingTrainId
        {
            get => _occupyingTrainId;
            set
            {
                if (_occupyingTrainId != value)
                {
                    _occupyingTrainId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<BlockCoordinate> Coordinates { get; set; } = new List<BlockCoordinate>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Yardımcı metod: Hangi hat için blok olduğunu döndür
        public string GetTrackType()
        {
            if (BlockId >= 1 && BlockId <= 31)
                return "UP (Depo → Darıca Sahil)";
            else if (BlockId >= 101 && BlockId <= 131)
                return "DOWN (Darıca Sahil → Depo)";
            else
                return "Bilinmeyen";
        }
    }

    public class BlockCoordinate
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public double DistanceFromStart { get; set; }
    }

    public class RouteInfo : INotifyPropertyChanged
    {
        private int _activeTrainCount;
        private string _routeStatus = string.Empty;

        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public List<int> BlockSequence { get; set; } = new List<int>();
        public double TotalRouteLength { get; set; }

        public int ActiveTrainCount
        {
            get => _activeTrainCount;
            set
            {
                if (_activeTrainCount != value)
                {
                    _activeTrainCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RouteStatus
        {
            get => _routeStatus;
            set
            {
                if (_routeStatus != value)
                {
                    _routeStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public double StartLatitude { get; set; }
        public double StartLongitude { get; set; }
        public double EndLatitude { get; set; }
        public double EndLongitude { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}