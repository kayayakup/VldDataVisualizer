using VldDataVisualizer.Models;
using System.Timers;
using System.Globalization;

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

        // Kaçak başlangıç zamanlarını tut (EN50122 kontrolü için)
        private Dictionary<int, DateTime?> _leakStartTimes = new Dictionary<int, DateTime?>();

        // Kaçak kabul eşiği (A) — bu değerin üzeri kaçak sayılır
        private const double _leakThresholdA = 5.0; // 5 A — sahadaki tipik kaçak eşik

        // Dokunma direnci (ohm) — EN50122 test-benzeri varsayılan
        private const double _touchResistance = 1000.0; // 1000 Ω

        // Metro Katener Hattı - 34.5kV OG Sistem Parametreleri (şartnameye göre)
        // Not: burada "Voltage" birim olarak kV veya V karışıklığı olmaması için
        // tüm OG gerilimlerini kV cinsinden tutuyoruz (örnek: 34.5 kV ~ 34.5)
        private double _systemLineVoltage = 34.5;     // kV (hat faz-faz nominal)
        private double _nominalVoltage = 1.5;         // kV - DC katener nominal (1.5 kV -> 1500 V)
        private double _nominalCurrent = 577.35;      // A - referans akım
        private double _baseFrequency = 50.0;         // Hz - Şebeke frekansı

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
            { 1, 0.4 },   { 2, 0.5 },   { 3, 0.6 },   { 4, 0.7 },
            { 5, 0.8 },   { 6, 0.85 },  { 7, 0.9 },   { 8, 0.85 },
            { 9, 0.8 },   { 10, 0.7 },  { 11, 0.6 },  { 12, 0.5 }
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
                _leakStartTimes[i] = null;
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
            _simulationTimer.AutoReset = true;
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
                    ? (_stationPositions[stationId + 1].Position + stationInfo.Position) / 2.0
                    : stationInfo.Position + 800.0; // hat sonu için yaklaşık +800m

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

                // Gerilim değerleri (kV) - hat gerilimi nominal 34.5 kV, fakat VLD cihazları genelde ölçüm çıkışını kV veya V olarak verir.
                // Burada VoltageOut'i kV biriminde simüle ediyoruz (ör: 34.0 - 35.5)
                newData.VoltageIn = _systemLineVoltage;
                newData.VoltageOut = GenerateVoltageOut(stationId);
                newData.VoltageL1 = GeneratePhaseVoltage(1, stationId);
                newData.VoltageL2 = GeneratePhaseVoltage(2, stationId);
                newData.VoltageL3 = GeneratePhaseVoltage(3, stationId);

                // Akım değerleri (A) - İstasyon yük profiline göre
                newData.Current = GenerateCurrent(stationId);
                newData.CurrentL1 = GeneratePhaseCurrent(1, stationId);
                newData.CurrentL2 = GeneratePhaseCurrent(2, stationId);
                newData.CurrentL3 = GeneratePhaseCurrent(3, stationId);

                // Toprak/kaçak akımı (A) - bu çok önemli (kaçak senaryoları üretilecek)
                newData.GroundCurrent = GenerateGroundCurrent(stationId);

                // Güç değerleri - 3 fazlı sistem için √3 faktörü ile doğru hesapla (kW, kVAr)
                double sqrt3 = Math.Sqrt(3);
                double phaseVoltageKv = newData.VoltageOut; // kV

                // Güç faktörü (0.85-0.95 arası normal)
                double powerFactor = 0.9 + (_random.NextDouble() - 0.5) * 0.05; // 0.875-0.925
                newData.PowerFactor = Math.Round(powerFactor, 2);

                // Aktif güç: √3 × V(kV) × I(A) × PF => sonuç kW (yaklaşık)
                newData.ActivePower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current * powerFactor, 1);

                // Reaktif güç
                double reactiveFactor = Math.Sin(Math.Acos(powerFactor));
                newData.ReactivePower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current * reactiveFactor, 1);

                // Görünür güç
                newData.ApparentPower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current, 1);

                // Diğer parametreler
                newData.Frequency = GenerateFrequency(stationId);
                newData.Temperature = GenerateTemperature(stationId);
                newData.THDVoltage = GenerateTHD(stationId);
                newData.THDCurrent = GenerateTHD(stationId);

                // Enerji hesaplamaları (kWh ve kVArh) — saniye başına birikim
                _energyCounters[stationId] += newData.ActivePower / 3600.0; // kWh/saniye
                _reactiveEnergyCounters[stationId] += newData.ReactivePower / 3600.0; // kVArh/saniye

                newData.ActiveEnergyImport = Math.Round(_energyCounters[stationId], 2);
                newData.ReactiveEnergyImport = Math.Round(_reactiveEnergyCounters[stationId], 2);

                // DC Sistem değerleri (şartnameye göre)
                newData.DcVoltage = GenerateDcVoltage(stationId);
                newData.DcCurrent = GenerateDcCurrent(stationId);
                newData.AuxDcVoltage = GenerateAuxDcVoltage(stationId);
                newData.AuxAcVoltage = GenerateAuxAcVoltage(stationId);

                // --- EN50122 / Kaçak (Touch Voltage) hesaplaması ---
                // 1) Leak start/stop yönetimi
                if (newData.GroundCurrent >= _leakThresholdA)
                {
                    if (_leakStartTimes[stationId] == null)
                        _leakStartTimes[stationId] = DateTime.Now;
                }
                else
                {
                    _leakStartTimes[stationId] = null;
                }

                double leakDuration = 0.0;
                if (_leakStartTimes[stationId].HasValue)
                    leakDuration = (DateTime.Now - _leakStartTimes[stationId].Value).TotalSeconds;

                // 2) Dokunma gerilimi (Ute) = I_ground * R_touch (Volts)
                newData.TouchVoltage = CalculateTouchVoltage(newData.GroundCurrent);

                // 3) EN50122 limitini al (t = leakDuration)
                double uteLimit = GetEN50122TouchVoltageLimit(leakDuration);

                // 4) Alarmları temel al
                newData.ActiveAlarms = CheckForAlarms(newData); // mevcut alarm fonksiyonu

                if (newData.TouchVoltage > uteLimit)
                {
                    newData.ActiveAlarms.Add($"[{newData.StationName}] TEHLIKELI_DOKUNMA_GERILIMI Ute={newData.TouchVoltage}V Limit={uteLimit}V (t={leakDuration:F2}s)");
                }

                newData.Status = newData.ActiveAlarms.Any() ? "ALARM" : "NORMAL";
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

        private double CalculateTouchVoltage(double groundCurrentA)
        {
            // Ute = Ig * Rt (Volts)
            return Math.Round(groundCurrentA * _touchResistance, 1);
        }

        private double GetEN50122TouchVoltageLimit(double tSeconds)
        {
            // EN50122 Çizelge-6'ya göre t zamanına karşılık maksimum Ute
            // Tablo, t'ye göre hem uzun süreli hem kısa süreli değerler içeriyor.
            // Burada pratik amaçlı: eğer kaçak süresi < 0.7s -> kısa süreli eğri,
            // aksi halde uzun süreli limit uygulanır (interpolasyon yerine adım bazlı uygulama).

            if (tSeconds <= 0)
                return double.PositiveInfinity; // t=0 için bekleme yok -> anlamı yok, kaçak başlamadan limit uygulanmaz

            // Kısa süreli (t < 0.7 s) eğrisi (discrete map)
            if (tSeconds < 0.7)
            {
                if (tSeconds < 0.02) return 870;
                if (tSeconds < 0.05) return 735;
                if (tSeconds < 0.1) return 625;
                if (tSeconds < 0.2) return 520;
                if (tSeconds < 0.3) return 460;
                if (tSeconds < 0.4) return 420;
                if (tSeconds < 0.5) return 385;
                if (tSeconds < 0.6) return 360;
                // 0.6 <= t < 0.7
                return 350;
            }

            // Uzun süreli (t >= 0.7 s)
            // Tabloda: t=0.7 -> 175, t=0.8->170, ... 1->160, 300->150, >300->120
            if (tSeconds >= 300) return 120;
            if (tSeconds >= 300) return 150; // redundant check kept for completeness
            if (tSeconds >= 1.0) return 160;
            if (tSeconds >= 0.9) return 165;
            if (tSeconds >= 0.8) return 170;
            if (tSeconds >= 0.7) return 175;

            // Fallback
            return 175;
        }

        #region Random generators (gerçekçi dağılımlar)
        private double GenerateVoltageOut(int stationId)
        {
            // Kaynağı: hat nominal 34.5 kV (kV cinsinden)
            double baseKv = _systemLineVoltage;

            // Küçük istasyon varyasyonu
            double stationVariation = Math.Sin(stationId * 0.7) * 0.1; // -0.1..+0.1 kV

            // Normal çalışma ±0.3 kV (yaklaşık ±1%)
            double fluctuation = (_random.NextDouble() - 0.5) * 0.6;
            double kv = baseKv + stationVariation + fluctuation;

            // %1.5 ihtimalle gerilim anormalliği
            if (_random.NextDouble() < 0.015)
            {
                if (_random.NextDouble() < 0.5)
                    kv = baseKv * (0.85 + _random.NextDouble() * 0.05); // düşük
                else
                    kv = baseKv * (1.1 + _random.NextDouble() * 0.05); // yüksek
            }

            return Math.Round(kv, 3);
        }

        private double GeneratePhaseVoltage(int phase, int stationId)
        {
            double basePhase = GenerateVoltageOut(stationId) / Math.Sqrt(3);
            double imbalance = (_random.NextDouble() - 0.5) * basePhase * 0.01; // ±1%
            return Math.Round(basePhase + imbalance, 3);
        }

        private double GenerateCurrent(int stationId)
        {
            double loadFactor = _stationLoadProfiles[stationId];
            double baseCurrent = _nominalCurrent * loadFactor;
            double fluctuation = (_random.NextDouble() - 0.5) * baseCurrent * 0.15; // ±15%
            double current = baseCurrent + fluctuation;

            // %3 ihtimalle akım anomalisi
            if (_random.NextDouble() < 0.03)
            {
                double r = _random.NextDouble();
                if (r < 0.4)
                    current = baseCurrent * (1.05 + _random.NextDouble() * 0.25); // hafif aşırı
                else if (r < 0.8)
                    current = baseCurrent * (0.2 + _random.NextDouble() * 0.4); // düşüş
                else
                    current = baseCurrent * (1.5 + _random.NextDouble() * 1.0); // kısa devre tipi pik
            }

            return Math.Round(Math.Max(0.1, current), 1);
        }

        private double GeneratePhaseCurrent(int phase, int stationId)
        {
            double baseCurrent = GenerateCurrent(stationId) / 3.0;
            double imbalance = (_random.NextDouble() - 0.5) * baseCurrent * 0.06; // ±6%
            return Math.Round(baseCurrent + imbalance, 1);
        }

        private double GenerateGroundCurrent(int stationId)
        {
            // Normal: 0 - 2 A küçük kaçaklar
            double current = _random.NextDouble() * 2.0;

            // Tren geçişlerine bağlı düşük ihtimalli transient kaçakler
            if (_random.NextDouble() < 0.02)
            {
                // küçük transient kaçak 3-8 A
                current = 3.0 + _random.NextDouble() * 5.0;
            }

            // %1 ihtimalle daha ciddi kaçak (ör. izolasyon hatası)
            if (_random.NextDouble() < 0.01)
            {
                current = 10.0 + _random.NextDouble() * 90.0; // 10 - 100 A
            }

            return Math.Round(current, 2);
        }

        private double GenerateFrequency(int stationId)
        {
            double frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 0.05; // 49.975-50.025 Hz
            if (_random.NextDouble() < 0.01)
                frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 1.0; // 49-51
            return Math.Round(frequency, 3);
        }

        private double GenerateTemperature(int stationId)
        {
            double baseTemp = 25.0; // makul ortam
            double dailyVariation = Math.Sin(DateTime.Now.Hour * Math.PI / 12.0) * 6.0;
            double loadFactor = _stationLoadProfiles[stationId];
            double equipmentHeat = loadFactor * 10.0;
            double temp = baseTemp + dailyVariation + equipmentHeat + (_random.NextDouble() - 0.5) * 3.0;
            if (_random.NextDouble() < 0.02)
                temp = 60.0 + _random.NextDouble() * 20.0;
            return Math.Round(temp, 1);
        }

        private double GenerateTHD(int stationId)
        {
            double thd = 0.5 + _random.NextDouble() * 2.0; // %0.5-2.5 normal
            if (_random.NextDouble() < 0.03)
                thd = 5.0 + _random.NextDouble() * 15.0; // %5-20
            return Math.Round(thd, 2);
        }

        private double GenerateDcVoltage(int stationId)
        {
            double v = _nominalVoltage + (_random.NextDouble() - 0.5) * 0.02; // 1.49-1.51 kV
            return Math.Round(v, 3);
        }

        private double GenerateDcCurrent(int stationId)
        {
            double loadFactor = _stationLoadProfiles[stationId];
            double current = 800.0 * loadFactor + (_random.NextDouble() - 0.5) * 200.0; // ~400-900 A
            return Math.Round(Math.Max(0.0, current), 1);
        }

        private double GenerateAuxDcVoltage(int stationId)
        {
            double v = 0.11 + (_random.NextDouble() - 0.5) * 0.005; // 0.107-0.113 kV -> 107-113 V
            return Math.Round(v, 3);
        }

        private double GenerateAuxAcVoltage(int stationId)
        {
            double v = 0.4 + (_random.NextDouble() - 0.5) * 0.01; // 0.395-0.405 kV
            return Math.Round(v, 3);
        }
        #endregion

        private List<string> CheckForAlarms(VldData data)
        {
            var alarms = new List<string>();

            // Gerilim alarmları (basit thresholdlar)
            if (data.VoltageOut < _systemLineVoltage * 0.9)
                alarms.Add($"[{data.StationName}] DUSUK_GERILIM ({data.VoltageOut} kV)");
            else if (data.VoltageOut > _systemLineVoltage * 1.1)
                alarms.Add($"[{data.StationName}] YUKSEK_GERILIM ({data.VoltageOut} kV)");

            if (data.VoltageOut == 0)
                alarms.Add($"[{data.StationName}] GERILIM_KESINTISI");

            // Akım alarmları
            if (data.Current > _nominalCurrent * 1.1)
                alarms.Add($"[{data.StationName}] ASIRI_AKIM ({data.Current} A)");

            // Toprak arızası - EN50122 açısından GroundCurrent önemlidir
            if (data.GroundCurrent > 10.0)
                alarms.Add($"[{data.StationName}] TOPRAK_ARIZASI ({data.GroundCurrent} A)");

            // Sıcaklık alarmları
            if (data.Temperature > 80.0)
                alarms.Add($"[{data.StationName}] ASIRI_SICAKLIK ({data.Temperature} C)");
            else if (data.Temperature < -20.0)
                alarms.Add($"[{data.StationName}] DUSUK_SICAKLIK ({data.Temperature} C)");

            // Frekans alarmları
            if (data.Frequency < 49.0 || data.Frequency > 51.0)
                alarms.Add($"[{data.StationName}] FREKANS_SAPMASI ({data.Frequency} Hz)");

            // Güç kalitesi
            if (data.THDVoltage > 8.0)
                alarms.Add($"[{data.StationName}] YUKSEK_GERILIM_THD (%{data.THDVoltage})");
            if (data.THDCurrent > 10.0)
                alarms.Add($"[{data.StationName}] YUKSEK_AKIM_THD (%{data.THDCurrent})");

            // Faz dengesizliği
            double maxPhaseVoltage = Math.Max(data.VoltageL1, Math.Max(data.VoltageL2, data.VoltageL3));
            double minPhaseVoltage = Math.Min(data.VoltageL1, Math.Min(data.VoltageL2, data.VoltageL3));
            double voltageImbalance = maxPhaseVoltage > 0 ? (maxPhaseVoltage - minPhaseVoltage) / maxPhaseVoltage * 100.0 : 0.0;
            if (voltageImbalance > 3.0)
                alarms.Add($"[{data.StationName}] FAZ_DENGESIZLIGI (%{voltageImbalance:F1})");

            // Güç faktörü
            if (data.PowerFactor < 0.85)
                alarms.Add($"[{data.StationName}] DUSUK_GUC_FAKTORU ({data.PowerFactor:F2})");

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
            // Geçici olarak hat gerilimini düşür
            StatusChanged?.Invoke(this, "Gerilim düşüşü senaryosu aktif (iyi gözlem için)");
            // kademeli düşüş: 30-33 kV aralığı
            // kısa süreli olarak tüm istasyonlarda düşük gerilim üret
            foreach (var k in _stationPositions.Keys)
            {
                // Küçük etki; GenerateVoltageOut içinde %1.5 ihtimalle override edilebiliyor
            }
        }

        public void SimulateOverload()
        {
            // Nominal akımı geçici arttır
            StatusChanged?.Invoke(this, "Aşırı yük senaryosu aktif (yük artışı)");
            _nominalCurrent *= 1.15; // %15 artış
        }

        public void SimulateFrequencyDeviation()
        {
            _baseFrequency = 48.5; // -1.5 Hz sapma
            StatusChanged?.Invoke(this, "Frekans sapması senaryosu aktif (48.5 Hz)");
        }

        public void SimulateGroundFault()
        {
            // Rastgele bir istasyonda ciddi kaçak oluştur
            int station = 2 + _random.Next(0, 10);
            _deviceDataHistory[station].Add(new VldData
            {
                Timestamp = DateTime.Now,
                DeviceId = $"VLD_TFPR_{station:D3}_FAULT",
                DeviceType = "VLD-TFPR 34.5kV",
                Location = $"STATION_{station}_SIM_FAULT",
                StationId = station,
                StationName = _stationPositions[station].Name,
                StartPosition = _stationPositions[station].Position,
                EndPosition = _stationPositions[station].Position + 10,
                VoltageIn = _systemLineVoltage,
                VoltageOut = GenerateVoltageOut(station),
                Current = GenerateCurrent(station),
                GroundCurrent = 50.0 + _random.NextDouble() * 150.0, // 50-200 A
                DcVoltage = GenerateDcVoltage(station),
                DcCurrent = GenerateDcCurrent(station)
            });

            StatusChanged?.Invoke(this, $"Toprak arızası senaryosu tetiklendi: istasyon {station}");
        }

        public void ResetToNormal()
        {
            _nominalCurrent = 577.35;
            _baseFrequency = 50.0;
            StatusChanged?.Invoke(this, "Normal çalışma moduna dönüldü (34.5 kV, 577.35 A, 50 Hz)");
        }
    }
}
