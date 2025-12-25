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
        private readonly List<DateTime> _timestamps = new();

        #region Public Properties

        public string Title { get; set; } = "Chart";
        public string YAxisTitle { get; set; } = "Value";
        public string XAxisTitle { get; set; } = "Zaman";

        public Brush LineColor { get; set; } = Brushes.Blue;
        public Brush BackgroundColor { get; set; } = Brushes.White;

        public bool IsPaused { get; set; }

        public double MinY { get; set; } = 0;
        public double MaxY { get; set; } = 100;

        #endregion

        #region Data Methods

        public void AddValue(double value)
        {
            if (IsPaused) return;

            _values.Add(value);
            _timestamps.Add(DateTime.Now);

            if (_values.Count > 50)
            {
                _values.RemoveAt(0);
                _timestamps.RemoveAt(0);
            }

            InvalidateVisual();
        }

        public void Clear()
        {
            _values.Clear();
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
                new Point(ActualWidth / 2 - 30, ActualHeight - 20));
        }

        private void DrawAxes(DrawingContext dc)
        {
            Pen axisPen = new(Brushes.Black, 1);

            // Y Axis
            dc.DrawLine(axisPen,
                new Point(50, 30),
                new Point(50, ActualHeight - 30));

            // X Axis
            dc.DrawLine(axisPen,
                new Point(50, ActualHeight - 30),
                new Point(ActualWidth - 10, ActualHeight - 30));
        }

        private void DrawGrid(DrawingContext dc)
        {
            double height = ActualHeight - 60;
            double minY = MinY;
            double maxY = MaxY;
            double range = maxY - minY;

            Pen gridPen = new(Brushes.LightGray, 0.5);

            for (int i = 0; i <= 5; i++)
            {
                double value = minY + (range * i / 5);
                double y = (ActualHeight - 30) - (value - minY) / range * height;

                dc.DrawLine(gridPen,
                    new Point(50, y),
                    new Point(ActualWidth - 10, y));

                DrawText(dc, value.ToString("0.0"), 9, FontWeights.Normal,
                    new Point(10, y - 7));
            }
        }

        private void DrawData(DrawingContext dc)
        {
            if (_values.Count < 2) return;

            double width = ActualWidth - 60;
            double height = ActualHeight - 60;
            double minY = MinY;
            double maxY = MaxY;
            double range = maxY - minY;

            double xStep = width / (_values.Count - 1);

            StreamGeometry geometry = new();
            using (var ctx = geometry.Open())
            {
                for (int i = 0; i < _values.Count; i++)
                {
                    double x = 50 + i * xStep;
                    double y = (ActualHeight - 30) - (_values[i] - minY) / range * height;

                    if (i == 0)
                        ctx.BeginFigure(new Point(x, y), false, false);
                    else
                        ctx.LineTo(new Point(x, y), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(LineColor, 2), geometry);
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
