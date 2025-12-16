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
            if (value is string status)
            {
                return status switch
                {
                    "NORMAL" => Brushes.Green,
                    "MOVING" => Brushes.Blue,
                    "DURUYOR" => Brushes.Orange,
                    "HAREKET HALİNDE" => Brushes.Green,
                    "ALARM" => Brushes.Red,
                    "WARNING" => Brushes.Orange,
                    "COMMUNICATION_ERROR" => Brushes.Red,
                    _ => Brushes.Gray
                };
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}