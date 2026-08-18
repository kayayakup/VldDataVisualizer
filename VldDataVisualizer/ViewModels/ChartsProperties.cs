using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VldDataVisualizer.ViewModels
{
    public class ChartsProperties : FrameworkElement
    {
        private readonly List<double> _values = new();
        private readonly List<double> _secondaryValues = new();
        private readonly List<DateTime> _timestamps = new();

        #region Public Properties

        public string Title { get; set; } = "Chart";
        public string YAxisTitle { get; set; } = "Value";
        public string XAxisTitle { get; set; } = "Zaman";

        public Brush LineColor { get; set; } = Brushes.Blue;
        public Brush SecondaryLineColor { get; set; } = Brushes.Blue;
        public bool HasSecondarySeries { get; set; } = false;
        public Brush BackgroundColor { get; set; } = Brushes.White;

        public bool IsPaused { get; set; }

        public double MinY { get; set; } = 0;
        public double MaxY { get; set; } = 100;

        #endregion

        #region Data Methods

        public void AddValue(double value, double? secondaryValue = null)
        {
            if (IsPaused) return;

            _values.Add(value);
            
            if (secondaryValue.HasValue)
            {
                HasSecondarySeries = true;
                _secondaryValues.Add(secondaryValue.Value);
            }
            else if (HasSecondarySeries)
            {
                _secondaryValues.Add(0); // Veya en son değeri koruyabiliriz
            }

            _timestamps.Add(DateTime.Now);

            if (_values.Count > 50)
            {
                _values.RemoveAt(0);
                if (HasSecondarySeries && _secondaryValues.Count > 0)
                    _secondaryValues.RemoveAt(0);
                _timestamps.RemoveAt(0);
            }

            InvalidateVisual();
        }

        public void Clear()
        {
            _values.Clear();
            _secondaryValues.Clear();
            _timestamps.Clear();
            InvalidateVisual();
        }

        #endregion

        #region Rendering

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;

            DrawBackground(dc);
            DrawAxes(dc);
            DrawTitles(dc);
            DrawGrid(dc);
            DrawData(dc);
        }

        private void DrawBackground(DrawingContext dc)
        {
            dc.DrawRectangle(
                BackgroundColor,
                new Pen(Brushes.LightGray, 1),
                new Rect(0, 0, ActualWidth, ActualHeight));
        }

        private void DrawTitles(DrawingContext dc)
        {
            DrawText(dc, Title, 12, FontWeights.Bold,
                new Point(ActualWidth / 2 - 40, 5));

            DrawText(dc, YAxisTitle, 10, FontWeights.Normal,
                new Point(5, ActualHeight / 2),
                -90);

            DrawText(dc, XAxisTitle, 10, FontWeights.Normal,
                new Point(ActualWidth / 2 - 20, ActualHeight - 15));
        }

        private void DrawAxes(DrawingContext dc)
        {
            Pen axisPen = new(Brushes.Black, 1);

            // Y Axis
            dc.DrawLine(axisPen,
                new Point(60, 30),
                new Point(60, ActualHeight - 40));

            // X Axis
            dc.DrawLine(axisPen,
                new Point(60, ActualHeight - 40),
                new Point(ActualWidth - 10, ActualHeight - 40));
        }

        private void DrawGrid(DrawingContext dc)
        {
            double height = ActualHeight - 70;
            double minY = MinY;
            double maxY = MaxY;
            double range = maxY - minY;

            Pen gridPen = new(Brushes.LightGray, 0.5);

            for (int i = 0; i <= 5; i++)
            {
                double value = minY + (range * i / 5);
                double y = (ActualHeight - 40) - (value - minY) / range * height;

                dc.DrawLine(gridPen,
                    new Point(60, y),
                    new Point(ActualWidth - 10, y));

                DrawText(dc, value.ToString("0.0"), 9, FontWeights.Normal,
                    new Point(20, y - 7));
            }

            // X Axis Time Labels
            if (_timestamps.Count > 1)
            {
                double width = ActualWidth - 70;
                int numLabels = 5;
                for (int i = 0; i <= numLabels; i++)
                {
                    int idx = (int)((_timestamps.Count - 1) * ((double)i / numLabels));
                    double x = 60 + i * (width / numLabels);
                    string timeStr = _timestamps[idx].ToString("HH:mm:ss");
                    DrawText(dc, timeStr, 8, FontWeights.Normal, new Point(x - 18, ActualHeight - 35));
                    
                    dc.DrawLine(gridPen, new Point(x, 30), new Point(x, ActualHeight - 40));
                }
            }
        }

        private void DrawData(DrawingContext dc)
        {
            if (_values.Count < 2) return;

            double width = ActualWidth - 70;
            double height = ActualHeight - 70;
            double minY = MinY;
            double maxY = MaxY;
            double range = maxY - minY;

            double xStep = width / (_values.Count - 1);

            // Çizgi 1 (Primary)
            StreamGeometry geometry1 = new();
            using (var ctx = geometry1.Open())
            {
                for (int i = 0; i < _values.Count; i++)
                {
                    double x = 60 + i * xStep;
                    double y = (ActualHeight - 40) - (_values[i] - minY) / range * height;

                    if (i == 0)
                        ctx.BeginFigure(new Point(x, y), false, false);
                    else
                        ctx.LineTo(new Point(x, y), true, false);
                }
            }

            geometry1.Freeze();
            dc.DrawGeometry(null, new Pen(LineColor, 2), geometry1);

            // Çizgi 2 (Secondary)
            if (HasSecondarySeries && _secondaryValues.Count >= 2)
            {
                StreamGeometry geometry2 = new();
                using (var ctx = geometry2.Open())
                {
                    for (int i = 0; i < _secondaryValues.Count && i < _values.Count; i++)
                    {
                        double x = 60 + i * xStep;
                        double y = (ActualHeight - 40) - (_secondaryValues[i] - minY) / range * height;

                        if (i == 0)
                            ctx.BeginFigure(new Point(x, y), false, false);
                        else
                            ctx.LineTo(new Point(x, y), true, false);
                    }
                }

                geometry2.Freeze();
                dc.DrawGeometry(null, new Pen(SecondaryLineColor, 2), geometry2);
            }
        }

        private void DrawText(
            DrawingContext dc,
            string text,
            double size,
            FontWeight weight,
            Point position,
            double rotate = 0)
        {
            FormattedText ft = new(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.PushTransform(new RotateTransform(rotate, position.X, position.Y));
            dc.DrawText(ft, position);
            dc.Pop();
        }

        #endregion
    }
}
