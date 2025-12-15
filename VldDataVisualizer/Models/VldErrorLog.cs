using System;
using System.Collections.Generic;
using System.Linq;

namespace VldDataVisualizer.Models
{
    public class VldErrorLog
    {
        public DateTime Timestamp { get; set; }
        public string Category { get; set; } // YEŞİL / SARI / KIRMIZI (ESKİ)
        public string AnomalyType { get; set; }
        public string Summary { get; set; }
        public bool HasAnomaly { get; set; }
        public int RepeatCount { get; set; }
        public List<AnomalyDevice> AffectedDevices { get; set; } = new List<AnomalyDevice>();
        public List<TrainInfoLog> AllTrains { get; set; } = new List<TrainInfoLog>();

        // YENİ: EN 50122 Özel Özellikler
        public string Standard { get; set; } = "EN 50122-1";
        public List<EN50122Violation> EN50122Violations { get; set; } = new List<EN50122Violation>();
        public string OverallEN50122Category { get; set; } // NORMAL / SARI / KIRMIZI (EN 50122)
        public double MaxTouchVoltage { get; set; }
        public double MaxTouchVoltageDuration { get; set; }
        public double MaxDcVoltageDeviation { get; set; }
        public double MaxGroundCurrent { get; set; }
        public int CriticalViolationCount { get; set; }
        public int WarningViolationCount { get; set; }

        // Görüntüleme için formatlanmış özellikler (ESKİ)
        public string DevicesDisplay => string.Join(", ",
            AffectedDevices.Select(d => $"{d.DeviceId}({d.Category})"));
        public string TrainPositionsDisplay => string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Position:N0}m"));
        public string TrainSpeedsDisplay => string.Join("\n", AllTrains.Select(t => $"{t.TrainId}: {t.Speed:N0}km/h"));
        public string CategoryColor
        {
            get
            {
                // Öncelik EN 50122 kategorisi
                if (!string.IsNullOrEmpty(OverallEN50122Category))
                {
                    return OverallEN50122Category switch
                    {
                        "KIRMIZI" => "#F44336",
                        "SARI" => "#FFC107",
                        "NORMAL" => "#4CAF50",
                        _ => "#9E9E9E"
                    };
                }

                // Eski kategori sistemi
                return Category switch
                {
                    "YEŞİL" => "#4CAF50",
                    "SARI" => "#FFC107",
                    "KIRMIZI" => "#F44336",
                    _ => "#9E9E9E"
                };
            }
        }

        // YENİ: EN 50122 Görüntüleme Özellikleri
        public string EN50122Summary
        {
            get
            {
                if (!EN50122Violations.Any())
                    return "EN 50122: Normal";

                var critical = EN50122Violations.Count(v => v.Category == "KIRMIZI");
                var warnings = EN50122Violations.Count(v => v.Category == "SARI");

                if (critical > 0)
                    return $"EN 50122: {critical} KRİTİK, {warnings} UYARI";

                if (warnings > 0)
                    return $"EN 50122: {warnings} UYARI";

                return "EN 50122: Normal";
            }
        }

        public string DcVoltageViolationsDisplay => string.Join(", ",
            EN50122Violations.Where(v => v.Type == "DC_VOLTAGE" && v.Category != "NORMAL")
                           .Select(v => $"{v.DeviceId}:{v.Value:N0}V"));

        public string TouchVoltageViolationsDisplay => string.Join(", ",
            EN50122Violations.Where(v => v.Type == "TOUCH_VOLTAGE" && v.Category != "NORMAL")
                           .Select(v => $"{v.DeviceId}:{v.Value:N0}V/{v.Duration:F1}s"));

        public string GroundCurrentViolationsDisplay => string.Join(", ",
            EN50122Violations.Where(v => v.Type == "GROUND_CURRENT" && v.Category != "NORMAL")
                           .Select(v => $"{v.DeviceId}:{v.Value:N1}A"));

        public string MaxValuesDisplay =>
            $"Max Ute: {MaxTouchVoltage:N0}V/{MaxTouchVoltageDuration:F1}s | " +
            $"Max UDC Sapma: {MaxDcVoltageDeviation:N0}V | " +
            $"Max IG: {MaxGroundCurrent:N1}A";
    }

    public class AnomalyDevice
    {
        public string DeviceId { get; set; }
        public double Voltage { get; set; }
        public double Kilometer { get; set; }
        public double StartPosition { get; set; }
        public double EndPosition { get; set; }
        public double DcVoltage { get; set; }
        public double DcCurrent { get; set; }
        public double GroundCurrent { get; set; }
        public double TouchVoltage { get; set; }
        public string Category { get; set; }
        public double Duration { get; set; } // Hata süresi (saniye)

        // YENİ: EN 50122 Özellikleri
        public string StationName { get; set; }
        public int StationId { get; set; }
        public string DcVoltageCategory { get; set; } // NORMAL / SARI / KIRMIZI
        public string TouchVoltageCategory { get; set; } // NORMAL / SARI / KIRMIZI
        public string GroundCurrentCategory { get; set; } // NORMAL / SARI / KIRMIZI
        public string OverallEN50122Category { get; set; } // NORMAL / SARI / KIRMIZI
        public string OverallCategory { get; set; } // NORMAL / SARI / KIRMIZI
        public double DcPower { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; }

        public string EN50122Details =>
            $"UDC: {DcVoltage:N0}V ({DcVoltageCategory}) | " +
            $"Ute: {TouchVoltage:N0}V/{Duration:F1}s ({TouchVoltageCategory}) | " +
            $"IG: {GroundCurrent:N1}A ({GroundCurrentCategory})";

        public string GetDetails()
        {
            return $"UDC: {DcVoltage:N0}V ({DcVoltageCategory}) | " +
                   $"Ute: {TouchVoltage:N0}V/{Duration:F1}s ({TouchVoltageCategory}) | " +
                   $"IG: {GroundCurrent:N1}A ({GroundCurrentCategory})";
        }
    }

    public class TrainInfoLog
    {
        public int TrainId { get; set; }
        public string TrainName { get; set; }
        public double Position { get; set; } // metre
        public double Speed { get; set; } // km/h
        public string TrackType { get; set; }
        public string Status { get; set; }
        public string SectionDeviceId { get; set; }

        // YENİ: EN 50122 için ek özellikler
        public double Kilometer => Position / 1000.0;
        public bool IsInCriticalZone { get; set; }
        public double DistanceToNearestFault { get; set; }
        public string NearestFaultDeviceId { get; set; }
    }

    // YENİ: EN 50122 İhlal Sınıfı
    public class EN50122Violation
    {
        public string Type { get; set; } // DC_VOLTAGE, TOUCH_VOLTAGE, GROUND_CURRENT
        public string Category { get; set; } // NORMAL / SARI / KIRMIZI
        public string DeviceId { get; set; }
        public string StationName { get; set; }
        public double Value { get; set; } // Gerilim (V) veya Akım (A)
        public double Duration { get; set; } // Süre (saniye)
        public double Limit { get; set; } // Limit değeri
        public double DeviationPercent { get; set; } // Sapma yüzdesi
        public DateTime Timestamp { get; set; }
        public string Description { get; set; }

        public string DisplayValue
        {
            get
            {
                return Type switch
                {
                    "DC_VOLTAGE" => $"{Value:N0} V",
                    "TOUCH_VOLTAGE" => $"{Value:N0} V ({Duration:F1}s)",
                    "GROUND_CURRENT" => $"{Value:N1} A",
                    _ => $"{Value:N1}"
                };
            }
        }

        public string ColorCode
        {
            get
            {
                return Category switch
                {
                    "KIRMIZI" => "#F44336",
                    "SARI" => "#FFC107",
                    "NORMAL" => "#4CAF50",
                    _ => "#9E9E9E"
                };
            }
        }
    }

    // YENİ: EN 50122 Limit Sınıfı (Opsiyonel)
    public class EN50122Limit
    {
        public string Parameter { get; set; }
        public double NominalValue { get; set; }
        public double NormalMin { get; set; }
        public double NormalMax { get; set; }
        public double WarningMin { get; set; }
        public double WarningMax { get; set; }
        public double CriticalMin { get; set; }
        public double CriticalMax { get; set; }
        public string Unit { get; set; }

        public static EN50122Limit DcVoltageLimit()
        {
            return new EN50122Limit
            {
                Parameter = "DC Voltage",
                NominalValue = 1500,
                NormalMin = 1350,   // -10%
                NormalMax = 1650,   // +10%
                WarningMin = 1200,  // -20%
                WarningMax = 1800,  // +20%
                CriticalMin = 1050, // -30%
                CriticalMax = 1950, // +30%
                Unit = "V"
            };
        }

        public static EN50122Limit GroundCurrentLimit()
        {
            return new EN50122Limit
            {
                Parameter = "Ground Current",
                NominalValue = 0,
                NormalMin = 0,
                NormalMax = 3,
                WarningMin = 3,
                WarningMax = 6,
                CriticalMin = 6,
                CriticalMax = double.MaxValue,
                Unit = "A"
            };
        }

        public static EN50122Limit TouchVoltageLimit(double duration)
        {
            // EN 50122 Tablo 6'ya göre
            double longTermLimit = duration >= 0.7 ? 175 : 120;
            double shortTermLimit = duration < 0.7 ? GetShortTermLimit(duration) : 0;

            return new EN50122Limit
            {
                Parameter = "Touch Voltage",
                NominalValue = 0,
                NormalMin = 0,
                NormalMax = duration >= 0.7 ? longTermLimit : shortTermLimit,
                WarningMin = 0,
                WarningMax = 0,
                CriticalMin = 0,
                CriticalMax = 0,
                Unit = "V"
            };
        }

        private static double GetShortTermLimit(double duration)
        {
            return duration switch
            {
                <= 0.02 => 870,
                <= 0.05 => 735,
                <= 0.1 => 625,
                <= 0.2 => 520,
                <= 0.3 => 460,
                <= 0.4 => 420,
                <= 0.5 => 385,
                <= 0.6 => 360,
                _ => 350
            };
        }
    }

    public class EN50122AnomalyDevice
    {
        public string DeviceId { get; set; }
        public string StationName { get; set; }
        public int StationId { get; set; }

        // DC Sistemi
        public double DcVoltage { get; set; }
        public string DcVoltageCategory { get; set; }
        public double DcPower { get; set; }

        // Dokunma Gerilimi
        public double TouchVoltage { get; set; }
        public double Duration { get; set; }
        public string TouchVoltageCategory { get; set; }

        // Toprak Akımı
        public double GroundCurrent { get; set; }
        public string GroundCurrentCategory { get; set; }

        // AC Sistemi (opsiyonel)
        public double VoltageOut { get; set; }
        public double Current { get; set; }

        // Konum
        public double Kilometer { get; set; }
        public double StartPosition { get; set; }
        public double EndPosition { get; set; }

        // Genel
        public string OverallCategory { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }

        public string GetDetails()
        {
            return $"UDC: {DcVoltage:N0}V ({DcVoltageCategory}) | " +
                   $"Ute: {TouchVoltage:N0}V/{Duration:F1}s ({TouchVoltageCategory}) | " +
                   $"IG: {GroundCurrent:N1}A ({GroundCurrentCategory})";
        }
    }
}