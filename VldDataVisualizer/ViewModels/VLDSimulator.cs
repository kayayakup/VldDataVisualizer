using VldDataVisualizer.Models;
using System.Timers;

namespace VldDataVisualizer.ViewModels
{
    public class VLDSimulator
    {
        public event EventHandler<List<VldData>> DataGenerated; // Her istasyon için liste
        public event EventHandler<string> StatusChanged;

        private System.Timers.Timer _simulationTimer;
        private Random _random = new Random();
        private bool _isSimulationRunning = false;

        // Her cihaz için ayrı veri koleksiyonu
        private Dictionary<int, List<VldData>> _deviceDataHistory = new Dictionary<int, List<VldData>>();
        private Dictionary<int, double> _energyCounters = new Dictionary<int, double>();

        // VLD-TFPR tipik parametreleri (sartnamede 36kV sistem)
        private double _nominalVoltage = 1500.0;          // kV
        private double _nominalCurrent = 400.0;         // A
        private double _baseFrequency = 50.0;           // Hz
        private double _baseTemperature = 45.0;         // °C

        // İstasyon pozisyonları (metre cinsinden, 0 = Darıca Sahil, 15391 = Depo)
        private readonly Dictionary<int, (string Name, double Position)> _stationPositions = new Dictionary<int, (string, double)>
        {
            { 1, ("Depo", 15391.246) },
            { 2, ("OSB", 13873.215) },
            { 3, ("Mutlukent", 12081.341) },
            { 4, ("Adliye", 10385.942) },
            { 5, ("Akse Sapağı", 9070.108) },
            { 6, ("Gebze Stadyum", 8234.420) },
            { 7, ("Gebze Kent Meydanı", 7101.599) },
            { 8, ("Fatih Devlet Hastanesi", 5781.120) },
            { 9, ("TCDD Gar", 4389.210) },
            { 10, ("Farabi Devlet Hastanesi", 3298.624) },
            { 11, ("Darıca Cumhuriyet", 1379.242) },
            { 12, ("Darıca Sahil", 136.100) }
        };

        public bool IsRunning { get; private set; }

        public VLDSimulator()
        {
            // Her cihaz için enerji sayacını başlat
            for (int i = 1; i <= 12; i++)
            {
                _energyCounters[i] = 0.0;
                _deviceDataHistory[i] = new List<VldData>();
            }
        }

        public void StartSimulation()
        {
            IsRunning = true;
            if (_isSimulationRunning) return;

            _isSimulationRunning = true;
            StatusChanged?.Invoke(this, "12 adet VLD-TFPR simülasyonu başlatıldı");

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

            var allDevicesData = new List<VldData>();

            // Her istasyon için ayrı cihaz verisi üret
            for (int stationId = 1; stationId <= 12; stationId++)
            {
                var stationInfo = _stationPositions[stationId];

                // Kontrol bölgesi: Mevcut istasyondan bir sonraki istasyonun ortasına kadar
                double startPos = stationInfo.Position;
                double endPos = stationId < 12
                    ? (stationInfo.Position + _stationPositions[stationId + 1].Position) / 2
                    : 0; // Son istasyon için hat başına kadar

                var newData = new VldData
                {
                    Timestamp = DateTime.Now,
                    DeviceId = $"VLD_TFPR_{stationId:D3}",
                    DeviceType = "VLD-TFPR",
                    Location = $"STATION_{stationId}_{stationInfo.Name.Replace(" ", "_").ToUpper()}",
                    StationId = stationId,
                    StationName = stationInfo.Name,
                    StartPosition = Math.Min(startPos, endPos),
                    EndPosition = Math.Max(startPos, endPos)
                };

                // Gerilim değerleri (kV) - Her istasyon için farklı varyasyon
                newData.VoltageIn = _nominalVoltage;
                newData.VoltageOut = GenerateVoltageOut(stationId);
                newData.VoltageL1 = GeneratePhaseVoltage(1, stationId);
                newData.VoltageL2 = GeneratePhaseVoltage(2, stationId);
                newData.VoltageL3 = GeneratePhaseVoltage(3, stationId);

                // Akım değerleri (A)
                newData.Current = GenerateCurrent(stationId);
                newData.CurrentL1 = GeneratePhaseCurrent(1, stationId);
                newData.CurrentL2 = GeneratePhaseCurrent(2, stationId);
                newData.CurrentL3 = GeneratePhaseCurrent(3, stationId);
                newData.GroundCurrent = GenerateGroundCurrent(stationId);

                // Güç değerleri
                newData.ActivePower = newData.VoltageOut * newData.Current * 0.9; // kW
                newData.ReactivePower = newData.VoltageOut * newData.Current * 0.3; // kVAr
                newData.ApparentPower = Math.Sqrt(Math.Pow(newData.ActivePower, 2) + Math.Pow(newData.ReactivePower, 2));
                newData.PowerFactor = newData.ActivePower / newData.ApparentPower;

                // Diğer parametreler
                newData.Frequency = GenerateFrequency(stationId);
                newData.Temperature = GenerateTemperature(stationId);
                newData.THDVoltage = GenerateTHD(stationId);
                newData.THDCurrent = GenerateTHD(stationId);

                // Enerji hesaplamaları
                _energyCounters[stationId] += newData.ActivePower / 3600; // kWh/saniye
                newData.ActiveEnergyImport = _energyCounters[stationId];
                newData.ReactiveEnergyImport = _energyCounters[stationId] * 0.3;

                // Durum ve alarm kontrolü
                newData.ActiveAlarms = CheckForAlarms(newData);
                newData.Status = newData.ActiveAlarms.Count > 0 ? "ALARM" : "NORMAL";
                newData.IsCommunicationActive = _random.NextDouble() > 0.02; // %2 iletişim kaybı ihtimali

                // History'e ekle (son 500 kayıt tut)
                _deviceDataHistory[stationId].Add(newData);
                if (_deviceDataHistory[stationId].Count > 500)
                    _deviceDataHistory[stationId].RemoveAt(0);

                allDevicesData.Add(newData);
            }

            // Tüm cihazların verilerini gönder
            DataGenerated?.Invoke(this, allDevicesData);
        }

        private double GenerateVoltageOut(int stationId)
        {
            double baseVoltage = _nominalVoltage;

            // İstasyona göre farklı varyasyon (simülasyon için)
            double stationVariation = Math.Sin(stationId * 0.5) * 0.02;

            // Normal çalışma: ±%5 varyasyon
            double fluctuation = (_random.NextDouble() - 0.5) * _nominalVoltage * 0.05;
            double voltage = baseVoltage + fluctuation + (baseVoltage * stationVariation);

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

        private double GeneratePhaseVoltage(int phase, int stationId)
        {
            double baseVoltage = _nominalVoltage / Math.Sqrt(3); // Faz-nötr gerilimi
            double imbalance = (_random.NextDouble() - 0.5) * baseVoltage * 0.02; // ±%2 dengesizlik
            double stationFactor = 1 + (Math.Sin(stationId + phase) * 0.01);
            return Math.Round((baseVoltage + imbalance) * stationFactor, 2);
        }

        private double GenerateCurrent(int stationId)
        {
            // İstasyonlara göre farklı yük profili (merkez istasyonlar daha yoğun)
            double loadFactor = stationId >= 5 && stationId <= 9 ? 0.75 : 0.6;
            double baseCurrent = _nominalCurrent * loadFactor;

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

        private double GeneratePhaseCurrent(int phase, int stationId)
        {
            double baseCurrent = GenerateCurrent(stationId) / 3;
            double imbalance = (_random.NextDouble() - 0.5) * baseCurrent * 0.1; // ±%10 dengesizlik
            return Math.Round(baseCurrent + imbalance, 1);
        }

        private double GenerateGroundCurrent(int stationId)
        {
            double current = _random.NextDouble() * 5.0; // Normal: 0-5A
            // %1 ihtimalle toprak arızası
            if (_random.NextDouble() < 0.01)
                current = 50 + _random.NextDouble() * 100; // 50-150A toprak arızası

            return Math.Round(current, 2);
        }

        private double GenerateFrequency(int stationId)
        {
            double frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 0.2; // 49.9-50.1 Hz
            // %2 ihtimalle frekans anormalliği
            if (_random.NextDouble() < 0.02)
                frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 2.0; // 48-52 Hz

            return Math.Round(frequency, 2);
        }

        private double GenerateTemperature(int stationId)
        {
            // Merkez istasyonlar daha sıcak
            double baseTempAdjustment = stationId >= 5 && stationId <= 9 ? 5 : 0;
            double temperature = _baseTemperature + baseTempAdjustment + (_random.NextDouble() - 0.5) * 10; // 40-50°C

            // %3 ihtimalle sıcaklık anormalliği
            if (_random.NextDouble() < 0.03)
                temperature = 60 + _random.NextDouble() * 40; // 60-100°C

            return Math.Round(temperature, 1);
        }

        private double GenerateTHD(int stationId)
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
                alarms.Add($"[{data.StationName}] LOW_VOLTAGE");
            else if (data.VoltageOut > _nominalVoltage * 1.1)
                alarms.Add($"[{data.StationName}] HIGH_VOLTAGE");
            else if (data.VoltageOut == 0)
                alarms.Add($"[{data.StationName}] VOLTAGE_LOSS");

            // Akım alarmları
            if (data.Current > _nominalCurrent * 1.1)
                alarms.Add($"[{data.StationName}] OVER_CURRENT");
            if (data.GroundCurrent > 10)
                alarms.Add($"[{data.StationName}] GROUND_FAULT");

            // Sıcaklık alarmları
            if (data.Temperature > 75)
                alarms.Add($"[{data.StationName}] OVER_TEMPERATURE");

            // Frekans alarmları
            if (data.Frequency < 49.0 || data.Frequency > 51.0)
                alarms.Add($"[{data.StationName}] FREQUENCY_DEVIATION");

            // Güç kalitesi alarmları
            if (data.THDVoltage > 8.0)
                alarms.Add($"[{data.StationName}] HIGH_VOLTAGE_THD");
            if (data.THDCurrent > 10.0)
                alarms.Add($"[{data.StationName}] HIGH_CURRENT_THD");

            // Faz dengesizliği
            double maxPhaseVoltage = Math.Max(data.VoltageL1, Math.Max(data.VoltageL2, data.VoltageL3));
            double minPhaseVoltage = Math.Min(data.VoltageL1, Math.Min(data.VoltageL2, data.VoltageL3));
            double voltageImbalance = (maxPhaseVoltage - minPhaseVoltage) / maxPhaseVoltage * 100;

            if (voltageImbalance > 3.0)
                alarms.Add($"[{data.StationName}] VOLTAGE_IMBALANCE");

            return alarms;
        }

        public List<VldData> GetDataHistoryForStation(int stationId)
        {
            return _deviceDataHistory.ContainsKey(stationId)
                ? _deviceDataHistory[stationId]
                : new List<VldData>();
        }

        // Senaryo bazlı test fonksiyonları
        public void SimulateVoltageSag()
        {
            _nominalVoltage = 30.0;
            StatusChanged?.Invoke(this, "Gerilim düşüşü senaryosu aktif");
        }

        public void SimulateOverload()
        {
            _nominalCurrent = 600.0;
            StatusChanged?.Invoke(this, "Aşırı yük senaryosu aktif");
        }

        public void ResetToNormal()
        {
            _nominalVoltage = 36.0;
            _nominalCurrent = 400.0;
            StatusChanged?.Invoke(this, "Normal çalışma moduna dönüldü");
        }
    }
}