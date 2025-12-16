using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace VldDataVisualizer.Models
{
    /// <summary>
    /// EN 50122-1 Standardı: Raylı sistemlerde elektriksel güvenlik
    /// Çizelge 6: Zaman sürecinin bir fonksiyonu olarak d.a. cer sistemlerinde 
    /// azami müsaade edilebilir etkin dokunma gerilimleri Ute
    /// </summary>
    public class EN50122Analyzer
    {
        // EN 50122-1 Çizelge 6: Müsaade edilebilir dokunma gerilimleri
        private static readonly List<(double timeS, double longTermV, double shortTermV)> _touchVoltageLimits = new()
        {
            // Zaman (s) | Uzun Süreli (V) | Kısa Süreli (V)
            (double.MaxValue, 120, 0),      // > 300 s
            (300, 150, 0),                  // 300 s
            (1, 160, 0),                    // 1 s
            (0.9, 165, 0),                  // 0.9 s
            (0.8, 170, 0),                  // 0.8 s
            (0.7, 175, 0),                  // 0.7 s
            (0.6, 0, 360),                  // 0.6 s (kısa süreli)
            (0.5, 0, 385),                  // 0.5 s
            (0.4, 0, 420),                  // 0.4 s
            (0.3, 0, 460),                  // 0.3 s
            (0.2, 0, 520),                  // 0.2 s
            (0.1, 0, 625),                  // 0.1 s
            (0.05, 0, 735),                 // 0.05 s
            (0.02, 0, 870),                 // 0.02 s
        };

        // DC SİSTEM İÇİN GERÇEKÇİ LİMİTLER (1500V DC sistem)
        private const double NOMINAL_DC_VOLTAGE = 1500.0; // V
        private const double NORMAL_TOLERANCE = 0.10;     // %10 normal çalışma (±150V)
        private const double WARNING_TOLERANCE = 0.20;    // %20 uyarı (±300V)
        private const double ALARM_TOLERANCE = 0.30;      // %30 alarm (±450V)

        // HESAPLANMIŞ LİMİTLER
        private static readonly double NORMAL_MIN = NOMINAL_DC_VOLTAGE * (1 - NORMAL_TOLERANCE);  // 1350V
        private static readonly double NORMAL_MAX = NOMINAL_DC_VOLTAGE * (1 + NORMAL_TOLERANCE);  // 1650V
        private static readonly double WARNING_MIN = NOMINAL_DC_VOLTAGE * (1 - WARNING_TOLERANCE); // 1200V
        private static readonly double WARNING_MAX = NOMINAL_DC_VOLTAGE * (1 + WARNING_TOLERANCE); // 1800V
        private static readonly double ALARM_MIN = NOMINAL_DC_VOLTAGE * (1 - ALARM_TOLERANCE);     // 1050V
        private static readonly double ALARM_MAX = NOMINAL_DC_VOLTAGE * (1 + ALARM_TOLERANCE);     // 1950V

        /// <summary>
        /// EN 50122-1'e göre dokunma gerilimi kategorisini belirler
        /// </summary>
        public static string GetTouchVoltageCategory(double touchVoltage, double duration)
        {
            if (duration <= 0) return "NORMAL"; // Süre yoksa normal

            var limits = GetTouchVoltageLimits(duration);

            // KIRMIZI (ALARM) - kısa süreli limit aşıldı
            if (touchVoltage > limits.shortTermV && limits.shortTermV > 0)
                return "KIRMIZI";

            // SARI (WARNING) - uzun süreli limit aşıldı
            if (touchVoltage > limits.longTermV && limits.longTermV > 0)
                return "SARI";

            return "NORMAL";
        }

        private static (double longTermV, double shortTermV) GetTouchVoltageLimits(double duration)
        {
            foreach (var limit in _touchVoltageLimits.OrderBy(l => l.timeS))
            {
                if (duration <= limit.timeS)
                    return (limit.longTermV, limit.shortTermV);
            }

            return (120, 0);
        }

        /// <summary>
        /// DC cer gerilimi için EN 50122-1 limit kontrolü - DÜZELTİLDİ
        /// </summary>
        public static string GetDcVoltageCategory(double dcVoltage)
        {
            // ALARM (KIRMIZI) - %30'dan fazla sapma
            if (dcVoltage < ALARM_MIN || dcVoltage > ALARM_MAX)
                return "KIRMIZI";

            // WARNING (SARI) - %20-%30 arası sapma
            if (dcVoltage < WARNING_MIN || dcVoltage > WARNING_MAX)
                return "SARI";

            // NORMAL - %10 içinde sapma (1350-1650V)
            if (dcVoltage >= NORMAL_MIN && dcVoltage <= NORMAL_MAX)
                return "NORMAL";

            // %10-%20 arası: Hala normal sayalım ama edge case
            return "NORMAL";
        }

        /// <summary>
        /// Toprak akımı için EN 50122-1 limit kontrolü - DÜZELTİLDİ
        /// </summary>
        public static string GetGroundCurrentCategory(double groundCurrent)
        {
            // Gerçekçi limitler
            const double NORMAL_LIMIT = 3.0;     // A (Normal: < 3A)
            const double WARNING_LIMIT = 6.0;    // A (Uyarı: 3-6A)
            const double ALARM_LIMIT = 10.0;     // A (Alarm: > 10A)

            if (groundCurrent >= ALARM_LIMIT)
                return "KIRMIZI";

            if (groundCurrent >= WARNING_LIMIT)
                return "SARI";

            if (groundCurrent > NORMAL_LIMIT)
                return "NORMAL"; // 3-6A arası hala normal

            return "NORMAL";
        }

        /// <summary>
        /// Tüm parametreleri değerlendirerek genel durumu belirler
        /// </summary>
        public static string GetOverallStatus(
            double touchVoltage,
            double touchVoltageDuration,
            double dcVoltage,
            double groundCurrent)
        {
            // Her bir kategoriyi al
            string touchCategory = GetTouchVoltageCategory(touchVoltage, touchVoltageDuration);
            string dcCategory = GetDcVoltageCategory(dcVoltage);
            string groundCategory = GetGroundCurrentCategory(groundCurrent);

            // DEBUG için konsola yaz
            Console.WriteLine($"EN50122 Analiz: Ute={touchVoltage}V/{touchVoltageDuration}s={touchCategory}, " +
                            $"UDC={dcVoltage}V={dcCategory}, IG={groundCurrent}A={groundCategory}");

            // Kritiklik sırası: KIRMIZI > SARI > NORMAL
            if (touchCategory == "KIRMIZI" || dcCategory == "KIRMIZI" || groundCategory == "KIRMIZI")
                return "KIRMIZI";

            if (touchCategory == "SARI" || dcCategory == "SARI" || groundCategory == "SARI")
                return "SARI";

            return "NORMAL";
        }

        /// <summary>
        /// DC voltaj için detaylı bilgi döndürür
        /// </summary>
        public static string GetDcVoltageInfo(double dcVoltage)
        {
            string category = GetDcVoltageCategory(dcVoltage);
            string symbol = GetStatusSymbol(category);
            double deviation = Math.Abs(dcVoltage - NOMINAL_DC_VOLTAGE);
            double deviationPercent = (deviation / NOMINAL_DC_VOLTAGE) * 100;

            return $"{symbol} {dcVoltage:N0} V (%{deviationPercent:N1} sapma)\n" +
                   $"Normal: {NORMAL_MIN:N0}-{NORMAL_MAX:N0} V\n" +
                   $"Uyarı: {WARNING_MIN:N0}-{WARNING_MAX:N0} V\n" +
                   $"Alarm: {ALARM_MIN:N0}-{ALARM_MAX:N0} V";
        }

        /// <summary>
        /// Dokunma gerilimi için detaylı bilgi döndürür
        /// </summary>
        public static string GetTouchVoltageInfo(double touchVoltage, double duration)
        {
            string category = GetTouchVoltageCategory(touchVoltage, duration);
            string symbol = GetStatusSymbol(category);
            var limits = GetTouchVoltageLimits(duration);

            string limitInfo = limits.shortTermV > 0
                ? $"Kısa süreli limit: {limits.shortTermV} V"
                : $"Uzun süreli limit: {limits.longTermV} V";

            return $"{symbol} {touchVoltage:N0} V ({duration:F1}s)\n{limitInfo}";
        }

        /// <summary>
        /// Duruma göre renk döndürür
        /// </summary>
        public static Brush GetStatusColor(string status)
        {
            return status switch
            {
                "NORMAL" => Brushes.Green,
                "SARI" => Brushes.Orange,
                "KIRMIZI" => Brushes.Red,
                _ => Brushes.Gray
            };
        }

        /// <summary>
        /// Duruma göre sembol döndürür
        /// </summary>
        public static string GetStatusSymbol(string status)
        {
            return status switch
            {
                "NORMAL" => "✅",
                "SARI" => "⚠️",
                "KIRMIZI" => "🚨",
                _ => "⚡"
            };
        }

        public static double GetAllowedDurationForTouchVoltage(double touchVoltage)
        {
            // Dokunma gerilimine göre izin verilen maksimum süreyi bul

            // Kısa süreli limitler için kontrol
            var shortTermLimits = _touchVoltageLimits
                .Where(l => l.shortTermV > 0)
                .OrderBy(l => l.shortTermV);

            foreach (var limit in shortTermLimits)
            {
                if (touchVoltage <= limit.shortTermV)
                {
                    return limit.timeS;
                }
            }

            // Uzun süreli limitler için kontrol
            var longTermLimits = _touchVoltageLimits
                .Where(l => l.longTermV > 0)
                .OrderByDescending(l => l.longTermV);

            foreach (var limit in longTermLimits)
            {
                if (touchVoltage <= limit.longTermV)
                {
                    return double.MaxValue; // Süresiz izin verilir
                }
            }

            // Hiçbir limiti geçemiyorsa → 0 saniye (anında alarm)
            return 0.0;
        }

        /// <summary>
        /// Dokunma gerilimi limit tablosunu string olarak döndürür
        /// </summary>
        public static string GetLimitsTable()
        {
            var lines = new List<string>
            {
                "EN 50122-1 Çizelge 6: Dokunma Gerilimi Limitleri",
                "==============================================",
                "Zaman (s) | Uzun Süreli (V) | Kısa Süreli (V)",
                "----------------------------------------------"
            };

            foreach (var limit in _touchVoltageLimits.OrderByDescending(l => l.timeS))
            {
                string timeStr = limit.timeS == double.MaxValue ? ">300" : limit.timeS.ToString("F2");
                string longTermStr = limit.longTermV > 0 ? limit.longTermV.ToString() : "-";
                string shortTermStr = limit.shortTermV > 0 ? limit.shortTermV.ToString() : "-";

                lines.Add($"{timeStr,8} | {longTermStr,15} | {shortTermStr,15}");
            }

            lines.Add("\nDC Gerilim Limitleri (1500V Nominal):");
            lines.Add($"Normal: {NORMAL_MIN:N0} - {NORMAL_MAX:N0} V (±%10)");
            lines.Add($"Uyarı: {WARNING_MIN:N0} - {WARNING_MAX:N0} V (±%20)");
            lines.Add($"Alarm: {ALARM_MIN:N0} - {ALARM_MAX:N0} V (±%30)");

            return string.Join("\n", lines);
        }
    }
}