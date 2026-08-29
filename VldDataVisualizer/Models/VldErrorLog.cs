using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace VldDataVisualizer.Models
{
    /*
     * EN 50122-1: Railway applications – Fixed installations
     * Electrical safety, earthing and the return circuit
     *
     * Bu sınıf, EN 50122-1 standardına göre tespit edilen
     * anomalilerin, ihlallerin ve ilgili tren/cihaz bilgilerinin
     * loglanması ve görselleştirilmesi amacıyla kullanılır.
     */
    public class VldErrorLog
    {
        public DateTime Timestamp { get; set; }

        // ⚠️ ESKİ kategori (geri uyumluluk için tutulur)
        public string Category { get; set; } = string.Empty; // YEŞİL / SARI / KIRMIZI

        public string AnomalyType { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool HasAnomaly { get; set; }
        public int RepeatCount { get; set; }

        // Kaçak tespiti: aynı konumda ardışık tekrarlar sonucu bulunan kaçak
        public bool LeakDetected { get; set; }
        public double? LeakPosition { get; set; }
        public int? LeakTrainId { get; set; }
        public List<LeakLocationInfo> LeakLocations { get; set; } = new();
        public List<EN50122AnomalyDevice> AffectedDevices { get; set; } = new();
        public List<TrainInfoLog> AllTrains { get; set; } = new();

        /*
         * EN 50122-1 Madde 3, 4, 9
         * Standart bazlı analiz bilgileri
         */
        public string Standard { get; set; } = "EN 50122-1";
        public List<EN50122Violation> EN50122Violations { get; set; } = new();

        // EN 50122 genel sonuç
        public string OverallEN50122Category { get; set; } = string.Empty; // NORMAL / SARI / KIRMIZI

        /*
         * EN 50122-1 Çizelge 6 ve Madde 4.2
         * Maksimum ölçülen değerler (olay süresince)
         */
        public double MaxTouchVoltage { get; set; }
        public double MaxTouchVoltageDuration { get; set; }
        public double MaxDcVoltageDeviation { get; set; }
        public double MaxGroundCurrent { get; set; }

        public int CriticalViolationCount { get; set; }
        public int WarningViolationCount { get; set; }

        // =======================
        // Görselleştirme Amaçlı
        // =======================

        public string DevicesDisplay =>
            string.Join(", ", AffectedDevices.Select(d => $"{d.DeviceId}({d.OverallCategory ?? d.Category})"));

        // Toast bildirimi için kısa özet alanları
        public string StationNames =>
            string.Join(", ", AffectedDevices.Select(d => d.StationName).Distinct());

        public int CriticalDeviceCount => AffectedDevices.Count;

        public string OverallCategory => OverallEN50122Category ?? Category;

        public string EffectiveCategory =>
            string.IsNullOrWhiteSpace(OverallEN50122Category)
                ? (string.IsNullOrWhiteSpace(Category) ? "NORMAL" : Category)
                : OverallEN50122Category;

        public Brush CategoryBrush =>
            EffectiveCategory switch
            {
                "KIRMIZI" => Brushes.Red,
                "SARI" => Brushes.Orange,
                "YEŞİL" => Brushes.Green,
                "NORMAL" => Brushes.Green,
                _ => Brushes.Black
            };

        public string TrainPositionsDisplay =>
            string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Position:N0} m"));

        public string LeakTrackDisplay =>
            LeakLocations.Any()
                ? string.Join(" | ", LeakLocations.Select(l => $"{l.TrackDisplay} {l.Position:N0} m"))
                : (LeakPosition.HasValue ? $"{GetLeakTrackForPosition(LeakPosition.Value)}" : "Bilinmiyor");

        private string GetLeakTrackForPosition(double positionMeters)
        {
            var trainMatch = AllTrains
                .OrderBy(t => Math.Abs(t.Position - positionMeters))
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(trainMatch?.TrackType)
                ? "Ray bilinmiyor"
                : trainMatch.TrackType;
        }

        private string GetFaultMetricSummary()
        {
            if (AffectedDevices.Count == 0)
                return string.Empty;

            var metricLines = AffectedDevices
                .Where(d => d.TouchVoltage > 0 || d.GroundCurrent > 0)
                .Select(d => $"Ute {d.TouchVoltage:N0} V/{d.Duration:F1}s | IG {d.GroundCurrent:N1} A")
                .Distinct()
                .Take(2)
                .ToList();

            return metricLines.Count > 0
                ? string.Join(" | ", metricLines)
                : string.Empty;
        }

        private IEnumerable<EN50122AnomalyDevice> GetDevicesForPosition(double positionMeters)
        {
            var candidates = AffectedDevices
                .Where(d => d.TouchVoltage > 0 || d.GroundCurrent > 0)
                .ToList();

            if (candidates.Count == 0)
                return Enumerable.Empty<EN50122AnomalyDevice>();

            var exactMatch = candidates
                .Where(d =>
                    (d.StartPosition <= positionMeters && positionMeters <= d.EndPosition) ||
                    (d.StartPosition == 0 && d.EndPosition == 0 && Math.Abs((d.Kilometer * 1000.0) - positionMeters) <= 25) ||
                    Math.Abs(d.StartPosition - positionMeters) <= 5 ||
                    Math.Abs(d.EndPosition - positionMeters) <= 5)
                .OrderByDescending(d => d.TouchVoltage)
                .ThenByDescending(d => d.GroundCurrent)
                .ToList();

            if (exactMatch.Count > 0)
                return exactMatch;

            return candidates
                .OrderBy(d => Math.Abs(d.StartPosition - positionMeters))
                .ThenBy(d => Math.Abs(d.EndPosition - positionMeters))
                .ThenByDescending(d => d.TouchVoltage)
                .ThenByDescending(d => d.GroundCurrent)
                .Take(2);
        }

        private string GetFaultMetricSummaryForPosition(double positionMeters)
        {
            var metrics = GetDevicesForPosition(positionMeters)
                .Select(d => $"Ute {d.TouchVoltage:N0} V/{d.Duration:F1}s | IG {d.GroundCurrent:N1} A")
                .Distinct()
                .ToList();

            return string.Join(" | ", metrics);
        }

        public string LeakDisplay =>
            LeakDetected
                ? (LeakLocations.Any()
                    ? string.Join(Environment.NewLine, LeakLocations.Select(l =>
                        {
                            var trackText = string.IsNullOrWhiteSpace(l.TrackType) ? "Ray bilinmiyor" : l.TrackType;
                            var locationText = $"{trackText} | {l.Position:N0} m{(l.RepeatCount > 1 ? $" ↺{l.RepeatCount}" : string.Empty)} [{l.NearestDcStation} {l.NearestDcStationDistanceMeters:N0} m]";
                            var metrics = GetFaultMetricSummaryForPosition(l.Position);
                            return string.IsNullOrWhiteSpace(metrics) ? locationText : $"{locationText} {metrics}";
                        }))
                    : $"{(LeakPosition.HasValue ? (GetLeakTrackForPosition(LeakPosition.Value) + " | ") : string.Empty)}{LeakPosition?.ToString("N0")} m [{GetNearestDcStationName(LeakPosition ?? 0)} {GetNearestDcStationDistance(LeakPosition ?? 0):N0} m]{(string.IsNullOrWhiteSpace(GetFaultMetricSummaryForPosition(LeakPosition ?? 0)) ? string.Empty : $" {GetFaultMetricSummaryForPosition(LeakPosition ?? 0)}")}" )
                : string.Empty;

        public Brush LeakDisplayBrush =>
            LeakLocations.Any(l => l.IsStoppedOrWaiting)
                ? Brushes.Orange
                : CategoryBrush;

        public static string GetNearestDcStationName(double position)
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

            return dcStations
                .Select(s => new { Station = s.Key, Distance = Math.Abs(position - s.Value) })
                .OrderBy(s => s.Distance)
                .First()
                .Station;
        }

        public static double GetNearestDcStationDistance(double position)
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

            return dcStations
                .Select(s => new { Station = s.Key, Distance = Math.Abs(position - s.Value) })
                .OrderBy(s => s.Distance)
                .First()
                .Distance;
        }

        public string TrainSpeedsDisplay =>
            string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Speed:N0} km/h"));

        /*
         * EN 50122-1 Madde 9
         * Öncelik her zaman EN 50122 değerlendirmesindedir
         */
        public string CategoryColor
        {
            get
            {
                var category = EffectiveCategory;

                return category switch
                {
                    "KIRMIZI" => "#F44336",
                    "SARI" => "#FFC107",
                    "YEŞİL" => "#4CAF50",
                    "NORMAL" => "#4CAF50",
                    _ => "#9E9E9E"
                };
            }
        }

        /*
         * EN 50122-1 Madde 9
         * Standart bazlı özet ihlal bilgisi
         */
        public string EN50122Summary
        {
            get
            {
                if (!EN50122Violations.Any())
                    return "EN 50122: Normal";

                int critical = EN50122Violations.Count(v => v.Category == "KIRMIZI");
                int warnings = EN50122Violations.Count(v => v.Category == "SARI");

                if (critical > 0)
                    return $"EN 50122: {critical} KRİTİK, {warnings} UYARI";

                if (warnings > 0)
                    return $"EN 50122: {warnings} UYARI";

                return "EN 50122: Normal";
            }
        }

        /*
         * EN 50122-1 Madde 4.2
         * DC cer hattı gerilim ihlalleri
         */
        public string DcVoltageViolationsDisplay =>
            string.Join(", ",
                EN50122Violations
                    .Where(v => v.Type == "DC_VOLTAGE" && v.Category != "NORMAL")
                    .Select(v => $"{v.DeviceId}:{v.Value:N0} V"));

        /*
         * EN 50122-1 Çizelge 6
         * Dokunma gerilimi ihlalleri
         */
        public string TouchVoltageViolationsDisplay =>
            string.Join(", ",
                EN50122Violations
                    .Where(v => v.Type == "TOUCH_VOLTAGE" && v.Category != "NORMAL")
                    .Select(v => $"{v.DeviceId}:{v.Value:N0} V/{v.Duration:F1}s"));

        /*
         * EN 50122-1 Madde 6.3
         * Toprak dönüş akımı ihlalleri
         */
        public string GroundCurrentViolationsDisplay =>
            string.Join(", ",
                EN50122Violations
                    .Where(v => v.Type == "GROUND_CURRENT" && v.Category != "NORMAL")
                    .Select(v => $"{v.DeviceId}:{v.Value:N1} A"));

        public string MaxValuesDisplay =>
            $"Max Ute: {MaxTouchVoltage:N0} V/{MaxTouchVoltageDuration:F1}s | " +
            $"Max IG: {MaxGroundCurrent:N1} A";
    }

    /*
     * EN 50122-1 Madde 3 ve 9
     * Anomaliye sebep olan sabit tesis cihazı
     */
    public class EN50122AnomalyDevice
    {
        public string DeviceId { get; set; } = string.Empty;

        public double Voltage { get; set; }
        public double Kilometer { get; set; }
        public double StartPosition { get; set; }
        public double EndPosition { get; set; }

        // EN 50122 ölçümleri
        public double DcVoltage { get; set; }
        public double DcCurrent { get; set; }
        public double GroundCurrent { get; set; }
        public double TouchVoltage { get; set; }
        public double Duration { get; set; }
        public double VoltageOut { get; set; }
        public double Current { get; set; }

        // Kategoriler
        public string Category { get; set; } = string.Empty; // eski
        public string DcVoltageCategory { get; set; } = string.Empty;
        public string TouchVoltageCategory { get; set; } = string.Empty;
        public string GroundCurrentCategory { get; set; } = string.Empty;
        public string OverallEN50122Category { get; set; } = string.Empty;

        // ⚠️ DÜZELTME: tek bir genel kategoriye bağlandı
        public string OverallCategory { get; set; } = string.Empty;

        public string StationName { get; set; } = string.Empty;
        public int StationId { get; set; }

        public double DcPower { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } = string.Empty;

        /*
         * EN 50122-1 Çizelge 6 + Madde 4.2 + 6.3
         */
        public string EN50122Details =>
            $"Ute: {TouchVoltage:N0} V/{Duration:F1}s ({TouchVoltageCategory}) | " +
            $"IG: {GroundCurrent:N1} A ({GroundCurrentCategory})";

        public string GetDetails() => EN50122Details;
    }

    /*
     * Tren konumu ve işletme durumu
     * (EN 50122 Madde 9 – işletme senaryosu bağlamı)
     */
    public class TrainInfoLog
    {
        public int TrainId { get; set; }
        public string TrainName { get; set; } = string.Empty;
        public double Position { get; set; }
        public double Speed { get; set; }
        public string TrackType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SectionDeviceId { get; set; } = string.Empty;

        public double Kilometer => Position / 1000.0;

        // EN 50122 bağlamsal analiz ve Tekrar Sayısı
        public int RepeatCount { get; set; } = 1;
        public bool IsRepeated => RepeatCount > 1;
        public string RepeatBadge => RepeatCount > 1 ? $"↺ {RepeatCount}x Tekrar" : string.Empty;
        public bool HasRepeatBadge => RepeatCount > 1;
        public string BadgeVisibility => HasRepeatBadge ? "Visible" : "Collapsed";

        // Belirgin çerçeve ve arka plan renkleri (2 tekrar: Sarı/Turuncu, 3+ tekrar: Kırmızı)
        public string CardBorderBrush => RepeatCount >= 3 ? "#F44336" : (RepeatCount == 2 ? "#FF9800" : "#D5DDE2");
        public string CardBorderThickness => RepeatCount > 1 ? "2.5" : "1";
        public string CardBackground => RepeatCount >= 3 ? "#FFF0F0" : (RepeatCount == 2 ? "#FFFDF0" : "White");
        public string BadgeBackground => RepeatCount >= 3 ? "#F44336" : "#FF9800";

        public bool IsInCriticalZone { get; set; }
        public double DistanceToNearestFault { get; set; }
        public string NearestFaultDeviceId { get; set; } = string.Empty;
    }

    public class LeakLocationInfo
    {
        public double Position { get; set; }
        public int RepeatCount { get; set; }
        public int TrainId { get; set; }
        public string TrainStatus { get; set; } = string.Empty;
        public string TrackType { get; set; } = string.Empty;
        public string TrackDisplay => string.IsNullOrWhiteSpace(TrackType) ? "Ray bilinmiyor" : TrackType;
        public bool IsStoppedOrWaiting => TrainStatus == "STOPPED" || TrainStatus == "WAITING";
        public string StationName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string NearestDcStation { get; set; } = string.Empty;
        public double NearestDcStationDistanceMeters { get; set; }

        public string Marker => RepeatCount > 1 ? $"↺{RepeatCount}" : "•";
        public string Display => $"{TrackDisplay} | {Position:N0} m {Marker} [{NearestDcStation} {NearestDcStationDistanceMeters:N0} m]";
    }

    /*
     * EN 50122-1 Madde 3, 4, 6
     * Tekil standart ihlali kaydı
     */
    public class EN50122Violation
    {
        public string Type { get; set; } = string.Empty; // DC_VOLTAGE, TOUCH_VOLTAGE, GROUND_CURRENT
        public string Category { get; set; } = string.Empty; // NORMAL / SARI / KIRMIZI

        public string DeviceId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;

        public double Value { get; set; }
        public double Duration { get; set; }
        public double Limit { get; set; }
        public double DeviationPercent { get; set; }
        public DateTime Timestamp { get; set; }

        public string Description { get; set; } = string.Empty;

        public string DisplayValue =>
            Type switch
            {
                "DC_VOLTAGE" => $"{Value:N0} V",
                "TOUCH_VOLTAGE" => $"{Value:N0} V ({Duration:F1}s)",
                "GROUND_CURRENT" => $"{Value:N1} A",
                _ => $"{Value:N1}"
            };

        public string ColorCode =>
            Category switch
            {
                "KIRMIZI" => "#F44336",
                "SARI" => "#FFC107",
                "NORMAL" => "#4CAF50",
                _ => "#9E9E9E"
            };
    }
}