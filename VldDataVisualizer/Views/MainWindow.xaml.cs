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
using System.Windows.Shapes;

namespace VldDataVisualizer.Views
{
    public partial class MainWindow : Window
    {
        private VLDSimulator _vldSimulator;
        private SignalizationSimulator _signalizationSimulator;

        // 12 cihaz için ayrı veri koleksiyonları
        private Dictionary<int, ObservableCollection<VldData>> _deviceDataCollections;
        private Dictionary<int, List<string>> _deviceAlarms;

        // Collections
        private ObservableCollection<TrainInfo> _activeTrains;
        private ObservableCollection<StationInfo> _stations;
        private ObservableCollection<TrackBlock> _blocks;
        private ObservableCollection<RouteInfo> _routes;

        // Timers
        private DispatcherTimer _chartUpdateTimer;
        private DispatcherTimer _railwayUpdateTimer;
        private DispatcherTimer _detailUpdateTimer;

        // Railway drawing constants
        private const double CANVAS_HEIGHT = 400;
        private const double CANVAS_WIDTH = 2000;
        private const double TRACK_SPACING = 60;
        private const double UP_TRACK_Y = CANVAS_HEIGHT / 2 - TRACK_SPACING / 2;
        private const double DOWN_TRACK_Y = CANVAS_HEIGHT / 2 + TRACK_SPACING / 2;
        private const double TOTAL_TRACK_LENGTH = 15391.246;
        private const int MAX_TOTAL_TRAINS = 7;
        private const double TRAIN_LENGTH = 88;
        private const double MIN_TRAIN_DISTANCE = 500;
        private const double STATION_STOP_TIME = 30;
        private Random _random = new Random();

        // Chart referansları
        private List<ChartsProperties> _allCharts;
        private int _selectedStationId = 1;

        public MainWindow()
        {
            InitializeComponent();
            InitializeSystems();
        }

        private void InitializeSystems()
        {
            _vldSimulator = new VLDSimulator();
            _signalizationSimulator = new SignalizationSimulator();

            // 12 cihaz için koleksiyonlar
            _deviceDataCollections = new Dictionary<int, ObservableCollection<VldData>>();
            _deviceAlarms = new Dictionary<int, List<string>>();

            for (int i = 1; i <= 12; i++)
            {
                _deviceDataCollections[i] = new ObservableCollection<VldData>();
                _deviceAlarms[i] = new List<string>();
            }

            _activeTrains = new ObservableCollection<TrainInfo>();
            _stations = new ObservableCollection<StationInfo>();
            _blocks = new ObservableCollection<TrackBlock>();
            _routes = new ObservableCollection<RouteInfo>();

            // Chart listesi
            _allCharts = new List<ChartsProperties>
    {
        Chart1, Chart2, Chart3, Chart4, Chart5, Chart6,
        Chart7, Chart8, Chart9, Chart10, Chart11, Chart12
    };

            // Event handler'lar
            _vldSimulator.DataGenerated += OnVLDDataGenerated;
            _vldSimulator.StatusChanged += OnVLDStatusChanged;
            _signalizationSimulator.DataGenerated += OnSignalizationDataGenerated;

            InitializeTimers();
            InitializeTrackSystem();

            // YENİ: VLD-TFPR kontrol bölgelerini başlat
            InitializeDeviceControlAreas();

            CreateDeviceTabs(); // 12 alt sekme oluştur

            // DataGrid ItemsSource'larını bağla
            TrainsDataGrid.ItemsSource = _activeTrains;
            StationsDataGrid.ItemsSource = _stations;

            // Başlangıç trenleri ekle
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AddInitialTrains();
                DrawRailwaySystem();
            }), DispatcherPriority.Background);

            UpdateHeaderStatus("🟢 12 Cihaz Hazır", Colors.Green);
        }

        // InitializeSystems metoduna ekleyin
        private void InitializeDeviceControlAreas()
        {
            // İstasyon pozisyonları
            var stationPositions = new Dictionary<int, double>
    {
        {1, 15391.246},   // Depo
        {2, 13873.215},   // OSB
        {3, 12081.341},   // Mutlukent
        {4, 10385.942},   // Adliye
        {5, 9070.108},    // Akse Sapağı
        {6, 8234.420},    // Gebze Stadyum
        {7, 7101.599},    // Gebze Kent Meydanı
        {8, 5781.120},    // Fatih D.H.
        {9, 4389.210},    // TCDD Gar
        {10, 3298.624},   // Farabi D.H.
        {11, 1379.242},   // Darıca Cumhuriyet
        {12, 136.100}     // Darıca Sahil
    };

            // Her VLD-TFPR için boş veri oluştur
            for (int stationId = 1; stationId <= 12; stationId++)
            {
                // Kontrol bölgesi başlangıç ve bitiş
                double startPos, endPos;

                if (stationId <= 10) // İlk 10 cihaz: 3 istasyon
                {
                    startPos = stationPositions[stationId];
                    endPos = stationPositions[stationId + 2];
                }
                else if (stationId == 11) // Darıca Cumhuriyet
                {
                    startPos = stationPositions[11];
                    endPos = stationPositions[12];
                }
                else // Darıca Sahil
                {
                    startPos = stationPositions[12];
                    endPos = 0; // Hat sonu
                }

                // Veri oluştur veya güncelle
                if (_deviceDataCollections[stationId].Count == 0)
                {
                    var data = new VldData
                    {
                        StationId = stationId,
                        DeviceId = $"VLD-TFPR-{stationId:D3}",
                        StartPosition = startPos,
                        EndPosition = endPos,
                        Timestamp = DateTime.Now,
                        Status = "NORMAL",
                        IsCommunicationActive = true,
                        TrainsInSection = 0
                    };
                    _deviceDataCollections[stationId].Add(data);
                }
                else
                {
                    var data = _deviceDataCollections[stationId].First();
                    data.StartPosition = startPos;
                    data.EndPosition = endPos;
                }

                Console.WriteLine($"VLD-TFPR-{stationId:D3}: {startPos} → {endPos}");
            }
        }

        private void CreateDeviceTabs()
        {
            var stationNames = new string[]
            {
                "İdari Bina ve Atölye", "OSB", "Mutlukent", "Adliye", "Akse Sapağı",
                "Gebze Stadyum", "Gebze Kent Meydanı", "Fatih Devlet Hastanesi",
                "TCDD Gar", "Farabi Devlet Hastanesi", "Darıca Cumhuriyet", "Darıca Sahil"
            };

            for (int i = 1; i <= 12; i++)
            {
                int stationId = i; // Closure için local copy

                var tabItem = new TabItem
                {
                    Header = $"🚉 {stationNames[i - 1]}",
                    Tag = stationId
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var stackPanel = new StackPanel
                {
                    Margin = new Thickness(10),
                    Name = $"DevicePanel{stationId}"
                };

                scrollViewer.Content = stackPanel;
                tabItem.Content = scrollViewer;

                DeviceTabControl.Items.Add(tabItem);
            }
        }

        private void InitializeTimers()
        {
            // Chart güncelleme timer'ı
            _chartUpdateTimer = new DispatcherTimer();
            _chartUpdateTimer.Interval = TimeSpan.FromMilliseconds(1000);

            // Tren hareketi timer'ı
            _railwayUpdateTimer = new DispatcherTimer();
            _railwayUpdateTimer.Interval = TimeSpan.FromMilliseconds(100);
            _railwayUpdateTimer.Tick += RailwayUpdateTimer_Tick;

            // Detay paneli güncelleme timer'ı
            _detailUpdateTimer = new DispatcherTimer();
            _detailUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _detailUpdateTimer.Tick += DetailUpdateTimer_Tick;
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
            var stationPositions = new double[]
            {
                15391.246, 13873.215, 12081.341, 10385.942, 9070.108,
                8234.420, 7101.599, 5781.120, 4389.210, 3298.624,
                1379.242, 136.100
            };

            var stationNames = new string[]
            {
                "İdari Bina ve Atölye", "OSB", "Mutlukent", "Adliye", "Akse Sapağı",
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
                    WaitingPassengers = _random.Next(50, 200),
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
                RouteName = "İdari Bina ve Atölye → Darıca Sahil",
                BlockSequence = Enumerable.Range(1, 31).ToList(),
                TotalRouteLength = TOTAL_TRACK_LENGTH,
                ActiveTrainCount = 0,
                RouteStatus = "ACTIVE"
            });

            _routes.Add(new RouteInfo
            {
                RouteId = 2,
                RouteName = "Darıca Sahil → İdari Bina ve Atölye",
                BlockSequence = Enumerable.Range(101, 31).ToList(),
                TotalRouteLength = TOTAL_TRACK_LENGTH,
                ActiveTrainCount = 0,
                RouteStatus = "ACTIVE"
            });
        }

        private void AddInitialTrains()
        {
            AddTrainToTrack("UP", 15000);
            AddTrainToTrack("UP", 12000);
            AddTrainToTrack("UP", 9000);
            AddTrainToTrack("DOWN", 500);
            AddTrainToTrack("DOWN", 1500);
            AddTrainToTrack("DOWN", 3000);
            AddTrainToTrack("DOWN", 7000);
        }

        #region Signalization Info

        #endregion

        #region BUTON CLICK EVENTS

        private void StartAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.StartSimulation();
                _signalizationSimulator.StartSimulation();
                _railwayUpdateTimer.Start();
                _chartUpdateTimer.Start();
                _detailUpdateTimer.Start();

                UpdateButtonStates(true);
                UpdateHeaderStatus("🟡 12 Cihaz Çalışıyor", Colors.Orange);
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
                _detailUpdateTimer.Stop();

                UpdateButtonStates(false);
                UpdateHeaderStatus("🟢 12 Cihaz Hazır", Colors.Green);
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
            _railwayUpdateTimer.Start();
            ShowStatusMessage("Sinyalizasyon simülasyonu başlatıldı", StatusType.Info);
        }

        private void StopSignalizationButton_Click(object sender, RoutedEventArgs e)
        {
            _signalizationSimulator.StopSimulation();
            _railwayUpdateTimer.Stop();
            ShowStatusMessage("Sinyalizasyon simülasyonu durduruldu", StatusType.Info);
        }

        #endregion

        #region EVENT HANDLERS

        private void OnVLDDataGenerated(object sender, List<VldData> allDevicesData)
        {
            Dispatcher.Invoke(() =>
            {
                double totalPower = 0;
                double totalCurrent = 0;
                int activeDevices = 0;

                // Her cihazın verisini işle
                for (int i = 0; i < allDevicesData.Count && i < 12; i++)
                {
                    var data = allDevicesData[i];
                    int stationId = data.StationId;

                    // Mevcut kontrol bölgesi bilgilerini koru
                    var existingData = _deviceDataCollections[stationId].FirstOrDefault();
                    if (existingData != null)
                    {
                        data.StartPosition = existingData.StartPosition;
                        data.EndPosition = existingData.EndPosition;

                        // Bölgedeki tren sayısını hesapla
                        double minPos = Math.Min(data.StartPosition, data.EndPosition);
                        double maxPos = Math.Max(data.StartPosition, data.EndPosition);
                        data.TrainsInSection = _activeTrains.Count(train =>
                        {
                            double pos = train.CurrentPosition;
                            return pos >= minPos && pos <= maxPos;
                        });
                    }

                    // Veri koleksiyonuna ekle
                    _deviceDataCollections[stationId].Insert(0, data);
                    if (_deviceDataCollections[stationId].Count > 100)
                        _deviceDataCollections[stationId].RemoveAt(_deviceDataCollections[stationId].Count - 1);

                    // Alarm listesini güncelle
                    _deviceAlarms[stationId] = data.ActiveAlarms;

                    // Toplam değerleri hesapla
                    totalPower += data.ActivePower;
                    totalCurrent += data.Current;
                    if (data.IsCommunicationActive) activeDevices++;

                    // İlgili chart'ı güncelle (index 0-11)
                    if (i < _allCharts.Count)
                    {
                        _allCharts[i].AddValue(data.VoltageOut); // Gerilim grafiği

                        // Header'ı güncelle
                        UpdateStationHeader(stationId, data.Status, data.VoltageOut);
                    }
                }

                DataCountText.Content = $"TFPR Veri: {_deviceDataCollections.Sum(d => d.Value.Count)}";
                LastUpdateText.Content = $"Son Güncelleme: {DateTime.Now:HH:mm:ss}";
            });
        }

        private void UpdateStationHeader(int stationId, string status, double voltage)
        {
            TextBlock header = null;

            switch (stationId)
            {
                case 1: header = Station1Header; break;
                case 2: header = Station2Header; break;
                case 3: header = Station3Header; break;
                case 4: header = Station4Header; break;
                case 5: header = Station5Header; break;
                case 6: header = Station6Header; break;
                case 7: header = Station7Header; break;
                case 8: header = Station8Header; break;
                case 9: header = Station9Header; break;
                case 10: header = Station10Header; break;
                case 11: header = Station11Header; break;
                case 12: header = Station12Header; break;
            }

            if (header != null)
            {
                var station = _stations.FirstOrDefault(s => s.StationId == stationId);
                string stationName = station?.StationName ?? $"İstasyon {stationId}";

                header.Text = $"🚉 {stationName}\n{voltage:N1} kV"; // DEĞİŞTİ: kW -> kV

                // Durum rengine göre başlık rengi
                header.Foreground = status switch
                {
                    "NORMAL" => new SolidColorBrush(Colors.Green),
                    "WARNING" => new SolidColorBrush(Colors.Orange),
                    "ALARM" => new SolidColorBrush(Colors.Red),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
        }

        private void OnSignalizationDataGenerated(object sender, SignalizationData data)
        {
            Dispatcher.Invoke(() =>
            {
                TotalTrainsText.Text = $"🚆 Aktif Tren: {_activeTrains.Count}"; // Yerel sayıyı kullan
                SystemStatusText.Text = $"📡 Sistem: {data.SystemStatus}";

                SignalCommunicationText.Text = $"📶 İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
                SignalCommunicationBorder.Background = data.IsCommunicationActive ?
                    new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

                UpdateStationsDynamicData(data.Stations);

                TrainsDataGrid.Items.Refresh();
                StationsDataGrid.Items.Refresh();

                // Blok ve route doluluklarını yerel trenlere göre güncelle
                UpdateBlocksOccupancy(_blocks.ToList(), _activeTrains.ToList());
                UpdateRoutesTrainCounts(_routes.ToList(), _activeTrains.ToList());

                CanvasInfoText.Text = $"İstasyonlar: {_stations.Count} | Bloklar: {_blocks.Count} | Trenler: {_activeTrains.Count}";
            });
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

        private void OnVLDStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                HeaderStatusText.Text = $"🔵 {status}";
                ShowStatusMessage(status, StatusType.Info);
            });
        }
        #endregion

        #region TIMER EVENTS

        private void RailwayUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateTrainPositions();
            UpdateTrainCountsInSections();
            DrawRailwaySystem();
        }

        private void DetailUpdateTimer_Tick(object sender, EventArgs e)
{
    // Sadece aktif sekmeyi güncelle
    var selectedTab = DeviceTabControl.SelectedItem as TabItem;
    if (selectedTab != null && selectedTab.Tag is int stationId)
    {
        UpdateDevicePanel(stationId);
        
        // Hat görselini güncelle
        var scrollViewer = selectedTab.Content as ScrollViewer;
        var stackPanel = scrollViewer?.Content as StackPanel;
        if (stackPanel != null && stackPanel.Children.Count > 1)
        {
            if (stackPanel.Children[1] is GroupBox controlAreaGroup)
            {
                if (controlAreaGroup.Content is Grid grid && grid.Children[0] is Canvas canvas)
                {
                    DrawStationControlArea(canvas, stationId);
                }
            }
        }
    }
}

        #endregion

        #region DEVICE PANEL UPDATES

        private void UpdateDevicePanel(int stationId)
        {
            // İlgili TabItem'ı bul
            var tabItem = DeviceTabControl.Items.Cast<TabItem>()
        .FirstOrDefault(t => (int)t.Tag == stationId);

            if (tabItem == null) return;

            var scrollViewer = tabItem.Content as ScrollViewer;
            var stackPanel = scrollViewer?.Content as StackPanel;
            if (stackPanel == null) return;

            // Panel içeriğini temizle
            stackPanel.Children.Clear();

            var station = _stations.FirstOrDefault(s => s.StationId == stationId);
            if (station == null) return;

            // Başlık
            var titleText = new TextBlock
            {
                Text = $"🚉 {station.StationName} - VLD-TFPR-{stationId:D3}",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            };
            stackPanel.Children.Add(titleText);

            // YENİ: İstasyon Kontrol Bölgesi Hat Görseli
            var controlAreaGroup = CreateGroupBox($"Kontrol Bölgesi: {GetControlSectionName(stationId)}");
            var controlAreaGrid = new Grid { Height = 250, Margin = new Thickness(0, 0, 0, 20) };

            var localCanvas = new Canvas
            {
                Width = 800,
                Height = 250,
                Background = Brushes.White,
                Margin = new Thickness(10)
            };

            // ÖNCE BASİT BİR TEST ÇİZİMİ
            DrawTestControlArea(localCanvas, stationId);

            // Sonra gerçek çizimi yap
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DrawStationControlArea(localCanvas, stationId);
            }), DispatcherPriority.Background);

            controlAreaGrid.Children.Add(localCanvas);
            controlAreaGroup.Content = controlAreaGrid;
            stackPanel.Children.Add(controlAreaGroup);

            // Son veri
            var latestData = _deviceDataCollections[stationId].FirstOrDefault();
            if (latestData == null)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Henüz veri yok",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                return;
            }

            // Cihaz Bilgileri
            var deviceInfoGroup = CreateGroupBox("Cihaz Bilgileri");
            var deviceInfoPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            deviceInfoPanel.Children.Add(CreateInfoBorder("🔧 Cihaz ID", latestData.DeviceId, "#e3f2fd"));
            deviceInfoPanel.Children.Add(CreateInfoBorder("📍 Lokasyon", latestData.Location, "#e3f2fd"));
            deviceInfoPanel.Children.Add(CreateInfoBorder("📏 Kontrol Bölgesi",
                $"{latestData.StartPosition:N0}m - {latestData.EndPosition:N0}m", "#fff3e0"));
            deviceInfoPanel.Children.Add(CreateInfoBorder("🚆 Bölgedeki Tren",
                latestData.TrainsInSection.ToString(), "#e8f5e8"));
            deviceInfoPanel.Children.Add(CreateInfoBorder("⚡ Durum", latestData.Status,
                latestData.Status == "NORMAL" ? "#e8f5e8" : "#ffebee"));
            deviceInfoPanel.Children.Add(CreateInfoBorder("📶 İletişim",
                latestData.IsCommunicationActive ? "AKTİF" : "KESİNTİ",
                latestData.IsCommunicationActive ? "#e8f5e8" : "#ffebee"));

            deviceInfoGroup.Content = deviceInfoPanel;
            stackPanel.Children.Add(deviceInfoGroup);

            // Anlık Güç Değerleri
            var powerGroup = CreateGroupBox("Anlık Güç Değerleri");
            var powerPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            powerPanel.Children.Add(CreateInfoBorder("🔌 Giriş Gerilimi", $"{latestData.VoltageIn:N1} kV", "#e8f5e8"));
            powerPanel.Children.Add(CreateInfoBorder("⚡ Çıkış Gerilimi", $"{latestData.VoltageOut:N1} kV", "#e8f5e8"));
            powerPanel.Children.Add(CreateInfoBorder("🔋 Akım", $"{latestData.Current:N0} A", "#e3f2fd"));
            powerPanel.Children.Add(CreateInfoBorder("📊 Aktif Güç", $"{latestData.ActivePower:N0} kW", "#fff3e0"));
            powerPanel.Children.Add(CreateInfoBorder("📈 Reaktif Güç", $"{latestData.ReactivePower:N0} kVAr", "#fff3e0"));
            powerPanel.Children.Add(CreateInfoBorder("🎯 Güç Faktörü", $"{latestData.PowerFactor:N2}", "#e3f2fd"));

            powerGroup.Content = powerPanel;
            stackPanel.Children.Add(powerGroup);

            // Faz Değerleri
            var phaseGroup = CreateGroupBox("Faz Değerleri");
            var phasePanel = new WrapPanel { Orientation = Orientation.Horizontal };

            phasePanel.Children.Add(CreateInfoBorder("L1 Gerilim", $"{latestData.VoltageL1:N1} kV", "#f3e5f5"));
            phasePanel.Children.Add(CreateInfoBorder("L2 Gerilim", $"{latestData.VoltageL2:N1} kV", "#f3e5f5"));
            phasePanel.Children.Add(CreateInfoBorder("L3 Gerilim", $"{latestData.VoltageL3:N1} kV", "#f3e5f5"));
            phasePanel.Children.Add(CreateInfoBorder("L1 Akım", $"{latestData.CurrentL1:N0} A", "#e8f5e8"));
            phasePanel.Children.Add(CreateInfoBorder("L2 Akım", $"{latestData.CurrentL2:N0} A", "#e8f5e8"));
            phasePanel.Children.Add(CreateInfoBorder("L3 Akım", $"{latestData.CurrentL3:N0} A", "#e8f5e8"));

            phaseGroup.Content = phasePanel;
            stackPanel.Children.Add(phaseGroup);

            // Sistem Parametreleri
            var systemGroup = CreateGroupBox("Sistem Parametreleri");
            var systemPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            systemPanel.Children.Add(CreateInfoBorder("🌡️ Sıcaklık", $"{latestData.Temperature:N1} °C", "#ffebee"));
            systemPanel.Children.Add(CreateInfoBorder("📏 Frekans", $"{latestData.Frequency:N2} Hz", "#e3f2fd"));
            systemPanel.Children.Add(CreateInfoBorder("⚡ Toprak Akımı", $"{latestData.GroundCurrent:N1} A", "#fff3e0"));
            systemPanel.Children.Add(CreateInfoBorder("📉 Gerilim THD", $"{latestData.THDVoltage:N1} %", "#f3e5f5"));
            systemPanel.Children.Add(CreateInfoBorder("📉 Akım THD", $"{latestData.THDCurrent:N1} %", "#f3e5f5"));

            systemGroup.Content = systemPanel;
            stackPanel.Children.Add(systemGroup);

            // Enerji Ölçümleri
            var energyGroup = CreateGroupBox("Enerji Ölçümleri");
            var energyPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            energyPanel.Children.Add(CreateInfoBorder("🔋 Tüketilen Enerji", $"{latestData.ActiveEnergyImport:N2} kWh", "#e8f5e8"));
            energyPanel.Children.Add(CreateInfoBorder("📊 Tüketilen Reaktif", $"{latestData.ReactiveEnergyImport:N2} kVArh", "#fff3e0"));

            energyGroup.Content = energyPanel;
            stackPanel.Children.Add(energyGroup);

            // Alarmlar
            if (latestData.ActiveAlarms.Any())
            {
                var alarmGroup = CreateGroupBox("⚠️ Aktif Alarmlar");
                var alarmPanel = new WrapPanel { Orientation = Orientation.Horizontal };

                foreach (var alarm in latestData.ActiveAlarms)
                {
                    var alarmBorder = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffebee")),
                        BorderBrush = Brushes.Red,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5),
                        //Padding = new Thickness(8, 4),
                        Margin = new Thickness(3)
                    };

                    alarmBorder.Child = new TextBlock
                    {
                        Text = alarm,
                        Foreground = Brushes.Red,
                        FontWeight = FontWeights.Bold
                    };

                    alarmPanel.Children.Add(alarmBorder);
                }

                alarmGroup.Content = alarmPanel;
                stackPanel.Children.Add(alarmGroup);
            }

            // 4 Grafik (2x2) - TAMAMEN YENİ
            var chartsGrid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            chartsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(250) });
            chartsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(250) });
            chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 1. GRAFİK: GÜÇ Grafiği (ESKİ: Gerilim Grafiği)
            var powerChartGroup = CreateGroupBox("Zaman - Güç Grafiği");
            var powerChart = new ChartsProperties
            {
                Name = $"PowerChart{stationId}",
                Title = "Aktif Güç",
                YAxisTitle = "Güç (kW)",
                XAxisTitle = "Zaman",
                MinY = 0,
                MaxY = 3000,
                AutoScaleY = true,
                LineColor = Brushes.Green,
                BackgroundColor = Brushes.White
            };
            powerChartGroup.Content = powerChart;
            Grid.SetRow(powerChartGroup, 0);
            Grid.SetColumn(powerChartGroup, 0);
            chartsGrid.Children.Add(powerChartGroup);

            // 2. GRAFİK: GERİLİM Grafiği (ESKİ: Akım Grafiği)
            var voltageChartGroup = CreateGroupBox("Zaman - Gerilim Grafiği");
            var voltageChart = new ChartsProperties
            {
                Name = $"VoltageChart{stationId}",
                Title = "Çıkış Gerilimi",
                YAxisTitle = "Gerilim (kV)",
                XAxisTitle = "Zaman",
                MinY = 20,
                MaxY = 40,
                AutoScaleY = true,
                LineColor = Brushes.Blue,
                BackgroundColor = Brushes.White
            };
            voltageChartGroup.Content = voltageChart;
            Grid.SetRow(voltageChartGroup, 0);
            Grid.SetColumn(voltageChartGroup, 1);
            chartsGrid.Children.Add(voltageChartGroup);

            // 3. GRAFİK: AKIM Grafiği (ESKİ: Sıcaklık Grafiği)
            var currentChartGroup = CreateGroupBox("Zaman - Akım Grafiği");
            var currentChart = new ChartsProperties
            {
                Name = $"CurrentChart{stationId}",
                Title = "Akım",
                YAxisTitle = "Akım (A)",
                XAxisTitle = "Zaman",
                MinY = 500,
                MaxY = 2500,
                AutoScaleY = true,
                LineColor = Brushes.Red,
                BackgroundColor = Brushes.White
            };
            currentChartGroup.Content = currentChart;
            Grid.SetRow(currentChartGroup, 1);
            Grid.SetColumn(currentChartGroup, 0);
            chartsGrid.Children.Add(currentChartGroup);

            // 4. GRAFİK: SICAKLIK Grafiği (ESKİ: THD Grafiği)
            var tempChartGroup = CreateGroupBox("Zaman - Sıcaklık Grafiği");
            var tempChart = new ChartsProperties
            {
                Name = $"TempChart{stationId}",
                Title = "Sıcaklık",
                YAxisTitle = "Sıcaklık (°C)",
                XAxisTitle = "Zaman",
                MinY = -20,
                MaxY = 100,
                AutoScaleY = true,
                LineColor = Brushes.Orange,
                BackgroundColor = Brushes.White
            };
            tempChartGroup.Content = tempChart;
            Grid.SetRow(tempChartGroup, 1);
            Grid.SetColumn(tempChartGroup, 1);
            chartsGrid.Children.Add(tempChartGroup);

            stackPanel.Children.Add(chartsGrid);

            // Grafiklere veri ekle
            if (_deviceDataCollections[stationId].Count > 0)
            {
                foreach (var data in _deviceDataCollections[stationId].Take(50).Reverse())
                {
                    powerChart.AddValue(data.ActivePower);     // Güç grafiği
                    voltageChart.AddValue(data.VoltageOut);    // Gerilim grafiği
                    currentChart.AddValue(data.Current);       // Akım grafiği
                    tempChart.AddValue(data.Temperature);      // Sıcaklık grafiği
                }
            }

            // Son 10 Veri Tablosu
            var dataTableGroup = CreateGroupBox("Son 10 Ölçüm");
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 250,
                ItemsSource = _deviceDataCollections[stationId].Take(10)
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Zaman", Binding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "HH:mm:ss" }, Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Gerilim (kV)", Binding = new System.Windows.Data.Binding("VoltageOut") { StringFormat = "N1" }, Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Akım (A)", Binding = new System.Windows.Data.Binding("Current") { StringFormat = "N0" }, Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Güç (kW)", Binding = new System.Windows.Data.Binding("ActivePower") { StringFormat = "N0" }, Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Sıcaklık (°C)", Binding = new System.Windows.Data.Binding("Temperature") { StringFormat = "N1" }, Width = 90 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Durum", Binding = new System.Windows.Data.Binding("Status"), Width = 80 });

            dataTableGroup.Content = dataGrid;
            stackPanel.Children.Add(dataTableGroup);
        }
        private void DrawTestControlArea(Canvas canvas, int stationId)
        {
            var testText = new TextBlock
            {
                Text = $"VLD-TFPR-{stationId:D3}\nKontrol Bölgesi\nYükleniyor...",
                FontSize = 16,
                Foreground = Brushes.Gray,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas.SetLeft(testText, 300);
            Canvas.SetTop(testText, 100);
            canvas.Children.Add(testText);
        }

        private string GetControlSectionName(int stationId)
        {
            var stationNames = new string[]
            {
        "İdari Bina ve Atölye", "OSB", "Mutlukent", "Adliye", "Akse Sapağı",
        "Gebze Stadyum", "Gebze Kent Meydanı", "Fatih Devlet Hastanesi",
        "TCDD Gar", "Farabi Devlet Hastanesi", "Darıca Cumhuriyet", "Darıca Sahil"
            };

            // Her VLD-TFPR cihazı: Kendisi -> 2 sonraki istasyon
            return stationId switch
            {
                1 => $"{stationNames[0]} → {stationNames[1]} → {stationNames[2]}",  // Depo → OSB → Mutlukent
                2 => $"{stationNames[1]} → {stationNames[2]} → {stationNames[3]}",  // OSB → Mutlukent → Adliye
                3 => $"{stationNames[2]} → {stationNames[3]} → {stationNames[4]}",  // Mutlukent → Adliye → Akse
                4 => $"{stationNames[3]} → {stationNames[4]} → {stationNames[5]}",  // Adliye → Akse → Gebze Stadyum
                5 => $"{stationNames[4]} → {stationNames[5]} → {stationNames[6]}",  // Akse → Gebze Stadyum → Gebze KM
                6 => $"{stationNames[5]} → {stationNames[6]} → {stationNames[7]}",  // Gebze Stadyum → Gebze KM → Fatih
                7 => $"{stationNames[6]} → {stationNames[7]} → {stationNames[8]}",  // Gebze KM → Fatih → TCDD
                8 => $"{stationNames[7]} → {stationNames[8]} → {stationNames[9]}",  // Fatih → TCDD → Farabi
                9 => $"{stationNames[8]} → {stationNames[9]} → {stationNames[10]}", // TCDD → Farabi → Darıca C.
                10 => $"{stationNames[9]} → {stationNames[10]} → {stationNames[11]}", // Farabi → Darıca C. → Darıca S.
                11 => $"{stationNames[10]} → {stationNames[11]} → Hat Sonu", // Darıca C. → Darıca S. → Son
                12 => $"{stationNames[11]} → Hat Sonu", // Darıca S. → Son
                _ => $"İstasyon {stationId} Bölgesi"
            };
        }

        private void DrawStationControlArea(Canvas canvas, int stationId)
        {
            canvas.Children.Clear();

            var latestData = _deviceDataCollections[stationId].FirstOrDefault();
            if (latestData == null)
            {
                DrawEmptyControlArea(canvas, stationId);
                return;
            }

            // İstasyon pozisyonları
            var stationPositions = new Dictionary<int, (string name, double position)>
    {
        {1, ("İdari Bina ve Atölye", 15391.246)},
        {2, ("OSB", 13873.215)},
        {3, ("Mutlukent", 12081.341)},
        {4, ("Adliye", 10385.942)},
        {5, ("Akse Sapağı", 9070.108)},
        {6, ("Gebze Stadyum", 8234.420)},
        {7, ("Gebze Kent Meydanı", 7101.599)},
        {8, ("Fatih D.H.", 5781.120)},
        {9, ("TCDD Gar", 4389.210)},
        {10, ("Farabi D.H.", 3298.624)},
        {11, ("Darıca C.", 1379.242)},
        {12, ("Darıca Sahil", 136.100)}
    };

            // Kontrol edilen istasyonları belirle
            List<int> controlledStations = GetControlledStations(stationId);

            // Kontrol edilen istasyonların verilerini al ve sırala
            var controlledStationData = controlledStations
                .Where(id => id >= 1 && id <= 12)
                .Select(id => (id, stationPositions[id]))
                .OrderBy(s => s.Item2.position)
                .ToList();

            if (!controlledStationData.Any())
            {
                DrawEmptyControlArea(canvas, stationId);
                return;
            }

            // Canvas ayarları
            double canvasWidth = 750;
            double canvasHeight = 200;
            double upTrackY = canvasHeight / 2 + 70;
            double downTrackY = canvasHeight / 2 + 130;

            // Ölçeklendirme
            double minPos = controlledStationData.Min(s => s.Item2.position);
            double maxPos = controlledStationData.Max(s => s.Item2.position);
            double sectionLength = maxPos - minPos;
            double scaleFactor = (canvasWidth - 100) / (sectionLength + 400);

            // Hat çizgileri
            DrawTrackLines(canvas, sectionLength, scaleFactor, upTrackY, downTrackY);

            // İstasyonları çiz
            DrawStations(canvas, controlledStationData, stationId, minPos, scaleFactor, upTrackY);

            // Mesafe çizgileri
            DrawDistanceLines(canvas, controlledStationData, minPos, scaleFactor, upTrackY);

            // Trenleri çiz
            DrawTrainsInSection(canvas, minPos, maxPos, scaleFactor, upTrackY, downTrackY);

            // Başlık ve etiketler
            DrawLabels(canvas, stationId, controlledStationData, trainsInSection, sectionLength, upTrackY, downTrackY);

            // Cihaz konumu
            DrawDeviceLocation(canvas, stationId, stationPositions[stationId].position, minPos, scaleFactor, upTrackY);
        }

        private List<int> GetControlledStations(int stationId)
        {
            switch (stationId)
            {
                case 1:  // Depo: Sadece Depo ve OSB
                    return new List<int> { 1, 2 };
                case 12: // Darıca Sahil: Sadece Darıca C. ve Darıca Sahil
                    return new List<int> { 11, 12 };
                default: // Diğer tüm istasyonlar: Önceki, kendisi, sonraki (3 istasyon)
                    if (stationId > 1 && stationId < 12)
                        return new List<int> { stationId - 1, stationId, stationId + 1 };
                    else
                        return new List<int> { stationId };
            }
        }

        private void DrawTrackLines(Canvas canvas, double sectionLength, double scaleFactor, double upTrackY, double downTrackY)
        {
            // Üst hat (Mavi - Darıca Yönü)
            var upTrack = new Line
            {
                X1 = 50,
                Y1 = upTrackY,
                X2 = 50 + (sectionLength * scaleFactor),
                Y2 = upTrackY,
                Stroke = Brushes.Blue,
                StrokeThickness = 4,
                Opacity = 0.8
            };
            canvas.Children.Add(upTrack);

            // Alt hat (Kırmızı - Depo Yönü)
            var downTrack = new Line
            {
                X1 = 50,
                Y1 = downTrackY,
                X2 = 50 + (sectionLength * scaleFactor),
                Y2 = downTrackY,
                Stroke = Brushes.Red,
                StrokeThickness = 4,
                Opacity = 0.8
            };
            canvas.Children.Add(downTrack);
        }

        private void DrawStations(Canvas canvas, List<(int id, (string name, double position) station)> stationData,
                                  int selectedStationId, double minPos, double scaleFactor, double upTrackY)
        {
            foreach (var (stationIndex, (name, position)) in stationData)
            {
                double xPos = 50 + ((position - minPos) * scaleFactor);
                bool isSelected = stationIndex == selectedStationId;

                // İstasyon işaretleyici
                var stationMarker = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = isSelected ? Brushes.Green : Brushes.DarkOrange,
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    ToolTip = $"{name} ({position / 1000:0.00}km)"
                };
                Canvas.SetLeft(stationMarker, xPos - 10);
                Canvas.SetTop(stationMarker, upTrackY - 10);
                canvas.Children.Add(stationMarker);

                // İstasyon numarası
                var stationNumber = new TextBlock
                {
                    Text = $"{stationIndex}",
                    FontSize = 10,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Width = 14, // Sabit genişlik
                    Height = 14 // Sabit yükseklik
                };

                // TextBlock'u tam ortalamak için
                Canvas.SetLeft(stationNumber, xPos - 7);  // -7 yerine -8 veya -9 deneyebilirsiniz
                Canvas.SetTop(stationNumber, upTrackY - 8);
                canvas.Children.Add(stationNumber);

                // İstasyon adı
                var stationName = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isSelected ? Brushes.Green : Brushes.DarkOrange,
                    MaxWidth = 120,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Background = Brushes.WhiteSmoke,
                    Padding = new Thickness(5, 2, 5, 2)
                };
                Canvas.SetLeft(stationName, xPos - 60);
                Canvas.SetTop(stationName, upTrackY - 50);
                canvas.Children.Add(stationName);

                // Kilometre bilgisi
                var kmText = new TextBlock
                {
                    Text = $"{position / 1000:0.00}km",
                    FontSize = 9,
                    Foreground = Brushes.DarkSlateGray,
                    Background = Brushes.LightYellow,
                    Padding = new Thickness(4, 1, 4, 1),
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(kmText, xPos - 25);
                Canvas.SetTop(kmText, upTrackY + 20);
                canvas.Children.Add(kmText);
            }
        }

        private void DrawDistanceLines(Canvas canvas, List<(int id, (string name, double position) station)> stationData,
                                       double minPos, double scaleFactor, double upTrackY)
        {
            for (int i = 0; i < stationData.Count - 1; i++)
            {
                var current = stationData[i];
                var next = stationData[i + 1];

                double currentX = 50 + ((current.station.position - minPos) * scaleFactor);
                double nextX = 50 + ((next.station.position - minPos) * scaleFactor);
                double distance = Math.Abs(current.station.position - next.station.position);

                // Mesafe çizgisi
                var distanceLine = new Line
                {
                    X1 = currentX,
                    Y1 = upTrackY - 25,
                    X2 = nextX,
                    Y2 = upTrackY - 25,
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 5, 3 }
                };
                canvas.Children.Add(distanceLine);

                // Mesafe bilgisi
                var distanceText = new TextBlock
                {
                    Text = $"{distance:0}m",
                    FontSize = 8,
                    Foreground = Brushes.DarkSlateGray,
                    Background = Brushes.White,
                    Padding = new Thickness(3, 1, 3, 1),
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(distanceText, (currentX + nextX) / 2 - 15);
                Canvas.SetTop(distanceText, upTrackY - 40);
                canvas.Children.Add(distanceText);
            }
        }

        private List<TrainInfo> trainsInSection; // Sınıf seviyesinde tanımlanmalı veya döndürülmeli

        private void DrawTrainsInSection(Canvas canvas, double minPos, double maxPos,
                                         double scaleFactor, double upTrackY, double downTrackY)
        {
            trainsInSection = _activeTrains.Where(train =>
            {
                if (train == null) return false;
                double pos = train.CurrentPosition;
                return pos >= minPos - 500 && pos <= maxPos + 500;
            }).ToList();

            foreach (var train in trainsInSection)
            {
                double xPos = 50 + ((train.CurrentPosition - minPos) * scaleFactor);
                double yPos = train.TrackType == "UP" ? upTrackY : downTrackY;

                // Tren simgesi
                var trainRect = new Rectangle
                {
                    Width = 40,
                    Height = 18,
                    Fill = train.TrackType == "UP" ? Brushes.Blue : Brushes.Red,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    RadiusX = 5,
                    RadiusY = 5,
                    ToolTip = $"Tren {train.TrainId}\nHız: {train.Speed:0}km/h\nKonum: {train.CurrentPosition:0}m"
                };
                Canvas.SetLeft(trainRect, xPos - 20);
                Canvas.SetTop(trainRect, yPos - 10);
                canvas.Children.Add(trainRect);

                // Tren yön oku
                var directionArrow = new TextBlock
                {
                    Text = train.TrackType == "UP" ? "◀" : "▶",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(directionArrow, xPos - 6);
                Canvas.SetTop(directionArrow, yPos - 8);
                canvas.Children.Add(directionArrow);

                // Tren hızı
                var speedText = new TextBlock
                {
                    Text = $"{train.Speed:0}",
                    FontSize = 8,
                    Foreground = Brushes.Black,
                    Background = Brushes.WhiteSmoke,
                    Padding = new Thickness(3, 1, 3, 1),
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(speedText, xPos - 10);
                Canvas.SetTop(speedText, train.TrackType == "UP" ? yPos - 35 : yPos + 12);
                canvas.Children.Add(speedText);
            }
        }

        private void DrawLabels(Canvas canvas, int stationId, List<(int id, (string name, double position) station)> stationData,
                               List<TrainInfo> trains, double sectionLength, double upTrackY, double downTrackY)
        {
            // BÖLGE BAŞLIĞI
            var sectionHeader = new TextBlock
            {
                Text = $"VLD-TFPR-{stationId:D3} KONTROL BÖLGESİ",
                FontSize = 14,
                Foreground = Brushes.DarkBlue,
                FontWeight = FontWeights.Bold,
                Background = Brushes.LightCyan,
                Padding = new Thickness(15, 8, 15, 8)
            };
            Canvas.SetLeft(sectionHeader, 250);
            Canvas.SetTop(sectionHeader, 10);
            canvas.Children.Add(sectionHeader);

            // BÖLGE BİLGİLERİ
            string stationList = string.Join(" → ", stationData.Select(s => s.station.name));
            var sectionInfo = new TextBlock
            {
                Text = $"📍 Kontrol Edilen İstasyonlar: {stationList}\n" +
                       $"🚆 Aktif Tren Sayısı: {trains.Count}\n" +
                       $"📏 Bölge Uzunluğu: {sectionLength:0}m",
                FontSize = 10,
                Foreground = Brushes.DarkGreen,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Honeydew,
                Padding = new Thickness(10, 6, 10, 6)
            };
            Canvas.SetLeft(sectionInfo, 200);
            Canvas.SetTop(sectionInfo, 45);
            canvas.Children.Add(sectionInfo);

            // YÖN ETİKETLERİ
            var upLabel = new TextBlock
            {
                Text = "← Darıca Yönü",
                FontSize = 11,
                Foreground = Brushes.Blue,
                FontWeight = FontWeights.Bold,
                Background = Brushes.AliceBlue,
                Padding = new Thickness(10, 4, 10, 4)
            };
            Canvas.SetLeft(upLabel, -75);
            Canvas.SetTop(upLabel, upTrackY - 15);
            canvas.Children.Add(upLabel);

            var downLabel = new TextBlock
            {
                Text = "İdari Bina ve Atölye Yönü →",
                FontSize = 11,
                Foreground = Brushes.Red,
                FontWeight = FontWeights.Bold,
                Background = Brushes.MistyRose,
                Padding = new Thickness(10, 4, 10, 4)
            };
            Canvas.SetLeft(downLabel, -145);
            Canvas.SetTop(downLabel, downTrackY - 15);
            canvas.Children.Add(downLabel);
        }

        private void DrawDeviceLocation(Canvas canvas, int stationId, double devicePosition,
                                       double minPos, double scaleFactor, double upTrackY)
        {
            double deviceXPos = 50 + ((devicePosition - minPos) * scaleFactor);
            var deviceMarker = new TextBlock
            {
                Text = "★",
                FontSize = 20,
                Foreground = Brushes.Green,
                FontWeight = FontWeights.Bold,
                ToolTip = $"VLD-TFPR-{stationId:D3} Konumu"
            };
            Canvas.SetLeft(deviceMarker, deviceXPos - 10);
            Canvas.SetTop(deviceMarker, upTrackY - 75);
            canvas.Children.Add(deviceMarker);
        }

        // Boş kontrol bölgesi çizmek için yardımcı metod (ZATEN VAR)
        private void DrawEmptyControlArea(Canvas canvas, int stationId)
        {
            canvas.Children.Clear();

            var errorText = new TextBlock
            {
                Text = $"VLD-TFPR-{stationId:D3} Kontrol Bölgesi\n\n" +
                       "⚠️ Veri yükleniyor...\n" +
                       "Kontrol bölgesi henüz başlatılmadı.",
                FontSize = 14,
                Foreground = Brushes.OrangeRed,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas.SetLeft(errorText, 200);
            Canvas.SetTop(errorText, 80);
            canvas.Children.Add(errorText);
        }
        #endregion

        #region DETAIL PANEL (Artık kullanılmıyor - DevicePanel'e taşındı)

        private GroupBox CreateGroupBox(string header)
        {
            return new GroupBox
            {
                Header = header,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10),
                FontWeight = FontWeights.Bold
            };
        }

        private Border CreateInfoBorder(string label, string value, string bgColor)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(3)
            };

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 16,
                Foreground = Brushes.Gray
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = FontWeights.Bold
            });

            border.Child = stackPanel;
            return border;
        }

        #endregion

        #region TREN HAREKET SİSTEMİ

        private class TrainState
        {
            public double StopTimer { get; set; }
            public bool IsStoppedAtStation { get; set; }
            public int CurrentStationId { get; set; }
            public int LastDepartureStationId { get; set; } = -1;
        }

        private Dictionary<int, TrainState> _trainStates = new Dictionary<int, TrainState>();

        private void AddTrainToTrack(string trackType, double startPosition)
        {
            string direction = trackType == "UP" ? "NORTHBOUND" : "SOUTHBOUND";
            int startBlockId = trackType == "UP" ? 1 : 101;
            int gridY = trackType == "UP" ? (int)UP_TRACK_Y : (int)DOWN_TRACK_Y;

            var newTrain = new TrainInfo
            {
                TrainNumber = _activeTrains.Count + 1,
                TrainId = _activeTrains.Count + 100 + _random.Next(1000),
                TrainName = $"TR-{_random.Next(100, 999)}",
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
        }

        private void UpdateTrainPositions()
        {
            try
            {
                List<TrainInfo> trainsToRemove = new List<TrainInfo>();

                foreach (var train in _activeTrains.ToList())
                {
                    if (!_trainStates.ContainsKey(train.TrainId))
                        _trainStates[train.TrainId] = new TrainState();

                    var trainState = _trainStates[train.TrainId];

                    if (trainState.IsStoppedAtStation)
                    {
                        trainState.StopTimer += 0.1;
                        train.Status = "STOPPED";
                        train.Speed = 0;

                        if (trainState.StopTimer >= STATION_STOP_TIME)
                        {
                            trainState.IsStoppedAtStation = false;
                            trainState.StopTimer = 0;
                            trainState.LastDepartureStationId = trainState.CurrentStationId;
                            train.Status = "MOVING";
                            train.Speed = 60 + _random.Next(0, 20);
                        }
                        continue;
                    }

                    if (IsTrainAhead(train))
                    {
                        train.Status = "WAITING";
                        train.Speed = 0;
                        continue;
                    }

                    var approachingStation = GetApproachingStation(train);
                    if (approachingStation != null && approachingStation.StationId != trainState.LastDepartureStationId)
                    {
                        double distanceToStation = Math.Abs(train.CurrentPosition - approachingStation.GridX);

                        if (distanceToStation < 200)
                        {
                            train.Speed = Math.Max(20, train.Speed - 2);

                            if (distanceToStation < 20)
                            {
                                train.Status = "STOPPED";
                                train.Speed = 0;
                                trainState.IsStoppedAtStation = true;
                                trainState.StopTimer = 0;
                                trainState.CurrentStationId = approachingStation.StationId;
                                train.PassengerCount = _random.Next(50, 200);
                                approachingStation.WaitingPassengers = _random.Next(20, 100);
                                continue;
                            }
                        }
                    }

                    if (trainState.LastDepartureStationId != -1)
                    {
                        var lastStation = _stations.FirstOrDefault(s => s.StationId == trainState.LastDepartureStationId);
                        if (lastStation != null)
                        {
                            double dist = Math.Abs(train.CurrentPosition - lastStation.GridX);
                            if (dist > 250)
                                trainState.LastDepartureStationId = -1;
                        }
                    }

                    if (train.Status == "MOVING" || train.Status == "WAITING")
                    {
                        double currentSpeed = (train.Status == "WAITING") ? 0 : train.Speed;
                        double movement = (currentSpeed / 3.6) * 0.5;

                        if (train.TrackType == "UP")
                        {
                            train.CurrentPosition -= movement;
                            if (train.CurrentPosition <= -100) trainsToRemove.Add(train);
                        }
                        else
                        {
                            train.CurrentPosition += movement;
                            if (train.CurrentPosition >= TOTAL_TRACK_LENGTH + 100) trainsToRemove.Add(train);
                        }

                        train.GridX = (int)(train.CurrentPosition / 8);
                        UpdateTrainCurrentBlock(train);

                        var block = _blocks.FirstOrDefault(b => b.BlockId == train.CurrentBlockId + (train.TrackType == "DOWN" ? 100 : 0));
                        if (block != null)
                            train.PositionInBlock = Math.Abs(train.CurrentPosition - block.StartPosition);
                    }

                    else
                    {
                        train.Status = "MOVING";
                        continue;
                    }
                }

                foreach (var train in trainsToRemove)
                {
                    _activeTrains.Remove(train);
                    _trainStates.Remove(train.TrainId);
                    AddTrainToTrack(train.TrackType, train.TrackType == "UP" ? 15391 : 0);
                }
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
            double safeDistance = 1000;

            if (currentTrain.TrackType == "UP")
            {
                var aheadTrain = sameTrackTrains
                    .Where(t => t.CurrentPosition < currentTrain.CurrentPosition)
                    .OrderByDescending(t => t.CurrentPosition)
                    .FirstOrDefault();

                if (aheadTrain != null)
                {
                    double distance = currentTrain.CurrentPosition - aheadTrain.CurrentPosition;
                    return distance < safeDistance;
                }
            }
            else
            {
                var aheadTrain = sameTrackTrains
                    .Where(t => t.CurrentPosition > currentTrain.CurrentPosition)
                    .OrderBy(t => t.CurrentPosition)
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
            foreach (var station in _stations)
            {
                double dist = Math.Abs(train.CurrentPosition - station.GridX);
                if (dist < 300)
                    return station;
            }
            return null;
        }

        private void UpdateTrainCurrentBlock(TrainInfo train)
        {
            double pos = train.CurrentPosition;
            double blockLen = TOTAL_TRACK_LENGTH / 31.0;
            int blockIndex = (int)(pos / blockLen);
            if (blockIndex < 0) blockIndex = 0;
            if (blockIndex > 30) blockIndex = 30;
            train.CurrentBlockId = blockIndex + 1;
        }

        private void UpdateTrainCountsInSections()
        {
            // Her VLD-TFPR cihazının kontrol ettiği bölgedeki tren sayısını hesapla
            for (int stationId = 1; stationId <= 12; stationId++)
            {
                var latestData = _deviceDataCollections[stationId].FirstOrDefault();
                if (latestData == null) continue;

                double startPos = latestData.StartPosition;
                double endPos = latestData.EndPosition;

                int trainCount = _activeTrains.Count(train =>
                {
                    double pos = train.CurrentPosition;
                    return pos >= Math.Min(startPos, endPos) && pos <= Math.Max(startPos, endPos);
                });

                latestData.TrainsInSection = trainCount;
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
                foreach (var station in _stations) DrawStation(station, scaleFactor);
                foreach (var train in _activeTrains.ToList()) DrawTrain(train, scaleFactor);
                DrawGridAndScale(scaleFactor);
                DrawTrackLabels();
            }
            catch (Exception) { }
        }

        private void DrawDoubleTrackSystem(double scaleFactor)
        {
            double scaledLength = TOTAL_TRACK_LENGTH * scaleFactor;

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
                FontSize = 18,
                Foreground = Brushes.Black,
                RenderTransform = new RotateTransform(-25)
            };
            Canvas.SetLeft(text, xPos - 10);
            Canvas.SetTop(text, UP_TRACK_Y - 75);
            RailwayCanvas.Children.Add(text);
        }

        private void DrawTrain(TrainInfo train, double scaleFactor)
        {
            double xPos = 50 + (train.CurrentPosition * scaleFactor);
            if (xPos < -50 || xPos > CANVAS_WIDTH + 50) return;

            bool isUp = train.TrackType == "UP";
            double yPos = isUp ? UP_TRACK_Y : DOWN_TRACK_Y;

            var rect = new Rectangle
            {
                Width = 40,
                Height = 16,
                Fill = train.Status == "STOPPED" ? Brushes.Orange : (isUp ? Brushes.Blue : Brushes.Red),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(rect, xPos - 20);
            Canvas.SetTop(rect, yPos - 8);
            RailwayCanvas.Children.Add(rect);

            var arrow = new TextBlock
            {
                Text = isUp ? "◀" : "▶",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(arrow, xPos - 4);
            Canvas.SetTop(arrow, yPos - 7);
            RailwayCanvas.Children.Add(arrow);

            var infoText = new TextBlock
            {
                Text = $"{train.TrainId}\n{train.Speed:0} km/h\n{train.CurrentPosition:0}m",
                FontSize = 10,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(2)
            };
            Canvas.SetLeft(infoText, xPos - 22.5f);
            Canvas.SetTop(infoText, yPos - 50);
            Panel.SetZIndex(infoText, 100);
            RailwayCanvas.Children.Add(infoText);
        }

        private void DrawGridAndScale(double scaleFactor)
        {
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

                var txt = new TextBlock { Text = $"{i}km", FontSize = 16, Foreground = Brushes.Gray };
                Canvas.SetLeft(txt, x + 2);
                Canvas.SetTop(txt, DOWN_TRACK_Y + 20);
                RailwayCanvas.Children.Add(txt);
            }
        }

        private void DrawTrackLabels()
        {
            var upLabel = new TextBlock { Text = "Darıca Yönü", Foreground = Brushes.Blue, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(upLabel, 60);
            Canvas.SetTop(upLabel, UP_TRACK_Y - 40);
            RailwayCanvas.Children.Add(upLabel);

            var downLabel = new TextBlock { Text = "İdari Bina ve Atölye Yönü", Foreground = Brushes.Red, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(downLabel, 60);
            Canvas.SetTop(downLabel, DOWN_TRACK_Y + 45);
            RailwayCanvas.Children.Add(downLabel);
        }

        #endregion

        #region UTILITIES

        private void UpdateButtonStates(bool isRunning)
        {
            StartAllButton.IsEnabled = !isRunning;
            StopAllButton.IsEnabled = isRunning;

            foreach (var chart in _allCharts)
                chart.IsPaused = !isRunning;
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
            _detailUpdateTimer.Stop();

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
            _detailUpdateTimer?.Stop();
            base.OnClosed(e);
        }
    }

    public enum StatusType { Info, Warning, Error, Emergency }
}