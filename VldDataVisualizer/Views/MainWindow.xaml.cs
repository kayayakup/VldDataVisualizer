using System;
using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Linq;
using System.Collections.Generic;
using VldDataVisualizer.Models;
using VldDataVisualizer.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace VldDataVisualizer.Views
{
    public partial class MainWindow : Window
    {
        private VLDSimulator _vldSimulator;
        private SignalizationSimulator _signalizationSimulator;

        // Collections
        private ObservableCollection<VldData> _vldDataCollection;
        private ObservableCollection<TrainInfo> _activeTrains;
        private ObservableCollection<StationInfo> _stations;
        private ObservableCollection<TrackBlock> _blocks;
        private ObservableCollection<RouteInfo> _routes;
        private ObservableCollection<string> _vldAlarms;

        // Timers
        private DispatcherTimer _chartUpdateTimer;
        private DispatcherTimer _railwayUpdateTimer;

        // Railway drawing constants
        private const double CANVAS_HEIGHT = 400;
        private const double CANVAS_WIDTH = 2000;
        private const double BLOCK_WIDTH = 50;
        private const double TRAIN_WIDTH = 30;
        private const double STATION_WIDTH = 40;
        private const double TRACK_SPACING = 60;

        // Hat konumları
        private const double UP_TRACK_Y = CANVAS_HEIGHT / 2 - TRACK_SPACING / 2; // Üst hat
        private const double DOWN_TRACK_Y = CANVAS_HEIGHT / 2 + TRACK_SPACING / 2; // Alt hat

        // Sınırlar ve Yönler
        // 0 metre (Sol taraf) = Darıca Sahil
        // 15391 metre (Sağ taraf) = Depo
        private const double TOTAL_TRACK_LENGTH = 15391.246;

        private const int MAX_TOTAL_TRAINS = 7;
        private const double TRAIN_LENGTH = 88;
        private const double MIN_TRAIN_DISTANCE = 500;
        private const double STATION_STOP_TIME = 30; // saniye
        private Random _random = new Random();

        public MainWindow()
        {
            InitializeComponent();
            InitializeSystems();
        }

        private void InitializeSystems()
        {
            _vldSimulator = new VLDSimulator();
            _signalizationSimulator = new SignalizationSimulator();

            _vldDataCollection = new ObservableCollection<VldData>();
            _activeTrains = new ObservableCollection<TrainInfo>();
            _stations = new ObservableCollection<StationInfo>();
            _blocks = new ObservableCollection<TrackBlock>();
            _routes = new ObservableCollection<RouteInfo>();
            _vldAlarms = new ObservableCollection<string>();

            // Event handler'lar
            _vldSimulator.DataGenerated += OnVLDDataGenerated;
            _vldSimulator.StatusChanged += OnVLDStatusChanged;

            // DİKKAT: SignalizationDataGenerated artık trenleri doğrudan ezmeyecek,
            // sadece istatistik güncelleyecek.
            _signalizationSimulator.DataGenerated += OnSignalizationDataGenerated;
            _signalizationSimulator.StatusChanged += OnSignalizationStatusChanged;

            DataGrid.ItemsSource = _vldDataCollection;
            AlarmsItemsControl.ItemsSource = _vldAlarms;
            TrainsDataGrid.ItemsSource = _activeTrains;
            StationsDataGrid.ItemsSource = _stations;
            BlockDataGrid.ItemsSource = _blocks;
            RoutesDataGrid.ItemsSource = _routes;

            InitializeTimers();
            InitializeTrackSystem();

            // Başlangıç trenleri ekle
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AddInitialTrains();
                DrawRailwaySystem();
            }), DispatcherPriority.Background);

            UpdateHeaderStatus("🟢 Sistem Hazır", Colors.Green);
        }

        private void AddInitialTrains()
        {
            // ÜST HAT (UP): Sola gidecek (Depo -> Darıca)
            // Başlangıç noktası yüksek metre (sağ taraf) olmalı
            AddTrainToTrack("UP", 15000);  // Depo çıkışı
            AddTrainToTrack("UP", 12000);  // Yolun başı

            // ALT HAT (DOWN): Sağa gidecek (Darıca -> Depo)
            // Başlangıç noktası düşük metre (sol taraf) olmalı
            AddTrainToTrack("DOWN", 500);   // Darıca çıkışı
            AddTrainToTrack("DOWN", 4000);  // Yolun başı
        }

        private void InitializeTimers()
        {
            _chartUpdateTimer = new DispatcherTimer();
            _chartUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;
            _chartUpdateTimer.Start();

            // Tren hareketi için daha akıcı bir timer (örn: 100ms)
            // Ancak mevcut kod yapısını korumak için 500ms devam ettiriyoruz,
            // movement hesaplamasını buna göre yapacağız.
            _railwayUpdateTimer = new DispatcherTimer();
            _railwayUpdateTimer.Interval = TimeSpan.FromMilliseconds(100); // Daha akıcı hareket için hızlandırdım
            _railwayUpdateTimer.Tick += RailwayUpdateTimer_Tick;
        }

        private void InitializeTrackSystem()
        {
            InitializeBlocks();
            InitializeStations();
            InitializeRoutes();
        }

        private void InitializeBlocks()
        {
            _blocks.Clear();
            int blockCount = 31;
            double blockLength = TOTAL_TRACK_LENGTH / blockCount;

            for (int i = 0; i < blockCount; i++)
            {
                double startPos = i * blockLength;
                double endPos = (i + 1) * blockLength;

                _blocks.Add(new TrackBlock
                {
                    BlockId = i + 1,
                    BlockName = $"B{i + 1:00}-UP",
                    StartPosition = startPos,
                    EndPosition = endPos,
                    BlockLength = blockLength,
                    SpeedLimit = 80,
                    BlockType = "MAIN",
                    Status = "FREE"
                });

                _blocks.Add(new TrackBlock
                {
                    BlockId = i + 101,
                    BlockName = $"B{i + 1:00}-DOWN",
                    StartPosition = startPos,
                    EndPosition = endPos,
                    BlockLength = blockLength,
                    SpeedLimit = 80,
                    BlockType = "MAIN",
                    Status = "FREE"
                });
            }
        }

        private void InitializeStations()
        {
            _stations.Clear();
            // Bu pozisyonlar soldan sağa (0 -> 15391) metre cinsindendir
            var stationPositions = new double[]
            {
                15391.246, 13873.215, 12081.341, 10385.942, 9070.108,
                8234.420, 7101.599, 5781.120, 4389.210, 3298.624,
                1379.242, 136.100
            };

            // İsimler Depo'dan (Sağ) Darıca'ya (Sol) sıralanmış.
            // Koordinat sistemi 0=Darıca olduğu için ters çevirmemiz veya dikkatli eşleştirmemiz lazım.
            // Yukarıdaki array'de 15391 Depo'ya denk geliyor. Doğru.

            var stationNames = new string[]
            {
                "Depo", "OSB", "Mutlukent", "Adliye", "Akse Sapağı",
                "Gebze Stadyum", "Gebze Kent Meydanı", "Fatih Devlet Hastanesi",
                "TCDD Gar", "Farabi Devlet Hastanesi", "Darıca Cumhuriyet", "Darıca Sahil"
            };

            for (int i = 0; i < stationPositions.Length; i++)
            {
                _stations.Add(new StationInfo
                {
                    StationId = i + 1,
                    StationName = stationNames[i],
                    GridX = (int)stationPositions[i],
                    GridY = (int)(CANVAS_HEIGHT / 2),
                    PlatformLength = 120,
                    PlatformCount = 2,
                    StationLength = 200,
                    WaitingPassengers = new Random().Next(50, 200),
                    Status = "NORMAL"
                });
            }
        }

        private void InitializeRoutes()
        {
            _routes.Clear();

            _routes.Add(new RouteInfo
            {
                RouteId = 1,
                RouteName = "Depo → Darıca Sahil (ÜST HAT)",
                BlockSequence = Enumerable.Range(1, 31).ToList(),
                TotalRouteLength = TOTAL_TRACK_LENGTH,
                ActiveTrainCount = 0,
                RouteStatus = "ACTIVE"
            });

            _routes.Add(new RouteInfo
            {
                RouteId = 2,
                RouteName = "Darıca Sahil → Depo (ALT HAT)",
                BlockSequence = Enumerable.Range(101, 31).ToList(),
                TotalRouteLength = TOTAL_TRACK_LENGTH,
                ActiveTrainCount = 0,
                RouteStatus = "ACTIVE"
            });
        }

        #region BUTON CLICK EVENT HANDLERS

        private void StartAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.StartSimulation();
                _signalizationSimulator.StartSimulation();
                _railwayUpdateTimer.Start();
                _chartUpdateTimer.Start();

                UpdateButtonStates(true);
                UpdateHeaderStatus("🟡 Sistem Çalışıyor", Colors.Orange);
                ShowStatusMessage("Tüm simülasyonlar başlatıldı", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Başlatma hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void StopAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.StopSimulation();
                _signalizationSimulator.StopSimulation();
                _railwayUpdateTimer.Stop();
                _chartUpdateTimer.Stop();

                UpdateButtonStates(false);
                UpdateHeaderStatus("🟢 Sistem Hazır", Colors.Green);
                ShowStatusMessage("Tüm simülasyonlar durduruldu", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Durdurma hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void EmergencyButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "🚨 ACİL DURUM 🚨\n\nTüm sistemleri acil durdurma moduna almak istiyor musunuz?",
                "Acil Dur Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                EmergencyStopAllSystems();
            }
        }

        private void StartSignalizationButton_Click(object sender, RoutedEventArgs e)
        {
            _signalizationSimulator.StartSimulation();
            StartSignalizationButton.IsEnabled = false;
            StopSignalizationButton.IsEnabled = true;
            _railwayUpdateTimer.Start(); // Tren hareketi için gerekli
            ShowStatusMessage("Sinyalizasyon simülasyonu başlatıldı", StatusType.Info);
        }

        private void StopSignalizationButton_Click(object sender, RoutedEventArgs e)
        {
            _signalizationSimulator.StopSimulation();
            StartSignalizationButton.IsEnabled = true;
            StopSignalizationButton.IsEnabled = false;
            _railwayUpdateTimer.Stop();
            ShowStatusMessage("Sinyalizasyon simülasyonu durduruldu", StatusType.Info);
        }

        private void AddTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count >= MAX_TOTAL_TRAINS) return;

            // Dengeli ekleme
            int upTrackCount = _activeTrains.Count(t => t.TrackType == "UP");
            int downTrackCount = _activeTrains.Count(t => t.TrackType == "DOWN");
            string trackType = upTrackCount <= downTrackCount ? "UP" : "DOWN";

            double startPosition = GetSafeStartPosition(trackType);
            AddTrainToTrack(trackType, startPosition);
        }

        private void AddTrainToTrack(string trackType, double startPosition)
        {
            // UP Track (Üst) = Depo -> Darıca = Sola Gider (Position Azalır) = NORTHBOUND
            // DOWN Track (Alt) = Darıca -> Depo = Sağa Gider (Position Artar) = SOUTHBOUND

            string direction = trackType == "UP" ? "NORTHBOUND" : "SOUTHBOUND";
            int startBlockId = trackType == "UP" ? 1 : 101;
            int gridY = trackType == "UP" ? (int)UP_TRACK_Y : (int)DOWN_TRACK_Y;

            var newTrain = new TrainInfo
            {
                TrainNumber = _activeTrains.Count + 1,
                TrainId = _activeTrains.Count + 100 + new Random().Next(1000), // Unique ID
                TrainName = $"TR-{new Random().Next(100, 999)}",
                Speed = 60 + _random.Next(0, 20),
                CurrentBlockId = startBlockId,
                CurrentPosition = startPosition,
                PositionInBlock = 0,
                Status = "MOVING",
                IsInService = true,
                PassengerCount = _random.Next(50, 200),
                LastUpdateTime = DateTime.Now,
                GridX = (int)(startPosition / 8),
                GridY = gridY,
                Direction = direction,
                Heading = trackType == "UP" ? 180 : 0,
                TrainLength = TRAIN_LENGTH,
                TrackType = trackType
            };

            _activeTrains.Add(newTrain);
            UpdateBlockOccupancyForAllTrains();
        }

        private double GetSafeStartPosition(string trackType)
        {
            var sameTrackTrains = _activeTrains.Where(t => t.TrackType == trackType).ToList();

            if (trackType == "UP")
            {
                // Üst hat: Sağdan (15391) sola (0) gider.
                // Yeni tren en sağdan (Depo) girmeli.
                if (!sameTrackTrains.Any()) return 15391;

                // En sağdaki treni bul (Pozisyonu en büyük olan)
                double maxPos = sameTrackTrains.Max(t => t.CurrentPosition);
                // Eğer en sağdaki tren 15391'den yeterince uzaklaştıysa başlangıca koy
                if (15391 - maxPos > MIN_TRAIN_DISTANCE) return 15391;

                // Aksi takdirde, en arkadaki trenin biraz arkasına koy (güvenlik mesafesi)
                // Ama ekran dışına taşmamalı
                return Math.Min(15391, maxPos + MIN_TRAIN_DISTANCE);
            }
            else // DOWN
            {
                // Alt hat: Soldan (0) sağa (15391) gider.
                // Yeni tren en soldan (Darıca) girmeli.
                if (!sameTrackTrains.Any()) return 0;

                // En soldaki treni bul (Pozisyonu en küçük olan)
                double minPos = sameTrackTrains.Min(t => t.CurrentPosition);

                if (minPos > MIN_TRAIN_DISTANCE) return 0;

                return Math.Max(0, minPos - MIN_TRAIN_DISTANCE);
            }
        }

        private void RemoveTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count > 0)
            {
                var lastTrain = _activeTrains.Last();
                _activeTrains.Remove(lastTrain);
                UpdateBlockOccupancyForAllTrains();
            }
        }

        private void AddUpTrackTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count >= MAX_TOTAL_TRAINS) return;
            // Üst hat başlangıcı (Sağ taraf)
            AddTrainToTrack("UP", 15391);
        }

        private void AddDownTrackTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count >= MAX_TOTAL_TRAINS) return;
            // Alt hat başlangıcı (Sol taraf)
            AddTrainToTrack("DOWN", 0);
        }

        private void ResetTrainsButton_Click(object sender, RoutedEventArgs e)
        {
            _activeTrains.Clear();
            _trainStates.Clear();
            AddInitialTrains();
            ShowStatusMessage("Trenler sıfırlandı", StatusType.Info);
        }

        // TFPR Butonları
        private void StartVLDButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.StartSimulation();
            StartVLDButton.IsEnabled = false;
            StopVLDButton.IsEnabled = true;
            _chartUpdateTimer.Start();
        }

        private void StopVLDButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.StopSimulation();
            StartVLDButton.IsEnabled = true;
            StopVLDButton.IsEnabled = false;
            _chartUpdateTimer.Stop();
        }

        private void VoltageSagButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.SimulateVoltageSag();
        }

        private void OverloadButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.SimulateOverload();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.ResetToNormal();
        }

        #endregion

        #region EVENT HANDLERS

        private void OnVLDDataGenerated(object sender, VldData data)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateVLDRealTimeValues(data);
                UpdateVLDAlarms(data);

                _vldDataCollection.Insert(0, data);
                if (_vldDataCollection.Count > 100)
                    _vldDataCollection.RemoveAt(_vldDataCollection.Count - 1);

                DataCountText.Content = $"TFPR Veri: {_vldDataCollection.Count}";
            });
        }

        private void OnSignalizationDataGenerated(object sender, SignalizationData data)
        {
            Dispatcher.Invoke(() =>
            {
                // ÇOK ÖNEMLİ DÜZELTME:
                // Simülatörden gelen tren verileri (_activeTrains) ile yerel hareket mantığını çakıştırmıyoruz.
                // Burada sadece genel istatistikleri güncelliyoruz.
                // Trenlerin konumu RailwayUpdateTimer tarafından yönetilecek.

                TotalTrainsText.Text = $"🚆 Aktif Tren: {_activeTrains.Count}"; // Yerel sayıyı kullan
                SystemStatusText.Text = $"📡 Sistem: {data.SystemStatus}";

                SignalCommunicationText.Text = $"📶 İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
                SignalCommunicationBorder.Background = data.IsCommunicationActive ?
                    new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

                double avgSpeed = _activeTrains.Any() ? _activeTrains.Average(t => t.Speed) : 0;
                AvgSpeedText.Text = $"⚡ Ort. Hız: {avgSpeed:0} km/s";

                int totalPassengers = _activeTrains.Sum(t => t.PassengerCount) + _stations.Sum(s => s.WaitingPassengers);
                TotalPassengersText.Text = $"👥 Toplam Yolcu: {totalPassengers}";

                // UpdateTrainsCollection(data.ActiveTrains); // <--- BU SATIR SİLİNDİ/YORUMLANDI (Flickering sebebi)
                // UpdateBlocksOccupancy(data.TrackBlocks, data.ActiveTrains); // <--- Bunu yerel verilerle yapacağız

                UpdateStationsDynamicData(data.Stations);

                // Blok ve route doluluklarını yerel trenlere göre güncelle
                UpdateBlocksOccupancy(_blocks.ToList(), _activeTrains.ToList());
                UpdateRoutesTrainCounts(_routes.ToList(), _activeTrains.ToList());

                CanvasInfoText.Text = $"İstasyonlar: {_stations.Count} | Bloklar: {_blocks.Count} | Trenler: {_activeTrains.Count}";
            });
        }

        private void OnVLDStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                HeaderStatusText.Text = $"🔵 {status}";
                ShowStatusMessage(status, StatusType.Info);
            });
        }

        private void OnSignalizationStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                SignalizationStatusText.Text = $"🔵 {status}";
            });
        }

        #endregion

        #region REAL-TIME UPDATES

        private void UpdateVLDRealTimeValues(VldData data)
        {
            DeviceIdText.Text = $"🔧 Cihaz: {data.DeviceId}";
            LocationText.Text = $"📍 Lokasyon: {data.Location}";

            CommunicationText.Text = $"📶 İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
            CommunicationStatusBorder.Background = data.IsCommunicationActive ?
                new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

            VoltageInText.Text = $"🔌 Giriş Gerilimi: {data.VoltageIn:0.0} kV";
            VoltageOutText.Text = $"⚡ Çıkış Gerilimi: {data.VoltageOut:0.0} kV";
            CurrentText.Text = $"🔋 Toplam Akım: {data.Current:0} A";
            ActivePowerText.Text = $"📊 Aktif Güç: {data.ActivePower:0} kW";
            ReactivePowerText.Text = $"📈 Reaktif Güç: {data.ReactivePower:0} kVAr";
            PowerFactorText.Text = $"🎯 Güç Faktörü: {data.PowerFactor:0.00}";

            VoltageL1Text.Text = $"L1 🔌 Gerilim: {data.VoltageL1:0.0} kV";
            VoltageL2Text.Text = $"L2 🔌 Gerilim: {data.VoltageL2:0.0} kV";
            VoltageL3Text.Text = $"L3 🔌 Gerilim: {data.VoltageL3:0.0} kV";
            CurrentL1Text.Text = $"L1 🔋 Akım: {data.CurrentL1:0} A";
            CurrentL2Text.Text = $"L2 🔋 Akım: {data.CurrentL2:0} A";
            CurrentL3Text.Text = $"L3 🔋 Akım: {data.CurrentL3:0} A";

            TemperatureText.Text = $"🌡️ Sıcaklık: {data.Temperature:0.0} °C";
            FrequencyText.Text = $"📏 Frekans: {data.Frequency:0.00} Hz";
            GroundCurrentText.Text = $"⚡ Toprak Akımı: {data.GroundCurrent:0.0} A";
            THDVoltageText.Text = $"📉 Gerilim THD: {data.THDVoltage:0.0} %";
            THDCurrentText.Text = $"📉 Akım THD: {data.THDCurrent:0.0} %";

            EnergyImportText.Text = $"🔋 Tüketilen Enerji: {data.ActiveEnergyImport:0} kWh";
            VLDStatusText.Text = $"⚡ Durum: {data.Status}";

            VLDStatusBorder.Background = data.Status switch
            {
                "NORMAL" => new SolidColorBrush(Colors.LightGreen),
                "WARNING" => new SolidColorBrush(Colors.LightYellow),
                "ALARM" => new SolidColorBrush(Colors.LightCoral),
                _ => new SolidColorBrush(Colors.LightGray)
            };
        }

        private void UpdateStationsDynamicData(List<StationInfo> newStations)
        {
            foreach (var newStation in newStations)
            {
                var existingStation = _stations.FirstOrDefault(s => s.StationId == newStation.StationId);
                if (existingStation != null)
                {
                    existingStation.WaitingPassengers = newStation.WaitingPassengers;
                    existingStation.Status = newStation.Status;
                }
            }
        }

        private void UpdateBlocksOccupancy(List<TrackBlock> newBlocks, List<TrainInfo> activeTrains)
        {
            foreach (var block in _blocks)
            {
                var isOccupied = activeTrains.Any(train =>
                {
                    bool isUpTrackMatch = block.BlockId <= 31 &&
                                          train.TrackType == "UP" &&
                                          train.CurrentBlockId == block.BlockId;

                    bool isDownTrackMatch = block.BlockId >= 101 &&
                                            train.TrackType == "DOWN" &&
                                            train.CurrentBlockId == (block.BlockId - 100);

                    return isUpTrackMatch || isDownTrackMatch;
                });

                block.IsOccupied = isOccupied;
                block.Status = isOccupied ? "OCCUPIED" : "FREE";
                block.OccupyingTrainId = isOccupied ? activeTrains.FirstOrDefault(t => t.CurrentBlockId == (block.BlockId > 100 ? block.BlockId - 100 : block.BlockId))?.TrainId ?? 0 : 0;
            }
        }

        private void UpdateRoutesTrainCounts(List<RouteInfo> newRoutes, List<TrainInfo> activeTrains)
        {
            foreach (var route in _routes)
            {
                route.ActiveTrainCount = activeTrains.Count(t =>
                    (route.BlockSequence.Contains(t.CurrentBlockId) && t.TrackType == "UP") ||
                    (route.BlockSequence.Contains(t.CurrentBlockId + 100) && t.TrackType == "DOWN"));
            }
        }

        private void UpdateVLDAlarms(VldData data)
        {
            _vldAlarms.Clear();
            foreach (var alarm in data.ActiveAlarms)
            {
                _vldAlarms.Add(alarm);
            }
        }

        #endregion

        #region TREN HAREKET SİSTEMİ - DÜZELTİLMİŞ

        private class TrainState
        {
            public double StopTimer { get; set; }
            public bool IsStoppedAtStation { get; set; }
            public int CurrentStationId { get; set; }
            public int LastDepartureStationId { get; set; } = -1;
        }

        private Dictionary<int, TrainState> _trainStates = new Dictionary<int, TrainState>();

        private void UpdateTrainPositions()
        {
            try
            {
                List<TrainInfo> trainsToRemove = new List<TrainInfo>();

                foreach (var train in _activeTrains.ToList())
                {
                    // TrainState yönetimi (Yoksa oluştur)
                    if (!_trainStates.ContainsKey(train.TrainId))
                        _trainStates[train.TrainId] = new TrainState();

                    var trainState = _trainStates[train.TrainId];

                    // ---------------------------------------------------------
                    // 1. İSTASYONDA BEKLEME MANTIĞI
                    // ---------------------------------------------------------
                    if (trainState.IsStoppedAtStation)
                    {
                        trainState.StopTimer += 0.1; // Timer interval'ı 100ms
                        train.Status = "STOPPED";
                        train.Speed = 0;

                        // Süre doldu mu?
                        if (trainState.StopTimer >= STATION_STOP_TIME)
                        {
                            // KALKIŞ ANI
                            trainState.IsStoppedAtStation = false;
                            trainState.StopTimer = 0;

                            // ÖNEMLİ: Hangi istasyondan kalktığımızı kaydediyoruz
                            // Böylece döngü bir sonraki adımda bizi tekrar durdurmayacak.
                            trainState.LastDepartureStationId = trainState.CurrentStationId;

                            train.Status = "MOVING";
                            train.Speed = 60 + _random.Next(0, 20); // Hız ver
                        }
                        continue; // Tren duruyorsa hareket hesaplamasına geçme
                    }

                    // ---------------------------------------------------------
                    // 2. ÖNDEKİ TREN KONTROLÜ (Mesafe Koruma)
                    // ---------------------------------------------------------
                    if (IsTrainAhead(train))
                    {
                        train.Status = "WAITING";
                        train.Speed = 0;
                        continue;
                    }

                    // ---------------------------------------------------------
                    // 3. İSTASYON YAKLAŞIM VE DURMA MANTIĞI
                    // ---------------------------------------------------------
                    var approachingStation = GetApproachingStation(train);

                    // Eğer bir istasyona yaklaşıyorsak VE bu istasyon az önce kalktığımız istasyon DEĞİLSE
                    if (approachingStation != null &&
                        approachingStation.StationId != trainState.LastDepartureStationId)
                    {
                        double distanceToStation = Math.Abs(train.CurrentPosition - approachingStation.GridX);

                        // 200m kala yavaşla
                        if (distanceToStation < 200)
                        {
                            train.Speed = Math.Max(20, train.Speed - 2);

                            // 20m kala DUR
                            if (distanceToStation < 20)
                            {
                                train.Status = "STOPPED";
                                train.Speed = 0;
                                trainState.IsStoppedAtStation = true;
                                trainState.StopTimer = 0;
                                trainState.CurrentStationId = approachingStation.StationId;

                                // Yolcu simülasyonu
                                train.PassengerCount = _random.Next(50, 200);
                                approachingStation.WaitingPassengers = _random.Next(20, 100);

                                continue; // Döngüyü kır, hareket etme
                            }
                        }
                    }

                    // ---------------------------------------------------------
                    // 4. SON KALKILAN İSTASYON HAFIZASINI TEMİZLEME
                    // ---------------------------------------------------------
                    // Eğer son kalktığımız istasyondan yeterince uzaklaştıysak (örn: 250m),
                    // hafızayı temizle. (Geri dönüşlerde veya hat değişimlerinde sorun olmasın diye)
                    if (trainState.LastDepartureStationId != -1)
                    {
                        var lastStation = _stations.FirstOrDefault(s => s.StationId == trainState.LastDepartureStationId);
                        if (lastStation != null)
                        {
                            double dist = Math.Abs(train.CurrentPosition - lastStation.GridX);
                            if (dist > 250) // 250 metre uzaklaştıysak
                            {
                                trainState.LastDepartureStationId = -1; // Artık unutabiliriz
                            }
                        }
                    }

                    // ---------------------------------------------------------
                    // 5. HAREKET MANTIĞI (FİZİKSEL POZİSYON GÜNCELLEME)
                    // ---------------------------------------------------------
                    if (train.Status == "MOVING" || train.Status == "WAITING")
                    {
                        // Bekliyorsa hızı 0 yap, değilse mevcut hız
                        double currentSpeed = (train.Status == "WAITING") ? 0 : train.Speed;

                        // Formül: (km/h / 3.6) * zaman(s) -> Timer 100ms olduğu için 0.1 ile çarpıyoruz
                        // Hareketi biraz daha belirgin yapmak için çarpanı 0.5 kullanabiliriz (daha akıcı görünür)
                        double movement = (currentSpeed / 3.6) * 0.5;

                        if (train.TrackType == "UP")
                        {
                            // ÜST HAT: Sağa (15391) -> Sola (0)
                            train.CurrentPosition -= movement;

                            if (train.CurrentPosition <= -100) trainsToRemove.Add(train);
                        }
                        else
                        {
                            // ALT HAT: Soldan (0) -> Sağa (15391)
                            train.CurrentPosition += movement;

                            if (train.CurrentPosition >= TOTAL_TRACK_LENGTH + 100) trainsToRemove.Add(train);
                        }

                        // Grid ve Blok Güncelleme
                        train.GridX = (int)(train.CurrentPosition / 8);
                        UpdateTrainCurrentBlock(train);

                        // Blok içi pozisyon
                        var block = _blocks.FirstOrDefault(b => b.BlockId == train.CurrentBlockId + (train.TrackType == "DOWN" ? 100 : 0));
                        if (block != null)
                            train.PositionInBlock = Math.Abs(train.CurrentPosition - block.StartPosition);
                    }
                }

                // Biten trenleri temizle
                foreach (var train in trainsToRemove)
                {
                    _activeTrains.Remove(train);
                    _trainStates.Remove(train.TrainId);

                    // Döngü olması için tren bitince başa yeni ekle (İsteğe bağlı)
                    AddTrainToTrack(train.TrackType, train.TrackType == "UP" ? 15391 : 0);
                }

                UpdateBlockOccupancyForAllTrains();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateTrainPositions Error: {ex.Message}");
            }
        }

        private bool IsTrainAhead(TrainInfo currentTrain)
        {
            var sameTrackTrains = _activeTrains
                .Where(t => t.TrackType == currentTrain.TrackType && t.TrainId != currentTrain.TrainId)
                .ToList();

            if (!sameTrackTrains.Any()) return false;

            double safeDistance = 1000; // Güvenli takip mesafesi

            if (currentTrain.TrackType == "UP")
            {
                // Üst Hat (Gidiş Yönü Sola/0'a Doğru)
                // Önündeki trenin pozisyonu daha KÜÇÜK olmalı
                var aheadTrain = sameTrackTrains
                    .Where(t => t.CurrentPosition < currentTrain.CurrentPosition)
                    .OrderByDescending(t => t.CurrentPosition) // En yakın olan (pozisyonu en büyük olan)
                    .FirstOrDefault();

                if (aheadTrain != null)
                {
                    double distance = currentTrain.CurrentPosition - aheadTrain.CurrentPosition;
                    return distance < safeDistance;
                }
            }
            else // DOWN
            {
                // Alt Hat (Gidiş Yönü Sağa/15000'e Doğru)
                // Önündeki trenin pozisyonu daha BÜYÜK olmalı
                var aheadTrain = sameTrackTrains
                    .Where(t => t.CurrentPosition > currentTrain.CurrentPosition)
                    .OrderBy(t => t.CurrentPosition) // En yakın olan (pozisyonu en küçük olan)
                    .FirstOrDefault();

                if (aheadTrain != null)
                {
                    double distance = aheadTrain.CurrentPosition - currentTrain.CurrentPosition;
                    return distance < safeDistance;
                }
            }

            return false;
        }

        private StationInfo GetApproachingStation(TrainInfo train)
        {
            // Basit yakınlık kontrolü
            // İstasyon pozisyonları sabittir
            foreach (var station in _stations)
            {
                double dist = Math.Abs(train.CurrentPosition - station.GridX);
                // Eğer çok yakınsa ve tren istasyona doğru geliyorsa
                if (dist < 300)
                {
                    // İstasyonu geçip geçmediğini kontrol etmeye gerek yok, 
                    // durma mantığı distance < 20 ile hallediliyor
                    return station;
                }
            }
            return null;
        }

        private void UpdateTrainCurrentBlock(TrainInfo train)
        {
            // Hangi blokta olduğunu bul
            double pos = train.CurrentPosition;
            // Blok uzunluğu yaklaşık 500m (Total / 31)
            double blockLen = TOTAL_TRACK_LENGTH / 31.0;

            int blockIndex = (int)(pos / blockLen);
            if (blockIndex < 0) blockIndex = 0;
            if (blockIndex > 30) blockIndex = 30;

            train.CurrentBlockId = blockIndex + 1;
        }

        private void UpdateBlockOccupancyForAllTrains()
        {
            foreach (var block in _blocks)
            {
                block.IsOccupied = false;
                block.OccupyingTrainId = 0;
                block.Status = "FREE";
            }

            foreach (var train in _activeTrains)
            {
                // Trenin olduğu blok ID'si
                int baseBlockId = train.CurrentBlockId;

                // TrackType'a göre gerçek blok ID (UP=1..31, DOWN=101..131)
                int realBlockId = train.TrackType == "UP" ? baseBlockId : baseBlockId + 100;

                var block = _blocks.FirstOrDefault(b => b.BlockId == realBlockId);
                if (block != null)
                {
                    block.IsOccupied = true;
                    block.OccupyingTrainId = train.TrainId;
                    block.Status = "OCCUPIED";
                }
            }
        }

        #endregion

        #region ÇİZİM SİSTEMİ

        private void DrawRailwaySystem()
        {
            try
            {
                RailwayCanvas.Children.Clear();
                if (_blocks.Count == 0 || _stations.Count == 0) return;

                double scaleFactor = (CANVAS_WIDTH - 100) / TOTAL_TRACK_LENGTH;

                DrawDoubleTrackSystem(scaleFactor);

                // Blokları çiz
                foreach (var block in _blocks) DrawBlock(block, scaleFactor);

                // İstasyonları çiz
                foreach (var station in _stations) DrawStation(station, scaleFactor);

                // Trenleri çiz
                foreach (var train in _activeTrains.ToList()) DrawTrain(train, scaleFactor);

                DrawGridAndScale(scaleFactor, TOTAL_TRACK_LENGTH);
                DrawTrackLabels();
            }
            catch (Exception) { }
        }

        private void DrawDoubleTrackSystem(double scaleFactor)
        {
            double scaledLength = TOTAL_TRACK_LENGTH * scaleFactor;

            // Üst Hat (Mavi)
            var upTrack = new Line
            {
                X1 = 50,
                Y1 = UP_TRACK_Y,
                X2 = 50 + scaledLength,
                Y2 = UP_TRACK_Y,
                Stroke = Brushes.Blue,
                StrokeThickness = 4
            };
            RailwayCanvas.Children.Add(upTrack);

            // Alt Hat (Kırmızı)
            var downTrack = new Line
            {
                X1 = 50,
                Y1 = DOWN_TRACK_Y,
                X2 = 50 + scaledLength,
                Y2 = DOWN_TRACK_Y,
                Stroke = Brushes.Red,
                StrokeThickness = 4
            };
            RailwayCanvas.Children.Add(downTrack);
        }

        private void DrawBlock(TrackBlock block, double scaleFactor)
        {
            // Blok çizimi
            double xPos = 50 + (block.StartPosition * scaleFactor);
            double width = (block.EndPosition - block.StartPosition) * scaleFactor;
            bool isUp = block.BlockId <= 31;
            double yPos = isUp ? UP_TRACK_Y : DOWN_TRACK_Y;

            var rect = new Rectangle
            {
                Width = width,
                Height = 10,
                Fill = block.IsOccupied ? Brushes.Red : Brushes.Transparent, // Doluysa kırmızı
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5,
                Opacity = 0.5
            };
            Canvas.SetLeft(rect, xPos);
            Canvas.SetTop(rect, yPos - 5);
            RailwayCanvas.Children.Add(rect);
        }

        private void DrawStation(StationInfo station, double scaleFactor)
        {
            double xPos = 50 + (station.GridX * scaleFactor);

            var rect = new Rectangle
            {
                Width = 10,
                Height = TRACK_SPACING + 20,
                Fill = Brushes.DarkGray,
                Opacity = 0.5
            };
            Canvas.SetLeft(rect, xPos - 5);
            Canvas.SetTop(rect, UP_TRACK_Y - 10);
            RailwayCanvas.Children.Add(rect);

            var text = new TextBlock
            {
                Text = station.StationName,
                FontSize = 8,
                Foreground = Brushes.Black,
                RenderTransform = new RotateTransform(-45)
            };
            Canvas.SetLeft(text, xPos - 10);
            Canvas.SetTop(text, UP_TRACK_Y - 30);
            RailwayCanvas.Children.Add(text);
        }

        private void DrawTrain(TrainInfo train, double scaleFactor)
        {
            double xPos = 50 + (train.CurrentPosition * scaleFactor);

            // Ekran dışındaysa çizme (Performans için)
            if (xPos < -50 || xPos > CANVAS_WIDTH + 50) return;

            bool isUp = train.TrackType == "UP";
            double yPos = isUp ? UP_TRACK_Y : DOWN_TRACK_Y;

            // 1. TREN GÖVDESİ (DİKDÖRTGEN)
            var rect = new Rectangle
            {
                Width = 40,
                Height = 16,
                // Duruyorsa Turuncu, hareketliyse Mavi/Kırmızı
                Fill = train.Status == "STOPPED" ? Brushes.Orange : (isUp ? Brushes.Blue : Brushes.Red),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2
            };

            Canvas.SetLeft(rect, xPos - 20); // Treni ortala
            Canvas.SetTop(rect, yPos - 8);
            RailwayCanvas.Children.Add(rect);

            // 2. YÖN OKU (TRENİN İÇİNDE)
            var arrow = new TextBlock
            {
                Text = isUp ? "◀" : "▶",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            // Ok konumunu ince ayar yap
            Canvas.SetLeft(arrow, xPos - 4);
            Canvas.SetTop(arrow, yPos - 7);
            RailwayCanvas.Children.Add(arrow);

            // 3. BİLGİ KUTUSU (İSİM + HIZ + KONUM)
            // Durum bilgisine göre metin hazırla
            string statusInfo = $"{train.TrainId}\n" +
                                $"{train.Speed:0} km/h\n" +
                                $"{train.CurrentPosition:0}m";

            var infoText = new TextBlock
            {
                Text = statusInfo,
                FontSize = 8,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                // Okunabilirlik için yarı saydam siyah arka plan
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(2)
            };

            // Bilgi kutusunu trenin üstüne ortalayarak yerleştir
            // Metin kutusunun genişliğini tahmini ortalamak için xPos'tan biraz sola kaydırıyoruz
            Canvas.SetLeft(infoText, xPos - 25);
            Canvas.SetTop(infoText, yPos - 45); // Trenin gövdesinin üstünde dursun

            // Z-Index vererek en üstte görünmesini sağla (isteğe bağlı ama iyidir)
            Panel.SetZIndex(infoText, 100);

            RailwayCanvas.Children.Add(infoText);
        }

        private void DrawGridAndScale(double scaleFactor, double totalLength)
        {
            // Her 1 km için çizgi
            for (int i = 0; i <= 15; i++)
            {
                double x = 50 + (i * 1000 * scaleFactor);
                var line = new Line
                {
                    X1 = x,
                    Y1 = UP_TRACK_Y - 20,
                    X2 = x,
                    Y2 = DOWN_TRACK_Y + 20,
                    Stroke = Brushes.LightGray,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                RailwayCanvas.Children.Add(line);

                var txt = new TextBlock { Text = $"{i}km", FontSize = 8, Foreground = Brushes.Gray };
                Canvas.SetLeft(txt, x + 2);
                Canvas.SetTop(txt, DOWN_TRACK_Y + 20);
                RailwayCanvas.Children.Add(txt);
            }
        }

        private void DrawTrackLabels()
        {
            var upLabel = new TextBlock { Text = "      DARICA YÖNÜ", Foreground = Brushes.Blue, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(upLabel, 60); Canvas.SetTop(upLabel, UP_TRACK_Y - 40);
            RailwayCanvas.Children.Add(upLabel);

            var downLabel = new TextBlock { Text = "        DEPO YÖNÜ", Foreground = Brushes.Red, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(downLabel, 60); Canvas.SetTop(downLabel, DOWN_TRACK_Y + 25);
            RailwayCanvas.Children.Add(downLabel);
        }

        #endregion

        #region TIMER EVENTS & UTILITIES

        private void ChartUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_vldSimulator.IsRunning && _vldDataCollection.Count > 0)
            {
                var latestData = _vldDataCollection.First();
                CurrentChart.AddValue(latestData.Current);
                VoltageChart.AddValue(latestData.VoltageOut);
                PowerChart.AddValue(latestData.ActivePower);
                TemperatureChart.AddValue(latestData.Temperature);
                THDChart.AddValue(latestData.THDVoltage);
                LastUpdateText.Content = $"Son Güncelleme: {DateTime.Now:HH:mm:ss}";
            }
        }

        private void RailwayUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateTrainPositions();
            DrawRailwaySystem();
        }

        private void UpdateButtonStates(bool isRunning)
        {
            StartAllButton.IsEnabled = !isRunning;
            StopAllButton.IsEnabled = isRunning;
            StartVLDButton.IsEnabled = !isRunning;
            StopVLDButton.IsEnabled = isRunning;
            StartSignalizationButton.IsEnabled = !isRunning;
            StopSignalizationButton.IsEnabled = isRunning;

            CurrentChart.IsPaused = !isRunning;
            VoltageChart.IsPaused = !isRunning;
            PowerChart.IsPaused = !isRunning;
            TemperatureChart.IsPaused = !isRunning;
            THDChart.IsPaused = !isRunning;
        }

        private void UpdateHeaderStatus(string status, Color color)
        {
            HeaderStatusText.Text = status;
            ((Border)HeaderStatusText.Parent).Background = new SolidColorBrush(color);
        }

        private void EmergencyStopAllSystems()
        {
            _vldSimulator.StopSimulation();
            _signalizationSimulator.StopSimulation();
            _railwayUpdateTimer.Stop();
            _chartUpdateTimer.Stop();

            foreach (var train in _activeTrains)
            {
                train.Status = "ACİL DURDURULDU";
                train.Speed = 0;
            }
            UpdateButtonStates(false);
            UpdateHeaderStatus("🔴 ACİL DUR", Colors.Red);
            ShowStatusMessage("🚨 ACİL DUR: Tüm sistemler durduruldu", StatusType.Emergency);
        }

        private void ShowStatusMessage(string message, StatusType type)
        {
            if (type == StatusType.Error || type == StatusType.Emergency)
                MessageBox.Show(message, type.ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _vldSimulator?.StopSimulation();
            _signalizationSimulator?.StopSimulation();
            _chartUpdateTimer?.Stop();
            _railwayUpdateTimer?.Stop();
            base.OnClosed(e);
        }
    }

    public enum StatusType { Info, Warning, Error, Emergency }
}