using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace VldDataVisualizer.Models
{
    /// <summary>
    /// EN 50122-1: Railway applications – Fixed installations – Electrical safety, earthing and the return circuit
    /// Bu sınıf, EN 50122-1 standardına göre dokunma gerilimi, DC cer gerilimi
    /// ve toprak akımı değerlendirmelerini yapar.
    /// </summary>
    public class EN50122Analyzer
    {
        /* TS EN 50122-1, Çizelge: t, Ic1, Uc1, Ub,azami, Ute,azami (Uzun süreli / Kısa süreli) */
        private static readonly List<(double timeS, double longTermV, double shortTermV)> _dcTouchVoltageLimits = new()
        {
            (double.MaxValue, 60, 0),       // > 300 s -> 60 V
            (300, 65, 0),                   // 300 s -> 65 V
            (1.0, 75, 0),                   // 1.0 s -> 75 V
            (0.9, 80, 0),                   // 0.9 s -> 80 V
            (0.8, 85, 0),                   // 0.8 s -> 85 V
            (0.7, 90, 0),                   // 0.7 s -> 90 V
            (0.6, 0, 180),                  // 0.6 s -> 180 V (kısa süreli < 0.7s: 155V)
            (0.5, 0, 220),                  // 0.5 s -> 220 V
            (0.4, 0, 295),                  // 0.4 s -> 295 V
            (0.3, 0, 480),                  // 0.3 s -> 480 V
            (0.2, 0, 645),                  // 0.2 s -> 645 V
            (0.1, 0, 785),                  // 0.1 s -> 785 V
            (0.05, 0, 835),                 // 0.05 s -> 835 V
            (0.02, 0, 865),                 // 0.02 s -> 865 V
        };

        /*
         * EN 50122-1 Madde 4.2 ve 5.2
         * DC cer besleme sistemleri için nominal gerilim ve işletme toleransları
         * (1500 V DC sistem varsayımı)
         */
        private const double NOMINAL_DC_VOLTAGE = 1500.0;
        private const double WARNING_TOLERANCE = 0.20;  // ±%20 – anormal durum
        private const double ALARM_TOLERANCE = 0.30;    // ±%30 – tehlikeli durum

        // EN 50122-1 işletme toleranslarından türetilmiş sınırlar
        private static readonly double WARNING_MIN = NOMINAL_DC_VOLTAGE * 0.80; // 1200 V
        private static readonly double WARNING_MAX = NOMINAL_DC_VOLTAGE * 1.20; // 1800 V
        private static readonly double ALARM_MIN = NOMINAL_DC_VOLTAGE * 0.70;   // 1050 V
        private static readonly double ALARM_MAX = NOMINAL_DC_VOLTAGE * 1.30;   // 1950 V

        /*
         * EN 50122-1 Dokunma Gerilimi (Ute) Kategorilendirmesi:
         * - KIRMIZI: Olay süresi için TS EN 50122-1 Çizelgesindeki azami Ute limitini aşan kesin tehlike
         * - SARI: Limitin %70'ine yaklaşmış veya uzun süreli sınırları zorlayan yüksek seviye uyarı
         * - YEŞİL: Düşük seviye kaçak gerilimi (izin verilen güvenli sınırın altında)
         * - NORMAL: İhmal edilebilir / sıfır gerilim
         */
        public static string GetTouchVoltageCategory(double touchVoltage, double duration)
        {
            if (touchVoltage <= 3.0)
                return "NORMAL";

            double allowedVoltage = GetAllowedTouchVoltage(duration);

            // Kesin kırmızı: Standart azami izin verilen sınır aşıldı
            if (touchVoltage > allowedVoltage)
                return "KIRMIZI";

            // Sarı uyarı: Sınıra yaklaşma (%70-%100 arası) veya uzun süreli eşik aşımı
            if (touchVoltage >= allowedVoltage * 0.70 || (duration >= 1.0 && touchVoltage >= 60.0))
                return "SARI";

            // Yeşil: Düşük seviyeli kaçak gerilimi (güvenli bölgede)
            return "YEŞİL";
        }

        /*
         * EN 50122-1 Çizelge
         * Süreye bağlı olarak geçerli olan dokunma gerilimi limitlerini seçer
         */
        private static (double longTermV, double shortTermV) GetTouchVoltageLimits(double duration)
        {
            double allowed = GetAllowedTouchVoltage(duration);
            return duration >= 0.7 ? (allowed, 0) : (0, allowed);
        }

        public static double GetAllowedTouchVoltage(double duration)
        {
            if (duration > 300)
                return 60.0;

            if (duration >= 300)
                return 65.0;

            if (duration >= 1.0)
                return InterpolateLimit(duration, 1.0, 75.0, 300.0, 65.0);

            if (duration >= 0.9)
                return InterpolateLimit(duration, 0.9, 80.0, 1.0, 75.0);

            if (duration >= 0.8)
                return InterpolateLimit(duration, 0.8, 85.0, 0.9, 80.0);

            if (duration >= 0.7)
                return InterpolateLimit(duration, 0.7, 90.0, 0.8, 85.0);

            if (duration >= 0.6)
                return InterpolateLimit(duration, 0.6, 180.0, 0.7, 155.0);

            if (duration >= 0.5)
                return InterpolateLimit(duration, 0.5, 220.0, 0.6, 180.0);

            if (duration >= 0.4)
                return InterpolateLimit(duration, 0.4, 295.0, 0.5, 220.0);

            if (duration >= 0.3)
                return InterpolateLimit(duration, 0.3, 480.0, 0.4, 295.0);

            if (duration >= 0.2)
                return InterpolateLimit(duration, 0.2, 645.0, 0.3, 480.0);

            if (duration >= 0.1)
                return InterpolateLimit(duration, 0.1, 785.0, 0.2, 645.0);

            if (duration >= 0.05)
                return InterpolateLimit(duration, 0.05, 835.0, 0.1, 785.0);

            if (duration >= 0.02)
                return InterpolateLimit(duration, 0.02, 865.0, 0.05, 835.0);

            return 865.0;
        }

        private static double InterpolateLimit(double duration, double lowerTime, double lowerVoltage,
            double upperTime, double upperVoltage)
        {
            if (Math.Abs(upperTime - lowerTime) < 0.0001)
                return lowerVoltage;

            double fraction = (duration - lowerTime) / (upperTime - lowerTime);
            return lowerVoltage + fraction * (upperVoltage - lowerVoltage);
        }

        /*
         * EN 50122-1 Madde 4.2
         * DC cer hattı geriliminin nominal değerden sapmasına göre
         * işletme durumunun değerlendirilmesi
         */
        public static string GetDcVoltageCategory(double dcVoltage)
        {
            if (dcVoltage < ALARM_MIN || dcVoltage > ALARM_MAX)
                return "KIRMIZI";

            if (dcVoltage < WARNING_MIN || dcVoltage > WARNING_MAX)
                return "SARI";

            return "NORMAL";
        }

        /*
         * EN 50122-1 Madde 6.3
         * Ray-toprak dönüş akımlarının güvenlik açısından değerlendirilmesi:
         * - KIRMIZI: >= 3.0 A (kritik arıza / kesin tehlike)
         * - SARI: >= 1.2 A (orta/yüksek seviye uyarı)
         * - YEŞİL: >= 0.3 A (düşük seviye kaçak izleme)
         * - NORMAL: < 0.3 A (normal akım)
         */
        public static string GetGroundCurrentCategory(double groundCurrent)
        {
            const double GREEN_LIMIT = 0.3;
            const double WARNING_LIMIT = 1.2;
            const double ALARM_LIMIT = 3.0;

            if (groundCurrent >= ALARM_LIMIT)
                return "KIRMIZI";

            if (groundCurrent >= WARNING_LIMIT)
                return "SARI";

            if (groundCurrent >= GREEN_LIMIT)
                return "YEŞİL";

            return "NORMAL";
        }

        /*
         * EN 50122-1 Madde 3 ve Madde 9
         * Dokunma gerilimi, cer gerilimi ve toprak akımının
         * birlikte değerlendirilmesiyle genel güvenlik durumu
         */
        public static string GetOverallStatus(
            double touchVoltage,
            double touchVoltageDuration,
            double dcVoltage,
            double groundCurrent)
        {
            string touchCategory = GetTouchVoltageCategory(touchVoltage, touchVoltageDuration);
            string dcCategory = GetDcVoltageCategory(dcVoltage);
            string groundCategory = GetGroundCurrentCategory(groundCurrent);

            if (touchCategory == "KIRMIZI" || dcCategory == "KIRMIZI" || groundCategory == "KIRMIZI")
                return "KIRMIZI";

            if (touchCategory == "SARI" || dcCategory == "SARI" || groundCategory == "SARI")
                return "SARI";

            if (touchCategory == "YEŞİL" || groundCategory == "YEŞİL")
                return "YEŞİL";

            return "NORMAL";
        }

        /*
         * EN 50122-1 Çizelge
         * Belirli bir dokunma gerilimi için izin verilen maksimum temas süresi
         */
        public static double GetAllowedDurationForTouchVoltage(double touchVoltage)
        {
            // Uzun süreli güvenli sınır (60 V) altındaysa süresiz izin verilir.
            if (touchVoltage <= 60.0)
                return double.MaxValue;

            // Süreye göre büyükten küçüğe sırala (double.MaxValue'dan 0.02s'ye)
            var sortedLimits = _dcTouchVoltageLimits
                .Where(l => l.shortTermV > 0 || l.longTermV > 0)
                .OrderByDescending(l => l.timeS);

            foreach (var limit in sortedLimits)
            {
                double limitVoltage = limit.shortTermV > 0 ? limit.shortTermV : limit.longTermV;
                if (touchVoltage <= limitVoltage)
                {
                    return limit.timeS == double.MaxValue ? double.MaxValue : limit.timeS;
                }
            }

            return 0.0; // Hiçbir limite uymuyorsa (örn: >865V) izin verilen süre 0'dır (anında tehlike)
        }
    }
}
