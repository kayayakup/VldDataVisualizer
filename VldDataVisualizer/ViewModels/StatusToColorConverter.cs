using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VldDataVisualizer.ViewModels
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string status)
                return Brushes.Gray;

            // Normalize (büyük harf + trim)
            status = status.Trim().ToUpperInvariant();

            return status switch
            {
                // NORMAL / OK / ÇALIŞIYOR
                "NORMAL" => Brushes.Green,
                "OK" => Brushes.Green,

                // HAREKET
                "MOVING" => Brushes.Blue,
                "HAREKET HALİNDE" => Brushes.Blue,

                // DURUYOR / BEKLEME
                "DURUYOR" => Brushes.Orange,
                "STOPPED" => Brushes.Orange,
                "WARNING" => Brushes.Orange,

                // HATA / ALARM
                "ALARM" => Brushes.Red,
                "ERROR" => Brushes.Red,
                "COMMUNICATION_ERROR" => Brushes.Red,

                _ => Brushes.Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("StatusToColorConverter ConvertBack desteklemez.");
        }
    }
}
