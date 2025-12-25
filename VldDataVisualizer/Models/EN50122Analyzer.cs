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
        /*
         * EN 50122-1:2011
         * Çizelge 6 – Zamanın bir fonksiyonu olarak d.a. cer sistemleri için
         * azami müsaade edilebilir etkin dokunma gerilimleri (Ute)
         */
        private static readonly List<(double timeS, double longTermV, double shortTermV)> _touchVoltageLimits = new()
        {
            (double.MaxValue, 120, 0),   // >300 s – uzun süreli temas
            (300, 150, 0),
            (1, 160, 0),
            (0.9, 165, 0),
            (0.8, 170, 0),
            (0.7, 175, 0),
            (0.6, 0, 360),               // kısa süreli temas
            (0.5, 0, 385),
            (0.4, 0, 420),
            (0.3, 0, 460),
            (0.2, 0, 520),
            (0.1, 0, 625),
            (0.05, 0, 735),
            (0.02, 0, 870),
        };

        /*
         * EN 50122-1 Madde 4.2 ve 5.2
         * DC cer besleme sistemleri için nominal gerilim ve işletme toleransları
         * (1500 V DC sistem varsayımı)
         */
        private const double NOMINAL_DC_VOLTAGE = 1500.0;
        private const double NORMAL_TOLERANCE = 0.10;   // ±%10 – normal işletme
        private const double WARNING_TOLERANCE = 0.20;  // ±%20 – anormal durum
        private const double ALARM_TOLERANCE = 0.30;    // ±%30 – tehlikeli durum

        // EN 50122-1 işletme toleranslarından türetilmiş sınırlar
        private static readonly double NORMAL_MIN = NOMINAL_DC_VOLTAGE * 0.90;
        private static readonly double NORMAL_MAX = NOMINAL_DC_VOLTAGE * 1.10;
        private static readonly double WARNING_MIN = NOMINAL_DC_VOLTAGE * 0.80;
        private static readonly double WARNING_MAX = NOMINAL_DC_VOLTAGE * 1.20;
        private static readonly double ALARM_MIN = NOMINAL_DC_VOLTAGE * 0.70;
        private static readonly double ALARM_MAX = NOMINAL_DC_VOLTAGE * 1.30;

        /*
         * EN 50122-1 Çizelge 6
         * Dokunma gerilimi (Ute) ve maruz kalma süresine göre
         * güvenlik durumunun sınıflandırılması
         */
        public static string GetTouchVoltageCategory(double touchVoltage, double duration)
        {
            if (duration <= 0)
                return "NORMAL";

            var limits = GetTouchVoltageLimits(duration);

            if (touchVoltage > limits.shortTermV && limits.shortTermV > 0)
                return "KIRMIZI"; // Kısa süreli limit aşıldı

            if (touchVoltage > limits.longTermV && limits.longTermV > 0)
                return "SARI"; // Uzun süreli limit aşıldı

            return "NORMAL";
        }

        /*
         * EN 50122-1 Çizelge 6
         * Süreye bağlı olarak geçerli olan dokunma gerilimi limitlerini seçer
         */
        private static (double longTermV, double shortTermV) GetTouchVoltageLimits(double duration)
        {
            foreach (var limit in _touchVoltageLimits.OrderBy(l => l.timeS))
            {
                if (duration <= limit.timeS)
                    return (limit.longTermV, limit.shortTermV);
            }

            return (120, 0); // EN 50122-1 uzun süreli maksimum değer
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
         * Ray-toprak dönüş akımlarının güvenlik açısından değerlendirilmesi
         * (uygulamaya özgü mühendislik sınırları kullanılmıştır)
         */
        public static string GetGroundCurrentCategory(double groundCurrent)
        {
            const double NORMAL_LIMIT = 3.0;
            const double WARNING_LIMIT = 6.0;
            const double ALARM_LIMIT = 10.0;

            if (groundCurrent >= ALARM_LIMIT)
                return "KIRMIZI";

            if (groundCurrent >= WARNING_LIMIT)
                return "SARI";

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

            return "NORMAL";
        }

        /*
         * EN 50122-1 Çizelge 6
         * Belirli bir dokunma gerilimi için izin verilen maksimum temas süresi
         */
        public static double GetAllowedDurationForTouchVoltage(double touchVoltage)
        {
            var shortTermLimits = _touchVoltageLimits
                .Where(l => l.shortTermV > 0)
                .OrderBy(l => l.shortTermV);

            foreach (var limit in shortTermLimits)
            {
                if (touchVoltage <= limit.shortTermV)
                    return limit.timeS;
            }

            var longTermLimits = _touchVoltageLimits
                .Where(l => l.longTermV > 0)
                .OrderByDescending(l => l.longTermV);

            foreach (var limit in longTermLimits)
            {
                if (touchVoltage <= limit.longTermV)
                    return double.MaxValue;
            }

            return 0.0; // EN 50122'e göre anında tehlike
        }
    }
}