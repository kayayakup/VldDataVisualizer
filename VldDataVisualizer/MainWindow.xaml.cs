using System;
using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using VldDataVisualizer.Models;
using VldDataVisualizer.ViewModels;
using System.Windows.Controls;

namespace VldDataVisualizer
{
    public partial class MainWindow : Window
    {
        private VLDSimulator _simulator;
        private ObservableCollection<VldData> _dataCollection;
        private ObservableCollection<string> _activeAlarms;
        private DispatcherTimer _chartUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();

            _simulator = new VLDSimulator();
            _dataCollection = new ObservableCollection<VldData>();
            _activeAlarms = new ObservableCollection<string>();

            // Event handlers
            _simulator.DataGenerated += OnDataGenerated;
            _simulator.StatusChanged += OnStatusChanged;

            InitializeChartUpdateTimer();

            DataGrid.ItemsSource = _dataCollection;
            AlarmsItemsControl.ItemsSource = _activeAlarms;
        }

        private void InitializeChartUpdateTimer()
        {
            _chartUpdateTimer = new DispatcherTimer();
            _chartUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;
            _chartUpdateTimer.Start();
        }

        private void ChartUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateCharts();
            LastUpdateText.Content = $"Son Güncelleme: {DateTime.Now:HH:mm:ss}";
        }

        private void OnDataGenerated(object sender, VldData data)
        {
            Dispatcher.Invoke(() =>
            {
                // Anlık değerleri güncelle
                UpdateRealTimeValues(data);

                // Alarmları güncelle
                UpdateAlarms(data);

                // DataGrid'e ekle
                _dataCollection.Insert(0, data);
                if (_dataCollection.Count > 100)
                    _dataCollection.RemoveAt(_dataCollection.Count - 1);

                // Status bar'ı güncelle
                DataCountText.Content = $"Veri Sayısı: {_dataCollection.Count}";
            });
        }

        private void UpdateRealTimeValues(VldData data)
        {
            // Sistem bilgileri
            DeviceIdText.Text = $"Cihaz: {data.DeviceId}";
            LocationText.Text = $"Lokasyon: {data.Location}";
            CommunicationText.Text = $"İletişim: {(data.IsCommunicationActive ? "AKTİF" : "KESİNTİ")}";
            CommunicationStatusBorder.Background = data.IsCommunicationActive ?
                new SolidColorBrush(Color.FromRgb(76, 175, 80)) :
                new SolidColorBrush(Color.FromRgb(244, 67, 54));

            // Güç değerleri
            VoltageInText.Text = $"Giriş Gerilimi: {data.VoltageIn:0.0} kV";
            VoltageOutText.Text = $"Çıkış Gerilimi: {data.VoltageOut:0.0} kV";
            CurrentText.Text = $"Toplam Akım: {data.Current:0} A";
            ActivePowerText.Text = $"Aktif Güç: {data.ActivePower:0} kW";
            ReactivePowerText.Text = $"Reaktif Güç: {data.ReactivePower:0} kVAr";
            PowerFactorText.Text = $"Güç Faktörü: {data.PowerFactor:0.00}";

            // Faz değerleri
            VoltageL1Text.Text = $"L1 Gerilimi: {data.VoltageL1:0.0} kV";
            VoltageL2Text.Text = $"L2 Gerilimi: {data.VoltageL2:0.0} kV";
            VoltageL3Text.Text = $"L3 Gerilimi: {data.VoltageL3:0.0} kV";
            CurrentL1Text.Text = $"L1 Akımı: {data.CurrentL1:0} A";
            CurrentL2Text.Text = $"L2 Akımı: {data.CurrentL2:0} A";
            CurrentL3Text.Text = $"L3 Akımı: {data.CurrentL3:0} A";

            // Sistem parametreleri
            TemperatureText.Text = $"Sıcaklık: {data.Temperature:0.0} °C";
            FrequencyText.Text = $"Frekans: {data.Frequency:0.00} Hz";
            GroundCurrentText.Text = $"Toprak Akımı: {data.GroundCurrent:0.0} A";
            THDVoltageText.Text = $"Gerilim THD: {data.THDVoltage:0.0} %";
            THDCurrentText.Text = $"Akım THD: {data.THDCurrent:0.0} %";

            // Enerji ve durum
            EnergyImportText.Text = $"Tüketilen Enerji: {data.ActiveEnergyImport:0} kWh";
            StatusValueText.Text = $"Durum: {data.Status}";

            // Status'a göre renk değiştir
            StatusBorder.Background = data.Status switch
            {
                "NORMAL" => new SolidColorBrush(Color.FromRgb(232, 245, 232)),
                "WARNING" => new SolidColorBrush(Color.FromRgb(255, 243, 224)),
                "ALARM" => new SolidColorBrush(Color.FromRgb(255, 235, 238)),
                _ => new SolidColorBrush(Color.FromRgb(232, 245, 232))
            };

            // Alarm durumunda uyarı göster
            if (data.Status == "ALARM" && data.ActiveAlarms.Count > 0)
            {
                System.Media.SystemSounds.Exclamation.Play();

                // İlk alarmı status bar'da göster
                var mainAlarm = data.ActiveAlarms.FirstOrDefault();
                if (mainAlarm != null)
                {
                    StatusText.Text = $" ALARM: {mainAlarm}";
                    StatusText.Foreground = Brushes.Red;
                }
            }
        }

        private void UpdateAlarms(VldData data)
        {
            _activeAlarms.Clear();
            foreach (var alarm in data.ActiveAlarms)
            {
                _activeAlarms.Add(alarm);
            }
        }

        private void UpdateCharts()
        {
            if (_dataCollection.Count == 0) return;

            // Voltage Chart
            VoltageChart.Clear();
            foreach (var data in _dataCollection)
            {
                VoltageChart.AddValue(data.VoltageOut);
            }

            // Current Chart
            CurrentChart.Clear();
            foreach (var data in _dataCollection)
            {
                CurrentChart.AddValue(data.Current);
            }

            // Power Chart
            PowerChart.Clear();
            foreach (var data in _dataCollection)
            {
                PowerChart.AddValue(data.ActivePower);
            }

            // Temperature Chart
            TemperatureChart.Clear();
            foreach (var data in _dataCollection)
            {
                TemperatureChart.AddValue(data.Temperature);
            }
        }

        private void OnStatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = " " + status;
                StatusText.Foreground = Brushes.LightGreen;
            });
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _simulator.StartSimulation();
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = " Durum: Simülasyon Çalışıyor";
            StatusText.Foreground = Brushes.LightGreen;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _simulator.StopSimulation();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = " Durum: Simülasyon Durduruldu";
            StatusText.Foreground = Brushes.LightYellow;
        }

        private void VoltageSagButton_Click(object sender, RoutedEventArgs e)
        {
            _simulator.SimulateVoltageSag();
        }

        private void OverloadButton_Click(object sender, RoutedEventArgs e)
        {
            _simulator.SimulateOverload();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _simulator.ResetToNormal();
        }

        protected override void OnClosed(EventArgs e)
        {
            _simulator.StopSimulation();
            _chartUpdateTimer?.Stop();
            base.OnClosed(e);
        }
    }
}