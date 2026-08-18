using System;
using System.Collections.Generic;
using System.Linq;

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
        public string Category { get; set; } // YEŞİL / SARI / KIRMIZI

        public string AnomalyType { get; set; }
        public string Summary { get; set; }
        public bool HasAnomaly { get; set; }
        public int RepeatCount { get; set; }

        public List<EN50122AnomalyDevice> AffectedDevices { get; set; } = new();
        public List<TrainInfoLog> AllTrains { get; set; } = new();

        /*
         * EN 50122-1 Madde 3, 4, 9
         * Standart bazlı analiz bilgileri
         */
        public string Standard { get; set; } = "EN 50122-1";
        public List<EN50122Violation> EN50122Violations { get; set; } = new();

        // EN 50122 genel sonuç
        public string OverallEN50122Category { get; set; } // NORMAL / SARI / KIRMIZI

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

        public string TrainPositionsDisplay =>
            string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Position:N0} m"));

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
                if (!string.IsNullOrWhiteSpace(OverallEN50122Category))
                {
                    return OverallEN50122Category switch
                    {
                        "KIRMIZI" => "#F44336",
                        "SARI" => "#FFC107",
                        "NORMAL" => "#4CAF50",
                        _ => "#9E9E9E"
                    };
                }

                return Category switch
                {
                    "YEŞİL" => "#4CAF50",
                    "SARI" => "#FFC107",
                    "KIRMIZI" => "#F44336",
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
            $"Max UDC Sapma: {MaxDcVoltageDeviation:N0} V | " +
            $"Max IG: {MaxGroundCurrent:N1} A";
    }

    /*
     * EN 50122-1 Madde 3 ve 9
     * Anomaliye sebep olan sabit tesis cihazı
     */
    public class EN50122AnomalyDevice
    {
        public string DeviceId { get; set; }

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
        public string Category { get; set; } // eski
        public string DcVoltageCategory { get; set; }
        public string TouchVoltageCategory { get; set; }
        public string GroundCurrentCategory { get; set; }
        public string OverallEN50122Category { get; set; }

        // ⚠️ DÜZELTME: tek bir genel kategoriye bağlandı
        public string OverallCategory { get; set; }

        public string StationName { get; set; }
        public int StationId { get; set; }

        public double DcPower { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; }

        /*
         * EN 50122-1 Çizelge 6 + Madde 4.2 + 6.3
         */
        public string EN50122Details =>
            $"UDC: {DcVoltage:N0} V ({DcVoltageCategory}) | " +
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
        public string TrainName { get; set; }
        public double Position { get; set; }
        public double Speed { get; set; }
        public string TrackType { get; set; }
        public string Status { get; set; }
        public string SectionDeviceId { get; set; }

        public double Kilometer => Position / 1000.0;

        // EN 50122 bağlamsal analiz
        public bool IsInCriticalZone { get; set; }
        public double DistanceToNearestFault { get; set; }
        public string NearestFaultDeviceId { get; set; }
    }

    /*
     * EN 50122-1 Madde 3, 4, 6
     * Tekil standart ihlali kaydı
     */
    public class EN50122Violation
    {
        public string Type { get; set; } // DC_VOLTAGE, TOUCH_VOLTAGE, GROUND_CURRENT
        public string Category { get; set; } // NORMAL / SARI / KIRMIZI

        public string DeviceId { get; set; }
        public string StationName { get; set; }

        public double Value { get; set; }
        public double Duration { get; set; }
        public double Limit { get; set; }
        public double DeviationPercent { get; set; }
        public DateTime Timestamp { get; set; }

        public string Description { get; set; }

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