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
        private const double CANVAS_HEIGHT = 500;
        private const double CANVAS_WIDTH = 2400;
        private const double TRACK_SPACING = 80;
        private const double UP_TRACK_Y = 180;
        private const double DOWN_TRACK_Y = 260;
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

            // Error summary grid'i hata koleksiyonuna bağla (UI hazır olduğunda)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ErrorSummaryGrid.ItemsSource = _errorLogs;
                    ErrorSummaryGrid.Items.SortDescriptions.Clear();
                    ErrorSummaryGrid.Items.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Descending));
                }
                catch { }
            }), DispatcherPriority.Background);

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
        private TfprVldModbusTcpReader? _tcpReader;
        private TfprPollingService? _pollingService;
        private AtsSignalizationModbusReader? _atsReader;
        private System.Timers.Timer? _atsPollingTimer;
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

                    // YENİ: ATS Sinyalizasyon Gerçek Veri Modu
                    _atsReader = new AtsSignalizationModbusReader("192.168.1.20"); // Örnek ATS Modbus Gateway IP
                    _atsPollingTimer = new System.Timers.Timer(2000); // 2 saniyede bir güncelle
                    _atsPollingTimer.Elapsed += (s, ev) => 
                    {
                        if (_atsReader != null)
                        {
                            var signalData = _atsReader.ReadData();
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                OnSignalizationDataGenerated(this, signalData);
                            });
                        }
                    };
                    _atsPollingTimer.Start();

                    ShowStatusMessage("Gerçek Veri Modu (Modbus) başlatıldı", StatusType.Info);
                }
                else
                {
                    // SİMÜLASYON MODU
                    _vldSimulator.StartSimulation();
                    _signalizationSimulator.StartSimulation();
                    ShowStatusMessage("Simülasyon Modu başlatıldı", StatusType.Info);
                }

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

                    _atsPollingTimer?.Stop();
                    _atsPollingTimer?.Dispose();
                    _atsPollingTimer = null;

                    _atsReader?.Dispose();
                    _atsReader = null;
                }
                else
                {
                    _vldSimulator.StopSimulation();
                    _signalizationSimulator.StopSimulation();
                }

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

        private void OnVLDDataGenerated(object? sender, List<VldData> allDevicesData)
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
                        _allCharts[i].AddValue(data.TouchVoltage, data.Current); // Dokunma Gerilimi Ute ve Akım grafiği

                        // Header'ı güncelle - EN 50122 uyumlu
                        UpdateStationHeader(stationId, data.Status, data.DcVoltage, data.TouchVoltage, data.Current);
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
        private void UpdateStationHeader(int stationId, string status, double dcVoltage, double touchVoltage = 0, double current = 0)
        {
            TextBlock? header = null;

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

                // Dokunma gerilimi ve akım bilgisi
                string uteInfo = touchVoltage > 10 ? $"⚠️ Ute: {touchVoltage:N0} V" : $"Ute: {touchVoltage:N0} V";
                string currentInfo = $"\n⚡ Akım: {current:N0} A";

                header.Text = $"{symbol} {stationName}\n{uteInfo}{currentInfo}";

                // Durum rengine göre başlık rengi
                header.Foreground = ColorSituation.GetStatusColor(status);
            }
        }
        private void OnSignalizationDataGenerated(object? sender, SignalizationData data)
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

        private void OnVLDStatusChanged(object? sender, string status)
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

            // Anomali varsa log oluştur (Kritik alarm durumları veya en az 1 saniyedir devam eden uyarılar için)
            if (anomalyDevices.Any(d => d.OverallCategory == "KIRMIZI" || d.Duration >= 1.0))
            {
                // Kritik hataları filtrele
                var criticalDevices = anomalyDevices.Where(d =>
                    d.OverallCategory == "KIRMIZI" || d.Duration >= 0.5).ToList();

                if (!criticalDevices.Any())
                    return; // Kritik hata yoksa log oluşturma

                string key = $"EN50122_{overallCategory}_{DateTime.Now:yyyyMMddHHmmss}";

                // Tekrar sayısını hesapla (aynı konum anahtarı döndürülür)
                string? matchedKeyForRepeat = null;
                int repeatCount = CalculateEN50122RepeatCount(criticalDevices, out matchedKeyForRepeat);

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

                log.LeakLocations = BuildLeakLocations(criticalDevices, matchedKeyForRepeat);
                log.LeakDetected = repeatCount >= 3 || log.LeakLocations.Any(l => l.RepeatCount >= 3);
                log.RepeatCount = log.LeakLocations.Any() ? log.LeakLocations.Max(l => l.RepeatCount) : repeatCount;

                if (log.LeakLocations.Any())
                {
                    var firstLeak = log.LeakLocations.OrderBy(l => l.Position).First();
                    log.LeakPosition = firstLeak.Position;
                    log.LeakTrainId = firstLeak.TrainId;
                }
                else if (repeatCount >= 3 && !string.IsNullOrWhiteSpace(matchedKeyForRepeat))
                {
                    var parts = matchedKeyForRepeat.Split('_');
                    if (parts.Length >= 4)
                    {
                        if (double.TryParse(parts[2], out double pos)) log.LeakPosition = pos;
                        if (int.TryParse(parts[3], out int tid)) log.LeakTrainId = tid;
                    }
                }

                // En başa ekle (en yeni en üstte)
                _errorLogs.Insert(0, log);

                // Dosyaya yaz
                WriteEN50122LogToFile(log);

                // Hata özeti tablosunu yenile
                ErrorSummaryGrid.Items.Refresh();
                if (ErrorSummaryGrid.Items.Count > 0)
                {
                    ErrorSummaryGrid.ScrollIntoView(ErrorSummaryGrid.Items[0]);
                }

                // Çok fazla log varsa temizle
                if (_errorLogs.Count > 100)
                {
                    _errorLogs.RemoveAt(_errorLogs.Count - 1);
                }

                // Toast bildirimi göster
                string toastMsg = $"⚡ EN 50122 İhlali: {log.StationNames}\n" +
                                  $"Kategori: {log.OverallCategory} | " +
                                  $"Ute: {log.MaxTouchVoltage:N0} V | " +
                                  $"{log.CriticalDeviceCount} cihaz";
                ShowToastNotification(toastMsg, isError: true);

                // Ray görüntüsünü güncelle (kaçak marker'ları için)
                Dispatcher.Invoke(() => DrawRailwaySystem());
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

        private int CalculateEN50122RepeatCount(List<EN50122AnomalyDevice> anomalyDevices, out string? matchedKey)
        {
            int maxRepeatCount = 0;
            var activeTrains = _activeTrains.ToList();
            matchedKey = null;

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
                    string? localMatchedKey = null;
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
                                        localMatchedKey = existingKey;
                                        matchedCount = _errorRepeatCounts[existingKey];
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (localMatchedKey != null)
                    {
                        // Mevcut kayıt bulundu, sayacı artır
                        _errorRepeatCounts[localMatchedKey]++;
                        int val = _errorRepeatCounts[localMatchedKey];
                        if (val > maxRepeatCount)
                        {
                            maxRepeatCount = val;
                            matchedKey = localMatchedKey;
                        }
                    }
                    else
                    {
                        // Yeni kayıt oluştur
                        // Format: "StationId_Category_Position_TrainId"
                        string newKey = $"{device.StationId}_{device.OverallCategory}_{currentPosition:F1}_{train.TrainId}";
                        _errorRepeatCounts[newKey] = 1;
                        if (maxRepeatCount < 1)
                        {
                            maxRepeatCount = 1;
                            matchedKey = newKey;
                        }
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
                matchedKey = deviceKey;
                return _errorRepeatCounts[deviceKey];
            }

            return maxRepeatCount;
        }

        private double GetDcStationPosition(string stationName)
        {
            return stationName switch
            {
                "Depo" => 15000,
                "OSB" => 14000,
                "Mutlukent" => 12000,
                "Akse Sapağı" => 9000,
                "TCDD Gar" => 4000,
                "Darıca Cumhuriyet" => 1000,
                "Darıca Sahil" => 0,
                _ => 0
            };
        }

        private List<LeakLocationInfo> BuildLeakLocations(List<EN50122AnomalyDevice> anomalyDevices, string? matchedKeyForRepeat)
        {
            var groups = new Dictionary<double, LeakLocationInfo>();
            var dcStations = new Dictionary<string, double>
            {
                ["Depo"] = 15000,
                ["OSB"] = 14000,
                ["Mutlukent"] = 12000,
                ["Akse Sapağı"] = 9000,
                ["TCDD Gar"] = 4000,
                ["Darıca Cumhuriyet"] = 1000,
                ["Darıca Sahil"] = 0
            };

            foreach (var device in anomalyDevices)
            {
                var minPos = Math.Min(device.StartPosition, device.EndPosition);
                var maxPos = Math.Max(device.StartPosition, device.EndPosition);

                foreach (var train in _activeTrains.Where(t =>
                             t.CurrentPosition >= minPos - 250 && t.CurrentPosition <= maxPos + 250))
                {
                    double pos = Math.Round(train.CurrentPosition, 0);
                    var key = Math.Round(pos / 10.0, MidpointRounding.AwayFromZero) * 10.0;

                    if (!groups.ContainsKey(key))
                    {
                        groups[key] = new LeakLocationInfo
                        {
                            Position = key,
                            RepeatCount = 0,
                            TrainId = train.TrainId,
                            TrainStatus = train.Status,
                            TrackType = train.TrackType,
                            StationName = device.StationName,
                            Category = device.OverallCategory,
                            NearestDcStation = string.Empty,
                            NearestDcStationDistanceMeters = 0
                        };
                    }

                    groups[key].RepeatCount++;
                    groups[key].Category = device.OverallCategory;
                    groups[key].StationName = device.StationName;
                    groups[key].TrainId = train.TrainId;
                    groups[key].TrainStatus = train.Status;
                    groups[key].TrackType = train.TrackType;
                }
            }

            if (!groups.Any() && !string.IsNullOrWhiteSpace(matchedKeyForRepeat))
            {
                var parts = matchedKeyForRepeat.Split('_');
                if (parts.Length >= 4 && double.TryParse(parts[2], out double keyPos))
                {
                    groups[keyPos] = new LeakLocationInfo
                    {
                        Position = keyPos,
                        RepeatCount = 1,
                        TrainId = int.TryParse(parts[3], out int tid) ? tid : 0,
                        TrainStatus = _activeTrains.FirstOrDefault(t => t.TrainId == (int.TryParse(parts[3], out int parsedTrainId) ? parsedTrainId : 0))?.Status ?? "MOVING",
                        StationName = "Belirsiz",
                        Category = parts[1],
                        NearestDcStation = string.Empty,
                        NearestDcStationDistanceMeters = 0
                    };
                }
            }

            foreach (var item in groups.Values)
            {
                var nearest = dcStations
                    .Select(s => new { Station = s.Key, Distance = Math.Abs(item.Position - s.Value) })
                    .OrderBy(s => s.Distance)
                    .First();

                item.NearestDcStation = nearest.Station;
                item.NearestDcStationDistanceMeters = nearest.Distance;
            }

            return groups.Values
                .OrderBy(v => v.Position)
                .ToList();
        }

        private string GetNearestDcStationName(double position)
        {
            var dcStations = new Dictionary<string, double>
            {
                ["Depo"] = 15000,
                ["OSB"] = 14000,
                ["Mutlukent"] = 12000,
                ["Akse Sapağı"] = 9000,
                ["TCDD Gar"] = 4000,
                ["Darıca Cumhuriyet"] = 1000,
                ["Darıca Sahil"] = 0
            };

            var nearest = dcStations
                .Select(s => new { Station = s.Key, Distance = Math.Abs(position - s.Value) })
                .OrderBy(s => s.Distance)
                .First();

            return nearest.Station;
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

                // Kaçak bilgisi
                if (log.LeakDetected)
                {
                    line += nl + $"KAÇAK TESPİTİ: {log.LeakPosition:N0} m | Tren: {log.LeakTrainId}{nl}";
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

        private void LogCheckTimer_Tick(object? sender, EventArgs e)
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
            ErrorSummaryGrid.AutoGenerateColumns = false;
            ErrorSummaryGrid.Columns.Clear();
            ErrorSummaryGrid.ItemsSource = _errorLogs;
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

        private void RailwayUpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateTrainPositions();
            UpdateTrainCountsInSections();
            DrawRailwaySystem();
        }

        private void DetailUpdateTimer_Tick(object? sender, EventArgs e)
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

                // Grafikleri oluştur ve referanslara kaydet - UTE VE DC DEĞERLER İÇİN
                refs.PowerChart = CreateChart($"PowerChart{stationId}", "DC Aktif Güç", "Güç (kW)", Brushes.Green, 350000, 750000);
                refs.VoltageChart = CreateChart($"VoltageChart{stationId}", "Ute", "Ute (V) / Akım (A)", Brushes.Red, 0, 1500);
                refs.VoltageChart.SecondaryLineColor = Brushes.Blue; // Akım için ikinci renk
                refs.CurrentChart = CreateChart($"CurrentChart{stationId}", "DC Akım", "Akım (A)", Brushes.Blue, 0, 1500);
                refs.TempChart = CreateChart($"TempChart{stationId}", "Sıcaklık", "Sıcaklık (°C)", Brushes.Orange, -10, 100);

                AddToGrid(chartsGrid, CreateGroupBoxForChart("DC Güç", refs.PowerChart), 0, 0);
                AddToGrid(chartsGrid, CreateGroupBoxForChart("Ute", refs.VoltageChart), 0, 1);
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

                // KOLON TANIMLARI - UTE DOKUNMA GERİLİMİ VE AKIM DEĞERLERİ İLE
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Zaman",
                    Binding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "HH:mm:ss" },
                    Width = 70
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Ute (V)",
                    Binding = new System.Windows.Data.Binding("TouchVoltage") { StringFormat = "N1" },
                    Width = 70
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Toprak Akım (A)",
                    Binding = new System.Windows.Data.Binding("GroundCurrent") { StringFormat = "N1" },
                    Width = 90
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DC Akım (A)",
                    Binding = new System.Windows.Data.Binding("DcCurrent") { StringFormat = "N0" },
                    Width = 75
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DC Gerilim (V)",
                    Binding = new System.Windows.Data.Binding("DcVoltage") { StringFormat = "N1" },
                    Width = 85
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DC Güç (kW)",
                    Binding = new System.Windows.Data.Binding("DcPower") { StringFormat = "N0" },
                    Width = 75
                });
                refs.DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Sıcaklık (°C)",
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
            if (uiRefs == null)
                return;

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

            // Grafikleri Güncelle - UTE VE DC DEĞERLER İLE
            uiRefs.PowerChart.AddValue(dcPower); // DC güç
            uiRefs.VoltageChart.AddValue(latestData.TouchVoltage, latestData.Current); // Dokunma gerilimi Ute (V) ve Akım (A)
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

        private StationInfo? GetApproachingStation(TrainInfo train)
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

        #region PAN VE ZOOM KONTROLLERİ

        private bool _isPanning;
        private Point _panOrigin;
        private double _startTranslateX;
        private double _startTranslateY;

        private void Canvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            
            // Eğer doğrudan bir trene (Border nesnesi) tıklanmadıysa pan işlemini başlat.
            var srcBorder = e.OriginalSource as Border;
            if (!(srcBorder != null && srcBorder.ToolTip != null) && !(e.OriginalSource is TextBlock))
            {
                _isPanning = true;
                _panOrigin = e.GetPosition(border);
                _startTranslateX = CanvasTranslateTransform.X;
                _startTranslateY = CanvasTranslateTransform.Y;
                border.CaptureMouse();
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border != null && _isPanning)
            {
                _isPanning = false;
                border.ReleaseMouseCapture();
            }
        }

        private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isPanning) return;

            var border = sender as Border;
            if (border == null) return;

            Point currentPos = e.GetPosition(border);
            double deltaX = currentPos.X - _panOrigin.X;
            double deltaY = currentPos.Y - _panOrigin.Y;

            CanvasTranslateTransform.X = _startTranslateX + deltaX;
            CanvasTranslateTransform.Y = _startTranslateY + deltaY;
        }

        private void Canvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            double zoomFactor = 1.1;
            if (e.Delta < 0) zoomFactor = 1.0 / zoomFactor;

            // Farenin bulunduğu noktayı al
            Point mousePos = e.GetPosition(RailwayCanvas);

            // Yeni scale değerini hesapla
            double newScaleX = CanvasScaleTransform.ScaleX * zoomFactor;
            double newScaleY = CanvasScaleTransform.ScaleY * zoomFactor;

            // Scale limitleri (çok fazla küçülmeyi ve büyümeyi engelle)
            if (newScaleX < 0.2 || newScaleX > 5.0) return;

            // Merkez noktayı fare imlecinin olduğu yer olarak ayarla
            CanvasTranslateTransform.X -= mousePos.X * (newScaleX - CanvasScaleTransform.ScaleX);
            CanvasTranslateTransform.Y -= mousePos.Y * (newScaleY - CanvasScaleTransform.ScaleY);

            CanvasScaleTransform.ScaleX = newScaleX;
            CanvasScaleTransform.ScaleY = newScaleY;
        }

        #endregion

        #region ÇİZİM SİSTEMİ

        private bool _isStaticDrawn = false;
        private Dictionary<int, Border> _trainUIElements = new Dictionary<int, Border>();
        private List<UIElement> _leakUIElements = new List<UIElement>();

        private void DrawRailwaySystem()
        {
            try
            {
                if (_blocks.Count == 0 || _stations.Count == 0) return;
                double scaleFactor = (CANVAS_WIDTH - 120) / TOTAL_TRACK_LENGTH;

                if (!_isStaticDrawn)
                {
                    RailwayCanvas.Children.Clear();
                    DrawDoubleTrackSystem(scaleFactor);
                    DrawGridAndScale(scaleFactor);
                    foreach (var station in _stations) DrawStation(station, scaleFactor);
                    DrawTrackLabels(scaleFactor);
                    _isStaticDrawn = true;
                }

                UpdateTrainsUI(scaleFactor);
                // Kaçak marker'larını çiz
                DrawLeakMarkers(scaleFactor);
            }
            catch (Exception) { }
        }

        private string GetLeakTooltipText(VldErrorLog leak, double meterPosition)
        {
            var nearestStation = _stations
                .OrderBy(s => Math.Abs(s.GridX - meterPosition))
                .FirstOrDefault();

            var nearestDcStation = VldErrorLog.GetNearestDcStationName(meterPosition);
            var nearestDcStationDistance = VldErrorLog.GetNearestDcStationDistance(meterPosition);

            var lastTrainPassText = leak.LeakLocations
                .Where(l => Math.Abs(l.Position - meterPosition) <= 50)
                .Select(l => $"{l.TrainId} / {l.TrainStatus}")
                .FirstOrDefault();

            var passTime = DateTime.Now.ToString("dd.MM HH:mm:ss");
            if (!string.IsNullOrWhiteSpace(lastTrainPassText))
            {
                passTime = DateTime.Now.ToString("dd.MM HH:mm:ss");
            }

            return $"Son tren geçişi: {passTime}\n" +
                   $"Metraj: {meterPosition:N0} m\n" +
                   $"En yakın istasyon: {(nearestStation != null ? nearestStation.StationName : "—")}\n" +
                   $"En yakın CER istasyonu: {nearestDcStation} ({nearestDcStationDistance:N0} m)\n" +
                   $"Hata: {leak.OverallEN50122Category} | {leak.MaxTouchVoltage:N0} V / {leak.MaxGroundCurrent:N1} A";
        }

        private void DrawLeakMarkers(double scaleFactor)
        {
            foreach (var el in _leakUIElements)
            {
                RailwayCanvas.Children.Remove(el);
            }
            _leakUIElements.Clear();

            double x0 = 60;

            foreach (var leak in _errorLogs)
            {
                if (!leak.LeakDetected || !leak.LeakPosition.HasValue) continue;

                double pos = leak.LeakPosition.Value;
                double x = x0 + pos * scaleFactor;

                var matchingLeak = leak.LeakLocations
                    .OrderBy(l => Math.Abs(l.Position - pos))
                    .FirstOrDefault(l => Math.Abs(l.Position - pos) <= 50)
                    ?? new LeakLocationInfo
                    {
                        Position = pos,
                        TrackType = _activeTrains
                            .Where(t => Math.Abs(t.CurrentPosition - pos) <= 80)
                            .OrderBy(t => Math.Abs(t.CurrentPosition - pos))
                            .Select(t => t.TrackType)
                            .FirstOrDefault() ?? "HAT - 1",
                        TrainStatus = "MOVING"
                    };

                bool onUpperTrack = matchingLeak.TrackType == "HAT - 1";
                double yTop = onUpperTrack ? UP_TRACK_Y - 16 : DOWN_TRACK_Y - 16;
                double yBottom = onUpperTrack ? UP_TRACK_Y + 16 : DOWN_TRACK_Y + 16;

                var category = string.IsNullOrWhiteSpace(leak.OverallEN50122Category)
                    ? leak.EffectiveCategory
                    : leak.OverallEN50122Category;

                var markerBrush = category switch
                {
                    "KIRMIZI" => Brushes.Red,
                    "SARI" => Brushes.Orange,
                    "YEŞİL" => Brushes.Green,
                    "NORMAL" => Brushes.Green,
                    _ => Brushes.Green
                };

                var crossingTrain = _activeTrains
                    .Where(t => Math.Abs(t.CurrentPosition - pos) <= 60)
                    .OrderBy(t => Math.Abs(t.CurrentPosition - pos))
                    .FirstOrDefault();

                bool isBlinking = crossingTrain != null;
                bool blinkVisible = !isBlinking || ((DateTime.Now.Millisecond / 250) % 2 == 0);

                var marker = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = yTop,
                    Y2 = yBottom,
                    Stroke = markerBrush,
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Flat,
                    StrokeEndLineCap = PenLineCap.Flat,
                    Opacity = blinkVisible ? 1.0 : 0.2,
                    ToolTip = new ToolTip
                    {
                        Content = GetLeakTooltipText(leak, pos) + $"\nRay: {matchingLeak.TrackType}",
                        Background = Brushes.White,
                        Foreground = Brushes.Black,
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8),
                        MaxWidth = 300
                    }
                };

                Panel.SetZIndex(marker, 35);
                RailwayCanvas.Children.Add(marker);
                _leakUIElements.Add(marker);
            }
        }

        private void UpdateTrainsUI(double scaleFactor)
        {
            var activeTrainIds = _activeTrains.Select(t => t.TrainId).ToList();

            // Kaldırılan trenleri sil
            var idsToRemove = _trainUIElements.Keys.Except(activeTrainIds).ToList();
            foreach (var id in idsToRemove)
            {
                RailwayCanvas.Children.Remove(_trainUIElements[id]);
                _trainUIElements.Remove(id);
            }

            // Mevcut trenleri güncelle veya yeni ekle
            foreach (var train in _activeTrains.ToList())
            {
                if (!_trainUIElements.TryGetValue(train.TrainId, out var body))
                {
                    body = CreateTrainUI(train, scaleFactor);
                    _trainUIElements[train.TrainId] = body;
                    RailwayCanvas.Children.Add(body);
                }
                else
                {
                    UpdateTrainUI(body, train, scaleFactor);
                }
            }
        }

        private void DrawDoubleTrackSystem(double scaleFactor)
        {
            double scaledLength = TOTAL_TRACK_LENGTH * scaleFactor;
            double x0 = 60;

            // Üst hat gölgesi
            var upGlow = new Line
            {
                X1 = x0, Y1 = UP_TRACK_Y, X2 = x0 + scaledLength, Y2 = UP_TRACK_Y,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 66, 165, 245)),
                StrokeThickness = 12
            };
            RailwayCanvas.Children.Add(upGlow);

            // Üst hat çizgisi
            var upTrack = new Line
            {
                X1 = x0, Y1 = UP_TRACK_Y, X2 = x0 + scaledLength, Y2 = UP_TRACK_Y,
                Stroke = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 20, 3 }
            };
            RailwayCanvas.Children.Add(upTrack);

            // Alt hat gölgesi
            var downGlow = new Line
            {
                X1 = x0, Y1 = DOWN_TRACK_Y, X2 = x0 + scaledLength, Y2 = DOWN_TRACK_Y,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 239, 83, 80)),
                StrokeThickness = 12
            };
            RailwayCanvas.Children.Add(downGlow);

            // Alt hat çizgisi
            var downTrack = new Line
            {
                X1 = x0, Y1 = DOWN_TRACK_Y, X2 = x0 + scaledLength, Y2 = DOWN_TRACK_Y,
                Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 20, 3 }
            };
            RailwayCanvas.Children.Add(downTrack);
        }

        private void DrawStation(StationInfo station, double scaleFactor)
        {
            double xPos = 60 + (station.GridX * scaleFactor);
            double topY = UP_TRACK_Y - 12;
            double botY = DOWN_TRACK_Y + 12;

            // İstasyon dikey bağlantı çizgisi (iki hat arasında)
            var connector = new Line
            {
                X1 = xPos, Y1 = topY, X2 = xPos, Y2 = botY,
                Stroke = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            RailwayCanvas.Children.Add(connector);

            // Üst peron marker (yuvarlak)
            var upMarker = new Ellipse
            {
                Width = 12, Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                Stroke = Brushes.White, StrokeThickness = 2
            };
            Canvas.SetLeft(upMarker, xPos - 6);
            Canvas.SetTop(upMarker, UP_TRACK_Y - 6);
            Panel.SetZIndex(upMarker, 50);
            RailwayCanvas.Children.Add(upMarker);

            // Alt peron marker (yuvarlak)
            var downMarker = new Ellipse
            {
                Width = 12, Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                Stroke = Brushes.White, StrokeThickness = 2
            };
            Canvas.SetLeft(downMarker, xPos - 6);
            Canvas.SetTop(downMarker, DOWN_TRACK_Y - 6);
            Panel.SetZIndex(downMarker, 50);
            RailwayCanvas.Children.Add(downMarker);

            // İstasyon adı (üst tarafta, eğik)
            var nameText = new TextBlock
            {
                Text = station.StationName,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42)),
                RenderTransform = new RotateTransform(-35)
            };
            Canvas.SetLeft(nameText, xPos - 8);
            Canvas.SetTop(nameText, UP_TRACK_Y - 80);
            Panel.SetZIndex(nameText, 60);
            RailwayCanvas.Children.Add(nameText);

            // Kilometre etiketi (alt tarafta)
            double km = station.GridX / 1000.0;
            var kmText = new TextBlock
            {
                Text = $"{km:0.0} km",
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(kmText, xPos - 16);
            Canvas.SetTop(kmText, DOWN_TRACK_Y + 18);
            RailwayCanvas.Children.Add(kmText);
        }

        private Border CreateTrainUI(TrainInfo train, double scaleFactor)
        {
            bool isUp = train.TrackType == "HAT - 1";
            double yPos = isUp ? UP_TRACK_Y : DOWN_TRACK_Y;

            // Renk belirleme
            Color trainBodyColor;
            Color trainBorderColor;
            if (train.Status == "STOPPED" || train.Status == "WAITING")
            {
                trainBodyColor = Color.FromRgb(0xFF, 0xA7, 0x26); // Turuncu
                trainBorderColor = Color.FromRgb(0xE6, 0x8A, 0x00);
            }
            else if (isUp)
            {
                trainBodyColor = Color.FromRgb(0x29, 0xB6, 0xF6); // Açık mavi
                trainBorderColor = Color.FromRgb(0x01, 0x88, 0xD1);
            }
            else
            {
                trainBodyColor = Color.FromRgb(0xEF, 0x53, 0x50); // Kırmızı
                trainBorderColor = Color.FromRgb(0xC6, 0x28, 0x28);
            }

            // Tren gövdesi (yuvarlatılmış dikdörtgen)
            var body = new Border
            {
                Width = 48,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(trainBodyColor),
                BorderBrush = new SolidColorBrush(trainBorderColor),
                BorderThickness = new Thickness(1.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = trainBodyColor,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.6
                }
            };

            // Tren üzerindeki yön oku ve ID
            var bodyContent = new TextBlock
            {
                Text = isUp ? $"◀ {train.TrainName}" : $"{train.TrainName} ▶",
                Foreground = Brushes.White,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            body.Child = bodyContent;

            // Fare hover efektleri
            body.MouseEnter += (s, e) =>
            {
                body.RenderTransform = new ScaleTransform(1.15, 1.15, 24, 10);
                Panel.SetZIndex(body, 500);
            };
            body.MouseLeave += (s, e) =>
            {
                body.RenderTransform = null;
                Panel.SetZIndex(body, 200);
            };

            // Zengin ToolTip oluştur (ilk kez)
            var tooltip = new ToolTip
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(trainBorderColor),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(0),
                HasDropShadow = true
            };
            body.ToolTip = tooltip;

            UpdateTrainUI(body, train, scaleFactor);

            return body;
        }

        private void UpdateTrainUI(Border body, TrainInfo train, double scaleFactor)
        {
            double xPos = 60 + (train.CurrentPosition * scaleFactor);
            
            // Ekran dışındaysa gizle
            if (xPos < -60 || xPos > CANVAS_WIDTH + 60)
            {
                body.Visibility = Visibility.Hidden;
                return;
            }
            body.Visibility = Visibility.Visible;

            bool isUp = train.TrackType == "HAT - 1";
            double yPos = isUp ? UP_TRACK_Y : DOWN_TRACK_Y;

            Canvas.SetLeft(body, xPos - 24);
            Canvas.SetTop(body, yPos - 10);

            // Renk ve yön içeriğini (hareket sırasında durum değişebilir) güncelle
            Color trainBodyColor;
            Color trainBorderColor;
            if (train.Status == "STOPPED" || train.Status == "WAITING")
            {
                trainBodyColor = Color.FromRgb(0xFF, 0xA7, 0x26);
                trainBorderColor = Color.FromRgb(0xE6, 0x8A, 0x00);
            }
            else if (isUp)
            {
                trainBodyColor = Color.FromRgb(0x29, 0xB6, 0xF6);
                trainBorderColor = Color.FromRgb(0x01, 0x88, 0xD1);
            }
            else
            {
                trainBodyColor = Color.FromRgb(0xEF, 0x53, 0x50);
                trainBorderColor = Color.FromRgb(0xC6, 0x28, 0x28);
            }

            body.Background = new SolidColorBrush(trainBodyColor);
            body.BorderBrush = new SolidColorBrush(trainBorderColor);
            
            if (body.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
            {
                shadow.Color = trainBodyColor;
            }

            if (body.Child is TextBlock bodyContent)
            {
                bodyContent.Text = isUp ? $"◀ {train.TrainName}" : $"{train.TrainName} ▶";
            }

            // En yakın istasyonu bul
            string nearestStationName = "—";
            double nearestStationDist = double.MaxValue;
            foreach (var st in _stations)
            {
                double d = Math.Abs(train.CurrentPosition - st.GridX);
                if (d < nearestStationDist)
                {
                    nearestStationDist = d;
                    nearestStationName = st.StationName;
                }
            }

            // Durum metni
            string statusEmoji;
            string statusText;
            switch (train.Status)
            {
                case "STOPPED":
                    statusEmoji = "🔴";
                    statusText = "İstasyonda Durdu";
                    break;
                case "WAITING":
                    statusEmoji = "🟡";
                    statusText = "Bekliyor (Önde Tren)";
                    break;
                default:
                    statusEmoji = "🟢";
                    statusText = "Seyir Halinde";
                    break;
            }

            string hatYonu = isUp ? "← Darıca Sahil Yönü (HAT-1)" : "→ İdari Bina Yönü (HAT-2)";
            double kmPos = train.CurrentPosition / 1000.0;

            if (body.ToolTip is ToolTip tooltip)
            {
                tooltip.BorderBrush = new SolidColorBrush(trainBorderColor);

                var tooltipPanel = new StackPanel { Margin = new Thickness(12, 10, 12, 10), MinWidth = 240 };

                // Başlık
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(trainBodyColor),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                headerBorder.Child = new TextBlock
                {
                    Text = $"🚆 {train.TrainName}  (ID: {train.TrainId})",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                tooltipPanel.Children.Add(headerBorder);

                // Bilgi satırları
                AddTooltipRow(tooltipPanel, "Durum", $"{statusEmoji} {statusText}");
                AddTooltipRow(tooltipPanel, "Hız", $"{train.Speed:0.0} km/h");
                AddTooltipRow(tooltipPanel, "Konum", $"{train.CurrentPosition:N0} m  ({kmPos:0.00} km)");
                AddTooltipRow(tooltipPanel, "Hat / Yön", hatYonu);
                AddTooltipRow(tooltipPanel, "En Yakın İstasyon", $"{nearestStationName} ({nearestStationDist:N0} m)");
                AddTooltipRow(tooltipPanel, "Yolcu Sayısı", $"{train.PassengerCount} kişi");
                AddTooltipRow(tooltipPanel, "Blok No", $"{train.CurrentBlockId}");
                AddTooltipRow(tooltipPanel, "Tren Uzunluğu", $"{train.TrainLength:0} m");
                AddTooltipRow(tooltipPanel, "Son Güncelleme", $"{train.LastUpdateTime:HH:mm:ss}");

                tooltip.Content = tooltipPanel;
            }
        }

        private void AddTooltipRow(StackPanel panel, string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = label + ": ",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9)),
                FontFamily = new FontFamily("Segoe UI"),
                Width = 120
            });
            row.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 11,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            });
            panel.Children.Add(row);
        }

        private void DrawGridAndScale(double scaleFactor)
        {
            // Kilometre çizgileri (her 1km'de bir)
            for (int i = 0; i <= 15; i++)
            {
                double x = 60 + (i * 1000 * scaleFactor);

                var line = new Line
                {
                    X1 = x, Y1 = UP_TRACK_Y - 30,
                    X2 = x, Y2 = DOWN_TRACK_Y + 40,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                RailwayCanvas.Children.Add(line);

                // Kilometre çentiği (üstte)
                var tick = new Line
                {
                    X1 = x, Y1 = DOWN_TRACK_Y + 40,
                    X2 = x, Y2 = DOWN_TRACK_Y + 48,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                    StrokeThickness = 1.5
                };
                RailwayCanvas.Children.Add(tick);

                var txt = new TextBlock
                {
                    Text = $"{i} km",
                    FontSize = 10,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))
                };
                Canvas.SetLeft(txt, x - 10);
                Canvas.SetTop(txt, DOWN_TRACK_Y + 50);
                RailwayCanvas.Children.Add(txt);
            }

            // Alt cetvel çizgisi
            var ruler = new Line
            {
                X1 = 60, Y1 = DOWN_TRACK_Y + 45,
                X2 = 60 + (TOTAL_TRACK_LENGTH * scaleFactor), Y2 = DOWN_TRACK_Y + 45,
                Stroke = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                StrokeThickness = 1
            };
            RailwayCanvas.Children.Add(ruler);
        }

        private void DrawTrackLabels(double scaleFactor)
        {
            double scaledLength = TOTAL_TRACK_LENGTH * scaleFactor;

            // === HAT-1 Etiket Paneli (Sol tarafta) ===
            var upLabelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0x42, 0xA5, 0xF5)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3)
            };
            upLabelBorder.Child = new TextBlock
            {
                Text = "◀ HAT-1  Darıca Sahil Yönü",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(upLabelBorder, 60);
            Canvas.SetTop(upLabelBorder, UP_TRACK_Y - 30);
            Panel.SetZIndex(upLabelBorder, 80);
            RailwayCanvas.Children.Add(upLabelBorder);

            // Sağ ok (HAT-1 sonunda)
            var upEndBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0x42, 0xA5, 0xF5)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            upEndBorder.Child = new TextBlock
            {
                Text = "İdari Bina ▶",
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = Brushes.White
            };
            Canvas.SetLeft(upEndBorder, 60 + scaledLength - 80);
            Canvas.SetTop(upEndBorder, UP_TRACK_Y - 28);
            Panel.SetZIndex(upEndBorder, 80);
            RailwayCanvas.Children.Add(upEndBorder);

            // === HAT-2 Etiket Paneli (Sol tarafta) ===
            var downLabelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0xEF, 0x53, 0x50)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3)
            };
            downLabelBorder.Child = new TextBlock
            {
                Text = "HAT-2  İdari Bina ve Atölye Yönü ▶",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(downLabelBorder, 60);
            Canvas.SetTop(downLabelBorder, DOWN_TRACK_Y + 10);
            Panel.SetZIndex(downLabelBorder, 80);
            RailwayCanvas.Children.Add(downLabelBorder);

            // Sağ etiket (HAT-2 sonunda)
            var downEndBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0xEF, 0x53, 0x50)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            downEndBorder.Child = new TextBlock
            {
                Text = "◀ Darıca Sahil",
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = Brushes.White
            };
            Canvas.SetLeft(downEndBorder, 60 + scaledLength - 90);
            Canvas.SetTop(downEndBorder, DOWN_TRACK_Y + 12);
            Panel.SetZIndex(downEndBorder, 80);
            RailwayCanvas.Children.Add(downEndBorder);
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
                ShowToastNotification(message, isError: true);
        }

        private System.Threading.CancellationTokenSource? _toastCts;

        private void ShowToastNotification(string message, bool isError = false)
        {
            // Önceki toast timer'ını iptal et
            _toastCts?.Cancel();
            _toastCts = new System.Threading.CancellationTokenSource();
            var token = _toastCts.Token;

            Dispatcher.Invoke(() =>
            {
                ToastNotificationText.Text = message;

                var severity = isError ? "KIRMIZI" : message.Contains("SARI", StringComparison.OrdinalIgnoreCase) ? "SARI" : "NORMAL";

                Color bgColor = severity switch
                {
                    "KIRMIZI" => Color.FromRgb(0xB0, 0x00, 0x20),
                    "SARI" => Color.FromRgb(0xD9, 0x8A, 0x00),
                    _ => Color.FromRgb(0x1B, 0x7A, 0x3A)
                };

                ToastNotificationBorder.Background = new SolidColorBrush(bgColor);
                ToastNotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                ToastNotificationBorder.BorderThickness = new Thickness(1.5);
                ToastNotificationText.Foreground = Brushes.White;
                ToastNotificationBorder.Visibility = Visibility.Visible;
            });

            // 5 saniye sonra gizle
            System.Threading.Tasks.Task.Delay(10000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ToastNotificationBorder.Visibility = Visibility.Collapsed;
                    });
                }
            });
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

            ErrorSummaryGrid.ItemsSource = _errorLogs;
            ErrorSummaryGrid.Items.Refresh();
            ErrorSummaryGrid.Items.SortDescriptions.Clear();
            ErrorSummaryGrid.Items.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Descending));
        }

        // GridSplitter'ı bulmak için yardımcı metod
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
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
                ErrorSummaryGrid.ItemsSource = null;
                ErrorSummaryGrid.ItemsSource = _errorLogs;
                ErrorSummaryGrid.Items.Refresh();
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