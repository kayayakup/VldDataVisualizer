using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VldDataVisualizer.ViewModels
{
    public class ChartsProperties : Canvas
    {
        private List<double> _values = new List<double>();
        private List<DateTime> _timestamps = new List<DateTime>();
        private string _title = "Chart";
        private string _yAxisTitle = "Value";
        private string _xAxisTitle = "Zaman"; // X ekseni başlığı eklendi
        private double _minY = 0;
        private double _maxY = 100;
        private Brush _lineColor = Brushes.Blue;
        private Brush _backgroundColor = Brushes.White;
        private bool _autoScaleY = true;

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

        public string XAxisTitle
        {
            get => _xAxisTitle;
            set
            {
                _xAxisTitle = value;
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

        public bool AutoScaleY
        {
            get => _autoScaleY;
            set
            {
                _autoScaleY = value;
                UpdateChart();
            }
        }

        private bool _isPaused = false;

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                // Gerekirse paused durumunda görsel değişiklik yap
            }
        }

        public void AddValue(double value)
        {
            if (_isPaused) return; // Eğer paused ise değer ekleme

            _values.Add(value);
            _timestamps.Add(DateTime.Now);

            if (_values.Count > 50)
            {
                _values.RemoveAt(0);
                _timestamps.RemoveAt(0);
            }

            UpdateChart();
        }

        public void Clear()
        {
            _values.Clear();
            _timestamps.Clear();
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

            // %10 margin ekle
            double range = max - min;
            double margin = range * 0.1;

            return (min - margin, max + margin);
        }

        private void UpdateChart()
        {
            Children.Clear();

            if (_values.Count == 0) return;

            double canvasWidth = ActualWidth - 60; // Eksenler için margin
            double canvasHeight = ActualHeight - 60; // X ekseni etiketi için daha fazla margin

            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            // Otomatik Y ekseni aralığını hesapla
            double minY = _minY;
            double maxY = _maxY;

            //if (_autoScaleY)
            //{
            //    var autoRange = CalculateAutoYRange();
            //    minY = autoRange.min;
            //    maxY = autoRange.max;

            //    // Minimum range kontrolü (çok küçük değerler için)
            //    if (maxY - minY < 1.0)
            //    {
            //        minY -= 0.5;
            //        maxY += 0.5;
            //    }
            //}

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
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetTop(titleText, 5);
            Canvas.SetLeft(titleText, ActualWidth / 2 - (_title.Length * 4)); // Daha iyi merkezleme
            Children.Add(titleText);

            // Draw Y-axis title
            var yAxisText = new TextBlock
            {
                Text = _yAxisTitle,
                FontSize = 10,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.SemiBold,
                RenderTransform = new RotateTransform(-90)
            };
            Canvas.SetTop(yAxisText, ActualHeight / 2 - 20);
            Canvas.SetLeft(yAxisText, 5);
            Children.Add(yAxisText);

            // Draw X-axis title
            var xAxisText = new TextBlock
            {
                Text = _xAxisTitle,
                FontSize = 10,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetTop(xAxisText, ActualHeight - 20);
            Canvas.SetLeft(xAxisText, ActualWidth / 2 - (_xAxisTitle.Length * 3));
            Children.Add(xAxisText);

            // Calculate scaling factors
            double xStep = canvasWidth / Math.Max(1, _values.Count - 1);
            double yRange = maxY - minY;
            if (yRange == 0) yRange = 1;

            // Draw grid lines and Y-axis labels
            for (int i = 0; i <= 5; i++)
            {
                double value = minY + (yRange * i / 5);
                double y = canvasHeight - (value - minY) / yRange * canvasHeight + 30;

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
                Canvas.SetLeft(yLabel, 25);
                Children.Add(yLabel);
            }

            // Draw X-axis time labels
            if (_timestamps.Count > 0)
            {
                // Başlangıç, orta ve bitiş zamanlarını göster
                int[] keyIndices = { 0, _timestamps.Count / 2, _timestamps.Count - 1 };

                foreach (int index in keyIndices)
                {
                    if (index < _timestamps.Count)
                    {
                        double x = 50 + index * xStep;
                        var timeLabel = new TextBlock
                        {
                            Text = _timestamps[index].ToString("HH:mm:ss"),
                            FontSize = 8,
                            Foreground = Brushes.Black,
                            Background = Brushes.White
                        };
                        Canvas.SetTop(timeLabel, canvasHeight + 35);
                        Canvas.SetLeft(timeLabel, x - 20);
                        Children.Add(timeLabel);

                        // Zaman çizgisi
                        var timeLine = new Line
                        {
                            X1 = x,
                            Y1 = 30,
                            X2 = x,
                            Y2 = canvasHeight + 30,
                            Stroke = Brushes.LightGray,
                            StrokeThickness = 0.3,
                            StrokeDashArray = new DoubleCollection { 2, 2 }
                        };
                        Children.Add(timeLine);
                    }
                }
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
                double y = canvasHeight - (_values[i] - minY) / yRange * canvasHeight + 30;

                // Değerleri sınırla (chart dışına çıkmasın)
                y = Math.Max(30, Math.Min(canvasHeight + 30, y));

                polyline.Points.Add(new Point(x, y));
            }

            Children.Add(polyline);

            // Draw X-axis
            var xAxis = new Line
            {
                X1 = 50,
                Y1 = canvasHeight + 30,
                X2 = ActualWidth - 10,
                Y2 = canvasHeight + 30,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Children.Add(xAxis);

            // Draw Y-axis
            var yAxis = new Line
            {
                X1 = 50,
                Y1 = 30,
                X2 = 50,
                Y2 = canvasHeight + 30,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Children.Add(yAxis);

            // Auto-scale bilgisini göster (debug için - isteğe bağlı)
            if (_autoScaleY)
            {
                var scaleInfo = new TextBlock
                {
                    Text = $"Y: {minY:0} - {maxY:0}",
                    FontSize = 7,
                    Foreground = Brushes.Gray
                };
                Canvas.SetTop(scaleInfo, ActualHeight - 40);
                Canvas.SetLeft(scaleInfo, ActualWidth - 60);
                Children.Add(scaleInfo);
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateChart();
        }
    }
}