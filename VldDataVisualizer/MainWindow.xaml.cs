using System;
using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Linq;
using VldDataVisualizer.Models;
using VldDataVisualizer.ViewModels;
using System.Windows.Controls.Primitives;

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
        private ObservableCollection<BlockInfo> _blocks;
        private ObservableCollection<string> _vldAlarms;

        // Timers
        private DispatcherTimer _chartUpdateTimer;
        private DispatcherTimer _railwayUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();
            InitializeSystems();
        }

        private void InitializeSystems()
        {
            // Simülatörleri başlat
            _vldSimulator = new VLDSimulator();
            _signalizationSimulator = new SignalizationSimulator();

            // Collections'ları başlat
            _vldDataCollection = new ObservableCollection<VldData>();
            _activeTrains = new ObservableCollection<TrainInfo>();
            _stations = new ObservableCollection<StationInfo>();
            _blocks = new ObservableCollection<BlockInfo>();
            _vldAlarms = new ObservableCollection<string>();

            // Event handlers
            _vldSimulator.DataGenerated += OnVLDDataGenerated;
            _vldSimulator.StatusChanged += OnVLDStatusChanged;
            _signalizationSimulator.DataGenerated += OnSignalizationDataGenerated;
            _signalizationSimulator.StatusChanged += OnSignalizationStatusChanged;

            // Data binding
            DataGrid.ItemsSource = _vldDataCollection;
            AlarmsItemsControl.ItemsSource = _vldAlarms;
            TrainsDataGrid.ItemsSource = _activeTrains;
            StationsDataGrid.ItemsSource = _stations;
            BlockDataGrid.ItemsSource = _blocks;

            InitializeTimers();
            InitializeBlocks();
            UpdateHeaderStatus("🟢 Sistem Hazır", Colors.Green);
        }

        private void InitializeTimers()
        {
            // Chart güncelleme timer'ı
            _chartUpdateTimer = new DispatcherTimer();
            _chartUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;
            _chartUpdateTimer.Start();

            // Railway görsel güncelleme timer'ı
            _railwayUpdateTimer = new DispatcherTimer();
            _railwayUpdateTimer.Interval = TimeSpan.FromMilliseconds(2000);
            _railwayUpdateTimer.Tick += RailwayUpdateTimer_Tick;
        }

        private void InitializeBlocks()
        {
            _blocks.Clear();
            // 18km hat için 36 blok (her 500m'de bir)
            for (int i = 0; i < 36; i++)
            {
                _blocks.Add(new BlockInfo
                {
                    BlockId = i + 1,
                    StartPosition = i * 500,
                    EndPosition = (i + 1) * 500,
                    CurrentTrain = "Boş",
                    Status = "AÇIK",
                    SignalStatus = "YEŞİL"
                });
            }
        }

        #region BUTON CLICK EVENT HANDLERS

        // ==================== ANA KONTROL BUTONLARI ====================
        private void StartAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vldSimulator.StartSimulation();
                _signalizationSimulator.StartSimulation();
                _railwayUpdateTimer.Start();

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

        // ==================== SİNYALİZASYON BUTONLARI ====================
        private void StartSignalizationButton_Click(object sender, RoutedEventArgs e)
        {
            _signalizationSimulator.StartSimulation();
            StartSignalizationButton.IsEnabled = false;
            StopSignalizationButton.IsEnabled = true;
            ShowStatusMessage("Sinyalizasyon simülasyonu başlatıldı", StatusType.Info);
        }

        private void StopSignalizationButton_Click(object sender, RoutedEventArgs e)
        {
            _signalizationSimulator.StopSimulation();
            StartSignalizationButton.IsEnabled = true;
            StopSignalizationButton.IsEnabled = false;
            ShowStatusMessage("Sinyalizasyon simülasyonu durduruldu", StatusType.Info);
        }

        private void AddTrainButton_Click(object sender, RoutedEventArgs e)
        {
            // Simülasyona yeni tren ekleme
            // Bu işlev sinyalizasyon simülatörüne eklenebilir
            ShowStatusMessage("Yeni tren simülasyona eklendi", StatusType.Info);
        }

        private void RemoveTrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTrains.Count > 0)
            {
                // Son treni kaldır
                var lastTrain = _activeTrains.Last();
                _activeTrains.Remove(lastTrain);
                ShowStatusMessage($"{lastTrain.TrainName} simülasyondan kaldırıldı", StatusType.Warning);
            }
            else
            {
                ShowStatusMessage("Kaldırılacak tren bulunamadı", StatusType.Warning);
            }
        }

        // ==================== TFPR BUTONLARI ====================
        private void StartVLDButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.StartSimulation();
            StartVLDButton.IsEnabled = false;
            StopVLDButton.IsEnabled = true;
            ShowStatusMessage("VLD-TFPR simülasyonu başlatıldı", StatusType.Info);
        }

        private void StopVLDButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.StopSimulation();
            StartVLDButton.IsEnabled = true;
            StopVLDButton.IsEnabled = false;
            ShowStatusMessage("VLD-TFPR simülasyonu durduruldu", StatusType.Info);
        }

        private void VoltageSagButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.SimulateVoltageSag();
            ShowStatusMessage("Gerilim düşüşü senaryosu aktif", StatusType.Warning);
        }

        private void OverloadButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.SimulateOverload();
            ShowStatusMessage("Aşırı yük senaryosu aktif", StatusType.Warning);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _vldSimulator.ResetToNormal();
            ShowStatusMessage("Normal çalışma moduna dönüldü", StatusType.Info);
        }

        #endregion

        #region EVENT HANDLERS

        private void OnVLDDataGenerated(object sender, VldData data)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateVLDRealTimeValues(data);
                UpdateVLDAlarms(data);

                // DataGrid'e ekle (son 100 kayıt tut)
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
            // TFPR değerlerini güncelle
            DeviceIdText.Text = $"🔧 Cihaz: {data.DeviceId}";
            LocationText.Text = $"📍 Lokasyon: {data.Location}";

            CommunicationText.Text = $"📶 İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
            CommunicationStatusBorder.Background = data.IsCommunicationActive ?
                new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

            // Güç değerleri
            VoltageInText.Text = $"🔌 Giriş Gerilimi: {data.VoltageIn:0.0} kV";
            VoltageOutText.Text = $"⚡ Çıkış Gerilimi: {data.VoltageOut:0.0} kV";
            CurrentText.Text = $"🔋 Toplam Akım: {data.Current:0} A";
            ActivePowerText.Text = $"📊 Aktif Güç: {data.ActivePower:0} kW";
            ReactivePowerText.Text = $"📈 Reaktif Güç: {data.ReactivePower:0} kVAr";
            PowerFactorText.Text = $"🎯 Güç Faktörü: {data.PowerFactor:0.00}";

            // Faz değerleri
            VoltageL1Text.Text = $"L1 🔌 Gerilim: {data.VoltageL1:0.0} kV";
            VoltageL2Text.Text = $"L2 🔌 Gerilim: {data.VoltageL2:0.0} kV";
            VoltageL3Text.Text = $"L3 🔌 Gerilim: {data.VoltageL3:0.0} kV";
            CurrentL1Text.Text = $"L1 🔋 Akım: {data.CurrentL1:0} A";
            CurrentL2Text.Text = $"L2 🔋 Akım: {data.CurrentL2:0} A";
            CurrentL3Text.Text = $"L3 🔋 Akım: {data.CurrentL3:0} A";

            // Sistem parametreleri
            TemperatureText.Text = $"🌡️ Sıcaklık: {data.Temperature:0.0} °C";
            FrequencyText.Text = $"📏 Frekans: {data.Frequency:0.00} Hz";
            GroundCurrentText.Text = $"⚡ Toprak Akımı: {data.GroundCurrent:0.0} A";
            THDVoltageText.Text = $"📉 Gerilim THD: {data.THDVoltage:0.0} %";
            THDCurrentText.Text = $"📉 Akım THD: {data.THDCurrent:0.0} %";

            // Enerji ve durum
            EnergyImportText.Text = $"🔋 Tüketilen Enerji: {data.ActiveEnergyImport:0} kWh";
            VLDStatusText.Text = $"⚡ Durum: {data.Status}";

            // Status renkleri
            VLDStatusBorder.Background = data.Status switch
            {
                "NORMAL" => new SolidColorBrush(Colors.LightGreen),
                "WARNING" => new SolidColorBrush(Colors.LightYellow),
                "ALARM" => new SolidColorBrush(Colors.LightCoral),
                _ => new SolidColorBrush(Colors.LightGray)
            };

            // Alarm durumu
            if (data.Status == "ALARM" && data.ActiveAlarms.Count > 0)
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
        }

        private void UpdateSignalizationRealTimeValues(SignalizationData data)
        {
            // Sinyalizasyon değerlerini güncelle
            TotalTrainsText.Text = $"🚆 Aktif Tren: {data.TotalActiveTrains}";
            SystemStatusText.Text = $"📡 Sistem: {data.SystemStatus}";

            SignalCommunicationText.Text = $"📶 İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
            SignalCommunicationBorder.Background = data.IsCommunicationActive ?
                new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

            // Ortalama hız
            double avgSpeed = data.ActiveTrains.Any() ? data.ActiveTrains.Average(t => t.Speed) : 0;
            AvgSpeedText.Text = $"⚡ Ort. Hız: {avgSpeed:0} km/s";

            // Toplam yolcu
            int totalPassengers = data.ActiveTrains.Sum(t => t.PassengerCount) + data.Stations.Sum(s => s.WaitingPassengers);
            TotalPassengersText.Text = $"👥 Toplam Yolcu: {totalPassengers}";

            // Trenleri güncelle
            _activeTrains.Clear();
            foreach (var train in data.ActiveTrains)
            {
                _activeTrains.Add(train);
            }

            // İstasyonları güncelle
            _stations.Clear();
            foreach (var station in data.Stations)
            {
                _stations.Add(station);
            }

            // Blokları güncelle
            UpdateBlocks(data.ActiveTrains);
        }

        private void UpdateBlocks(System.Collections.Generic.List<TrainInfo> trains)
        {
            foreach (var block in _blocks)
            {
                block.CurrentTrain = "Boş";
                block.Status = "AÇIK";
                block.SignalStatus = "YEŞİL";

                // Blokta tren var mı kontrol et
                var trainInBlock = trains.FirstOrDefault(t =>
                    t.CurrentPosition >= block.StartPosition &&
                    t.CurrentPosition < block.EndPosition);

                if (trainInBlock != null)
                {
                    block.CurrentTrain = trainInBlock.TrainName;
                    block.Status = "DOLU";
                    block.SignalStatus = "KIRMIZI";
                }
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

        #region TIMER EVENTS & UTILITIES

        private void ChartUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateCharts();
            LastUpdateText.Content = $"Son Güncelleme: {DateTime.Now:HH:mm:ss}";
        }

        private void RailwayUpdateTimer_Tick(object sender, EventArgs e)
        {
            // Railway görselini güncelle
            DrawRailway();
        }

        private void UpdateCharts()
        {
            if (_vldDataCollection.Count == 0) return;

            var latestData = _vldDataCollection.First();

            // TFPR Grafikleri
            CurrentChart.AddValue(latestData.Current);
            VoltageChart.AddValue(latestData.VoltageOut);
            PowerChart.AddValue(latestData.ActivePower);
            TemperatureChart.AddValue(latestData.Temperature);
            THDChart.AddValue(latestData.THDVoltage);
        }

        private void DrawRailway()
        {
            // Basit railway çizimi
            RailwayCanvas.Children.Clear();

            // Burada railway görseli çizilecek
            // Şimdilik basit bir placeholder
            var text = new TextBlock
            {
                Text = $"🚆 Tren Hatları Görünümü - Aktif Tren: {_activeTrains.Count}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
                Foreground = Brushes.Gray
            };

            Canvas.SetLeft(text, RailwayCanvas.ActualWidth / 2 - 100);
            Canvas.SetTop(text, RailwayCanvas.ActualHeight / 2 - 10);
            RailwayCanvas.Children.Add(text);
        }

        private void UpdateButtonStates(bool isRunning)
        {
            StartAllButton.IsEnabled = !isRunning;
            StopAllButton.IsEnabled = isRunning;
            StartVLDButton.IsEnabled = !isRunning;
            StopVLDButton.IsEnabled = isRunning;
            StartSignalizationButton.IsEnabled = !isRunning;
            StopSignalizationButton.IsEnabled = isRunning;
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

            // Tüm trenleri durdur
            foreach (var train in _activeTrains)
            {
                train.Status = "ACİL DURDURULDU";
            }

            UpdateButtonStates(false);
            UpdateHeaderStatus("🔴 ACİL DUR", Colors.Red);
            ShowStatusMessage("🚨 ACİL DUR: Tüm sistemler durduruldu", StatusType.Emergency);

            System.Media.SystemSounds.Hand.Play();
        }

        private void ShowStatusMessage(string message, StatusType type)
        {
            // Status mesajını göster (isteğe bağlı status bar güncellemesi)
            //var statusBarItem = (StatusBarItem)StatusBar.Items[4];
            //statusBarItem.Content = message;

            //Buraya dikkat et*********************************************
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