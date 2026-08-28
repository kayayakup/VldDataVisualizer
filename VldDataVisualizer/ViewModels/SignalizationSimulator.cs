// ViewModels/SignalizationSimulator.cs
using VldDataVisualizer.Models;
using System.Timers;

namespace VldDataVisualizer.ViewModels
{
    public class SignalizationSimulator
    {
        public event EventHandler<SignalizationData>? DataGenerated;
        public event EventHandler<string>? StatusChanged;

        private System.Timers.Timer? _simulationTimer;
        private Random _random = new Random();
        private bool _isSimulationRunning = false;

        // Gebze-Darıca Metro hattı istasyonları
        private readonly List<Station> _stations = new List<Station>
        {
            new Station { Id = 1, Name = "Darıca Sahil P1", Position = 0 },
            new Station { Id = 2, Name = "Darıca Sahil P2", Position = 850 },
            new Station { Id = 3, Name = "Darıca Cumhuriyet Meydanı P1", Position = 1700 },
            new Station { Id = 4, Name = "Darıca Cumhuriyet Meydanı P2", Position = 2550 },
            new Station { Id = 5, Name = "Farabi Devlet Hastanesi P1", Position = 3400 },
            new Station { Id = 6, Name = "Farabi Devlet Hastanesi P2", Position = 4250 },
            new Station { Id = 7, Name = "TCDD Gar P1", Position = 5100 },
            new Station { Id = 8, Name = "TCDD Gar P2", Position = 5950 },
            new Station { Id = 9, Name = "Fatih Devlet Hastanesi P1", Position = 6800 },
            new Station { Id = 10, Name = "Fatih Devlet Hastanesi P2", Position = 7650 },
            new Station { Id = 11, Name = "Gebze Kent Meydanı P1", Position = 8500 },
            new Station { Id = 12, Name = "Gebze Kent Meydanı P2", Position = 9350 },
            new Station { Id = 13, Name = "Gebze Stadyum P1", Position = 10200 },
            new Station { Id = 14, Name = "Gebze Stadyum P2", Position = 11050 },
            new Station { Id = 15, Name = "Akse Sapağı P1", Position = 11900 },
            new Station { Id = 16, Name = "Akse Sapağı P2", Position = 12750 },
            new Station { Id = 17, Name = "Adliye P1", Position = 13600 },
            new Station { Id = 18, Name = "Adliye P2", Position = 14450 },
            new Station { Id = 19, Name = "Mutlukent P1", Position = 15300 },
            new Station { Id = 20, Name = "Mutlukent P2", Position = 16150 },
            new Station { Id = 21, Name = "OSB P1", Position = 17000 },
            new Station { Id = 22, Name = "OSB P2", Position = 17850 }
        };

        private List<TrainSimulation> _activeTrains = new List<TrainSimulation>();

        public void StartSimulation()
        {
            if (_isSimulationRunning) return;

            _isSimulationRunning = true;
            InitializeTrains();
            StatusChanged?.Invoke(this, "Sinyalizasyon simülasyonu başlatıldı");

            _simulationTimer = new System.Timers.Timer(2000); // 2 saniyede bir
            _simulationTimer.Elapsed += GenerateData;
            _simulationTimer.Start();
        }

        public void StopSimulation()
        {
            _isSimulationRunning = false;
            _simulationTimer?.Stop();
            _simulationTimer?.Dispose();
            StatusChanged?.Invoke(this, "Sinyalizasyon simülasyonu durduruldu");
        }

        private void InitializeTrains()
        {
            _activeTrains.Clear();

            // 5 tren ile başlat
            for (int i = 1; i <= 5; i++)
            {
                var train = new TrainSimulation
                {
                    TrainNumber = i,
                    TrainId = 100 + i,
                    TrainName = $"Tren {i:00}",
                    CurrentPosition = _random.Next(0, 18000),
                    Speed = 30 + _random.NextDouble() * 40, // 30-70 km/h
                    Direction = _random.Next(0, 2) == 0 ? 1 : -1,
                    IsInService = true,
                    PassengerCount = _random.Next(50, 300)
                };

                UpdateTrainNextStation(train);
                _activeTrains.Add(train);
            }
        }

        private void GenerateData(object sender, ElapsedEventArgs e)
        {
            if (!_isSimulationRunning) return;

            var signalData = new SignalizationData
            {
                Timestamp = DateTime.Now,
                SystemStatus = "NORMAL",
                IsCommunicationActive = _random.NextDouble() > 0.01 // %1 iletişim kaybı
            };

            // Trenleri güncelle
            UpdateAllTrains();
            signalData.ActiveTrains = _activeTrains.Select(t => CreateTrainInfo(t)).ToList();

            // İstasyon bilgilerini güncelle
            signalData.Stations = UpdateStations(signalData.ActiveTrains);

            // Sistem alarmlarını kontrol et
            signalData.SystemAlarms = CheckSystemAlarms(signalData);

            DataGenerated?.Invoke(this, signalData);

            // %5 ihtimalle yeni tren ekle
            if (_random.NextDouble() < 0.05 && _activeTrains.Count < 10)
            {
                AddNewTrain();
            }

            // %3 ihtimalle tren çıkar
            if (_random.NextDouble() < 0.03 && _activeTrains.Count > 3)
            {
                RemoveRandomTrain();
            }
        }

        private void UpdateAllTrains()
        {
            foreach (var train in _activeTrains)
            {
                // Tren pozisyonunu güncelle
                double distanceKm = train.Speed / 3600 * 2; // 2 saniyede gidilen km
                double distanceMeters = distanceKm * 1000;
                train.CurrentPosition += distanceMeters * train.Direction;

                // Hat sonuna gelince yön değiştir
                if (train.CurrentPosition >= 17850 || train.CurrentPosition <= 0)
                {
                    train.Direction *= -1;
                    train.CurrentPosition = Math.Max(0, Math.Min(17850, train.CurrentPosition));
                }

                // Hız değişikliği
                train.Speed = Math.Max(10, Math.Min(80, train.Speed + (_random.NextDouble() - 0.5) * 5));

                // Yolcu sayısını güncelle
                train.PassengerCount += _random.Next(-10, 15);
                train.PassengerCount = Math.Max(0, Math.Min(400, train.PassengerCount));

                // Sonraki istasyonu güncelle
                UpdateTrainNextStation(train);

                train.LastUpdateTime = DateTime.Now;
            }
        }

        private void UpdateTrainNextStation(TrainSimulation train)
        {
            var nextStation = _stations
                .Where(s => train.Direction > 0 ? s.Position > train.CurrentPosition : s.Position < train.CurrentPosition)
                .OrderBy(s => train.Direction > 0 ? s.Position : -s.Position)
                .FirstOrDefault();

            if (nextStation != null)
            {
                train.NextStationId = nextStation.Id;
                train.NextStationName = nextStation.Name;
                train.DistanceToNextStation = Math.Abs(nextStation.Position - train.CurrentPosition);
            }
        }

        private TrainInfo CreateTrainInfo(TrainSimulation train)
        {
            return new TrainInfo
            {
                TrainNumber = train.TrainNumber,
                TrainId = train.TrainId,
                TrainName = train.TrainName,
                Speed = Math.Round(train.Speed, 1),
                CurrentBlockId = (int)(train.CurrentPosition / 500) + 1, // Her 500m'de bir blok
                CurrentPosition = Math.Round(train.CurrentPosition, 0),
                NextStationId = train.NextStationId,
                NextStationName = train.NextStationName,
                DistanceToNextStation = Math.Round(train.DistanceToNextStation, 0),
                Status = train.Speed < 5 ? "DURUYOR" : "HAREKET HALİNDE",
                IsInService = train.IsInService,
                PassengerCount = train.PassengerCount,
                LastUpdateTime = train.LastUpdateTime
            };
        }

        private List<StationInfo> UpdateStations(List<TrainInfo> activeTrains)
        {
            var stationInfos = new List<StationInfo>();

            foreach (var station in _stations)
            {
                var stationInfo = new StationInfo
                {
                    StationId = station.Id,
                    StationName = station.Name,
                    WaitingPassengers = _random.Next(0, 200),
                    Status = "NORMAL"
                };

                // İstasyona yaklaşan trenleri bul
                var arrivingTrains = activeTrains
                    .Where(t => t.NextStationId == station.Id && t.DistanceToNextStation < 2000)
                    .Select(t => new ArrivingTrain
                    {
                        TrainId = t.TrainId,
                        TrainName = t.TrainName,
                        ArrivalMinutes = (int)(t.DistanceToNextStation / (t.Speed * 1000 / 60)), // Dakika cinsinden
                        Destination = t.NextStationName,
                        WillStop = _random.NextDouble() > 0.1 // %10 ihtimalle durmaz
                    })
                    .ToList();

                stationInfo.ArrivingTrains = arrivingTrains;
                stationInfos.Add(stationInfo);
            }

            return stationInfos;
        }

        private List<string> CheckSystemAlarms(SignalizationData data)
        {
            var alarms = new List<string>();

            // Hız alarmı
            var speedingTrains = data.ActiveTrains.Where(t => t.Speed > 75).ToList();
            if (speedingTrains.Any())
                alarms.Add($"AŞIRI HIZ: {string.Join(", ", speedingTrains.Select(t => t.TrainName))}");

            // Yoğunluk alarmı
            var crowdedTrains = data.ActiveTrains.Where(t => t.PassengerCount > 350).ToList();
            if (crowdedTrains.Any())
                alarms.Add($"YOĞUNLUK: {string.Join(", ", crowdedTrains.Select(t => t.TrainName))}");

            // İletişim alarmı
            if (!data.IsCommunicationActive)
                alarms.Add("SİSTEM İLETİŞİM KESİNTİSİ");

            // Tren sayısı alarmı
            if (data.TotalActiveTrains > 8)
                alarms.Add("FAZLA TREN: Sistem kapasitesi aşıldı");

            return alarms;
        }

        private void AddNewTrain()
        {
            var newTrainNumber = _activeTrains.Max(t => t.TrainNumber) + 1;
            var newTrain = new TrainSimulation
            {
                TrainNumber = newTrainNumber,
                TrainId = 100 + newTrainNumber,
                TrainName = $"Tren {newTrainNumber:00}",
                CurrentPosition = _random.Next(0, 5000),
                Speed = 40 + _random.NextDouble() * 30,
                Direction = 1,
                IsInService = true,
                PassengerCount = _random.Next(20, 150)
            };

            UpdateTrainNextStation(newTrain);
            _activeTrains.Add(newTrain);
        }

        private void RemoveRandomTrain()
        {
            if (_activeTrains.Count > 3)
            {
                var trainToRemove = _activeTrains[_random.Next(_activeTrains.Count)];
                _activeTrains.Remove(trainToRemove);
            }
        }
    }

    // Internal simulation classes
    internal class Station
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Position { get; set; } // metre cinsinden
    }

    internal class TrainSimulation
    {
        public int TrainNumber { get; set; }
        public int TrainId { get; set; }
        public string TrainName { get; set; } = string.Empty;
        public double Speed { get; set; }
        public double CurrentPosition { get; set; }
        public int Direction { get; set; } // 1: ileri, -1: geri
        public bool IsInService { get; set; }
        public int PassengerCount { get; set; }
        public int NextStationId { get; set; }
        public string NextStationName { get; set; } = string.Empty;
        public double DistanceToNextStation { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
}