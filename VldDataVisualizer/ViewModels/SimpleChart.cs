using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VldDataVisualizer.ViewModels
{
    public class SimpleChart : Canvas
    {
        private List<double> _values = new List<double>();
        private string _title = "Chart";
        private string _yAxisTitle = "Value";
        private double _minY = 0;
        private double _maxY = 100;
        private Brush _lineColor = Brushes.Blue;
        private Brush _backgroundColor = Brushes.White;
        private bool _autoScaleY = true; // Yeni özellik: Otomatik Y ekseni ölçekleme

        public List<double> Values
        {
            get => _values;
            set
            {
                _values = value;
                UpdateChart();
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                UpdateChart();
            }
        }

        public string YAxisTitle
        {
            get => _yAxisTitle;
            set
            {
                _yAxisTitle = value;
                UpdateChart();
            }
        }

        public double MinY
        {
            get => _minY;
            set
            {
                _minY = value;
                UpdateChart();
            }
        }

        public double MaxY
        {
            get => _maxY;
            set
            {
                _maxY = value;
                UpdateChart();
            }
        }

        public Brush LineColor
        {
            get => _lineColor;
            set
            {
                _lineColor = value;
                UpdateChart();
            }
        }

        public Brush BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                _backgroundColor = value;
                UpdateChart();
            }
        }

        // Yeni özellik: Otomatik Y ekseni ölçekleme
        public bool AutoScaleY
        {
            get => _autoScaleY;
            set
            {
                _autoScaleY = value;
                UpdateChart();
            }
        }

        public void AddValue(double value)
        {
            _values.Add(value);
            if (_values.Count > 50) // Son 50 değeri tut
                _values.RemoveAt(0);

            UpdateChart();
        }

        public void Clear()
        {
            _values.Clear();
            UpdateChart();
        }

        private (double min, double max) CalculateAutoYRange()
        {
            if (_values.Count == 0)
                return (MinY, MaxY);

            double min = double.MaxValue;
            double max = double.MinValue;

            foreach (var value in _values)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }

            // Eğer tüm değerler aynıysa, range'i genişlet
            if (Math.Abs(max - min) < 0.001)
            {
                min = min - 1;
                max = max + 1;
            }

            // %10 margin ekle (verinin 50 fazlası değil, %10'u daha iyi)
            double range = max - min;
            double margin = range * 0.1;

            return (min - margin, max + margin);
        }

        private void UpdateChart()
        {
            Children.Clear();

            if (_values.Count == 0) return;

            double canvasWidth = ActualWidth - 60; // Eksenler için margin
            double canvasHeight = ActualHeight - 40;

            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            // Otomatik Y ekseni aralığını hesapla
            double minY = _minY;
            double maxY = _maxY;

            if (_autoScaleY)
            {
                var autoRange = CalculateAutoYRange();
                minY = autoRange.min;
                maxY = autoRange.max;

                // Minimum range kontrolü (çok küçük değerler için)
                if (maxY - minY < 1.0)
                {
                    minY -= 0.5;
                    maxY += 0.5;
                }
            }

            // Draw background
            var background = new Rectangle
            {
                Width = ActualWidth,
                Height = ActualHeight,
                Fill = _backgroundColor,
                Stroke = Brushes.LightGray,
                StrokeThickness = 1
            };
            Children.Add(background);

            // Draw title
            var titleText = new TextBlock
            {
                Text = _title,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetTop(titleText, 5);
            Canvas.SetLeft(titleText, ActualWidth / 2 - titleText.Text.Length * 3);
            Children.Add(titleText);

            // Draw Y-axis title
            var yAxisText = new TextBlock
            {
                Text = _yAxisTitle,
                FontSize = 10,
                Foreground = Brushes.Black,
                RenderTransform = new RotateTransform(-90)
            };
            Canvas.SetTop(yAxisText, ActualHeight / 2);
            Canvas.SetLeft(yAxisText, 5);
            Children.Add(yAxisText);

            // Calculate scaling factors
            double xStep = canvasWidth / Math.Max(1, _values.Count - 1);
            double yRange = maxY - minY;
            if (yRange == 0) yRange = 1;

            // Draw grid lines and Y-axis labels
            for (int i = 0; i <= 5; i++)
            {
                double value = minY + (yRange * i / 5);
                double y = canvasHeight - (value - minY) / yRange * canvasHeight + 20;

                // Grid line
                var gridLine = new Line
                {
                    X1 = 50,
                    Y1 = y,
                    X2 = ActualWidth - 10,
                    Y2 = y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5
                };
                Children.Add(gridLine);

                // Y-axis label - format based on value size
                string labelFormat = Math.Abs(value) >= 1000 ? "0" :
                                   Math.Abs(value) >= 100 ? "0" :
                                   Math.Abs(value) >= 10 ? "0.0" : "0.00";

                var yLabel = new TextBlock
                {
                    Text = value.ToString(labelFormat),
                    FontSize = 9,
                    Foreground = Brushes.Black
                };
                Canvas.SetTop(yLabel, y - 8);
                Canvas.SetLeft(yLabel, 30);
                Children.Add(yLabel);
            }

            // Draw data line
            Polyline polyline = new Polyline
            {
                Stroke = _lineColor,
                StrokeThickness = 2,
                Points = new PointCollection()
            };

            for (int i = 0; i < _values.Count; i++)
            {
                double x = 50 + i * xStep;
                double y = canvasHeight - (_values[i] - minY) / yRange * canvasHeight + 20;

                // Değerleri sınırla (chart dışına çıkmasın)
                y = Math.Max(20, Math.Min(canvasHeight + 20, y));

                polyline.Points.Add(new Point(x, y));
            }

            Children.Add(polyline);

            // Draw X-axis
            var xAxis = new Line
            {
                X1 = 50,
                Y1 = canvasHeight + 20,
                X2 = ActualWidth - 10,
                Y2 = canvasHeight + 20,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Children.Add(xAxis);

            // Draw Y-axis
            var yAxis = new Line
            {
                X1 = 50,
                Y1 = 20,
                X2 = 50,
                Y2 = canvasHeight + 20,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Children.Add(yAxis);

            // Auto-scale bilgisini göster (debug için)
            var scaleInfo = new TextBlock
            {
                Text = $"Range: {minY:0} - {maxY:0}",
                FontSize = 8,
                Foreground = Brushes.Gray
            };
            Canvas.SetTop(scaleInfo, ActualHeight - 15);
            Canvas.SetLeft(scaleInfo, ActualWidth - 80);
            Children.Add(scaleInfo);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateChart();
        }
    }
}