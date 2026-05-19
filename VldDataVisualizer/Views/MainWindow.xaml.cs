using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows.Controls;
using VldDataVisualizer.Models;
using VldDataVisualizer.ViewModels;
using System.Windows.Shapes;
using System.IO;
using System.Windows.Media.Animation;
using System.ComponentModel;
using System.Windows.Data;
using VldDataVisualizer.Helpers;
using VldDataVisualizer.Services;
using System.Threading;

namespace VldDataVisualizer.Views
{
    public partial class MainWindow : Window
    {
        private VLDSimulator _vldSimulator = null!;
        private SignalizationSimulator _signalizationSimulator = null!;

        // 12 cihaz için ayrı veri koleksiyonları
        private Dictionary<int, ObservableCollection<VldData>> _deviceDataCollections = null!;
        private Dictionary<int, List<string>> _deviceAlarms = null!;

        // Collections
        private ObservableCollection<TrainInfo> _activeTrains = null!;
        private ObservableCollection<StationInfo> _stations = null!;
        private ObservableCollection<TrackBlock> _blocks = null!;
        private ObservableCollection<RouteInfo> _routes = null!;

        // Timers
        private DispatcherTimer _chartUpdateTimer = null!;
        private DispatcherTimer _railwayUpdateTimer = null!;
        private DispatcherTimer _detailUpdateTimer = null!;
        private DispatcherTimer _logCheckTimer = null!;

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
        private List<ChartsProperties> _allCharts = null!;
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

            _logCheckTimer = new DispatcherTimer();
            _logCheckTimer.Interval = TimeSpan.FromMilliseconds(100);
            _logCheckTimer.Tick += LogCheckTimer_Tick;
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
            AddTrainToTrack("HAT - 1", 15000);
            AddTrainToTrack("HAT - 1", 12000);
            AddTrainToTrack("HAT - 1", 9000);
            AddTrainToTrack("HAT - 2", 500);
            AddTrainToTrack("HAT - 2", 1500);
            AddTrainToTrack("HAT - 2", 3000);
            AddTrainToTrack("HAT - 2", 7000);
        }

        #region Signalization Info

        #endregion

        #region BUTON CLICK EVENTS
        private TfprVldModbusTcpReader _tcpReader;
        private TfprPollingService _pollingService;
        private void StartAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (RealDataModeCheckBox.IsChecked == true)
                {
                    // GERÇEK VERİ MODU
                    _tcpReader = new TfprVldModbusTcpReader("192.168.1.10");
                    _pollingService = new TfprPollingService(_tcpReader);
                    _pollingService.DataReceived += (s, pollingData) =>
                    {
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            OnVLDDataGenerated(this, new List<VldData> { pollingData });
                            OutputBox.Text = FormatVldStatusText(pollingData);
                        });
                    };
                    _pollingService.Start(_tcpReader, 500);

                    // İlk okumayı yap ve göster
                    try
                    {
                        var initialData = _tcpReader.Read();
                        OutputBox.Text = FormatVldStatusText(initialData);
                    }
                    catch (Exception ex)
                    {
                        OutputBox.Text = $"İlk okuma hatası: {ex.Message}";
                    }
                    
                    ShowStatusMessage("Gerçek Veri Modu (Modbus) başlatıldı", StatusType.Info);
                }
                else
                {
                    // SİMÜLASYON MODU
                    _vldSimulator.StartSimulation();
                    ShowStatusMessage("Simülasyon Modu başlatıldı", StatusType.Info);
                }

                _signalizationSimulator.StartSimulation();
                _railwayUpdateTimer.Start();
                _chartUpdateTimer.Start();
                _detailUpdateTimer.Start();
                _logCheckTimer.Start(); // YENİ: Log kontrol timer'ını başlat

                UpdateButtonStates(true);
                UpdateHeaderStatus("🟡 12 Cihaz Çalışıyor", Colors.Orange);
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
                if (RealDataModeCheckBox.IsChecked == true)
                {
                    _pollingService?.Stop();
                    _tcpReader?.Dispose();
                    _tcpReader = null;
                }
                else
                {
                    _vldSimulator.StopSimulation();
                }

                _signalizationSimulator.StopSimulation();
                _railwayUpdateTimer.Stop();
                _chartUpdateTimer.Stop();
                _detailUpdateTimer.Stop();
                _logCheckTimer.Stop(); // YENİ: Log kontrol timer'ını durdur

                UpdateButtonStates(false);
                UpdateHeaderStatus("🟢 12 Cihaz Hazır", Colors.Green);
                ShowStatusMessage("Tüm sistemler durduruldu", StatusType.Info);
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

        #region GERÇEK VERİ FORMAT HELPER

        /// <summary>
        /// VLD panosundan okunan gerçek bit sinyallerini kullanıcı dostu formata çevirir.
        /// Fotoğraftaki adres tablosuna göre:
        /// Adres 0x06: DC_Gerilim_Trip (bit4), AC_Gerilim_Trip (bit5), I_RMS_Trip (bit6)
        /// Adres 0x07: Ayirici_Acik (bit0), Ayirici_Kapali (bit1), Toprak_Poz (bit4)
        /// </summary>
        private string FormatVldStatusText(VldData data)
        {
            if (data == null)
                return "Veri okunamadı!";

            if (!data.IsCommunicationActive)
                return $"❌ HABERLEŞME KAYBI\nZaman: {data.Timestamp:HH:mm:ss}";

            // DeviceStatusWord'den bit sinyallerini çöz
            ushort tripWord = (ushort)(data.DeviceStatusWord & 0x00FF);   // Alt byte: Adres 0x06
            ushort switchWord = (ushort)((data.DeviceStatusWord >> 8) & 0x00FF); // Üst byte: Adres 0x07

            bool dcGerilimTrip = (tripWord & (1 << 4)) != 0;
            bool acGerilimTrip = (tripWord & (1 << 5)) != 0;
            bool iRmsTrip = (tripWord & (1 << 6)) != 0;

            bool ayiriciAcik = (switchWord & (1 << 0)) != 0;
            bool ayiriciKapali = (switchWord & (1 << 1)) != 0;
            bool toprakPoz = (switchWord & (1 << 4)) != 0;

            // Ayırıcı durumu metni
            string ayiriciDurum;
            if (ayiriciAcik && !ayiriciKapali)
                ayiriciDurum = "🔓 AÇIK";
            else if (!ayiriciAcik && ayiriciKapali)
                ayiriciDurum = "🔒 KAPALI";
            else if (ayiriciAcik && ayiriciKapali)
                ayiriciDurum = "⚠️ HATA (Çelişkili)";
            else
                ayiriciDurum = "❓ BELİRSİZ";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══════════════════════════════════");
            sb.AppendLine("   VLD PANO DURUM BİLGİSİ");
            sb.AppendLine("═══════════════════════════════════");
            sb.AppendLine($"📅 Zaman: {data.Timestamp:HH:mm:ss.fff}");
            sb.AppendLine($"🔧 Cihaz: {data.DeviceId}");
            sb.AppendLine($"📊 Genel Durum: {data.Status}");
            sb.AppendLine("───────────────────────────────────");
            sb.AppendLine("  TRIP DURUMLARI (Adres 0x06)");
            sb.AppendLine("───────────────────────────────────");
            sb.AppendLine($"  ⚡ DC Gerilim Trip:  {(dcGerilimTrip ? "🔴 AKTİF" : "🟢 Normal")}");
            sb.AppendLine($"  ⚡ AC Gerilim Trip:  {(acGerilimTrip ? "🔴 AKTİF" : "🟢 Normal")}");
            sb.AppendLine($"  🔌 I RMS Trip:       {(iRmsTrip ? "🔴 AKTİF" : "🟢 Normal")}");
            sb.AppendLine("───────────────────────────────────");
            sb.AppendLine("  ANAHTAR DURUMLARI (Adres 0x07)");
            sb.AppendLine("───────────────────────────────────");
            sb.AppendLine($"  🔀 Ayırıcı:          {ayiriciDurum}");
            sb.AppendLine($"  🌍 Toprak Pozisyonu: {(toprakPoz ? "🟡 AKTİF" : "⚪ Pasif")}");
            sb.AppendLine("═══════════════════════════════════");

            return sb.ToString();
        }

        #endregion

        #region EVENT HANDLERS - GÜNCELLENMİŞ VERSİYON

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

                    // EN 50122 uyumluluk kontrolü - YENİ EKLENDİ
                    CheckEN50122Compliance(data);

                    // Veri koleksiyonuna ekle
                    _deviceDataCollections[stationId].Insert(0, data);
                    if (_deviceDataCollections[stationId].Count > 100)
                        _deviceDataCollections[stationId].RemoveAt(_deviceDataCollections[stationId].Count - 1);

                    // Alarm listesini güncelle
                    _deviceAlarms[stationId] = data.ActiveAlarms;

                    // Toplam değerleri hesapla
                    totalPower += data.ActivePower;
                    totalCurrent += data.DcCurrent;
                    if (data.IsCommunicationActive) activeDevices++;

                    // İlgili chart'ı güncelle (index 0-11)
                    if (i < _allCharts.Count)
                    {
                        _allCharts[i].AddValue(data.DcVoltage); // Gerilim grafiği

                        // Header'ı güncelle - EN 50122 uyumlu
                        UpdateStationHeader(stationId, data.Status, data.DcVoltage, data.TouchVoltage);
                    }
                }

                DataCountText.Content = $"TFPR Veri: {_deviceDataCollections.Sum(d => d.Value.Count)}";
                LastUpdateText.Content = $"Son Güncelleme: {DateTime.Now:HH:mm:ss}";

                if (allDevicesData != null && allDevicesData.Any())
                {
                    // EN 50122 standardına göre anomali kontrolü - GÜNCELLENDİ
                    CheckAndLogEN50122Error(allDevicesData);
                }
            });
        }

        // Güncellenmiş UpdateStationHeader metodu
        private void UpdateStationHeader(int stationId, string status, double dcVoltage, double touchVoltage = 0)
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

                // DC voltaj kategorisine göre sembol
                string dcCategory = EN50122Analyzer.GetDcVoltageCategory(dcVoltage);
                string symbol = ColorSituation.GetStatusSymbol(dcCategory);

                // Eğer dokunma gerilimi varsa göster
                double faultDuration = _faultDurations.ContainsKey(stationId) ? _faultDurations[stationId] : 0;
                string touchVoltageInfo = touchVoltage > 10 ? $"\n⚠️ Ute: {touchVoltage:N0}V" : "";

                header.Text = $"{symbol} {stationName}\n{dcVoltage:N0} V{touchVoltageInfo}";

                // Durum rengine göre başlık rengi
                header.Foreground = ColorSituation.GetStatusColor(status);
            }
        }
        private void OnSignalizationDataGenerated(object sender, SignalizationData data)
        {
            Dispatcher.Invoke(() =>
            {
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
                                          train.TrackType == "HAT - 1" &&
                                          train.CurrentBlockId == block.BlockId;

                    bool isDownTrackMatch = block.BlockId >= 101 &&
                                            train.TrackType == "HAT - 2" &&
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
                    (route.BlockSequence.Contains(t.CurrentBlockId) && t.TrackType == "HAT - 1") ||
                    (route.BlockSequence.Contains(t.CurrentBlockId + 100) && t.TrackType == "HAT - 2"));
            }
        }
        #endregion
        #region EN 50122 ERROR LOG FUNCTIONS

        private void CheckAndLogEN50122Error(List<VldData> allDevicesData)
        {
            var anomalyDevices = new List<EN50122AnomalyDevice>();
            string overallCategory = "NORMAL";

            foreach (var data in allDevicesData)
            {
                // Hata süresini al
                double faultDuration = _faultDurations.ContainsKey(data.StationId) ?
                    _faultDurations[data.StationId] : 0;

                // EN 50122 kategorilerini hesapla
                string touchVoltageCategory = EN50122Analyzer.GetTouchVoltageCategory(
                    data.TouchVoltage, faultDuration);
                string dcVoltageCategory = EN50122Analyzer.GetDcVoltageCategory(data.DcVoltage);
                string groundCurrentCategory = EN50122Analyzer.GetGroundCurrentCategory(data.GroundCurrent);

                // Genel kategoriyi belirle
                string deviceOverallCategory = GetMaxCategory(
                    touchVoltageCategory,
                    dcVoltageCategory,
                    groundCurrentCategory);

                // Sadece NORMAL olmayan cihazları log'a ekle
                if (deviceOverallCategory != "NORMAL")
                {
                    // En kritik kategoriyi güncelle
                    if ((deviceOverallCategory == "KIRMIZI" && overallCategory != "KIRMIZI") ||
                        (deviceOverallCategory == "SARI" && overallCategory == "NORMAL"))
                    {
                        overallCategory = deviceOverallCategory;
                    }

                    anomalyDevices.Add(new EN50122AnomalyDevice
                    {
                        DeviceId = data.DeviceId,
                        StationName = data.StationName,
                        StationId = data.StationId,
                        DcVoltage = data.DcVoltage,
                        DcVoltageCategory = dcVoltageCategory,
                        DcPower = data.DcPower,
                        TouchVoltage = data.TouchVoltage,
                        Duration = faultDuration,
                        TouchVoltageCategory = touchVoltageCategory,
                        GroundCurrent = data.GroundCurrent,
                        GroundCurrentCategory = groundCurrentCategory,
                        VoltageOut = data.VoltageOut,
                        Current = data.Current,
                        Kilometer = data.StartPosition / 1000.0,
                        StartPosition = data.StartPosition,
                        EndPosition = data.EndPosition,
                        OverallCategory = deviceOverallCategory,
                        Status = data.Status,
                        Timestamp = data.Timestamp
                    });
                }
            }

            // Anomali varsa log oluştur (minimum 1 saniye süren hatalar için)
            if (anomalyDevices.Any(d => d.Duration >= 1.0 || d.DcVoltageCategory == "KIRMIZI"))
            {
                // Kritik hataları filtrele
                var criticalDevices = anomalyDevices.Where(d =>
                    d.OverallCategory == "KIRMIZI" || d.Duration >= 0.5).ToList();

                if (!criticalDevices.Any())
                    return; // Kritik hata yoksa log oluşturma

                string key = $"EN50122_{overallCategory}_{DateTime.Now:yyyyMMddHHmmss}";

                // Tekrar sayısını hesapla
                int repeatCount = CalculateEN50122RepeatCount(criticalDevices);

                // Bölgedeki trenleri topla
                var trainsInAffectedArea = GetTrainsInAffectedArea(criticalDevices);

                var log = new VldErrorLog
                {
                    Timestamp = DateTime.Now,
                    Category = overallCategory,
                    RepeatCount = repeatCount,
                    AffectedDevices = criticalDevices.Select(d => new EN50122AnomalyDevice
                    {
                        // Temel özellikler
                        DeviceId = d.StationName,
                        Voltage = d.Voltage,
                        Kilometer = d.Kilometer,
                        StartPosition = d.StartPosition,
                        EndPosition = d.EndPosition,
                        DcVoltage = d.DcVoltage,
                        GroundCurrent = d.GroundCurrent,
                        TouchVoltage = d.TouchVoltage,
                        Category = d.OverallCategory,
                        Duration = d.Duration,

                        // YENİ: EN 50122 özellikleri
                        StationName = d.StationName,
                        StationId = d.StationId,
                        DcVoltageCategory = d.DcVoltageCategory,
                        TouchVoltageCategory = d.TouchVoltageCategory,
                        GroundCurrentCategory = d.GroundCurrentCategory,
                        OverallEN50122Category = d.OverallCategory,
                        DcPower = d.DcPower,
                        Timestamp = d.Timestamp,
                        Status = d.Status
                    }).ToList(),
                    AllTrains = trainsInAffectedArea,
                    Standard = "EN 50122-1",

                    // YENİ: EN 50122 özel özellikleri
                    OverallEN50122Category = overallCategory,
                    MaxTouchVoltage = criticalDevices.Max(d => d.TouchVoltage),
                    MaxTouchVoltageDuration = criticalDevices.Max(d => d.Duration),
                    MaxDcVoltageDeviation = criticalDevices.Max(d => Math.Abs(d.DcVoltage - 1500)),
                    MaxGroundCurrent = criticalDevices.Max(d => d.GroundCurrent),
                    CriticalViolationCount = criticalDevices.Count(d => d.OverallCategory == "KIRMIZI"),
                    WarningViolationCount = criticalDevices.Count(d => d.OverallCategory == "SARI"),

                    // YENİ: EN 50122 ihlalleri
                    EN50122Violations = GetEN50122Violations(criticalDevices)
                };

                // En başa ekle (en yeni en üstte)
                _errorLogs.Insert(0, log);

                // Dosyaya yaz
                WriteEN50122LogToFile(log);

                // Eğer panel açıksa güncelle
                if (_isLogPanelOpen)
                {
                    ErrorLogGrid.Items.Refresh();

                    // Yeni satıra kaydır
                    if (ErrorLogGrid.Items.Count > 0)
                    {
                        ErrorLogGrid.ScrollIntoView(ErrorLogGrid.Items[0]);
                    }
                }

                // Çok fazla log varsa temizle
                if (_errorLogs.Count > 100)
                {
                    _errorLogs.RemoveAt(_errorLogs.Count - 1);
                }

                // Debug bilgisi
                Console.WriteLine($"EN50122 Log: {criticalDevices.Count} cihaz, {overallCategory}, " +
                                 $"{trainsInAffectedArea.Count} tren");
            }
        }

        private List<EN50122Violation> GetEN50122Violations(List<EN50122AnomalyDevice> devices)
        {
            var violations = new List<EN50122Violation>();

            foreach (var device in devices)
            {
                // DC Voltaj ihlali
                if (device.DcVoltageCategory != "NORMAL")
                {
                    violations.Add(new EN50122Violation
                    {
                        Type = "DC_VOLTAGE",
                        Category = device.DcVoltageCategory,
                        DeviceId = device.DeviceId,
                        StationName = device.StationName,
                        Value = device.DcVoltage,
                        Duration = 0,
                        Limit = device.DcVoltageCategory == "KIRMIZI" ? 1950 : 1800,
                        DeviationPercent = Math.Abs((device.DcVoltage - 1500) / 1500 * 100),
                        Timestamp = device.Timestamp,
                        Description = $"DC Gerilim {device.DcVoltageCategory}: {device.DcVoltage:N0}V"
                    });
                }

                // Dokunma Gerilimi ihlali
                if (device.TouchVoltageCategory != "NORMAL" && device.Duration > 0)
                {
                    violations.Add(new EN50122Violation
                    {
                        Type = "TOUCH_VOLTAGE",
                        Category = device.TouchVoltageCategory,
                        DeviceId = device.DeviceId,
                        StationName = device.StationName,
                        Value = device.TouchVoltage,
                        Duration = device.Duration,
                        Limit = device.TouchVoltageCategory == "KIRMIZI" ?
                            GetShortTermLimit(device.Duration) :
                            GetLongTermLimit(device.Duration),
                        DeviationPercent = 0,
                        Timestamp = device.Timestamp,
                        Description = $"Dokunma Gerilimi {device.TouchVoltageCategory}: " +
                                    $"{device.TouchVoltage:N0}V/{device.Duration:F1}s"
                    });
                }

                // Toprak Akımı ihlali
                if (device.GroundCurrentCategory != "NORMAL")
                {
                    violations.Add(new EN50122Violation
                    {
                        Type = "GROUND_CURRENT",
                        Category = device.GroundCurrentCategory,
                        DeviceId = device.DeviceId,
                        StationName = device.StationName,
                        Value = device.GroundCurrent,
                        Duration = 0,
                        Limit = device.GroundCurrentCategory == "KIRMIZI" ? 10 : 6,
                        DeviationPercent = 0,
                        Timestamp = device.Timestamp,
                        Description = $"Toprak Akımı {device.GroundCurrentCategory}: {device.GroundCurrent:N1}A"
                    });
                }
            }

            return violations;
        }

        private string GetMaxCategory(params string[] categories)
        {
            if (categories.Contains("KIRMIZI")) return "KIRMIZI";
            if (categories.Contains("SARI")) return "SARI";
            return "NORMAL";
        }

        private int CalculateEN50122RepeatCount(List<EN50122AnomalyDevice> anomalyDevices)
        {
            int maxRepeatCount = 0;
            var activeTrains = _activeTrains.ToList();

            // Tolerans: ±10 metre
            const double POSITION_TOLERANCE = 10.0;

            foreach (var device in anomalyDevices)
            {
                // Bu cihazın kontrol bölgesindeki trenler
                var trainsInSection = activeTrains
                    .Where(train =>
                        train.CurrentPosition >= Math.Min(device.StartPosition, device.EndPosition) &&
                        train.CurrentPosition <= Math.Max(device.StartPosition, device.EndPosition))
                    .ToList();

                foreach (var train in trainsInSection)
                {
                    double currentPosition = train.CurrentPosition;

                    // Mevcut hataya benzer bir hata var mı kontrol et (±10m tolerans)
                    string matchedKey = null;
                    int matchedCount = 0;

                    foreach (var existingKey in _errorRepeatCounts.Keys.ToList())
                    {
                        // Key formatı: "StationId_Category_Position_TrainId"
                        var parts = existingKey.Split('_');

                        if (parts.Length >= 4 &&
                            parts[0] == device.StationId.ToString() &&
                            parts[1] == device.OverallCategory)
                        {
                            // Kayıtlı pozisyonu çıkar
                            if (double.TryParse(parts[2], out double recordedPosition))
                            {
                                // ±10 metre tolerans kontrolü
                                double positionDiff = Math.Abs(currentPosition - recordedPosition);

                                if (positionDiff <= POSITION_TOLERANCE)
                                {
                                    // Aynı tren mi kontrol et
                                    if (parts[3] == train.TrainId.ToString())
                                    {
                                        matchedKey = existingKey;
                                        matchedCount = _errorRepeatCounts[existingKey];
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (matchedKey != null)
                    {
                        // Mevcut kayıt bulundu, sayacı artır
                        _errorRepeatCounts[matchedKey]++;
                        maxRepeatCount = Math.Max(maxRepeatCount, _errorRepeatCounts[matchedKey]);
                    }
                    else
                    {
                        // Yeni kayıt oluştur
                        // Format: "StationId_Category_Position_TrainId"
                        string newKey = $"{device.StationId}_{device.OverallCategory}_{currentPosition:F1}_{train.TrainId}";
                        _errorRepeatCounts[newKey] = 1;
                        maxRepeatCount = Math.Max(maxRepeatCount, 1);
                    }
                }
            }

            // Eğer hiç tren yoksa ama anomali varsa
            if (maxRepeatCount == 0 && anomalyDevices.Any())
            {
                // Tren olmadan oluşan hatalar için basit key
                string deviceKey = string.Join("|",
                    anomalyDevices.Select(d => $"{d.StationId}_{d.OverallCategory}"));

                if (!_errorRepeatCounts.ContainsKey(deviceKey))
                    _errorRepeatCounts[deviceKey] = 0;

                _errorRepeatCounts[deviceKey]++;
                return _errorRepeatCounts[deviceKey];
            }

            return maxRepeatCount;
        }

        private List<TrainInfoLog> GetTrainsInAffectedArea(List<EN50122AnomalyDevice> anomalyDevices)
        {
            var trainsInArea = new List<TrainInfoLog>();
            var activeTrains = _activeTrains.ToList();

            // Tüm hatalı bölgeleri birleştir
            var affectedRanges = anomalyDevices
                .Select(d => new
                {
                    Min = Math.Min(d.StartPosition, d.EndPosition),
                    Max = Math.Max(d.StartPosition, d.EndPosition)
                })
                .ToList();

            foreach (var train in activeTrains)
            {
                // Tren herhangi bir hatalı bölgede mi?
                bool isInAffectedArea = affectedRanges.Any(range =>
                    train.CurrentPosition >= range.Min - 500 &&
                    train.CurrentPosition <= range.Max + 500);

                trainsInArea.Add(new TrainInfoLog
                {
                    TrainId = train.TrainId,
                    TrainName = train.TrainName,
                    Position = train.CurrentPosition,
                    Speed = train.Speed,
                    TrackType = train.TrackType,
                    Status = train.Status
                });

            }

            return trainsInArea;
        }

        private void WriteEN50122LogToFile(VldErrorLog log)
        {
            try
            {
                string nl = Environment.NewLine;

                // Zaman bilgisi
                string line =
                    $"{log.Timestamp:yyyy-MM-dd HH:mm:ss}{nl}" +
                    $"{log.Category}{nl}" +
                    $"Tekrar: {log.RepeatCount}{nl}{nl}";

                // 1. Anomali olan VLD-TFPR cihazları
                if (log.AffectedDevices.Any())
                {
                    line += $"Anomali Cihaz Sayısı: {log.AffectedDevices.Count}{nl}";

                    foreach (var device in log.AffectedDevices)
                    {
                        line +=
                            $" - {device.DeviceId} | " +
                            $"DC: {device.DcVoltage:N0} V | " +
                            $"Ig: {device.GroundCurrent:N1} A{nl}";
                    }

                    line += nl;
                }

                // 2. Tren konumları
                if (_activeTrains.Any())
                {
                    line += $"Tren Konumları:{nl}";

                    foreach (var train in _activeTrains.OrderBy(t => t.CurrentPosition))
                    {
                        line += $" - {train.TrainId}: {train.CurrentPosition:N0} m{nl}";
                    }

                    line += nl;
                }

                // 3. Tren hızları
                if (_activeTrains.Any())
                {
                    line += $"Tren Hızları:{nl}";

                    foreach (var train in _activeTrains)
                    {
                        line += $" - {train.TrainId}: {train.Speed:N0} km/h{nl}";
                    }
                }

                // Dosyaya yaz
                File.AppendAllText(LOG_FILE, line + nl + new string('=', 70) + nl);

                // Konsola yaz
                Console.WriteLine(new string('=', 70));
                Console.WriteLine(line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Log yazma hatası: {ex.Message}");
            }
        }

        private void LogCheckTimer_Tick(object sender, EventArgs e)
        {
            DateTime _lastLogCheck = DateTime.Now;
            try
            {
                // Sadece simülasyon çalışıyorsa kontrol et
                //if (!_vldSimulator.IsRunning)
                //    return;

                // Minimum 2 saniyede bir kontrol et (çok sık log oluşturma)
                if (DateTime.Now.Subtract(_lastLogCheck).TotalSeconds < 2.0)
                    return;


                // Tüm cihazların son verilerini topla
                var allLatestData = new List<VldData>();
                for (int i = 1; i <= 12; i++)
                {
                    var latestData = _deviceDataCollections[i].FirstOrDefault();
                    if (latestData != null)
                    {
                        allLatestData.Add(latestData);
                    }
                }

                // EN 50122 anomali kontrolü yap
                if (allLatestData.Any())
                {
                    CheckAndLogEN50122Error(allLatestData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EN 50122 Log kontrol hatası: {ex.Message}");
            }
        }

        // XAML DataGrid için kolon güncellemesi (isteğe bağlı)
        private void InitializeErrorLogGrid()
        {
            ErrorLogGrid.AutoGenerateColumns = false;
            ErrorLogGrid.Columns.Clear();

            // Kolon tanımlamaları
            ErrorLogGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Zaman",
                Binding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "HH:mm:ss" },
                Width = 80
            });

            ErrorLogGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Kategori",
                Binding = new System.Windows.Data.Binding("Category"),
                Width = 70,
                CellStyle = new Style(typeof(DataGridCell))
                {
                    Setters = {
                new Setter(Control.ForegroundProperty, new Binding("Category") {
                    Converter = new CategoryToColorConverter()
                })
            }
                }
            });

            ErrorLogGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Standart",
                Binding = new System.Windows.Data.Binding("Standard"),
                Width = 90
            });

            ErrorLogGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Özet",
                Binding = new System.Windows.Data.Binding("Summary"),
                Width = 200
            });

            ErrorLogGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Cihaz Sayısı",
                Binding = new System.Windows.Data.Binding("AffectedDevices.Count"),
                Width = 90
            });

            ErrorLogGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Tren Sayısı",
                Binding = new System.Windows.Data.Binding("AllTrains.Count"),
                Width = 90
            });
        }

        // Kategoriye göre renk converter (XAML için)
        public class CategoryToColorConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is string category)
                {
                    return category switch
                    {
                        "KIRMIZI" => Brushes.Red,
                        "SARI" => Brushes.Orange,
                        "NORMAL" => Brushes.Green,
                        _ => Brushes.Gray
                    };
                }
                return Brushes.Gray;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotImplementedException();
            }
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
            // TÜM PANEL'LERİ GÜNCELLE (sadece seçili olanı değil)
            for (int stationId = 1; stationId <= 12; stationId++)
            {
                UpdateDevicePanel(stationId);
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

            var station = _stations.FirstOrDefault(s => s.StationId == stationId);
            if (station == null) return;

            var latestData = _deviceDataCollections[stationId].FirstOrDefault();

            // ---------------------------------------------------------
            // 1. AŞAMA: Arayüz Oluşturma (Sadece ilk seferde çalışır)
            // ---------------------------------------------------------
            if (stackPanel.Tag == null)
            {
                var refs = new DevicePanelRefs();
                stackPanel.Children.Clear(); // Temiz bir başlangıç için

                // Veri Yok Mesajı (Başlangıçta gizli olabilir)
                refs.NoDataText = new TextBlock
                {
                    Text = "Henüz veri yok",
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 10, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                stackPanel.Children.Add(refs.NoDataText);

                // ANA GRID LAYOUT
                refs.MainContentGrid = new Grid();
                refs.MainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3.5, GridUnitType.Star) }); // Sol (Bilgi)
                refs.MainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6.5, GridUnitType.Star) }); // Sağ (Grafik)

                // --- SOL SÜTUN ---
                var leftColumn = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };

                // Helper fonksiyon ile TextBlock referanslarını saklayarak oluşturma
                leftColumn.Children.Add(CreateRefGroupBox("Cihaz Bilgileri", refs,
                    ("DevID", "🔧 Cihaz ID", "#e3f2fd"),
                    ("Loc", "📍 Lokasyon", "#e3f2fd"),
                    ("Region", "📏 Kontrol Bölgesi", "#fff3e0"),
                    ("Trains", "🚆 Bölgedeki Tren", "#e8f5e8"),
                    ("Status", "⚡ Durum", "#e8f5e8"),
                    ("Comm", "📶 İletişim", "#e8f5e8")
                ));

                // DC SİSTEM DEĞERLERİ (GÜNCELLENDİ)
                leftColumn.Children.Add(CreateRefGroupBox("DC Trafo Değerleri", refs,
                    ("ACin", "⚡ AC Giriş (kV)", "#e8f5e8"),
                    ("DCout", "🔋 DC Çıkış (V)", "#e8f5e8"),
                    ("DCCurr", "🔌 DC Akım (A)", "#e3f2fd"),
                    ("DCPow", "📊 DC Güç (kW)", "#fff3e0"),
                    ("GndCurr", "⚡ Toprak Akımı", "#ffebee"),
                    ("TouchV", "⚠️ Dokunma Gerilimi", "#ffebee")
                ));

                // AC SİSTEM DEĞERLERİ
                leftColumn.Children.Add(CreateRefGroupBox("AC Trafo Değerleri (34.5kV)", refs,
                    ("ACV", "AC Gerilim (kV)", "#f3e5f5"),
                    ("ACCurr", "AC Akım (A)", "#e8f5e8"),
                    ("ACPow", "AC Aktif Güç (kW)", "#fff3e0"),
                    ("ReactP", "AC Reaktif Güç (kVAr)", "#fff3e0"),
                    ("PF", "🎯 Güç Faktörü", "#e3f2fd"),
                    ("Freq", "📏 Frekans (Hz)", "#e3f2fd")
                ));

                // FAZ DEĞERLERİ
                leftColumn.Children.Add(CreateRefGroupBox("Faz Değerleri (AC)", refs,
                    ("L1V", "L1 Gerilim (kV)", "#f3e5f5"),
                    ("L2V", "L2 Gerilim (kV)", "#f3e5f5"),
                    ("L3V", "L3 Gerilim (kV)", "#f3e5f5"),
                    ("L1A", "L1 Akım (A)", "#e8f5e8"),
                    ("L2A", "L2 Akım (A)", "#e8f5e8"),
                    ("L3A", "L3 Akım (A)", "#e8f5e8")
                ));

                // SİSTEM PARAMETRELERİ
                leftColumn.Children.Add(CreateRefGroupBox("Sistem Parametreleri", refs,
                    ("Temp", "🌡️ Sıcaklık (°C)", "#ffebee"),
                    ("THDV", "📉 Gerilim THD (%)", "#f3e5f5"),
                    ("THDC", "📉 Akım THD (%)", "#f3e5f5"),
                    ("AuxDC", "🔋 Aux DC (V)", "#e3f2fd"),
                    ("AuxAC", "⚡ Aux AC (V)", "#e3f2fd")
                ));

                // ENERJİ ÖLÇÜMLERİ
                leftColumn.Children.Add(CreateRefGroupBox("Enerji Ölçümleri", refs,
                    ("Imp", "🔋 Tüketilen Enerji (kWh)", "#e8f5e8"),
                    ("ReactImp", "📊 Tüketilen Reaktif (kVArh)", "#fff3e0"),
                    ("DCEner", "🔌 DC Enerji (kWh)", "#e3f2fd")
                ));

                // --- SAĞ SÜTUN (Grafikler ve Canvas) ---
                var rightColumn = new StackPanel();

                // Grafikler Grid (2x2) - DC SİSTEM İÇİN GÜNCELLENDİ
                var chartsGrid = new Grid();
                chartsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) });
                chartsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) });
                chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Grafikleri oluştur ve referanslara kaydet - DC DEĞERLER İÇİN
                refs.PowerChart = CreateChart($"PowerChart{stationId}", "DC Aktif Güç", "Güç (kW)", Brushes.Green, 350000, 750000);
                refs.VoltageChart = CreateChart($"VoltageChart{stationId}", "DC Çıkış Gerilimi", "Gerilim (V)", Brushes.Blue, 1200, 1800);
                refs.CurrentChart = CreateChart($"CurrentChart{stationId}", "DC Akım", "Akım (A)", Brushes.Red, 0, 1500);
                refs.TempChart = CreateChart($"TempChart{stationId}", "Sıcaklık", "Sıcaklık (°C)", Brushes.Orange, -10, 100);

                AddToGrid(chartsGrid, CreateGroupBoxForChart("DC Güç", refs.PowerChart), 0, 0);
                AddToGrid(chartsGrid, CreateGroupBoxForChart("DC Gerilim", refs.VoltageChart), 0, 1);
                AddToGrid(chartsGrid, CreateGroupBoxForChart("DC Akım", refs.CurrentChart), 1, 0);
                AddToGrid(chartsGrid, CreateGroupBoxForChart("Sıcaklık", refs.TempChart), 1, 1);

                rightColumn.Children.Add(chartsGrid);

                // KONTROL BÖLGESİ CANVAS
                var controlGroup = CreateGroupBox("Kontrol Bölgesi");

                // ÖNEMLİ: ClipToBounds = true ile taşmayı engelliyoruz
                var canvasContainer = new Grid { Height = 250, ClipToBounds = true, Background = Brushes.WhiteSmoke, Margin = new Thickness(5) };
                refs.ControlCanvas = new Canvas { Width = double.NaN, Height = double.NaN };
                canvasContainer.Children.Add(refs.ControlCanvas);
                controlGroup.Content = canvasContainer;

                rightColumn.Children.Add(controlGroup);

                // Sütunları ana gride ekle
                Grid.SetColumn(leftColumn, 0);
                Grid.SetColumn(rightColumn, 1);
                refs.MainContentGrid.Children.Add(leftColumn);
                refs.MainContentGrid.Children.Add(rightColumn);

                stackPanel.Children.Add(refs.MainContentGrid);

                // ALT KISIM (Tablo ve Alarmlar)
                var tableGroup = CreateGroupBox("Son 10 Ölçüm - DC Sistem");
                refs.DataGrid = new DataGrid { AutoGenerateColumns = false, Height = 150 };

                // KOLON TANIMLARI - DC SİSTEM İÇİN
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Zaman",
                    Binding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "HH:mm:ss" },
                    Width = 80
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DC Gerilim",
                    Binding = new System.Windows.Data.Binding("DcVoltage") { StringFormat = "N1" },
                    Width = 80
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DC Akım",
                    Binding = new System.Windows.Data.Binding("DcCurrent") { StringFormat = "N0" },
                    Width = 70
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DC Güç",
                    Binding = new System.Windows.Data.Binding("DcPower") { StringFormat = "N0" },
                    Width = 70
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Toprak Akım",
                    Binding = new System.Windows.Data.Binding("GroundCurrent") { StringFormat = "N1" },
                    Width = 80
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Sıcaklık",
                    Binding = new System.Windows.Data.Binding("Temperature") { StringFormat = "N1" },
                    Width = 70
                });

                tableGroup.Content = refs.DataGrid;
                stackPanel.Children.Add(tableGroup);

                refs.AlarmGroup = CreateGroupBox("⚠️ Aktif Alarmlar");
                refs.AlarmPanel = new WrapPanel { Orientation = Orientation.Horizontal };
                refs.AlarmGroup.Content = refs.AlarmPanel;
                refs.AlarmGroup.Visibility = Visibility.Collapsed;
                stackPanel.Children.Add(refs.AlarmGroup);

                // Referansları StackPanel'in Tag özelliğine kaydet
                stackPanel.Tag = refs;
            }

            // ---------------------------------------------------------
            // 2. AŞAMA: Veri Güncelleme (Her timer tick'te çalışır)
            // ---------------------------------------------------------
            var uiRefs = stackPanel.Tag as DevicePanelRefs;

            if (latestData == null)
            {
                uiRefs.MainContentGrid.Visibility = Visibility.Collapsed;
                uiRefs.NoDataText.Visibility = Visibility.Visible;
                return;
            }

            uiRefs.MainContentGrid.Visibility = Visibility.Visible;
            uiRefs.NoDataText.Visibility = Visibility.Collapsed;

            // DC GÜÇ HESAPLAMA
            double dcPower = latestData.DcVoltage * latestData.DcCurrent; // kW * A = kW (zaten kV cinsinden)

            //// DC gerilimi V cinsine çevir (görüntüleme için)
            //double dcVoltageV = latestData.DcVoltage * 1000;

            // Metin Değerlerini Güncelle - DC SİSTEM İÇİN
            UpdateRefText(uiRefs, "DevID", latestData.DeviceId);
            UpdateRefText(uiRefs, "Loc", latestData.Location);
            UpdateRefText(uiRefs, "Region", $"{latestData.StartPosition:N0}m - {latestData.EndPosition:N0}m");
            UpdateRefText(uiRefs, "Trains", latestData.TrainsInSection.ToString());
            UpdateRefText(uiRefs, "Status", latestData.Status);
            UpdateRefText(uiRefs, "Comm", latestData.IsCommunicationActive ? "AKTİF" : "KESİNTİ");

            // DC SİSTEM DEĞERLERİ
            UpdateRefText(uiRefs, "ACin", $"{latestData.VoltageIn:N1} kV");
            UpdateRefText(uiRefs, "DCout", $"{latestData.DcVoltage:N0} V");
            UpdateRefText(uiRefs, "DCCurr", $"{latestData.DcCurrent:N0} A");
            UpdateRefText(uiRefs, "DCPow", $"{dcPower:N0} kW");
            UpdateRefText(uiRefs, "GndCurr", $"{latestData.GroundCurrent:N1} A");
            UpdateRefText(uiRefs, "TouchV", $"{latestData.TouchVoltage:N0} V");

            // AC SİSTEM DEĞERLERİ
            UpdateRefText(uiRefs, "ACV", $"{latestData.VoltageOut:N1} kV");
            UpdateRefText(uiRefs, "ACCurr", $"{latestData.Current:N0} A");
            UpdateRefText(uiRefs, "ACPow", $"{latestData.ActivePower:N0} kW");
            UpdateRefText(uiRefs, "ReactP", $"{latestData.ReactivePower:N0} kVAr");
            UpdateRefText(uiRefs, "PF", $"{latestData.PowerFactor:N2}");
            UpdateRefText(uiRefs, "Freq", $"{latestData.Frequency:N2} Hz");

            // FAZ DEĞERLERİ
            UpdateRefText(uiRefs, "L1V", $"{latestData.VoltageL1:N1} kV");
            UpdateRefText(uiRefs, "L2V", $"{latestData.VoltageL2:N1} kV");
            UpdateRefText(uiRefs, "L3V", $"{latestData.VoltageL3:N1} kV");
            UpdateRefText(uiRefs, "L1A", $"{latestData.CurrentL1:N0} A");
            UpdateRefText(uiRefs, "L2A", $"{latestData.CurrentL2:N0} A");
            UpdateRefText(uiRefs, "L3A", $"{latestData.CurrentL3:N0} A");

            // SİSTEM PARAMETRELERİ
            UpdateRefText(uiRefs, "Temp", $"{latestData.Temperature:N1} °C");
            UpdateRefText(uiRefs, "THDV", $"{latestData.THDVoltage:N1} %");
            UpdateRefText(uiRefs, "THDC", $"{latestData.THDCurrent:N1} %");
            UpdateRefText(uiRefs, "AuxDC", $"{(latestData.AuxDcVoltage * 1000):N0} V"); // kV -> V
            UpdateRefText(uiRefs, "AuxAC", $"{(latestData.AuxAcVoltage * 1000):N0} V"); // kV -> V

            // ENERJİ ÖLÇÜMLERİ
            UpdateRefText(uiRefs, "Imp", $"{latestData.ActiveEnergyImport:N0} kWh");
            UpdateRefText(uiRefs, "ReactImp", $"{latestData.ReactiveEnergyImport:N0} kVArh");
            UpdateRefText(uiRefs, "DCEner", $"{CalculateDcEnergy(latestData):N0} kWh");

            // Grafikleri Güncelle - DC DEĞERLER İLE
            uiRefs.PowerChart.AddValue(dcPower); // DC güç
            uiRefs.VoltageChart.AddValue(latestData.DcVoltage); // DC gerilim (V)
            uiRefs.CurrentChart.AddValue(latestData.DcCurrent); // DC akım
            uiRefs.TempChart.AddValue(latestData.Temperature);

            // Canvas Yeniden Çiz
            uiRefs.ControlCanvas.Children.Clear();
            DrawStationControlArea(uiRefs.ControlCanvas, stationId);

            // Tabloyu Güncelle - DC VERİLER İLE
            uiRefs.DataGrid.ItemsSource = null;
            uiRefs.DataGrid.ItemsSource = _deviceDataCollections[stationId]
                .Select(d => new
                {
                    d.Timestamp,
                    DcVoltage = d.DcVoltage * 1000, // V cinsinden
                    d.DcCurrent,
                    DcPower = d.DcVoltage * d.DcCurrent,
                    d.GroundCurrent,
                    d.Temperature
                })
                .Take(10);

            // Alarmları Güncelle
            if (latestData.ActiveAlarms.Any())
            {
                uiRefs.AlarmGroup.Visibility = Visibility.Visible;
                uiRefs.AlarmPanel.Children.Clear();
                foreach (var alarm in latestData.ActiveAlarms)
                {
                    // Alarm rengini içeriğe göre belirle
                    Brush bgColor = Brushes.MistyRose;
                    Brush fgColor = Brushes.Red;

                    if (alarm.Contains("DOKUNMA_GERILIMI"))
                    {
                        bgColor = Brushes.DarkRed;
                        fgColor = Brushes.White;
                    }
                    else if (alarm.Contains("TOPRAK_ARIZASI"))
                    {
                        bgColor = Brushes.OrangeRed;
                        fgColor = Brushes.White;
                    }
                    else if (alarm.Contains("ASIRI"))
                    {
                        bgColor = Brushes.Orange;
                        fgColor = Brushes.Black;
                    }

                    var border = new Border
                    {
                        Background = bgColor,
                        BorderBrush = Brushes.DarkGray,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(2),
                        Padding = new Thickness(5, 2, 5, 2)
                    };
                    border.Child = new TextBlock
                    {
                        Text = alarm,
                        Foreground = fgColor,
                        FontWeight = FontWeights.Bold,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 200
                    };
                    uiRefs.AlarmPanel.Children.Add(border);
                }
            }
            else
            {
                uiRefs.AlarmGroup.Visibility = Visibility.Collapsed;
            }
        }

        // YARDIMCI FONKSİYON: DC Enerji hesaplama
        private double CalculateDcEnergy(VldData data)
        {
            // Basit DC enerji hesaplama: P * t (kW * saat)
            // Bu sadece örnek, gerçekte zaman bazlı entegrasyon yapılmalı
            double dcPower = data.DcVoltage * data.DcCurrent; // kW
            return dcPower * 0.001; // kWh (1 saniye için yaklaşık)
        }

        // YARDIMCI METODLAR (UpdateDevicePanel içinde kullanılan)

        private GroupBox CreateRefGroupBox(string header, DevicePanelRefs refs, params (string key, string label, string color)[] items)
        {
            var group = CreateGroupBox(header);
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (var item in items)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.color)),
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(3),
                    Width = 140
                };

                var sp = new StackPanel { Margin = new Thickness(5) };
                sp.Children.Add(new TextBlock { Text = item.label, FontSize = 10, Foreground = Brushes.Gray });

                var txtValue = new TextBlock { Text = "-", FontSize = 14, FontWeight = FontWeights.Bold };
                sp.Children.Add(txtValue);

                border.Child = sp;
                panel.Children.Add(border);

                // Referansa kaydet
                refs.ValueTexts[item.key] = txtValue;
            }

            group.Content = panel;
            return group;
        }

        private void UpdateRefText(DevicePanelRefs refs, string key, string value)
        {
            if (refs.ValueTexts.ContainsKey(key))
            {
                refs.ValueTexts[key].Text = value;
            }
        }

        private ChartsProperties CreateChart(string name, string title, string yAxis, Brush color, double min, double max)
        {
            return new ChartsProperties
            {
                Name = name,
                Title = title,
                YAxisTitle = yAxis,
                XAxisTitle = "Zaman",
                MinY = min,
                MaxY = max,
                LineColor = color,
                BackgroundColor = Brushes.White,
                Height = 150
            };
        }

        private GroupBox CreateGroupBoxForChart(string header, UIElement content)
        {
            var gb = CreateGroupBox(header);
            gb.Content = content;
            gb.Margin = new Thickness(2);
            return gb;
        }

        private void AddToGrid(Grid grid, UIElement element, int row, int col)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            grid.Children.Add(element);
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
            double canvasWidth = 500;
            double canvasHeight = 200;
            double upTrackY = canvasHeight / 2 - 25;
            double downTrackY = canvasHeight / 2 + 30;

            // Ölçeklendirme
            double minPos = controlledStationData.Min(s => s.Item2.position);
            double maxPos = controlledStationData.Max(s => s.Item2.position);
            double sectionLength = maxPos - minPos;
            double scaleFactor = (canvasWidth - 100) / (sectionLength - 400);

            // Hat çizgileri
            DrawTrackLines(canvas, sectionLength, scaleFactor, upTrackY, downTrackY);

            // İstasyonları çiz
            DrawStations(canvas, controlledStationData, stationId, minPos, scaleFactor, upTrackY);

            // Mesafe çizgileri
            DrawDistanceLines(canvas, controlledStationData, minPos, scaleFactor, upTrackY);

            // Trenleri çiz
            DrawTrainsInSection(canvas, minPos, maxPos, scaleFactor, upTrackY, downTrackY);

            // Başlık ve etiketler
            DrawLabels(canvas, upTrackY, downTrackY);

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
                X1 = 50 + 200, // +300 eklendi
                Y1 = upTrackY,
                X2 = 50 + (sectionLength * scaleFactor) + 200, // +300 eklendi
                Y2 = upTrackY,
                Stroke = Brushes.Blue,
                StrokeThickness = 4,
                Opacity = 0.8
            };
            canvas.Children.Add(upTrack);

            // Alt hat (Kırmızı - Depo Yönü)
            var downTrack = new Line
            {
                X1 = 50 + 200, // +300 eklendi
                Y1 = downTrackY,
                X2 = 50 + (sectionLength * scaleFactor) + 200, // +300 eklendi
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
                Canvas.SetLeft(stationMarker, xPos + 200);
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
                Canvas.SetLeft(stationNumber, xPos + 201);  // -7 yerine -8 veya -9 deneyebilirsiniz
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
                Canvas.SetLeft(stationName, xPos + 175);
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
                Canvas.SetLeft(kmText, xPos + 220);
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
                    X1 = currentX + 200, // +300 eklendi
                    Y1 = upTrackY - 25,
                    X2 = nextX + 200,    // +300 eklendi
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
                Canvas.SetLeft(distanceText, ((currentX + nextX) / 2 - 15) + 200); // +300 eklendi
                Canvas.SetTop(distanceText, upTrackY - 40);
                canvas.Children.Add(distanceText);
            }
        }

        private void DrawTrainsInSection(Canvas canvas, double minPos, double maxPos,
                                 double scaleFactor, double upTrackY, double downTrackY)
        {
            var trainsInSection = _activeTrains.Where(train =>
            {
                if (train == null) return false;
                double pos = train.CurrentPosition;
                return pos >= minPos - 500 && pos <= maxPos + 500;
            }).ToList();

            foreach (var train in trainsInSection)
            {
                double xPos = 50 + ((train.CurrentPosition - minPos) * scaleFactor);
                double yPos = train.TrackType == "HAT - 1" ? upTrackY : downTrackY;

                // Tren simgesi
                var trainRect = new Rectangle
                {
                    Width = 40,
                    Height = 18,
                    Fill = train.TrackType == "HAT - 1" ? Brushes.Blue : Brushes.Red,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    RadiusX = 5,
                    RadiusY = 5,
                    ToolTip = $"Tren {train.TrainId}\nHız: {train.Speed:0}km/h\nKonum: {train.CurrentPosition:0}m"
                };
                Canvas.SetLeft(trainRect, xPos + 280); // -20 yerine +280 (300-20)
                Canvas.SetTop(trainRect, yPos - 10);
                canvas.Children.Add(trainRect);

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
                Canvas.SetLeft(speedText, xPos + 290); // -10 yerine +290 (300-10)
                Canvas.SetTop(speedText, train.TrackType == "HAT - 1" ? yPos - 35 : yPos + 12);
                canvas.Children.Add(speedText);
            }
        }

        private void DrawLabels(Canvas canvas, double upTrackY, double downTrackY)
        {
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
            Canvas.SetLeft(upLabel, 100);
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
            Canvas.SetLeft(downLabel, 30);
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
            Canvas.SetLeft(deviceMarker, deviceXPos + 200); // -10 yerine +290 (300-10)
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

        #region DETAIL PANEL

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
            string direction = trackType == "HAT - 1" ? "Darıca Sahil Yönü" : "Depo Yönü";
            int startBlockId = trackType == "HAT - 1" ? 1 : 101;
            int gridY = trackType == "HAT - 1" ? (int)UP_TRACK_Y : (int)DOWN_TRACK_Y;

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
                Heading = trackType == "HAT - 1" ? 180 : 0,
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

                        if (train.TrackType == "HAT - 1")
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

                        var block = _blocks.FirstOrDefault(b => b.BlockId == train.CurrentBlockId + (train.TrackType == "HAT - 2" ? 100 : 0));
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
                    AddTrainToTrack(train.TrackType, train.TrackType == "HAT - 1" ? 15391 : 0);
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

            if (currentTrain.TrackType == "HAT - 1")
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

            bool isUp = train.TrackType == "HAT - 1";
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
            _pollingService?.Stop();
            _tcpReader?.Dispose();
            _tcpReader = null;

            _signalizationSimulator.StopSimulation();
            _railwayUpdateTimer.Stop();
            _chartUpdateTimer.Stop();
            _detailUpdateTimer.Stop();
            _logCheckTimer.Stop();

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

        #region VldErrorLog
        private Dictionary<string, DateTime> _voltageStartTimes = new();
        private Dictionary<string, int> _errorRepeatCounts = new();
        private ObservableCollection<VldErrorLog> _errorLogs = new();
        private const string LOG_FILE = "VLD_TFPR_ErrorLog.txt";

        private bool _isLogPanelOpen = false;

        private void ToggleLogPanel(bool open)
        {
            _isLogPanelOpen = open;

            if (open)
            {
                // Panel açılıyor
                RightLogPanel.Visibility = Visibility.Visible;

                // GridSplitter'ı göster
                var splitter = FindVisualChild<GridSplitter>(this);
                if (splitter != null)
                {
                    splitter.Visibility = Visibility.Visible;
                }

                // Sütun genişliğini ayarla (750 piksel veya * kullan)
                RightPanelColumn.Width = new GridLength(785, GridUnitType.Pixel);

                // DataGrid'i güncelle
                ErrorLogGrid.ItemsSource = _errorLogs;
                ErrorLogGrid.Items.Refresh();
                ErrorLogGrid.Items.SortDescriptions.Clear();
                ErrorLogGrid.Items.SortDescriptions.Add(
                    new SortDescription("Timestamp", ListSortDirection.Descending));

                // Animasyon (isteğe bağlı)
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 785,
                    Duration = TimeSpan.FromMilliseconds(300),
                    AccelerationRatio = 0.2,
                    DecelerationRatio = 0.8
                };

                // Sütun genişliğini animasyonla değiştir
                RightPanelColumn.BeginAnimation(WidthProperty, animation);
            }
            else
            {
                // Panel kapanıyor
                var animation = new DoubleAnimation
                {
                    From = RightPanelColumn.ActualWidth,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    AccelerationRatio = 0.2,
                    DecelerationRatio = 0.8
                };

                animation.Completed += (s, e) =>
                {
                    RightLogPanel.Visibility = Visibility.Collapsed;
                    RightPanelColumn.Width = new GridLength(0, GridUnitType.Pixel);

                    // GridSplitter'ı gizle
                    var splitter = FindVisualChild<GridSplitter>(this);
                    if (splitter != null)
                    {
                        splitter.Visibility = Visibility.Collapsed;
                    }
                };

                RightPanelColumn.BeginAnimation(WidthProperty, animation);
            }
        }

        // GridSplitter'ı bulmak için yardımcı metod
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T result)
                    return result;

                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                    return childResult;
            }

            return null;
        }

        private string GetVoltageCategory(double dcVoltage)
        {
            // Müsaade edilebilir limitleri al
            double longLimit = GetLongTermLimit(dcVoltage);
            double shortLimit = GetShortTermLimit(dcVoltage);

            // KIRMIZI — kısa süreli limit aşıldı
            if (dcVoltage > shortLimit)
                return "KIRMIZI";

            // SARI — uzun süreli limit aşıldı ama kısa süreli aşılmadı
            if (dcVoltage > longLimit)
                return "SARI";

            // NORMAL — hiçbir limiti aşmıyor
            return "NORMAL";
        }

        private double GetAllowedDuration(double voltage)
        {
            // Uzun ve kısa süreli limitleri al
            double longLimit = GetLongTermLimit(voltage);
            double shortLimit = GetShortTermLimit(voltage);

            // Eğer kısa süreli limiti aşmış → hiç bekleme yok → anında alarm
            if (voltage > shortLimit)
                return 0.0;

            // Eğer uzun süreli limiti aşmış → cuma süresine bak
            if (voltage > longLimit)
            {
                // Tabloya göre uzun süreli bölge → zaman = 0.7 s
                return 0.7;
            }

            // Hiçbir limit aşılmamış → anomali yok
            return double.MaxValue;
        }

        private double GetLongTermLimit(double voltage)
        {
            // Uzun süreli limit 300V / 120V gibi sabittir
            if (voltage > 300) return 120;
            return 150;
        }

        private double GetShortTermLimit(double voltage)
        {
            // Kısa süreli limit aşağıdaki tabloya göre

            // (t, Ute_kısa)
            var table = new (double t, double u)[]
            {
        (0.7, 350),
        (0.6, 360),
        (0.5, 385),
        (0.4, 420),
        (0.3, 460),
        (0.2, 520),
        (0.1, 625),
        (0.05, 735),
        (0.02, 870)
            };

            // En düşük limit seçilir (yani gereksinim daha katı)
            double minShort = table.Min(row => row.u);
            return minShort;
        }

        private int CalculateRepeatCount(List<EN50122AnomalyDevice> anomalyDevices)
        {
            // Tren konumlarına göre tekrar sayısını hesapla
            int totalRepeatCount = 0;

            // Tüm aktif trenleri al
            var activeTrains = _activeTrains.ToList();

            // Her anomali cihazı için
            foreach (var device in anomalyDevices)
            {
                // Bu cihazın kontrol bölgesindeki trenleri bul
                var trainsInSection = activeTrains
                    .Where(train =>
                        train.CurrentPosition >= Math.Min(device.StartPosition, device.EndPosition) &&
                        train.CurrentPosition <= Math.Max(device.StartPosition, device.EndPosition))
                    .ToList();

                // Her trenin konumuna göre tekrar sayısını hesapla
                foreach (var train in trainsInSection)
                {
                    // Konumu metre cinsinden yuvarla (örn: 12554.3)
                    double roundedPosition = Math.Round(train.CurrentPosition, 1);

                    // Anahtar: Tren konumu + Hata kategorisi
                    string category = GetVoltageCategory(device.DcVoltage);
                    string positionKey = $"{roundedPosition:N1}_{category}";

                    // Eski Dictionary'yi kullanmaya devam et
                    if (!_errorRepeatCounts.ContainsKey(positionKey))
                    {
                        _errorRepeatCounts[positionKey] = 0;
                    }

                    // Tekrar sayısını artır
                    _errorRepeatCounts[positionKey]++;

                    // En yüksek tekrar sayısını sakla
                    totalRepeatCount = Math.Max(totalRepeatCount, _errorRepeatCounts[positionKey]);
                }
            }

            // Eğer hiç tren yoksa veya hata yoksa, mevcut mantığa dön
            if (totalRepeatCount == 0 && anomalyDevices.Any())
            {
                // Eski mantık (konum bazlı)
                string positionKey = string.Join("|",
                    anomalyDevices.Select(d => $"{d.StartPosition:N0}-{d.EndPosition:N0}"));

                if (!_errorRepeatCounts.ContainsKey(positionKey))
                {
                    _errorRepeatCounts[positionKey] = 0;
                }

                _errorRepeatCounts[positionKey]++;
                return _errorRepeatCounts[positionKey];
            }

            return totalRepeatCount;
        }

        // Aç/Kapa butonu
        private void OpenErrorLogWindow_Click(object sender, RoutedEventArgs e)
        {
            ToggleLogPanel(!_isLogPanelOpen);
        }

        // Kapat butonu
        private void CloseLogPanel_Click(object sender, RoutedEventArgs e)
        {
            ToggleLogPanel(false);
        }

        private void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(LOG_FILE))
                System.Diagnostics.Process.Start("notepad.exe", LOG_FILE);
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Tüm logları temizlemek istiyor musunuz?",
                                        "Onay",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _errorLogs.Clear();
                _voltageStartTimes.Clear();
                _errorRepeatCounts.Clear();

                // DataGrid'i yenile
                ErrorLogGrid.ItemsSource = null;
                ErrorLogGrid.ItemsSource = _errorLogs;
            }
        }
        #endregion

        #region EN 50122 VOLTAGE ANALYSIS FUNCTIONS

        private Dictionary<int, DateTime> _faultStartTimes = new Dictionary<int, DateTime>();
        private Dictionary<int, double> _faultDurations = new Dictionary<int, double>();

        // EN 50122-1 Çizelge 6: Dokunma gerilimi limitleri
        private static readonly List<(double timeS, double longTermV, double shortTermV)> _touchVoltageLimits = new()
        {
            // Zaman (s) | Uzun Süreli (V) | Kısa Süreli (V)
            (double.MaxValue, 120, 0),      // > 300 s
            (300, 150, 0),                  // 300 s
            (1, 160, 0),                    // 1 s
            (0.9, 165, 0),                  // 0.9 s
            (0.8, 170, 0),                  // 0.8 s
            (0.7, 175, 0),                  // 0.7 s
            (0.6, 0, 360),                  // 0.6 s (kısa süreli)
            (0.5, 0, 385),                  // 0.5 s
            (0.4, 0, 420),                  // 0.4 s
            (0.3, 0, 460),                  // 0.3 s
            (0.2, 0, 520),                  // 0.2 s
            (0.1, 0, 625),                  // 0.1 s
            (0.05, 0, 735),                 // 0.05 s
            (0.02, 0, 870),                 // 0.02 s
        };

        private string GetTouchVoltageCategory(double touchVoltage, double duration)
        {
            var limits = GetTouchVoltageLimits(duration);

            if (touchVoltage > limits.shortTermLimit && limits.shortTermLimit > 0)
                return "KIRMIZI";

            if (touchVoltage > limits.longTermLimit && limits.longTermLimit > 0)
                return "SARI";

            return "NORMAL";
        }

        private (double longTermLimit, double shortTermLimit) GetTouchVoltageLimits(double duration)
        {
            foreach (var limit in _touchVoltageLimits.OrderBy(l => l.timeS))
            {
                if (duration <= limit.timeS)
                    return (limit.longTermV, limit.shortTermV);
            }

            return (120, 0);
        }

        private string GetDcVoltageCategory(double dcVoltage)
        {
            // EN 50122-1'e göre DC cer sistemleri için gerilim limitleri
            const double NOMINAL_DC_VOLTAGE = 1500.0; // V
            const double NORMAL_TOLERANCE_PERCENT = 20.0; // %20
            const double WARNING_TOLERANCE_PERCENT = 30.0; // %30

            double normalMin = NOMINAL_DC_VOLTAGE * (1 - NORMAL_TOLERANCE_PERCENT / 100);
            double normalMax = NOMINAL_DC_VOLTAGE * (1 + NORMAL_TOLERANCE_PERCENT / 100);
            double warningMin = NOMINAL_DC_VOLTAGE * (1 - WARNING_TOLERANCE_PERCENT / 100);
            double warningMax = NOMINAL_DC_VOLTAGE * (1 + WARNING_TOLERANCE_PERCENT / 100);

            // ALARM (KIRMIZI) - %30'dan fazla sapma
            if (dcVoltage < warningMin || dcVoltage > warningMax)
                return "KIRMIZI";

            // WARNING (SARI) - %20-%30 arası sapma
            if (dcVoltage < normalMin || dcVoltage > normalMax)
                return "SARI";

            // NORMAL - %20 içinde
            return "NORMAL";
        }

        private string GetGroundCurrentCategory(double groundCurrent)
        {
            const double NORMAL_LIMIT = 5.0;     // A
            const double WARNING_LIMIT = 10.0;   // A

            if (groundCurrent > WARNING_LIMIT)
                return "KIRMIZI";

            if (groundCurrent > NORMAL_LIMIT)
                return "SARI";

            return "NORMAL";
        }

        private void CheckEN50122Compliance(VldData data)
        {
            // Hata süresini hesapla - sadece gerçek bir hata varsa
            double faultDuration = 0.0;

            // Gerçek bir toprak arızası kontrolü
            bool isRealFault = data.GroundCurrent >= 3.0 && data.TouchVoltage >= 50;

            if (isRealFault)
            {
                if (!_faultStartTimes.ContainsKey(data.StationId))
                    _faultStartTimes[data.StationId] = DateTime.Now;

                faultDuration = (DateTime.Now - _faultStartTimes[data.StationId]).TotalSeconds;
                _faultDurations[data.StationId] = faultDuration;
            }
            else
            {
                _faultStartTimes.Remove(data.StationId);
                _faultDurations.Remove(data.StationId);
            }

            // EN 50122 analizleri - YENİ FONKSİYONLAR
            string touchVoltageCategory = EN50122Analyzer.GetTouchVoltageCategory(data.TouchVoltage, faultDuration);
            string dcVoltageCategory = EN50122Analyzer.GetDcVoltageCategory(data.DcVoltage);
            string groundCurrentCategory = EN50122Analyzer.GetGroundCurrentCategory(data.GroundCurrent);

            // Genel durum
            string overallStatus = EN50122Analyzer.GetOverallStatus(
                data.TouchVoltage,
                faultDuration,
                data.DcVoltage,
                data.GroundCurrent);

            // Data'nın status'unu güncelle
            data.Status = overallStatus;

            // Sadece gerçek hatalar için alarm ekle
            if (overallStatus == "KIRMIZI")
            {
                // Önceki alarmları temizle (aynı tip alarmları)
                data.ActiveAlarms.RemoveAll(a => a.Contains("EN50122"));

                // Yeni alarm ekle
                data.ActiveAlarms.Add($"[EN50122-KRİTİK] {data.StationName} - " +
                                   $"UDC: {data.DcVoltage:N0}V ({dcVoltageCategory}), " +
                                   $"Ute: {data.TouchVoltage:N0}V/{faultDuration:F1}s ({touchVoltageCategory}), " +
                                   $"IG: {data.GroundCurrent:N1}A ({groundCurrentCategory})");
            }
            else if (overallStatus == "SARI" && faultDuration > 1.0) // 1 saniyeden uzun süren uyarılar
            {
                data.ActiveAlarms.RemoveAll(a => a.Contains("EN50122-UYARI"));

                data.ActiveAlarms.Add($"[EN50122-UYARI] {data.StationName} - " +
                                   $"UDC: {data.DcVoltage:N0}V, " +
                                   $"Ute: {data.TouchVoltage:N0}V/{faultDuration:F1}s");
            }
            else if (overallStatus == "NORMAL")
            {
                // Normal durumda EN50122 alarmlarını temizle
                data.ActiveAlarms.RemoveAll(a => a.Contains("EN50122"));
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            //_vldSimulator?.StopSimulation();
            _signalizationSimulator?.StopSimulation();
            _chartUpdateTimer?.Stop();
            _railwayUpdateTimer?.Stop();
            _detailUpdateTimer?.Stop();
            _logCheckTimer?.Stop();
            base.OnClosed(e);
        }
    }

    public enum StatusType { Info, Warning, Error, Emergency }
}