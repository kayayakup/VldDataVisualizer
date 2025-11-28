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

namespace VldDataVisualizer
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
        private const double UP_TRACK_Y = CANVAS_HEIGHT / 2 - TRACK_SPACING / 2; // Darıca Sahil yönü (Üst hat)
        private const double DOWN_TRACK_Y = CANVAS_HEIGHT / 2 + TRACK_SPACING / 2; // Depo yönü (Alt hat)

        private const int MAX_TOTAL_TRAINS = 7;
        private const double TRAIN_LENGTH = 88; // Modeldeki değerle eşleşiyor
        private const double MIN_TRAIN_DISTANCE = 500;
        private const double STATION_STOP_TIME = 30; // 30 saniye istasyonda durma süresi
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

            _vldSimulator.DataGenerated += OnVLDDataGenerated;
            _vldSimulator.StatusChanged += OnVLDStatusChanged;
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
            // ÜST HAT (UP) - Depo'dan başlayıp Darıca Sahil'e gidiyor (pozisyon azalır)
            AddTrainToTrack("UP", 15000);  // Depo yakınından başla
            AddTrainToTrack("UP", 12000);  // Ortada

            // ALT HAT (DOWN) - Darıca Sahil'den başlayıp Depo'ya gidiyor (pozisyon artar)
            AddTrainToTrack("DOWN", 1000);   // Darıca Sahil yakınından başla
            AddTrainToTrack("DOWN", 4000);   // Ortada
        }

        private void InitializeTimers()
        {
            _chartUpdateTimer = new DispatcherTimer();
            _chartUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;
            _chartUpdateTimer.Start();

            _railwayUpdateTimer = new DispatcherTimer();
            _railwayUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
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
            double totalTrackLength = 15391.246;
            int blockCount = 31;
            double blockLength = totalTrackLength / blockCount;

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
            var stationPositions = new double[]
            {
                15391.246, 13873.215, 12081.341, 10385.942, 9070.108,
                8234.420, 7101.599, 5781.120, 4389.210, 3298.624,
                1379.242, 136.100
            };

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
            double totalTrackLength = 15391.246;

            _routes.Add(new RouteInfo
            {
                RouteId = 1,
                RouteName = "Depo → Darıca Sahil",
                BlockSequence = Enumerable.Range(1, 31).ToList(),
                TotalRouteLength = totalTrackLength,
                ActiveTrainCount = 0,
                RouteStatus = "ACTIVE"
            });

            _routes.Add(new RouteInfo
            {
                RouteId = 2,
                RouteName = "Darıca Sahil → Depo",
                BlockSequence = Enumerable.Range(101, 31).ToList(),
                TotalRouteLength = totalTrackLength,
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
                DrawRailwaySystem();
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
                "🚨 ACİL DURUM 🚨\n\nTüm sistemleri acil durdurma moduna almak istiyor musunuz?\n\n" +
                "Bu işlem:\n• Tüm trenleri durduracak\n• TFPR sistemini kapatacak\n• Tüm alarmları aktif edecek",
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
            try
            {
                _signalizationSimulator.StartSimulation();
                StartSignalizationButton.IsEnabled = false;
                StopSignalizationButton.IsEnabled = true;
                ShowStatusMessage("Sinyalizasyon simülasyonu başlatıldı", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Sinyalizasyon başlatma hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void StopSignalizationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _signalizationSimulator.StopSimulation();
                StartSignalizationButton.IsEnabled = true;
                StopSignalizationButton.IsEnabled = false;
                _railwayUpdateTimer.Stop();
                ShowStatusMessage("Sinyalizasyon simülasyonu durduruldu", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Sinyalizasyon durdurma hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void AddTrainButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_activeTrains.Count >= MAX_TOTAL_TRAINS)
                {
                    ShowStatusMessage("Maksimum tren sayısına ulaşıldı (7 tren)", StatusType.Warning);
                    return;
                }

                int upTrackCount = _activeTrains.Count(t => t.TrackType == "UP" && t.Direction == "NORTHBOUND");
                int downTrackCount = _activeTrains.Count(t => t.TrackType == "DOWN" && t.Direction == "SOUTHBOUND");

                string trackType = upTrackCount <= downTrackCount ? "UP" : "DOWN";
                double startPosition = GetSafeStartPosition(trackType);

                AddTrainToTrack(trackType, startPosition);

                string trackName = trackType == "UP" ?
                    "Üst Hat (Depo → Darıca Sahil, NORTHBOUND)" :
                    "Alt Hat (Darıca Sahil → Depo, SOUTHBOUND)";
                ShowStatusMessage($"Yeni tren {trackName} eklendi", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Tren ekleme hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void AddTrainToTrack(string trackType, double startPosition)
        {
            // HAT VE YÖN TUTARLILIĞI:
            // UP Track (Üst Hat) = Depo → Darıca Sahil = Pozisyon azalır = NORTHBOUND
            // DOWN Track (Alt Hat) = Darıca Sahil → Depo = Pozisyon artar = SOUTHBOUND

            string direction = trackType == "UP" ? "NORTHBOUND" : "SOUTHBOUND";
            int startBlockId = trackType == "UP" ? 1 : 101;
            int gridY = trackType == "UP" ? (int)UP_TRACK_Y : (int)DOWN_TRACK_Y;

            var newTrain = new TrainInfo
            {
                TrainNumber = _activeTrains.Count + 1,
                TrainId = _activeTrains.Count + 100,
                TrainName = $"Tren_{_activeTrains.Count + 1:00}",
                Speed = 50 + _random.Next(0, 20),
                CurrentBlockId = startBlockId,
                CurrentPosition = startPosition,
                PositionInBlock = 0,
                Status = "MOVING",
                IsInService = true,
                PassengerCount = _random.Next(50, 200),
                LastUpdateTime = DateTime.Now,
                GridX = (int)(startPosition / 8),
                GridY = gridY,
                Direction = direction,  // ✓ DOĞRU: TrackType ile uyumlu
                Heading = trackType == "UP" ? 180 : 0,
                TrainLength = TRAIN_LENGTH,
                TrackType = trackType  // ✓ DOĞRU: Direction ile uyumlu
            };

            _activeTrains.Add(newTrain);
            UpdateBlockOccupancyForAllTrains();
        }

        private double GetSafeStartPosition(string trackType)
        {
            var sameTrackTrains = _activeTrains.Where(t => t.TrackType == trackType).ToList();

            if (!sameTrackTrains.Any())
            {
                return trackType == "UP" ? 15391.246 : 0;
            }

            if (trackType == "UP")
            {
                double minPosition = sameTrackTrains.Min(t => t.CurrentPosition);
                return Math.Max(0, minPosition - MIN_TRAIN_DISTANCE);
            }
            else
            {
                double maxPosition = sameTrackTrains.Max(t => t.CurrentPosition);
                return Math.Min(15391.246, maxPosition + MIN_TRAIN_DISTANCE);
            }
        }

        private void RemoveTrainButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_activeTrains.Count > 0)
                {
                    var lastTrain = _activeTrains.Last();
                    _activeTrains.Remove(lastTrain);
                    UpdateBlockOccupancyForAllTrains();
                    ShowStatusMessage($"{lastTrain.TrainName} kaldırıldı (Kalan: {_activeTrains.Count}/7)", StatusType.Warning);
                }
                else
                {
                    ShowStatusMessage("Kaldırılacak tren bulunamadı", StatusType.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Tren kaldırma hatası: {ex.Message}", StatusType.Error);
            }
        }

        // Manuel kontrol butonları
        private void AddUpTrackTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count >= MAX_TOTAL_TRAINS) return;
            double startPos = GetSafeStartPosition("UP");
            AddTrainToTrack("UP", startPos);
            ShowStatusMessage("Üst hatta tren eklendi (Depo → Darıca Sahil yönü)", StatusType.Info);
        }

        private void AddDownTrackTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count >= MAX_TOTAL_TRAINS) return;
            double startPos = GetSafeStartPosition("DOWN");
            AddTrainToTrack("DOWN", startPos);
            ShowStatusMessage("Alt hatta tren eklendi (Darıca Sahil → Depo yönü)", StatusType.Info);
        }

        private void ResetTrainsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _activeTrains.Clear();
                AddInitialTrains();
                ShowStatusMessage("Trenler sıfırlandı", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Reset hatası: {ex.Message}", StatusType.Error);
            }
        }

        // TFPR Butonları
        private void StartVLDButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.StartSimulation();
                StartVLDButton.IsEnabled = false;
                StopVLDButton.IsEnabled = true;
                _chartUpdateTimer.Start();
                ShowStatusMessage("VLD-TFPR simülasyonu başlatıldı", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"VLD başlatma hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void StopVLDButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.StopSimulation();
                StartVLDButton.IsEnabled = true;
                StopVLDButton.IsEnabled = false;
                _chartUpdateTimer.Stop();
                ShowStatusMessage("VLD-TFPR simülasyonu durduruldu", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"VLD durdurma hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void VoltageSagButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.SimulateVoltageSag();
                ShowStatusMessage("Gerilim düşüşü senaryosu aktif", StatusType.Warning);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Gerilim düşüşü senaryosu hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void OverloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.SimulateOverload();
                ShowStatusMessage("Aşırı yük senaryosu aktif", StatusType.Warning);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Aşırı yük senaryosu hatası: {ex.Message}", StatusType.Error);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.ResetToNormal();
                ShowStatusMessage("Normal çalışma moduna dönüldü", StatusType.Info);
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Reset hatası: {ex.Message}", StatusType.Error);
            }
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
                UpdateSignalizationRealTimeValues(data);
                TrainCountText.Content = $"Aktif Tren: {data.TotalActiveTrains}";
                BlockCountText.Content = $"Aktif Blok: {data.TrackBlocks.Count(b => b.IsOccupied)}";
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

            if (data.Status == "ALARM" && data.ActiveAlarms.Count > 0)
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
        }

        private void UpdateSignalizationRealTimeValues(SignalizationData data)
        {
            try
            {
                TotalTrainsText.Text = $"🚆 Aktif Tren: {data.TotalActiveTrains}";
                SystemStatusText.Text = $"📡 Sistem: {data.SystemStatus}";

                SignalCommunicationText.Text = $"📶 İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
                SignalCommunicationBorder.Background = data.IsCommunicationActive ?
                    new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

                double avgSpeed = data.ActiveTrains.Any() ? data.ActiveTrains.Average(t => t.Speed) : 0;
                AvgSpeedText.Text = $"⚡ Ort. Hız: {avgSpeed:0} km/s";

                int totalPassengers = data.ActiveTrains.Sum(t => t.PassengerCount) + data.Stations.Sum(s => s.WaitingPassengers);
                TotalPassengersText.Text = $"👥 Toplam Yolcu: {totalPassengers}";

                UpdateTrainsCollection(data.ActiveTrains);
                UpdateBlocksOccupancy(data.TrackBlocks, data.ActiveTrains);
                UpdateRoutesTrainCounts(data.ActiveRoutes, data.ActiveTrains);
                UpdateStationsDynamicData(data.Stations);

                CanvasInfoText.Text = $"İstasyonlar: {_stations.Count} | Bloklar: {_blocks.Count} | Trenler: {_activeTrains.Count}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateSignalizationRealTimeValues hatası: {ex.Message}");
            }
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
                    existingStation.ArrivingTrains.Clear();
                    foreach (var arrivingTrain in newStation.ArrivingTrains)
                    {
                        existingStation.ArrivingTrains.Add(arrivingTrain);
                    }
                }
            }
        }

        private void UpdateBlocksOccupancy(List<TrackBlock> newBlocks, List<TrainInfo> activeTrains)
        {
            foreach (var block in _blocks)
            {
                // Blok doluluk kontrolü - TrackType ve Direction ile tutarlı
                var isOccupied = activeTrains.Any(train =>
                {
                    // UP Track (1-31): NORTHBOUND trenler için
                    bool isUpTrackMatch = block.BlockId <= 31 &&
                                          train.TrackType == "UP" &&
                                          train.Direction == "NORTHBOUND" &&
                                          train.CurrentBlockId == block.BlockId;

                    // DOWN Track (101-131): SOUTHBOUND trenler için
                    bool isDownTrackMatch = block.BlockId >= 101 &&
                                            train.TrackType == "DOWN" &&
                                            train.Direction == "SOUTHBOUND" &&
                                            train.CurrentBlockId == (block.BlockId - 100);

                    return isUpTrackMatch || isDownTrackMatch;
                });

                block.IsOccupied = isOccupied;
                block.Status = isOccupied ? "OCCUPIED" : "FREE";
                block.OccupyingTrainId = isOccupied ?
                    activeTrains.FirstOrDefault(train =>
                    {
                        bool isUpTrackMatch = block.BlockId <= 31 &&
                                              train.TrackType == "UP" &&
                                              train.Direction == "NORTHBOUND" &&
                                              train.CurrentBlockId == block.BlockId;

                        bool isDownTrackMatch = block.BlockId >= 101 &&
                                                train.TrackType == "DOWN" &&
                                                train.Direction == "SOUTHBOUND" &&
                                                train.CurrentBlockId == (block.BlockId - 100);

                        return isUpTrackMatch || isDownTrackMatch;
                    })?.TrainId ?? 0 : 0;
            }
        }

        private void UpdateRoutesTrainCounts(List<RouteInfo> newRoutes, List<TrainInfo> activeTrains)
        {
            foreach (var route in _routes)
            {
                route.ActiveTrainCount = activeTrains.Count(t =>
                    route.BlockSequence.Contains(t.CurrentBlockId) ||
                    route.BlockSequence.Contains(t.CurrentBlockId + 100));
            }
        }

        private void UpdateTrainsCollection(List<TrainInfo> newTrains)
        {
            foreach (var newTrain in newTrains)
            {
                var existingTrain = _activeTrains.FirstOrDefault(t => t.TrainId == newTrain.TrainId);
                if (existingTrain != null)
                {
                    UpdateTrainProperties(existingTrain, newTrain);
                }
                else
                {
                    _activeTrains.Add(newTrain);
                }
            }

            var trainsToRemove = _activeTrains.Where(t => !newTrains.Any(nt => nt.TrainId == t.TrainId)).ToList();
            foreach (var train in trainsToRemove)
            {
                _activeTrains.Remove(train);
            }
        }

        private void UpdateTrainProperties(TrainInfo existing, TrainInfo updated)
        {
            existing.TrainNumber = updated.TrainNumber;
            existing.TrainName = updated.TrainName;
            existing.Speed = updated.Speed;
            existing.CurrentBlockId = updated.CurrentBlockId;
            existing.CurrentPosition = updated.CurrentPosition;
            existing.PositionInBlock = updated.PositionInBlock;
            existing.NextStationId = updated.NextStationId;
            existing.NextStationName = updated.NextStationName;
            existing.DistanceToNextStation = updated.DistanceToNextStation;
            existing.Status = updated.Status;
            existing.IsInService = updated.IsInService;
            existing.PassengerCount = updated.PassengerCount;
            existing.LastUpdateTime = updated.LastUpdateTime;
            existing.GridX = updated.GridX;
            existing.GridY = updated.GridY;
            existing.Direction = updated.Direction;
            existing.Heading = updated.Heading;
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

        #region TREN HAREKET SİSTEMİ - MODEL UYUMLU

        // Yardımcı sınıf için tren durum yönetimi
        private class TrainState
        {
            public double StopTimer { get; set; }
            public bool IsStoppedAtStation { get; set; }
            public int CurrentStationId { get; set; }
        }

        private Dictionary<int, TrainState> _trainStates = new Dictionary<int, TrainState>();

        private void UpdateTrainPositions()
        {
            try
            {
                List<TrainInfo> trainsToRemove = new List<TrainInfo>();

                foreach (var train in _activeTrains.ToList())
                {
                    // TrainState yönetimi
                    if (!_trainStates.ContainsKey(train.TrainId))
                    {
                        _trainStates[train.TrainId] = new TrainState();
                    }
                    var trainState = _trainStates[train.TrainId];

                    // İstasyonda durma kontrolü
                    if (trainState.IsStoppedAtStation)
                    {
                        trainState.StopTimer += 0.5; // 500ms ekle
                        train.Status = "STOPPED";
                        train.Speed = 0;

                        if (trainState.StopTimer >= STATION_STOP_TIME)
                        {
                            // 30 saniye doldu, hareket et
                            trainState.IsStoppedAtStation = false;
                            trainState.StopTimer = 0;
                            train.Status = "MOVING";
                            train.Speed = 50 + _random.Next(0, 20); // Normal hıza dön
                        }
                        continue; // Durdurulmuş treni hareket ettirme
                    }

                    // Öndeki tren kontrolü
                    if (IsTrainAhead(train))
                    {
                        train.Status = "WAITING";
                        train.Speed = 0;
                        continue; // Önde tren varsa hareket etme
                    }

                    // İstasyon yaklaşma kontrolü
                    var approachingStation = GetApproachingStation(train);
                    if (approachingStation != null && train.Status == "MOVING")
                    {
                        double distanceToStation = Math.Abs(train.CurrentPosition - approachingStation.GridX);
                        if (distanceToStation < 100) // 100 metre kala yavaşla
                        {
                            train.Speed = Math.Max(10, train.Speed - 5);

                            if (distanceToStation < 10) // İstasyona ulaştı
                            {
                                train.Status = "STOPPED";
                                train.Speed = 0;
                                trainState.IsStoppedAtStation = true;
                                trainState.StopTimer = 0;
                                trainState.CurrentStationId = approachingStation.StationId;

                                // Yolcu iniş-biniş simülasyonu
                                train.PassengerCount = _random.Next(50, 200);
                                approachingStation.WaitingPassengers = _random.Next(50, 200);

                                // İstasyona varan tren bilgisi güncelleme
                                UpdateStationArrivingTrain(approachingStation, train);

                                continue;
                            }
                        }
                    }

                    // Normal hareket
                    if (train.Status == "MOVING")
                    {
                        double movement = (train.Speed / 3.6) * 0.5; // 500ms için

                        if (train.TrackType == "UP")
                        {
                            train.CurrentPosition -= movement;
                            if (train.CurrentPosition <= 0)
                            {
                                train.CurrentPosition = 0;
                                trainsToRemove.Add(train);
                            }
                        }
                        else
                        {
                            train.CurrentPosition += movement;
                            if (train.CurrentPosition >= 15391.246)
                            {
                                train.CurrentPosition = 15391.246;
                                trainsToRemove.Add(train);
                            }
                        }

                        train.GridX = (int)(train.CurrentPosition / 8);
                        UpdateTrainCurrentBlock(train);

                        // PositionInBlock güncelleme
                        var currentBlock = _blocks.FirstOrDefault(b => b.BlockId == train.CurrentBlockId);
                        if (currentBlock != null)
                        {
                            train.PositionInBlock = train.CurrentPosition - currentBlock.StartPosition;
                        }
                    }
                }

                foreach (var train in trainsToRemove)
                {
                    _activeTrains.Remove(train);
                    _trainStates.Remove(train.TrainId);
                }

                UpdateBlockOccupancyForAllTrains();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateTrainPositions hatası: {ex.Message}");
            }
        }

        private void UpdateStationArrivingTrain(StationInfo station, TrainInfo train)
        {
            // Mevcut varış bilgilerini temizle
            station.ArrivingTrains.Clear();

            // Yeni varış bilgisi ekle
            station.ArrivingTrains.Add(new ArrivingTrain
            {
                TrainId = train.TrainId,
                TrainName = train.TrainName,
                ArrivalMinutes = 0, // İstasyonda
                Destination = train.TrackType == "UP" ? "Darıca Sahil" : "Depo",
                WillStop = true,
                CurrentDistance = 0
            });
        }

        private bool IsTrainAhead(TrainInfo currentTrain)
        {
            var sameTrackTrains = _activeTrains
                .Where(t => t.TrackType == currentTrain.TrackType && t.TrainId != currentTrain.TrainId)
                .ToList();

            if (!sameTrackTrains.Any()) return false;

            if (currentTrain.TrackType == "UP")
            {
                // Üst hat: öndeki tren daha küçük pozisyonda
                var aheadTrains = sameTrackTrains
                    .Where(t => t.CurrentPosition < currentTrain.CurrentPosition)
                    .OrderBy(t => t.CurrentPosition)
                    .ToList();

                if (aheadTrains.Any())
                {
                    var closestTrain = aheadTrains.First();
                    double distance = currentTrain.CurrentPosition - closestTrain.CurrentPosition;
                    return distance < MIN_TRAIN_DISTANCE;
                }
            }
            else
            {
                // Alt hat: öndeki tren daha büyük pozisyonda
                var aheadTrains = sameTrackTrains
                    .Where(t => t.CurrentPosition > currentTrain.CurrentPosition)
                    .OrderBy(t => t.CurrentPosition)
                    .ToList();

                if (aheadTrains.Any())
                {
                    var closestTrain = aheadTrains.First();
                    double distance = closestTrain.CurrentPosition - currentTrain.CurrentPosition;
                    return distance < MIN_TRAIN_DISTANCE;
                }
            }

            return false;
        }

        private StationInfo GetApproachingStation(TrainInfo train)
        {
            if (train.TrackType == "UP")
            {
                // Üst hat: pozisyon azalırken istasyonları kontrol et
                return _stations
                    .Where(s => s.GridX < train.CurrentPosition && s.GridX >= train.CurrentPosition - 200)
                    .OrderByDescending(s => s.GridX)
                    .FirstOrDefault();
            }
            else
            {
                // Alt hat: pozisyon artarken istasyonları kontrol et
                return _stations
                    .Where(s => s.GridX > train.CurrentPosition && s.GridX <= train.CurrentPosition + 200)
                    .OrderBy(s => s.GridX)
                    .FirstOrDefault();
            }
        }

        private void UpdateTrainCurrentBlock(TrainInfo train)
        {
            try
            {
                // TrackType'a göre doğru blok aralığını seç
                bool isUpTrack = train.TrackType == "UP" && train.Direction == "NORTHBOUND";
                bool isDownTrack = train.TrackType == "DOWN" && train.Direction == "SOUTHBOUND";

                int startBlock = isUpTrack ? 1 : 101;
                int endBlock = isUpTrack ? 31 : 131;

                for (int blockId = startBlock; blockId <= endBlock; blockId++)
                {
                    var block = _blocks.FirstOrDefault(b => b.BlockId == blockId);
                    if (block != null &&
                        train.CurrentPosition >= block.StartPosition &&
                        train.CurrentPosition <= block.EndPosition)
                    {
                        // UP Track için BlockId: 1-31
                        // DOWN Track için BlockId: blockId (101-131) ama train için 1-31 olarak sakla
                        train.CurrentBlockId = isDownTrack ? (blockId - 100) : blockId;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateTrainCurrentBlock hatası: {ex.Message}");
            }
        }

        private void UpdateBlockOccupancyForAllTrains()
        {
            try
            {
                foreach (var block in _blocks)
                {
                    block.IsOccupied = false;
                    block.OccupyingTrainId = 0;
                    block.Status = "FREE";
                }

                foreach (var train in _activeTrains)
                {
                    var currentBlock = _blocks.FirstOrDefault(b => b.BlockId == train.CurrentBlockId);
                    if (currentBlock != null)
                    {
                        currentBlock.IsOccupied = true;
                        currentBlock.OccupyingTrainId = train.TrainId;
                        currentBlock.Status = "OCCUPIED";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateBlockOccupancyForAllTrains hatası: {ex.Message}");
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

                double totalTrackLength = 15391.246;
                double scaleFactor = (CANVAS_WIDTH - 100) / totalTrackLength;

                DrawDoubleTrackSystem(scaleFactor);

                foreach (var block in _blocks)
                {
                    DrawBlock(block, scaleFactor);
                }

                foreach (var station in _stations)
                {
                    DrawStation(station, scaleFactor);
                }

                foreach (var train in _activeTrains)
                {
                    DrawTrain(train, scaleFactor);
                }

                DrawGridAndScale(scaleFactor, totalTrackLength);
                DrawTrackLabels();
                DrawDebugInfo(scaleFactor);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DrawRailwaySystem hatası: {ex.Message}");
            }
        }

        private void DrawDoubleTrackSystem(double scaleFactor)
        {
            double totalTrackLength = 15391.246;
            double scaledLength = totalTrackLength * scaleFactor;

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
            double xPosition = 50 + (block.StartPosition * scaleFactor);
            bool isUpTrack = block.BlockId <= 31;
            double trackY = isUpTrack ? UP_TRACK_Y : DOWN_TRACK_Y;
            Brush trackColor = isUpTrack ? Brushes.Blue : Brushes.Red;

            double blockWidth = Math.Max(10, block.BlockLength * scaleFactor);

            var blockRect = new Rectangle
            {
                Width = blockWidth,
                Height = 12,
                Fill = block.IsOccupied ? Brushes.LightCoral : Brushes.LightGreen,
                Stroke = trackColor,
                StrokeThickness = 1,
                Opacity = 0.7
            };

            Canvas.SetLeft(blockRect, xPosition);
            Canvas.SetTop(blockRect, trackY - 6);

            var blockText = new TextBlock
            {
                Text = block.BlockName.Replace("-UP", "").Replace("-DOWN", ""),
                FontSize = 7,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold
            };

            Canvas.SetLeft(blockText, xPosition + 2);
            Canvas.SetTop(blockText, trackY - 20);

            RailwayCanvas.Children.Add(blockRect);
            RailwayCanvas.Children.Add(blockText);
        }

        private void DrawStation(StationInfo station, double scaleFactor)
        {
            try
            {
                double xPosition = 50 + (station.GridX * scaleFactor);

                var stationRect = new Rectangle
                {
                    Width = STATION_WIDTH,
                    Height = TRACK_SPACING + 20,
                    Fill = Brushes.LightBlue,
                    Stroke = Brushes.DarkBlue,
                    StrokeThickness = 2,
                    Opacity = 0.8
                };

                Canvas.SetLeft(stationRect, xPosition - STATION_WIDTH / 2);
                Canvas.SetTop(stationRect, UP_TRACK_Y - 10);

                var stationText = new TextBlock
                {
                    Text = $"{station.StationName}\n({station.GridX}m)",
                    FontSize = 7,
                    Foreground = Brushes.DarkBlue,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Width = STATION_WIDTH,
                    TextAlignment = TextAlignment.Center,
                    Background = Brushes.White
                };

                Canvas.SetLeft(stationText, xPosition - STATION_WIDTH / 2);
                Canvas.SetTop(stationText, UP_TRACK_Y - 30);

                RailwayCanvas.Children.Add(stationRect);
                RailwayCanvas.Children.Add(stationText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DrawStation hatası ({station.StationName}): {ex.Message}");
            }
        }

        private void DrawTrain(TrainInfo train, double scaleFactor)
        {
            try
            {
                bool isUpTrack = train.TrackType == "UP";
                double trackY = isUpTrack ? UP_TRACK_Y : DOWN_TRACK_Y;
                Brush trainColor = isUpTrack ? Brushes.Blue : Brushes.Red;

                double trainWidth = TRAIN_WIDTH * 3;
                double xPosition = 50 + (train.CurrentPosition * scaleFactor);

                if (xPosition < -100 || xPosition > CANVAS_WIDTH + 100) return;

                // Tren durumuna göre renk
                Brush fillColor = train.Status == "MOVING" ? trainColor :
                                 train.Status == "STOPPED" ? Brushes.Orange :
                                 train.Status == "WAITING" ? Brushes.Yellow :
                                 trainColor;

                var trainRect = new Rectangle
                {
                    Width = trainWidth,
                    Height = 20,
                    Fill = fillColor,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Opacity = 1.0
                };

                Canvas.SetLeft(trainRect, xPosition);
                Canvas.SetTop(trainRect, trackY - 10);

                string directionArrow = isUpTrack ? "◀" : "▶";
                var arrowText = new TextBlock
                {
                    Text = directionArrow,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold
                };

                Canvas.SetLeft(arrowText, xPosition + (isUpTrack ? 5 : trainWidth - 20));
                Canvas.SetTop(arrowText, trackY - 12);

                // Durum bilgisi
                string statusText = train.Status;
                if (train.Status == "STOPPED" && _trainStates.ContainsKey(train.TrainId))
                {
                    var trainState = _trainStates[train.TrainId];
                    double remainingTime = STATION_STOP_TIME - trainState.StopTimer;
                    statusText = $"DURDU\n{remainingTime:0}s";
                }

                var trainInfo = new TextBlock
                {
                    Text = $"{train.TrainName}\n{train.Speed:0} km/s\n{statusText}",
                    FontSize = 7,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    TextAlignment = TextAlignment.Center,
                    Width = trainWidth
                };

                Canvas.SetLeft(trainInfo, xPosition);
                Canvas.SetTop(trainInfo, trackY - 35);

                RailwayCanvas.Children.Add(trainRect);
                RailwayCanvas.Children.Add(arrowText);
                RailwayCanvas.Children.Add(trainInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DrawTrain hatası: {ex.Message}");
            }
        }

        private void DrawGridAndScale(double scaleFactor, double totalTrackLength)
        {
            for (int km = 0; km <= 16; km++)
            {
                double kmPosition = km * 1000;
                if (kmPosition > totalTrackLength) break;

                double xPosition = 50 + (kmPosition * scaleFactor);

                var kmLine = new Line
                {
                    X1 = xPosition,
                    Y1 = UP_TRACK_Y - 15,
                    X2 = xPosition,
                    Y2 = DOWN_TRACK_Y + 15,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5
                };

                var kmText = new TextBlock
                {
                    Text = $"{km}km",
                    FontSize = 7,
                    Foreground = Brushes.Gray,
                    Background = Brushes.White
                };

                Canvas.SetLeft(kmText, xPosition - 8);
                Canvas.SetTop(kmText, DOWN_TRACK_Y + 20);

                RailwayCanvas.Children.Add(kmLine);
                RailwayCanvas.Children.Add(kmText);
            }
        }

        private void DrawTrackLabels()
        {
            var upLabel = new TextBlock
            {
                Text = "▲ ÜST HAT (UP): Depo → Darıca Sahil (NORTHBOUND)",
                FontSize = 9,
                Foreground = Brushes.Blue,
                FontWeight = FontWeights.Bold,
                Background = Brushes.White
            };

            Canvas.SetLeft(upLabel, 60);
            Canvas.SetTop(upLabel, UP_TRACK_Y - 50);

            var downLabel = new TextBlock
            {
                Text = "▼ ALT HAT (DOWN): Darıca Sahil → Depo (SOUTHBOUND)",
                FontSize = 9,
                Foreground = Brushes.Red,
                FontWeight = FontWeights.Bold,
                Background = Brushes.White
            };

            Canvas.SetLeft(downLabel, 60);
            Canvas.SetTop(downLabel, DOWN_TRACK_Y + 35);

            RailwayCanvas.Children.Add(upLabel);
            RailwayCanvas.Children.Add(downLabel);
        }

        private void DrawDebugInfo(double scaleFactor)
        {
            int upTrains = _activeTrains.Count(t => t.TrackType == "UP" && t.Direction == "NORTHBOUND");
            int downTrains = _activeTrains.Count(t => t.TrackType == "DOWN" && t.Direction == "SOUTHBOUND");
            int movingTrains = _activeTrains.Count(t => t.Status == "MOVING");
            int stoppedTrains = _activeTrains.Count(t => t.Status == "STOPPED");
            int waitingTrains = _activeTrains.Count(t => t.Status == "WAITING");

            var debugText = new TextBlock
            {
                Text = $"🔵 ÜST HAT (Depo→Sahil): {upTrains} tren\n" +
                       $"🔴 ALT HAT (Sahil→Depo): {downTrains} tren\n" +
                       $"🚆 Hareket: {movingTrains}\n" +
                       $"🟠 Durdu: {stoppedTrains}\n" +
                       $"🟡 Bekliyor: {waitingTrains}",
                FontSize = 9,
                Foreground = Brushes.DarkBlue,
                Background = Brushes.LightCyan,
                FontWeight = FontWeights.Bold,
                Width = 200,
                TextAlignment = TextAlignment.Left
            };

            Canvas.SetLeft(debugText, CANVAS_WIDTH - 210);
            Canvas.SetTop(debugText, 10);
            RailwayCanvas.Children.Add(debugText);
        }

        #endregion

        #region TIMER EVENTS & UTILITIES

        private void ChartUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_vldSimulator.IsRunning)
            {
                UpdateCharts();
                LastUpdateText.Content = $"Son Güncelleme: {DateTime.Now:HH:mm:ss}";
            }
        }

        private void RailwayUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                UpdateTrainPositions();
                DrawRailwaySystem();
                UpdateDebugInfo();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RailwayUpdateTimer_Tick hatası: {ex.Message}");
            }
        }

        private void UpdateDebugInfo()
        {
            int upTrains = _activeTrains.Count(t => t.TrackType == "UP" && t.Direction == "NORTHBOUND");
            int downTrains = _activeTrains.Count(t => t.TrackType == "DOWN" && t.Direction == "SOUTHBOUND");
            int movingTrains = _activeTrains.Count(t => t.Status == "MOVING");
            int stoppedTrains = _activeTrains.Count(t => t.Status == "STOPPED");
            int waitingTrains = _activeTrains.Count(t => t.Status == "WAITING");

            CanvasInfoText.Text = $"Trenler: {_activeTrains.Count}/7 | " +
                                 $"Üst(Depo→Sahil): {upTrains} | Alt(Sahil→Depo): {downTrains} | " +
                                 $"Hareket: {movingTrains} | Durdu: {stoppedTrains} | Bekliyor: {waitingTrains}";
        }

        private void UpdateCharts()
        {
            if (_vldDataCollection.Count == 0 || !_vldSimulator.IsRunning)
                return;

            var latestData = _vldDataCollection.First();
            CurrentChart.AddValue(latestData.Current);
            VoltageChart.AddValue(latestData.VoltageOut);
            PowerChart.AddValue(latestData.ActivePower);
            TemperatureChart.AddValue(latestData.Temperature);
            THDChart.AddValue(latestData.THDVoltage);
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
            System.Media.SystemSounds.Hand.Play();
        }

        private void ShowStatusMessage(string message, StatusType type)
        {
            var caption = type switch
            {
                StatusType.Info => "Bilgi",
                StatusType.Warning => "Uyarı",
                StatusType.Error => "Hata",
                StatusType.Emergency => "ACİL DURUM",
                _ => "Bilgi"
            };

            var icon = type switch
            {
                StatusType.Info => MessageBoxImage.Information,
                StatusType.Warning => MessageBoxImage.Warning,
                StatusType.Error => MessageBoxImage.Error,
                StatusType.Emergency => MessageBoxImage.Stop,
                _ => MessageBoxImage.Information
            };

            if (type == StatusType.Error || type == StatusType.Emergency)
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, icon);
            }
        }

        private string GetStatusIcon(StatusType type)
        {
            return type switch
            {
                StatusType.Info => "🔵",
                StatusType.Warning => "🟡",
                StatusType.Error => "🔴",
                StatusType.Emergency => "🚨",
                _ => "🔵"
            };
        }

        #endregion

        #region WINDOW EVENTS

        protected override void OnClosed(EventArgs e)
        {
            _vldSimulator?.StopSimulation();
            _signalizationSimulator?.StopSimulation();
            _chartUpdateTimer?.Stop();
            _railwayUpdateTimer?.Stop();
            base.OnClosed(e);
        }

        #endregion
    }

    public enum StatusType
    {
        Info,
        Warning,
        Error,
        Emergency
    }
}