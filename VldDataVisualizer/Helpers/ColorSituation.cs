using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace VldDataVisualizer.Helpers
{
    public class ColorSituation
    {
        public static Brush GetStatusColor(string status) { return status switch { "NORMAL" => Brushes.Green, "SARI" => Brushes.Orange, "KIRMIZI" => Brushes.Red, _ => Brushes.Gray }; }

        public static string GetStatusSymbol(string status) { return status switch { "NORMAL" => "✅", "SARI" => "⚠️", "KIRMIZI" => "🚨", _ => "⚡" }; }
    }
}
