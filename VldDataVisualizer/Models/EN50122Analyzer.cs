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
         * Çizelge 7 – Zamanın bir fonksiyonu olarak a.a. cer sistemlerinde
         * dokunma gerilimleri, vücut gerilimleri ve vücut akımları
         *
         * Ute,azami uzun süreli  → uzun süreli şartlar için ilgili dokunma gerilimi
         * Ute,azami kısa süreli → kısa süreli şartlar için ilgili dokunma gerilimi
         *   (eski ıslak ayakkabılar için ilave direnç dikkate alınarak)
         *
         * Ute,azami = Uc1 + Ra1 × Ic1 × 10⁻³
         *   Ra1 = 100 Ω (ıslak eski ayakkabılar için direnç)
         */
        private static readonly List<(double timeS, double longTermV, double shortTermV)> _touchVoltageLimits = new()
        {
            (double.MaxValue, 60, 0),    // >300 s – uzun süreli temas
            (300, 65, 0),                // 300 s
            (1.0, 75, 0),               // 1.0 s
            (0.9, 80, 0),               // 0.9 s
            (0.8, 85, 0),               // 0.8 s
            (0.7, 90, 0),               // 0.7 s – uzun/kısa süreli geçiş noktası
            (0.6, 0, 180),              // 0.6 s – kısa süreli temas başlangıcı
            (0.5, 0, 220),              // 0.5 s
            (0.4, 0, 295),              // 0.4 s
            (0.3, 0, 480),              // 0.3 s
            (0.2, 0, 645),              // 0.2 s
            (0.1, 0, 785),              // 0.1 s
            (0.05, 0, 835),             // 0.05 s
            (0.02, 0, 865),             // 0.02 s
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
         * EN 50122-1 Çizelge 7
         * Dokunma gerilimi (Ute) ve maruz kalma süresine göre
         * güvenlik durumunun sınıflandırılması (AC cer sistemi)
         */
        public static string GetTouchVoltageCategory(double touchVoltage, double duration)
        {
            // Uygulamada erken bildiri önceliği olduğu için kırmızı eşik yükseltildi,
            // sarı/yeşil hataları görünür tutmak için uyarı eşikleri daha gerçekçi ayarlandı.
            const double EARLY_WARNING_VOLTAGE = 25.0;
            const double CRITICAL_VOLTAGE = 70.0;
            const double SUDDEN_SPIKE_VOLTAGE = 95.0;

            if (touchVoltage >= SUDDEN_SPIKE_VOLTAGE || touchVoltage >= CRITICAL_VOLTAGE)
                return "KIRMIZI";

            if (touchVoltage >= EARLY_WARNING_VOLTAGE)
                return "SARI";

            return "NORMAL";
        }

        /*
         * EN 50122-1 Çizelge 7
         * Süreye bağlı olarak geçerli olan dokunma gerilimi limitlerini seçer
         */
        private static (double longTermV, double shortTermV) GetTouchVoltageLimits(double duration)
        {
            foreach (var limit in _touchVoltageLimits.OrderBy(l => l.timeS))
            {
                if (duration <= limit.timeS)
                    return (limit.longTermV, limit.shortTermV);
            }

            return (60, 0); // EN 50122-1 Çizelge 7 uzun süreli azami değer (>300s)
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
            // Kaçak ihtimalinden önce uyarı vermek için eşikler daha erken başlatıldı,
            // fakat gerçek kırmızı alarm için sadece kritik akım değerleri kullanılacak.
            const double NORMAL_LIMIT = 0.8;
            const double WARNING_LIMIT = 1.2;
            const double ALARM_LIMIT = 3.0;

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
         * EN 50122-1 Çizelge 7
         * Belirli bir dokunma gerilimi için izin verilen maksimum temas süresi
         */
        public static double GetAllowedDurationForTouchVoltage(double touchVoltage)
        {
            // Uzun süreli güvenli sınır (60V) altındaysa süresiz izin verilir
            if (touchVoltage <= 60)
                return double.MaxValue;

            // Süreye göre büyükten küçüğe sırala (double.MaxValue'dan 0.02s'ye)
            var sortedLimits = _touchVoltageLimits.OrderByDescending(l => l.timeS);

            foreach (var limit in sortedLimits)
            {
                double limitVoltage = limit.shortTermV > 0 ? limit.shortTermV : limit.longTermV;
                if (touchVoltage <= limitVoltage)
                {
                    return limit.timeS;
                }
            }

            return 0.0; // Hiçbir limite uymuyorsa (örn: >865V) izin verilen süre 0'dır (anında tehlike)
        }
    }
}