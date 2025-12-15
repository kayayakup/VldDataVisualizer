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

        private const double _leakThresholdA = 5.0;
        private const double _touchResistance = 1000.0;

        // AC SİSTEM - kV cinsinden
        private double _systemLineVoltage = 34.5;     // kV (AC)
        private double _nominalCurrent = 577.35;      // A
        private double _baseFrequency = 50.0;         // Hz

        // DC SİSTEM - V cinsinden (DEĞİŞTİRİLDİ)
        private double _nominalDcVoltage = 1500.0;    // V - DC katener nominal (1.5 kV yerine 1500 V)

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

                // AC GERİLİMLER - kV cinsinden
                newData.VoltageIn = _systemLineVoltage; // kV
                newData.VoltageOut = GenerateVoltageOut(stationId); // kV
                newData.VoltageL1 = GeneratePhaseVoltage(1, stationId); // kV
                newData.VoltageL2 = GeneratePhaseVoltage(2, stationId); // kV
                newData.VoltageL3 = GeneratePhaseVoltage(3, stationId); // kV

                // AC AKIMLAR - A cinsinden
                newData.Current = GenerateCurrent(stationId);
                newData.CurrentL1 = GeneratePhaseCurrent(1, stationId);
                newData.CurrentL2 = GeneratePhaseCurrent(2, stationId);
                newData.CurrentL3 = GeneratePhaseCurrent(3, stationId);

                // TOPRAK AKIMI - A cinsinden
                newData.GroundCurrent = GenerateGroundCurrent(stationId);

                // AC GÜÇ HESAPLAMALARI
                double sqrt3 = Math.Sqrt(3);
                double phaseVoltageKv = newData.VoltageOut;
                double powerFactor = 0.9 + (_random.NextDouble() - 0.5) * 0.05;
                newData.PowerFactor = Math.Round(powerFactor, 2);

                newData.ActivePower = Math.Round((sqrt3 * phaseVoltageKv * newData.Current * powerFactor), 1); // kW
                double reactiveFactor = Math.Sin(Math.Acos(powerFactor));
                newData.ReactivePower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current * reactiveFactor, 1); // kVAr
                newData.ApparentPower = Math.Round(sqrt3 * phaseVoltageKv * newData.Current, 1); // kVA

                // DİĞER PARAMETRELER
                newData.Frequency = GenerateFrequency(stationId);
                newData.Temperature = GenerateTemperature(stationId);
                newData.THDVoltage = GenerateTHD(stationId);
                newData.THDCurrent = GenerateTHD(stationId);

                // EN 50122 dokunma gerilimi hesabı
                newData.TouchVoltage = CalculateTouchVoltage(newData.GroundCurrent);

                // Hata süresini hesapla
                double faultDuration = 0.0;
                if (newData.GroundCurrent >= _leakThresholdA)
                {
                    if (!_leakStartTimes.ContainsKey(stationId))
                        _leakStartTimes[stationId] = DateTime.Now;

                    faultDuration = (DateTime.Now - _leakStartTimes[stationId].Value).TotalSeconds;
                }
                else
                {
                    _leakStartTimes.Remove(stationId);
                }

                // EN 50122-1'e göre limit kontrolü
                string touchVoltageStatus = EN50122Analyzer.GetTouchVoltageCategory(
                    newData.TouchVoltage,
                    faultDuration);

                // Limit aşımı kontrolü
                if (touchVoltageStatus == "KIRMIZI")
                {
                    newData.ActiveAlarms.Add($"[EN50122-KRİTİK] {newData.StationName} - " +
                                           $"Tehlikeli dokunma gerilimi: {newData.TouchVoltage}V, " +
                                           $"Süre: {faultDuration:F1}s");
                }
                else if (touchVoltageStatus == "SARI")
                {
                    newData.ActiveAlarms.Add($"[EN50122-UYARI] {newData.StationName} - " +
                                           $"Yüksek dokunma gerilimi: {newData.TouchVoltage}V, " +
                                           $"Süre: {faultDuration:F1}s");
                }

                // DC SİSTEM DEĞERLERİ - V cinsinden (DEĞİŞTİRİLDİ)
                newData.DcVoltage = GenerateDcVoltage(stationId); // V cinsinden
                newData.DcCurrent = GenerateDcCurrent(stationId); // A cinsinden
                newData.AuxDcVoltage = GenerateAuxDcVoltage(stationId); // V cinsinden
                newData.AuxAcVoltage = GenerateAuxAcVoltage(stationId); // V cinsinden

                // EN50122 / KAÇAK HESAPLAMASI
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

                newData.TouchVoltage = CalculateTouchVoltage(newData.GroundCurrent);
                double uteLimit = GetEN50122TouchVoltageLimit(leakDuration);

                newData.ActiveAlarms = CheckForAlarms(newData);

                if (newData.TouchVoltage > uteLimit)
                {
                    newData.ActiveAlarms.Add($"[{newData.StationName}] TEHLIKELI_DOKUNMA_GERILIMI Ute={newData.TouchVoltage}V Limit={uteLimit}V (t={leakDuration:F2}s)");
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

        private double GetEN50122TouchVoltageLimit(double durationSeconds)
        {
            return EN50122Analyzer.GetAllowedDurationForTouchVoltage(durationSeconds);
        }

        #region DC GENERATOR FUNCTIONS - V cinsinden (DEĞİŞTİRİLDİ)

        private double GenerateDcVoltage(int stationId)
        {
            // DC gerilim: 1500V nominal ±20V
            double baseV = _nominalDcVoltage; // 1500 V
            double stationVariation = Math.Sin(stationId * 0.7) * 10; // ±10 V
            double fluctuation = (_random.NextDouble() - 0.5) * 40; // ±20 V
            double voltage = baseV + stationVariation + fluctuation;

            // %2 ihtimalle DC gerilim anormalliği
            if (_random.NextDouble() < 0.02)
            {
                if (_random.NextDouble() < 0.5)
                    voltage = baseV * (0.85 + _random.NextDouble() * 0.05); // düşük (1275-1425V)
                else
                    voltage = baseV * (1.1 + _random.NextDouble() * 0.05); // yüksek (1650-1725V)
            }

            return Math.Round(voltage, 1); // V cinsinden
        }

        private double GenerateDcCurrent(int stationId)
        {
            double loadFactor = _stationLoadProfiles[stationId];
            double baseCurrent = 800.0 * loadFactor; // 400-900 A arası
            double fluctuation = (_random.NextDouble() - 0.5) * baseCurrent * 0.15;
            double current = baseCurrent + fluctuation;

            // %3 ihtimalle DC akım anomalisi
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

            return Math.Round(Math.Max(10.0, current), 1); // Minimum 10A
        }

        private double GenerateAuxDcVoltage(int stationId)
        {
            // Aux DC: 110V nominal ±3V
            double baseV = 110.0;
            double fluctuation = (_random.NextDouble() - 0.5) * 6;
            double voltage = baseV + fluctuation;

            if (_random.NextDouble() < 0.01)
                voltage = baseV + (_random.NextDouble() - 0.5) * 20; // ±10V anomali

            return Math.Round(voltage, 1); // V cinsinden
        }

        private double GenerateAuxAcVoltage(int stationId)
        {
            // Aux AC: 400V nominal ±5V
            double baseV = 400.0;
            double fluctuation = (_random.NextDouble() - 0.5) * 10;
            double voltage = baseV + fluctuation;

            if (_random.NextDouble() < 0.01)
                voltage = baseV + (_random.NextDouble() - 0.5) * 40; // ±20V anomali

            return Math.Round(voltage, 1); // V cinsinden
        }

        #endregion

        #region AC GENERATOR FUNCTIONS - kV cinsinden (Aynı kaldı)

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
            double current = _random.NextDouble() * 2.0;

            if (_random.NextDouble() < 0.02)
                current = 3.0 + _random.NextDouble() * 5.0;

            if (_random.NextDouble() < 0.01)
                current = 10.0 + _random.NextDouble() * 90.0;

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
            return Math.Round(groundCurrentA * _touchResistance, 1);
        }

        //private double GetEN50122TouchVoltageLimit(double tSeconds)
        //{
        //    if (tSeconds <= 0) return double.PositiveInfinity;

        //    if (tSeconds < 0.7)
        //    {
        //        if (tSeconds < 0.02) return 870;
        //        if (tSeconds < 0.05) return 735;
        //        if (tSeconds < 0.1) return 625;
        //        if (tSeconds < 0.2) return 520;
        //        if (tSeconds < 0.3) return 460;
        //        if (tSeconds < 0.4) return 420;
        //        if (tSeconds < 0.5) return 385;
        //        if (tSeconds < 0.6) return 360;
        //        return 350;
        //    }

        //    if (tSeconds >= 300) return 120;
        //    if (tSeconds >= 300) return 150;
        //    if (tSeconds >= 1.0) return 160;
        //    if (tSeconds >= 0.9) return 165;
        //    if (tSeconds >= 0.8) return 170;
        //    if (tSeconds >= 0.7) return 175;

        //    return 175;
        //}

        private List<string> CheckForAlarms(VldData data)
        {
            var alarms = new List<string>();

            // AC ALARMLARI
            if (data.VoltageOut < _systemLineVoltage * 0.9)
                alarms.Add($"[{data.StationName}] DUSUK_AC_GERILIM ({data.VoltageOut} kV)");
            else if (data.VoltageOut > _systemLineVoltage * 1.1)
                alarms.Add($"[{data.StationName}] YUKSEK_AC_GERILIM ({data.VoltageOut} kV)");

            if (data.Current > _nominalCurrent * 1.1)
                alarms.Add($"[{data.StationName}] ASIRI_AC_AKIM ({data.Current} A)");

            // DC ALARMLARI (V cinsinden)
            if (data.DcVoltage < 1400)
                alarms.Add($"[{data.StationName}] DUSUK_DC_GERILIM ({data.DcVoltage} V)");
            else if (data.DcVoltage > 1600)
                alarms.Add($"[{data.StationName}] YUKSEK_DC_GERILIM ({data.DcVoltage} V)");

            if (data.DcCurrent > 1000)
                alarms.Add($"[{data.StationName}] ASIRI_DC_AKIM ({data.DcCurrent} A)");

            // TOPRAK ARİZASI
            if (data.GroundCurrent > 10.0)
                alarms.Add($"[{data.StationName}] TOPRAK_ARIZASI ({data.GroundCurrent} A)");

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