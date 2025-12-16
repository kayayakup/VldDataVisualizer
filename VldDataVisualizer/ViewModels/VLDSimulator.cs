using VldDataVisualizer.Models;
using System.Timers;
using System.Globalization;

namespace VldDataVisualizer.ViewModels
{
    public class VLDSimulator
    {
        public event EventHandler<List<VldData>> DataGenerated;
        public event EventHandler<string> StatusChanged;

        private System.Timers.Timer _simulationTimer;
        private Random _random = new Random();
        private bool _isSimulationRunning = false;

        private Dictionary<int, List<VldData>> _deviceDataHistory = new Dictionary<int, List<VldData>>();
        private Dictionary<int, double> _energyCounters = new Dictionary<int, double>();
        private Dictionary<int, double> _reactiveEnergyCounters = new Dictionary<int, double>();
        private Dictionary<int, DateTime?> _leakStartTimes = new Dictionary<int, DateTime?>();

        // ✅ DÜZELTİLDİ: Daha gerçekçi eşik değerleri
        private const double _leakThresholdA = 3.0; // 3A yerine 5A → Daha sık hata
        private const double _touchResistance = 50.0; // ✅ 1000Ω → 50Ω (Gerçekçi toprak direnci)

        // AC SİSTEM - kV cinsinden
        private double _systemLineVoltage = 34.5;
        private double _nominalCurrent = 577.35;
        private double _baseFrequency = 50.0;

        // DC SİSTEM - V cinsinden
        private double _nominalDcVoltage = 1500.0;

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

        private readonly Dictionary<int, double> _stationLoadProfiles = new Dictionary<int, double>
        {
            { 1, 0.5 }, { 2, 0.5 }, { 3, 0.5 }, { 4, 0.5 },
            { 5, 0.5 }, { 6, 0.5 }, { 7, 0.5 }, { 8, 0.5 },
            { 9, 0.5 }, { 10, 0.5 }, { 11, 0.5 }, { 12, 0.5 }
        };

        public bool IsRunning { get; private set; }

        public VLDSimulator()
        {
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

            for (int stationId = 1; stationId <= 12; stationId++)
            {
                var stationInfo = _stationPositions[stationId];

                double startPos = stationInfo.Position;
                double endPos = stationId < 12
                    ? (_stationPositions[stationId + 1].Position + stationInfo.Position) / 2.0
                    : stationInfo.Position + 800.0;

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

                // AC GERİLİMLER
                newData.VoltageIn = _systemLineVoltage;
                newData.VoltageOut = GenerateVoltageOut(stationId);
                newData.VoltageL1 = GeneratePhaseVoltage(1, stationId);
                newData.VoltageL2 = GeneratePhaseVoltage(2, stationId);
                newData.VoltageL3 = GeneratePhaseVoltage(3, stationId);

                // AC AKIMLAR
                newData.Current = GenerateCurrent(stationId);
                newData.CurrentL1 = GeneratePhaseCurrent(1, stationId);
                newData.CurrentL2 = GeneratePhaseCurrent(2, stationId);
                newData.CurrentL3 = GeneratePhaseCurrent(3, stationId);

                // ✅ DÜZELTİLDİ: Daha sık toprak arızası üretimi
                newData.GroundCurrent = GenerateGroundCurrent(stationId);

                // AC GÜÇ HESAPLAMALARI
                double sqrt3 = Math.Sqrt(3);
                double phaseVoltageKv = newData.VoltageOut;
                double powerFactor = 0.9 + (_random.NextDouble() - 0.5) * 0.05;
                newData.PowerFactor = Math.Round(powerFactor, 2);

                newData.ActivePower = Math.Round((sqrt3 * phaseVoltageKv * newData.Current * powerFactor), 1);
                double reactiveFactor = Math.Sin(Math.Acos(powerFactor));
                newData.ReactivePower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current * reactiveFactor, 1);
                newData.ApparentPower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current, 1);

                // DİĞER PARAMETRELER
                newData.Frequency = GenerateFrequency(stationId);
                newData.Temperature = GenerateTemperature(stationId);
                newData.THDVoltage = GenerateTHD(stationId);
                newData.THDCurrent = GenerateTHD(stationId);

                // ✅ DÜZELTİLDİ: TouchVoltage hesaplama
                newData.TouchVoltage = CalculateTouchVoltage(newData.GroundCurrent);

                // DC SİSTEM DEĞERLERİ
                newData.DcVoltage = GenerateDcVoltage(stationId);
                newData.DcCurrent = GenerateDcCurrent(stationId);
                newData.AuxDcVoltage = GenerateAuxDcVoltage(stationId);
                newData.AuxAcVoltage = GenerateAuxAcVoltage(stationId);

                // ✅ DÜZELTİLDİ: EN50122 KAÇAK HESAPLAMASI
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

                // ✅ DÜZELTİLDİ: EN50122 limit kontrolü
                var touchLimits = EN50122Analyzer.GetAllowedDurationForTouchVoltage(newData.TouchVoltage);

                newData.ActiveAlarms = CheckForAlarms(newData, leakDuration);

                // Kritik: TouchVoltage limit kontrolü
                if (leakDuration > 0 && leakDuration > touchLimits)
                {
                    newData.ActiveAlarms.Add($"[{newData.StationName}] ❌ TEHLIKELI_DOKUNMA_GERİLİMİ " +
                        $"Ute={newData.TouchVoltage:N0}V " +
                        $"İzin Verilen Süre={touchLimits:F2}s " +
                        $"Geçen Süre={leakDuration:F2}s");
                }

                newData.Status = newData.ActiveAlarms.Any() ? "ALARM" : "NORMAL";
                newData.IsCommunicationActive = _random.NextDouble() > 0.02;

                // TARİHÇEYE EKLE
                _deviceDataHistory[stationId].Add(newData);
                if (_deviceDataHistory[stationId].Count > 500)
                    _deviceDataHistory[stationId].RemoveAt(0);

                allDevicesData.Add(newData);
            }

            DataGenerated?.Invoke(this, allDevicesData);
        }

        #region DC GENERATOR FUNCTIONS

        private double GenerateDcVoltage(int stationId)
        {
            double baseV = _nominalDcVoltage;
            double stationVariation = Math.Sin(stationId * 0.7) * 10;
            double fluctuation = (_random.NextDouble() - 0.5) * 40;
            double voltage = baseV + stationVariation + fluctuation;

            // ✅ ARTTIRILDI: %10 ihtimalle DC gerilim anormalliği (eskiden %2)
            if (_random.NextDouble() < 0.10)
            {
                if (_random.NextDouble() < 0.5)
                    voltage = baseV * (0.80 + _random.NextDouble() * 0.10); // Düşük: 1200-1350V
                else
                    voltage = baseV * (1.15 + _random.NextDouble() * 0.15); // Yüksek: 1725-1950V
            }

            return Math.Round(voltage, 1);
        }

        private double GenerateDcCurrent(int stationId)
        {
            double loadFactor = _stationLoadProfiles[stationId];
            double baseCurrent = 800.0 * loadFactor;
            double fluctuation = (_random.NextDouble() - 0.5) * baseCurrent * 0.15;
            double current = baseCurrent + fluctuation;

            // ✅ ARTTIRILDI: %8 ihtimalle DC akım anomalisi (eskiden %3)
            if (_random.NextDouble() < 0.08)
            {
                double r = _random.NextDouble();
                if (r < 0.4)
                    current = baseCurrent * (1.05 + _random.NextDouble() * 0.25);
                else if (r < 0.8)
                    current = baseCurrent * (0.2 + _random.NextDouble() * 0.4);
                else
                    current = baseCurrent * (1.5 + _random.NextDouble() * 1.0);
            }

            return Math.Round(Math.Max(10.0, current), 1);
        }

        private double GenerateAuxDcVoltage(int stationId)
        {
            double baseV = 110.0;
            double fluctuation = (_random.NextDouble() - 0.5) * 6;
            double voltage = baseV + fluctuation;

            if (_random.NextDouble() < 0.05) // %5 ihtimal
                voltage = baseV + (_random.NextDouble() - 0.5) * 20;

            return Math.Round(voltage, 1);
        }

        private double GenerateAuxAcVoltage(int stationId)
        {
            double baseV = 400.0;
            double fluctuation = (_random.NextDouble() - 0.5) * 10;
            double voltage = baseV + fluctuation;

            if (_random.NextDouble() < 0.05) // %5 ihtimal
                voltage = baseV + (_random.NextDouble() - 0.5) * 40;

            return Math.Round(voltage, 1);
        }

        #endregion

        #region AC GENERATOR FUNCTIONS

        private double GenerateVoltageOut(int stationId)
        {
            double baseKv = _systemLineVoltage;
            double stationVariation = Math.Sin(stationId * 0.7) * 0.1;
            double fluctuation = (_random.NextDouble() - 0.5) * 0.6;
            double kv = baseKv + stationVariation + fluctuation;

            if (_random.NextDouble() < 0.015)
            {
                if (_random.NextDouble() < 0.5)
                    kv = baseKv * (0.85 + _random.NextDouble() * 0.05);
                else
                    kv = baseKv * (1.1 + _random.NextDouble() * 0.05);
            }

            return Math.Round(kv, 3);
        }

        private double GeneratePhaseVoltage(int phase, int stationId)
        {
            double basePhase = GenerateVoltageOut(stationId) / Math.Sqrt(3);
            double imbalance = (_random.NextDouble() - 0.5) * basePhase * 0.01;
            return Math.Round(basePhase + imbalance, 3);
        }

        private double GenerateCurrent(int stationId)
        {
            double loadFactor = _stationLoadProfiles[stationId];
            double baseCurrent = _nominalCurrent * loadFactor;
            double fluctuation = (_random.NextDouble() - 0.5) * baseCurrent * 0.15;
            double current = baseCurrent + fluctuation;

            if (_random.NextDouble() < 0.03)
            {
                double r = _random.NextDouble();
                if (r < 0.4)
                    current = baseCurrent * (1.05 + _random.NextDouble() * 0.25);
                else if (r < 0.8)
                    current = baseCurrent * (0.2 + _random.NextDouble() * 0.4);
                else
                    current = baseCurrent * (1.5 + _random.NextDouble() * 1.0);
            }

            return Math.Round(Math.Max(0.1, current), 1);
        }

        private double GeneratePhaseCurrent(int phase, int stationId)
        {
            double baseCurrent = GenerateCurrent(stationId) / 3.0;
            double imbalance = (_random.NextDouble() - 0.5) * baseCurrent * 0.06;
            return Math.Round(baseCurrent + imbalance, 1);
        }

        private double GenerateGroundCurrent(int stationId)
        {
            // ✅ DÜZELTİLDİ: Daha sık toprak arızası
            double current = _random.NextDouble() * 2.0;

            // %15 ihtimalle 3-8A arası (SARI - Warning)
            if (_random.NextDouble() < 0.15)
                current = 3.0 + _random.NextDouble() * 5.0;

            // %8 ihtimalle 10-30A arası (KIRMIZI - Critical)
            if (_random.NextDouble() < 0.08)
                current = 10.0 + _random.NextDouble() * 20.0;

            return Math.Round(current, 2);
        }

        private double GenerateFrequency(int stationId)
        {
            double frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 0.05;
            if (_random.NextDouble() < 0.01)
                frequency = _baseFrequency + (_random.NextDouble() - 0.5) * 1.0;
            return Math.Round(frequency, 3);
        }

        private double GenerateTemperature(int stationId)
        {
            double baseTemp = 25.0;
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
            double thd = 0.5 + _random.NextDouble() * 2.0;
            if (_random.NextDouble() < 0.03)
                thd = 5.0 + _random.NextDouble() * 15.0;
            return Math.Round(thd, 2);
        }

        #endregion

        #region HELPER FUNCTIONS

        private double CalculateTouchVoltage(double groundCurrentA)
        {
            // ✅ DÜZELTİLDİ: 50Ω toprak direnci (gerçekçi)
            // 3A × 50Ω = 150V (EN50122 limitlerinde)
            // 10A × 50Ω = 500V (kritik seviye)
            return Math.Round(groundCurrentA * _touchResistance, 1);
        }

        private List<string> CheckForAlarms(VldData data, double leakDuration)
        {
            var alarms = new List<string>();

            // AC ALARMLARI
            if (data.VoltageOut < _systemLineVoltage * 0.9)
                alarms.Add($"[{data.StationName}] DUSUK_AC_GERILIM ({data.VoltageOut} kV)");
            else if (data.VoltageOut > _systemLineVoltage * 1.1)
                alarms.Add($"[{data.StationName}] YUKSEK_AC_GERILIM ({data.VoltageOut} kV)");

            if (data.Current > _nominalCurrent * 1.1)
                alarms.Add($"[{data.StationName}] ASIRI_AC_AKIM ({data.Current} A)");

            // DC ALARMLARI
            if (data.DcVoltage < 1200)
                alarms.Add($"[{data.StationName}] ⚠️ DUSUK_DC_GERILIM ({data.DcVoltage} V)");
            else if (data.DcVoltage > 1800)
                alarms.Add($"[{data.StationName}] ⚠️ YUKSEK_DC_GERILIM ({data.DcVoltage} V)");

            if (data.DcCurrent > 1000)
                alarms.Add($"[{data.StationName}] ASIRI_DC_AKIM ({data.DcCurrent} A)");

            // ✅ DÜZELTİLDİ: TOPRAK ARİZASI
            if (data.GroundCurrent > 10.0)
                alarms.Add($"[{data.StationName}] 🚨 TOPRAK_ARIZASI ({data.GroundCurrent} A, {leakDuration:F1}s)");
            else if (data.GroundCurrent > 3.0)
                alarms.Add($"[{data.StationName}] ⚠️ YUKSEK_TOPRAK_AKIMI ({data.GroundCurrent} A)");

            // SICAKLIK
            if (data.Temperature > 80.0)
                alarms.Add($"[{data.StationName}] ASIRI_SICAKLIK ({data.Temperature} C)");
            else if (data.Temperature < -20.0)
                alarms.Add($"[{data.StationName}] DUSUK_SICAKLIK ({data.Temperature} C)");

            // FREKANS
            if (data.Frequency < 49.0 || data.Frequency > 51.0)
                alarms.Add($"[{data.StationName}] FREKANS_SAPMASI ({data.Frequency} Hz)");

            // GÜÇ KALİTESİ
            if (data.THDVoltage > 8.0)
                alarms.Add($"[{data.StationName}] YUKSEK_GERILIM_THD (%{data.THDVoltage})");
            if (data.THDCurrent > 10.0)
                alarms.Add($"[{data.StationName}] YUKSEK_AKIM_THD (%{data.THDCurrent})");

            // GÜÇ FAKTÖRÜ
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

        #endregion

        #region TEST SCENARIOS

        public void SimulateVoltageSag()
        {
            StatusChanged?.Invoke(this, "Gerilim düşüşü senaryosu aktif");
        }

        public void SimulateOverload()
        {
            _nominalCurrent *= 1.15;
            StatusChanged?.Invoke(this, "Aşırı yük senaryosu aktif");
        }

        public void SimulateFrequencyDeviation()
        {
            _baseFrequency = 48.5;
            StatusChanged?.Invoke(this, "Frekans sapması senaryosu aktif (48.5 Hz)");
        }

        public void SimulateGroundFault()
        {
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
                GroundCurrent = 50.0 + _random.NextDouble() * 150.0,
                DcVoltage = GenerateDcVoltage(station),
                DcCurrent = GenerateDcCurrent(station)
            });

            StatusChanged?.Invoke(this, $"Toprak arızası senaryosu tetiklendi: istasyon {station}");
        }

        public void ResetToNormal()
        {
            _nominalCurrent = 577.35;
            _baseFrequency = 50.0;
            StatusChanged?.Invoke(this, "Normal çalışma moduna dönüldü");
        }

        #endregion
    }
}