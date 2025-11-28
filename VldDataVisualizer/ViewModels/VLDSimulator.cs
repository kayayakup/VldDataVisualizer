using VldDataVisualizer.Models;
using System.Timers;

namespace VldDataVisualizer.ViewModels
{
    public class VLDSimulator
    {
        public event EventHandler<VldData> DataGenerated;
        public event EventHandler<string> StatusChanged;

        private System.Timers.Timer _simulationTimer;
        private Random _random = new Random();
        private bool _isSimulationRunning = false;
        private List<VldData> _dataHistory = new List<VldData>();

        // VLD-TFPR tipik parametreleri (dokümanda 36kV sistem)
        private double _nominalVoltage = 36.0;          // kV
        private double _nominalCurrent = 400.0;         // A (dokümanda 400A mevcut)
        private double _baseFrequency = 50.0;           // Hz
        private double _baseTemperature = 45.0;         // °C
        private double _energyCounter = 0.0;

        public bool IsRunning { get; private set; }

        public void StartSimulation()
        {
            IsRunning = true;
            if (_isSimulationRunning) return;

            _isSimulationRunning = true;
            StatusChanged?.Invoke(this, "VLD-TFPR simülasyonu başlatıldı");

            // Her 1 saniyede bir yeni veri üret (gerçekçi SCADA hızı)
            _simulationTimer = new System.Timers.Timer(1000);
            _simulationTimer.Elapsed += GenerateData;
            _simulationTimer.Start();
        }

        public void StopSimulation()
        {
            IsRunning = false;
            _isSimulationRunning = false;
            _simulationTimer?.Stop();
            _simulationTimer?.Dispose();
            StatusChanged?.Invoke(this, "VLD-TFPR simülasyonu durduruldu");
        }

        private void GenerateData(object sender, ElapsedEventArgs e)
        {
            if (!_isSimulationRunning) return;

            var newData = new VldData
            {
                Timestamp = DateTime.Now,
                DeviceType = "VLD-TFPR",
                Location = "TRANSFORMER_STATION_01"
            };

            // Gerilim değerleri (kV)
            newData.VoltageIn = _nominalVoltage;
            newData.VoltageOut = GenerateVoltageOut();
            newData.VoltageL1 = GeneratePhaseVoltage(1);
            newData.VoltageL2 = GeneratePhaseVoltage(2);
            newData.VoltageL3 = GeneratePhaseVoltage(3);

            // Akım değerleri (A)
            newData.Current = GenerateCurrent();
            newData.CurrentL1 = GeneratePhaseCurrent(1);
            newData.CurrentL2 = GeneratePhaseCurrent(2);
            newData.CurrentL3 = GeneratePhaseCurrent(3);
            newData.GroundCurrent = GenerateGroundCurrent();

            // Güç değerleri
            newData.ActivePower = newData.VoltageOut * newData.Current * 0.9; // kW
            newData.ReactivePower = newData.VoltageOut * newData.Current * 0.3; // kVAr
            newData.ApparentPower = Math.Sqrt(Math.Pow(newData.ActivePower, 2) + Math.Pow(newData.ReactivePower, 2));
            newData.PowerFactor = newData.ActivePower / newData.ApparentPower;

            // Diğer parametreler
            newData.Frequency = GenerateFrequency();
            newData.Temperature = GenerateTemperature();
            newData.THDVoltage = GenerateTHD();
            newData.THDCurrent = GenerateTHD();

            // Enerji hesaplamaları
            _energyCounter += newData.ActivePower / 3600; // kWh/saniye
            newData.ActiveEnergyImport = _energyCounter;
            newData.ReactiveEnergyImport = _energyCounter * 0.3;

            // Durum ve alarm kontrolü
            newData.ActiveAlarms = CheckForAlarms(newData);
            newData.Status = newData.ActiveAlarms.Count > 0 ? "ALARM" : "NORMAL";
            newData.IsCommunicationActive = _random.NextDouble() > 0.02; // %2 iletişim kaybı ihtimali

            // History'e ekle (son 500 kayıt tut)
            _dataHistory.Add(newData);
            if (_dataHistory.Count > 500)
                _dataHistory.RemoveAt(0);

            // Event tetikle
            DataGenerated?.Invoke(this, newData);
        }

        private double GenerateVoltageOut()
        {
            double baseVoltage = _nominalVoltage;

            // Normal çalışma: ±%5 varyasyon
            double fluctuation = (_random.NextDouble() - 0.5) * _nominalVoltage * 0.05;
            double voltage = baseVoltage + fluctuation;

            // %3 ihtimalle anomali
            if (_random.NextDouble() < 0.03)
            {
                if (_random.NextDouble() < 0.4)
                    voltage = baseVoltage * 0.7 + _random.NextDouble() * baseVoltage * 0.1; // Düşük gerilim
                else if (_random.NextDouble() < 0.7)
                    voltage = baseVoltage * 1.15 + _random.NextDouble() * baseVoltage * 0.1; // Yüksek gerilim
                else
                    voltage = 0; // Kesinti
            }

            return Math.Round(voltage, 2);
        }

        private double GeneratePhaseVoltage(int phase)
        {
            double baseVoltage = _nominalVoltage / Math.Sqrt(3); // Faz-nötr gerilimi
            double imbalance = (_random.NextDouble() - 0.5) * baseVoltage * 0.02; // ±%2 dengesizlik
            return Math.Round(baseVoltage + imbalance, 2);
        }

        private double GenerateCurrent()
        {
            double baseCurrent = _nominalCurrent * 0.6; // Normal yük %60
            double fluctuation = (_random.NextDouble() - 0.5) * baseCurrent * 0.3; // ±%30 varyasyon
            double current = baseCurrent + fluctuation;

            // %4 ihtimalle anomali
            if (_random.NextDouble() < 0.04)
            {
                if (_random.NextDouble() < 0.3)
                    current = _nominalCurrent * 1.2 + _random.NextDouble() * _nominalCurrent * 0.3; // Aşırı akım
                else if (_random.NextDouble() < 0.6)
                    current = _nominalCurrent * 0.2 + _random.NextDouble() * _nominalCurrent * 0.1; // Düşük akım
                else
                    current = _nominalCurrent * 2.0 + _random.NextDouble() * _nominalCurrent * 1.0; // Kısa devre
            }

            return Math.Round(Math.Max(0, current), 1);
        }

        private double GeneratePhaseCurrent(int phase)
        {
            double baseCurrent = GenerateCurrent() / 3;
            double imbalance = (_random.NextDouble() - 0.5) * baseCurrent * 0.1; // ±%10 dengesizlik
            return Math.Round(baseCurrent + imbalance, 1);
        }

        private double GenerateGroundCurrent()
        {
            double current = _random.NextDouble() * 5.0; // Normal: 0-5A
            // %1 ihtimalle toprak arızası
            if (_random.NextDouble() < 0.01)
                current = 50 + _random.NextDouble() * 100; // 50-150A toprak arızası

            return Math.Round(current, 2);
        }

        private double GenerateFrequency()
        {
            double frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 0.2; // 49.9-50.1 Hz
            // %2 ihtimalle frekans anormalliği
            if (_random.NextDouble() < 0.02)
                frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 2.0; // 48-52 Hz

            return Math.Round(frequency, 2);
        }

        private double GenerateTemperature()
        {
            double temperature = _baseTemperature + (_random.NextDouble() - 0.5) * 10; // 40-50°C
            // %3 ihtimalle sıcaklık anormalliği
            if (_random.NextDouble() < 0.03)
                temperature = 60 + _random.NextDouble() * 40; // 60-100°C

            return Math.Round(temperature, 1);
        }

        private double GenerateTHD()
        {
            double thd = 1.0 + _random.NextDouble() * 4.0; // Normal: %1-5 THD
            // %5 ihtimalle yüksek harmonik
            if (_random.NextDouble() < 0.05)
                thd = 8.0 + _random.NextDouble() * 12.0; // %8-20 THD

            return Math.Round(thd, 1);
        }

        private List<string> CheckForAlarms(VldData data)
        {
            var alarms = new List<string>();

            // Gerilim alarmları
            if (data.VoltageOut < _nominalVoltage * 0.85)
                alarms.Add("LOW_VOLTAGE");
            else if (data.VoltageOut > _nominalVoltage * 1.1)
                alarms.Add("HIGH_VOLTAGE");
            else if (data.VoltageOut == 0)
                alarms.Add("VOLTAGE_LOSS");

            // Akım alarmları
            if (data.Current > _nominalCurrent * 1.1)
                alarms.Add("OVER_CURRENT");
            if (data.GroundCurrent > 10)
                alarms.Add("GROUND_FAULT");

            // Sıcaklık alarmları
            if (data.Temperature > 75)
                alarms.Add("OVER_TEMPERATURE");

            // Frekans alarmları
            if (data.Frequency < 49.0 || data.Frequency > 51.0)
                alarms.Add("FREQUENCY_DEVIATION");

            // Güç kalitesi alarmları
            if (data.THDVoltage > 8.0)
                alarms.Add("HIGH_VOLTAGE_THD");
            if (data.THDCurrent > 10.0)
                alarms.Add("HIGH_CURRENT_THD");

            // Faz dengesizliği
            double maxPhaseVoltage = Math.Max(data.VoltageL1, Math.Max(data.VoltageL2, data.VoltageL3));
            double minPhaseVoltage = Math.Min(data.VoltageL1, Math.Min(data.VoltageL2, data.VoltageL3));
            double voltageImbalance = (maxPhaseVoltage - minPhaseVoltage) / maxPhaseVoltage * 100;

            if (voltageImbalance > 3.0)
                alarms.Add("VOLTAGE_IMBALANCE");

            return alarms;
        }

        public List<VldData> GetDataHistory() => _dataHistory;

        // Senaryo bazlı test fonksiyonları
        public void SimulateVoltageSag()
        {
            // Gerilim düşüşü simülasyonu
            _nominalVoltage = 30.0;
            StatusChanged?.Invoke(this, "Gerilim düşüşü senaryosu aktif");
        }

        public void SimulateOverload()
        {
            // Aşırı yük simülasyonu
            _nominalCurrent = 600.0;
            StatusChanged?.Invoke(this, "Aşırı yük senaryosu aktif");
        }

        public void ResetToNormal()
        {
            // Normal değerlere dönüş
            _nominalVoltage = 36.0;
            _nominalCurrent = 400.0;
            StatusChanged?.Invoke(this, "Normal çalışma moduna dönüldü");
        }
    }
}