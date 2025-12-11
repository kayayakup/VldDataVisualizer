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
        private Dictionary<int, double> _reactiveEnergyCounters = new Dictionary<int, double>();

        // Metro Katener Hattı - 34.5kV OG Sistem Parametreleri (Şartnameye göre)
        private double _nominalVoltage = 1500.5;          // kV - OG işletme gerilimi (34.5 kV)
        private double _nominalCurrent = 577.35;       // A - 34.5 MW için (0.9 PF, √3 × 34.5kV × I × 0.9 = 34.5MW)
        private double _baseFrequency = 50.0;          // Hz - Şebeke frekansı
        private double _baseTemperature = 40.0;        // °C - Ortam sıcaklığı (-5°C min, +40°C maks)

        // Koruma parametreleri (Şartnameye göre)
        private double _basicInsulationLevel = 170.0;  // kV - Temel izolasyon seviyesi (170/70 kV)
        private double _shortCircuitDuration = 1.0;    // saniye - Kısa devre dayanım süresi

        // OG Kesici parametreleri
        private double _openingTime = 0.055;           // saniye - Açma zamanı (55 ms max)
        private double _preferredResponseTime = 0.0133; // saniye - Tercih edilen cevap zamanı (13.3 ms)

        // DC Sistem Parametreleri (Şartnameye göre)
        private double _dcNominalVoltage = 1.5;        // kV - 1500 V DC katener hattı
        private double _auxDcVoltage = 0.11;           // kV - 110 V DC yardımcı güç
        private double _auxAcVoltage = 0.4;            // kV - 400 V AC yardımcı güç

        // Trafo parametreleri
        private double _cerTransformerRatio = 34.5 / 2.4; // 34.5/2x1.2 kV Cer Trafoları
        private double _internalNeedTransformerRatio = 34.5 / 0.4; // 34.5/0.4 kV İç İhtiyaç Trafoları
        private double _generatorTransformerRatio = 6.3 / 34.5; // 6.3/34.5 kV Jeneratör Trafoları

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

        // İstasyon yük profilleri (merkez istasyonlar daha yoğun)
        private readonly Dictionary<int, double> _stationLoadProfiles = new Dictionary<int, double>
        {
            { 1, 0.4 },   // Depo: %40 yük
            { 2, 0.5 },   // OSB: %50 yük
            { 3, 0.6 },   // Mutlukent: %60 yük
            { 4, 0.7 },   // Adliye: %70 yük
            { 5, 0.8 },   // Akse Sapağı: %80 yük
            { 6, 0.85 },  // Gebze Stadyum: %85 yük
            { 7, 0.9 },   // Gebze Kent Meydanı: %90 yük
            { 8, 0.85 },  // Fatih Devlet Hastanesi: %85 yük
            { 9, 0.8 },   // TCDD Gar: %80 yük
            { 10, 0.7 },  // Farabi Devlet Hastanesi: %70 yük
            { 11, 0.6 },  // Darıca Cumhuriyet: %60 yük
            { 12, 0.5 }   // Darıca Sahil: %50 yük
        };

        public bool IsRunning { get; private set; }

        public VLDSimulator()
        {
            // Her cihaz için enerji sayaçlarını başlat
            for (int i = 1; i <= 12; i++)
            {
                _energyCounters[i] = 0.0;
                _reactiveEnergyCounters[i] = 0.0;
                _deviceDataHistory[i] = new List<VldData>();
            }
        }

        public void StartSimulation()
        {
            IsRunning = true;
            if (_isSimulationRunning) return;

            _isSimulationRunning = true;
            StatusChanged?.Invoke(this, "12 adet VLD-TFPR (34.5kV OG Sistem) simülasyonu başlatıldı");

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
                    DeviceType = "VLD-TFPR 34.5kV",
                    Location = $"STATION_{stationId}_{stationInfo.Name.Replace(" ", "_").ToUpper()}",
                    StationId = stationId,
                    StationName = stationInfo.Name,
                    StartPosition = Math.Min(startPos, endPos),
                    EndPosition = Math.Max(startPos, endPos)
                };

                // Gerilim değerleri (kV) - 34.5kV OG sistem
                newData.VoltageIn = _nominalVoltage;
                newData.VoltageOut = GenerateVoltageOut(stationId);
                newData.VoltageL1 = GeneratePhaseVoltage(1, stationId);
                newData.VoltageL2 = GeneratePhaseVoltage(2, stationId);
                newData.VoltageL3 = GeneratePhaseVoltage(3, stationId);

                // Akım değerleri (A) - İstasyon yük profiline göre
                newData.Current = GenerateCurrent(stationId);
                newData.CurrentL1 = GeneratePhaseCurrent(1, stationId);
                newData.CurrentL2 = GeneratePhaseCurrent(2, stationId);
                newData.CurrentL3 = GeneratePhaseCurrent(3, stationId);
                newData.GroundCurrent = GenerateGroundCurrent(stationId);

                // Güç değerleri - 3 fazlı sistem için √3 faktörü ile doğru hesapla
                double sqrt3 = Math.Sqrt(3); // 3 fazlı sistem faktörü

                // Güç faktörü (0.85-0.95 arası normal - şartnameye göre)
                double powerFactor = 0.9 + (_random.NextDouble() - 0.5) * 0.05; // 0.85-0.95
                newData.PowerFactor = Math.Round(powerFactor, 2);

                // Aktif güç: √3 × V(kV) × I(A) × PF = P(kW)
                newData.ActivePower = Math.Round(sqrt3 * newData.VoltageOut * newData.Current * powerFactor, 1);

                // Reaktif güç: √3 × V(kV) × I(A) × sin(acos(PF)) = Q(kVAr)
                double reactiveFactor = Math.Sin(Math.Acos(powerFactor));
                newData.ReactivePower = Math.Round(sqrt3 * newData.VoltageOut * newData.Current * reactiveFactor, 1);

                // Görünür güç: √3 × V(kV) × I(A) = S(kVA)
                newData.ApparentPower = Math.Round(sqrt3 * newData.VoltageOut * newData.Current, 1);

                // Diğer parametreler
                newData.Frequency = GenerateFrequency(stationId);
                newData.Temperature = GenerateTemperature(stationId);
                newData.THDVoltage = GenerateTHD(stationId);
                newData.THDCurrent = GenerateTHD(stationId);

                // Enerji hesaplamaları (kWh ve kVArh)
                _energyCounters[stationId] += newData.ActivePower / 3600; // kWh/saniye
                _reactiveEnergyCounters[stationId] += newData.ReactivePower / 3600; // kVArh/saniye

                newData.ActiveEnergyImport = Math.Round(_energyCounters[stationId], 2);
                newData.ReactiveEnergyImport = Math.Round(_reactiveEnergyCounters[stationId], 2);

                // DC Sistem değerleri (şartnameye göre)
                newData.DcVoltage = GenerateDcVoltage(stationId);
                newData.DcCurrent = GenerateDcCurrent(stationId);
                newData.AuxDcVoltage = GenerateAuxDcVoltage(stationId);
                newData.AuxAcVoltage = GenerateAuxAcVoltage(stationId);

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
            double baseVoltage = _nominalVoltage; // 34.5 kV

            // İstasyona göre küçük varyasyon
            double stationVariation = Math.Sin(stationId * 0.5) * 0.01;

            // Normal çalışma: ±%2 varyasyon (şartnameye göre daha kararlı)
            double fluctuation = (_random.NextDouble() - 0.5) * _nominalVoltage * 0.02;
            double voltage = baseVoltage + fluctuation + (baseVoltage * stationVariation);

            // %2 ihtimalle anomali (şartname alarm limitlerine göre)
            if (_random.NextDouble() < 0.02)
            {
                if (_random.NextDouble() < 0.4)
                    voltage = baseVoltage * 0.85 + _random.NextDouble() * baseVoltage * 0.05; // Düşük gerilim (%85-90)
                else if (_random.NextDouble() < 0.7)
                    voltage = baseVoltage * 1.1 + _random.NextDouble() * baseVoltage * 0.05; // Yüksek gerilim (%110-115)
                else
                    voltage = 0; // Kesinti
            }

            return Math.Round(voltage, 2);
        }

        private double GeneratePhaseVoltage(int phase, int stationId)
        {
            // Faz-Nötr gerilimi: Hat gerilimi / √3
            double baseVoltage = _nominalVoltage / Math.Sqrt(3); // kV
            double imbalance = (_random.NextDouble() - 0.5) * baseVoltage * 0.015; // ±%1.5 dengesizlik
            double stationFactor = 1 + (Math.Sin(stationId + phase) * 0.005);
            return Math.Round((baseVoltage + imbalance) * stationFactor, 2);
        }

        private double GenerateCurrent(int stationId)
        {
            // İstasyon yük profiline göre akım
            double loadFactor = _stationLoadProfiles[stationId];
            double baseCurrent = _nominalCurrent * loadFactor;

            // Normal çalışma: ±%20 varyasyon (tren trafiğine göre)
            double fluctuation = (_random.NextDouble() - 0.5) * baseCurrent * 0.2;
            double current = baseCurrent + fluctuation;

            // %3 ihtimalle anomali
            if (_random.NextDouble() < 0.03)
            {
                if (_random.NextDouble() < 0.4)
                    current = _nominalCurrent * 1.1 + _random.NextDouble() * _nominalCurrent * 0.1; // Aşırı akım (%110-120)
                else if (_random.NextDouble() < 0.7)
                    current = _nominalCurrent * 0.3 + _random.NextDouble() * _nominalCurrent * 0.2; // Düşük akım (%30-50)
                else
                    current = _nominalCurrent * 2.0 + _random.NextDouble() * _nominalCurrent * 1.0; // Kısa devre simülasyonu
            }

            return Math.Round(Math.Max(0, current), 1);
        }

        private double GeneratePhaseCurrent(int phase, int stationId)
        {
            double baseCurrent = GenerateCurrent(stationId) / 3;
            double imbalance = (_random.NextDouble() - 0.5) * baseCurrent * 0.08; // ±%8 dengesizlik
            return Math.Round(baseCurrent + imbalance, 1);
        }

        private double GenerateGroundCurrent(int stationId)
        {
            double current = _random.NextDouble() * 3.0; // Normal: 0-3A
            // %0.5 ihtimalle toprak arızası
            if (_random.NextDouble() < 0.005)
                current = 10 + _random.NextDouble() * 40; // 10-50A toprak arızası

            return Math.Round(current, 2);
        }

        private double GenerateFrequency(int stationId)
        {
            double frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 0.1; // 49.95-50.05 Hz (çok kararlı)
            // %1 ihtimalle frekans anormalliği
            if (_random.NextDouble() < 0.01)
                frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 1.0; // 49-51 Hz

            return Math.Round(frequency, 2);
        }

        private double GenerateTemperature(int stationId)
        {
            // Ortam sıcaklığı: -5°C ile +40°C arası (şartname)
            double baseTemp = 40.0; // Ortalama ortam sıcaklığı
            double dailyVariation = Math.Sin(DateTime.Now.Hour * Math.PI / 12) * 10; // Günlük varyasyon

            // Ekipman ısınması (yüke bağlı)
            double loadFactor = _stationLoadProfiles[stationId];
            double equipmentHeat = loadFactor * 15.0;

            double temperature = baseTemp + dailyVariation + equipmentHeat + (_random.NextDouble() - 0.5) * 5;

            // %2 ihtimalle sıcaklık anormalliği
            if (_random.NextDouble() < 0.02)
                temperature = 60 + _random.NextDouble() * 40; // 60-80°C (aşırı ısınma)

            return Math.Round(temperature, 1);
        }

        private double GenerateTHD(int stationId)
        {
            double thd = 0.5 + _random.NextDouble() * 4.5; // Normal: %0.5-5 THD
            // %3 ihtimalle yüksek harmonik
            if (_random.NextDouble() < 0.03)
                thd = 5.0 + _random.NextDouble() * 15.0; // %5-20 THD

            return Math.Round(thd, 1);
        }

        // DC Sistem değerleri
        private double GenerateDcVoltage(int stationId)
        {
            double voltage = _dcNominalVoltage + (_random.NextDouble() - 0.5) * 0.1; // 1495-1505 V
            return Math.Round(voltage, 2);
        }

        private double GenerateDcCurrent(int stationId)
        {
            // DC akım, OG akıma bağlı (trafodan sonra)
            double loadFactor = _stationLoadProfiles[stationId];
            double current = 1000 * loadFactor + (_random.NextDouble() - 0.5) * 200; // 800-1200 A
            return Math.Round(current, 1);
        }

        private double GenerateAuxDcVoltage(int stationId)
        {
            double voltage = _auxDcVoltage + (_random.NextDouble() - 0.5) * 0.01; // 109.5-110.5 V
            return Math.Round(voltage, 2);
        }

        private double GenerateAuxAcVoltage(int stationId)
        {
            double voltage = _auxAcVoltage + (_random.NextDouble() - 0.5) * 0.02; // 398-402 V
            return Math.Round(voltage, 2);
        }

        private List<string> CheckForAlarms(VldData data)
        {
            var alarms = new List<string>();

            // Gerilim alarmları (şartname limitlerine göre)
            if (data.VoltageOut < _nominalVoltage * 0.85)
                alarms.Add($"[{data.StationName}] DÜŞÜK_GERİLİM ({data.VoltageOut} kV)");
            else if (data.VoltageOut > _nominalVoltage * 1.1)
                alarms.Add($"[{data.StationName}] YÜKSEK_GERİLİM ({data.VoltageOut} kV)");
            else if (data.VoltageOut == 0)
                alarms.Add($"[{data.StationName}] GERİLİM_KESİNTİSİ");

            // Akım alarmları
            if (data.Current > _nominalCurrent * 1.1)
                alarms.Add($"[{data.StationName}] AŞIRI_AKIM ({data.Current} A)");
            if (data.GroundCurrent > 10)
                alarms.Add($"[{data.StationName}] TOPRAK_ARIZASI ({data.GroundCurrent} A)");

            // Sıcaklık alarmları
            if (data.Temperature > 80)
                alarms.Add($"[{data.StationName}] AŞIRI_SICAKLIK ({data.Temperature} °C)");
            else if (data.Temperature < -5)
                alarms.Add($"[{data.StationName}] DÜŞÜK_SICAKLIK ({data.Temperature} °C)");

            // Frekans alarmları
            if (data.Frequency < 49.0 || data.Frequency > 51.0)
                alarms.Add($"[{data.StationName}] FREKANS_SAPMASI ({data.Frequency} Hz)");

            // Güç kalitesi alarmları
            if (data.THDVoltage > 8.0)
                alarms.Add($"[{data.StationName}] YÜKSEK_GERİLİM_THD (%{data.THDVoltage})");
            if (data.THDCurrent > 10.0)
                alarms.Add($"[{data.StationName}] YÜKSEK_AKIM_THD (%{data.THDCurrent})");

            // Faz dengesizliği
            double maxPhaseVoltage = Math.Max(data.VoltageL1, Math.Max(data.VoltageL2, data.VoltageL3));
            double minPhaseVoltage = Math.Min(data.VoltageL1, Math.Min(data.VoltageL2, data.VoltageL3));
            double voltageImbalance = (maxPhaseVoltage - minPhaseVoltage) / maxPhaseVoltage * 100;

            if (voltageImbalance > 3.0)
                alarms.Add($"[{data.StationName}] FAZ_DENGESİZLİĞİ (%{voltageImbalance:F1})");

            // DC sistem alarmları
            if (data.DcVoltage < 1.4 || data.DcVoltage > 1.6)
                alarms.Add($"[{data.StationName}] DC_GERİLİM_ANORMALLİĞİ ({data.DcVoltage} kV)");

            // Güç faktörü alarmı
            if (data.PowerFactor < 0.85)
                alarms.Add($"[{data.StationName}] DÜŞÜK_GÜÇ_FAKTÖRÜ ({data.PowerFactor:F2})");

            return alarms;
        }

        public List<VldData> GetDataHistoryForStation(int stationId)
        {
            return _deviceDataHistory.ContainsKey(stationId)
                ? _deviceDataHistory[stationId]
                : new List<VldData>();
        }

        // Senaryo bazlı test fonksiyonları (şartnameye uygun)
        public void SimulateVoltageSag()
        {
            _nominalVoltage = 30.0; // %13 düşüş
            StatusChanged?.Invoke(this, "Gerilim düşüşü senaryosu aktif (30 kV)");
        }

        public void SimulateOverload()
        {
            _nominalCurrent = 700.0; // %21 artış
            StatusChanged?.Invoke(this, "Aşırı yük senaryosu aktif (700 A)");
        }

        public void SimulateFrequencyDeviation()
        {
            _baseFrequency = 48.5; // -1.5 Hz sapma
            StatusChanged?.Invoke(this, "Frekans sapması senaryosu aktif (48.5 Hz)");
        }

        public void SimulateGroundFault()
        {
            // Toprak arızası senaryosu - next cycle'da etkili olacak
            StatusChanged?.Invoke(this, "Toprak arızası senaryosu aktif");
        }

        public void ResetToNormal()
        {
            _nominalVoltage = 34.5;
            _nominalCurrent = 577.35;
            _baseFrequency = 50.0;
            StatusChanged?.Invoke(this, "Normal çalışma moduna dönüldü (34.5 kV, 577.35 A, 50 Hz)");
        }
    }
}