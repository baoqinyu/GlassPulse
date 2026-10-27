using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GlassPulse
{
    internal static class DashboardCharts
    {
        private static readonly Color[] Palette = {
            Color.FromRgb(166, 232, 203), Color.FromRgb(244, 198, 166), Color.FromRgb(155, 190, 229),
            Color.FromRgb(195, 175, 224), Color.FromRgb(129, 192, 198)
        };
        private static Brush Muted = new SolidColorBrush(Color.FromRgb(167, 183, 182));

        private static Brush Primary = Brushes.WhiteSmoke;
        internal static void Draw(WidgetWindow window, WeekSummary week, bool hourly, bool dollars)
        {
            Muted = (Brush)window.View.Resources["Muted"];
            Primary = (Brush)window.View.Resources["PrimaryText"];
            bool light = window.Preferences.Theme == "light";
            Palette[0] = light ? Color.FromRgb(41,124,97) : Color.FromRgb(166,232,203);
            Palette[1] = light ? Color.FromRgb(163,95,57) : Color.FromRgb(244,198,166);
            window.Get<TextBlock>("TrendTitle").Text = hourly ? "时段分布 · 一周累计" : "每日趋势";
            window.Get<Button>("TimeModeButton").Content = hourly ? "按时" : "按日";
            window.Get<Button>("MetricButton").Content = dollars ? "USD" : "Token";
            window.Get<TextBlock>("BarTitle").Text = dollars ? "每日 API 等值 · USD" : "每日用量 · tokens";
            DrawTrend(window.Get<Canvas>("TrendChart"), week, hourly, dollars);
            DrawBars(window.Get<Canvas>("Chart"), week, dollars);
            DrawDonut(window.Get<Canvas>("DonutChart"), window.Get<StackPanel>("ModelLegend"), week);
        }

        private static void Label(Canvas canvas, string text, double x, double y, double width = 45, TextAlignment align = TextAlignment.Left)
        {
            TextBlock label = new TextBlock { Text = text, FontSize = 9, Foreground = Muted, Width = width, TextAlignment = align };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label);
        }

        private static string Value(double value, bool dollars)
        {
            return dollars ? "$" + value.ToString(value < 10 ? "0.##" : "0", CultureInfo.InvariantCulture) : Format.Tokens((long)value);
        }

        private static double[] Series(ProviderSummary provider, bool hourly, bool dollars)
        {
            return dollars ? (hourly ? provider.HourlyCosts : provider.DailyCosts).Select(x => (double)x).ToArray() :
                (hourly ? provider.HourlyTokens : provider.DailyTokens).Select(x => (double)x).ToArray();
        }

        private static void DrawTrend(Canvas chart, WeekSummary week, bool hourly, bool dollars)
        {
            chart.Children.Clear();
            double width = Math.Max(120, chart.ActualWidth);
            double left = 38, right = width - 10, top = 8, bottom = 96;
            double[][] data = { Series(week.Codex, hourly, dollars), Series(week.Claude, hourly, dollars) };
            double max = Math.Max(1, data.SelectMany(x => x).Max());
            for (int i = 0; i <= 2; i++)
            {
                double y = top + (bottom - top) * i / 2;
                chart.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(22, 230, 240, 234)), StrokeThickness = 1 });
                Label(chart, Value(max * (2 - i) / 2, dollars), 0, y - 5, 33, TextAlignment.Right);
            }
            int count = data[0].Length;
            for (int provider = 0; provider < 2; provider++)
            {
                if ((week.Providers == "codex" && provider == 1) || (week.Providers == "claude" && provider == 0)) continue;
                PointCollection points = new PointCollection();
                for (int i = 0; i < count; i++) points.Add(new Point(left + (right - left) * i / (count - 1), bottom - (bottom - top) * data[provider][i] / max));
                Polyline line = new Polyline { Points = points, Stroke = new SolidColorBrush(Palette[provider]), StrokeThickness = 1.8,
                    StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
                chart.Children.Add(line);
                for (int i = 0; i < count; i++)
                {
                    string stamp = hourly ? i.ToString("00") + ":00–" + i.ToString("00") + ":59（整周累计）" : week.Start.AddDays(i).ToString("MM.dd");
                    Ellipse dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Palette[provider]),
                        ToolTip = stamp + " · " + (provider == 0 ? "Codex " : "Claude ") + Value(data[provider][i], dollars) + (dollars ? " API 等值" : " tokens") };
                    Canvas.SetLeft(dot, points[i].X - 3.5); Canvas.SetTop(dot, points[i].Y - 3.5); chart.Children.Add(dot);
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (hourly && i != 0 && i != 6 && i != 12 && i != 18 && i != 23) continue;
                string label = hourly ? i.ToString("00") + "时" : week.Start.AddDays(i).ToString("MM.dd");
                double x = left + (right - left) * i / (count - 1);
                Label(chart, label, x - 20, 106, 40, TextAlignment.Center);
            }
        }

        private static void DrawBars(Canvas chart, WeekSummary week, bool dollars)
        {
            chart.Children.Clear();
            double width = Math.Max(120, chart.ActualWidth);
            double[][] data = { Series(week.Codex, false, dollars), Series(week.Claude, false, dollars) };
            double max = Math.Max(1, data.SelectMany(x => x).Max());
            for (int day = 0; day < 7; day++)
            {
                double x = width * (day + .5) / 7 - 15;
                for (int provider = 0; provider < 2; provider++)
                {
                    if ((week.Providers == "codex" && provider == 1) || (week.Providers == "claude" && provider == 0)) continue;
                    double height = data[provider][day] == 0 ? 2 : Math.Max(3, data[provider][day] / max * 53);
                    Rectangle bar = new Rectangle { Width = 12, Height = height, RadiusX = 3, RadiusY = 3,
                        Fill = new SolidColorBrush(Palette[provider]), Opacity = data[provider][day] == 0 ? .16 : .85,
                        ToolTip = week.Start.AddDays(day).ToString("MM.dd") + " · " + (provider == 0 ? "Codex " : "Claude ") +
                            Value(data[provider][day], dollars) + (dollars ? " API 等值" : " tokens") };
                    Canvas.SetLeft(bar, x + provider * 17); Canvas.SetTop(bar, 53 - height); chart.Children.Add(bar);
                }
                Label(chart, week.Start.AddDays(day).ToString("ddd", CultureInfo.GetCultureInfo("zh-CN")), x - 6, 63, 42, TextAlignment.Center);
            }
        }

        private static Point Polar(double radius, double angle)
        {
            return new Point(45 + radius * Math.Cos(angle), 46 + radius * Math.Sin(angle));
        }

        private static void DrawDonut(Canvas chart, StackPanel legend, WeekSummary week)
        {
            chart.Children.Clear(); legend.Children.Clear();
            List<KeyValuePair<string, long>> all = week.ModelTokens.Where(x => x.Value > 0).OrderByDescending(x => x.Value).ToList();
            long total = all.Sum(x => x.Value);
            var displayed = all.Take(4).ToList();
            if (all.Count > 4) displayed.Add(new KeyValuePair<string, long>("其他模型", all.Skip(4).Sum(x => x.Value)));
            Ellipse track = new Ellipse { Width = 78, Height = 78, StrokeThickness = 11, Stroke = new SolidColorBrush(Color.FromArgb(22, 230, 240, 234)) };
            Canvas.SetLeft(track, 6); Canvas.SetTop(track, 7); chart.Children.Add(track);
            double angle = -Math.PI / 2;
            for (int i = 0; i < displayed.Count; i++)
            {
                var item = displayed[i];
                double fraction = item.Value / (double)total;
                double sweep = fraction * Math.PI * 2;
                Brush brush = new SolidColorBrush(Palette[i]);
                string tooltip = item.Key + " · " + Format.Tokens(item.Value) + " tokens · " + (fraction * 100).ToString("0.#") + "%";
                if (displayed.Count == 1)
                {
                    Ellipse ring = new Ellipse { Width = 78, Height = 78, Stroke = brush, StrokeThickness = 11, ToolTip = tooltip };
                    Canvas.SetLeft(ring, 6); Canvas.SetTop(ring, 7); chart.Children.Add(ring);
                }
                else
                {
                    double gap = Math.Min(.025, sweep / 4);
                    double begin = angle + gap, end = angle + sweep - gap;
                    bool large = end - begin > Math.PI;
                    StreamGeometry geometry = new StreamGeometry();
                    using (StreamGeometryContext context = geometry.Open())
                    {
                        context.BeginFigure(Polar(39, begin), true, true);
                        context.ArcTo(Polar(39, end), new Size(39, 39), 0, large, SweepDirection.Clockwise, true, false);
                        context.LineTo(Polar(28, end), true, false);
                        context.ArcTo(Polar(28, begin), new Size(28, 28), 0, large, SweepDirection.Counterclockwise, true, false);
                    }
                    chart.Children.Add(new Path { Data = geometry, Fill = brush, ToolTip = tooltip });
                }
                Grid row = new Grid { Margin = new Thickness(0, 0, 0, 10), ToolTip = tooltip };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
                Ellipse dot = new Ellipse { Width = 4, Height = 4, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(dot);
                string name = item.Key.Replace("claude-", "").Replace("gpt-", "");
                TextBlock label = new TextBlock { Text = name, FontSize = 9, Foreground = Primary, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(label, 1); row.Children.Add(label);
                TextBlock percentage = new TextBlock { Text = (fraction * 100).ToString("0") + "%", FontSize = 9, Foreground = Muted, TextAlignment = TextAlignment.Right };
                Grid.SetColumn(percentage, 2); row.Children.Add(percentage);
                legend.Children.Add(row);
                angle += sweep;
            }
            Label(chart, all.Count == 0 ? "—" : all.Count.ToString(), 20, 28, 50, TextAlignment.Center);
            Label(chart, all.Count == 0 ? "暂无数据" : "个模型", 20, 48, 50, TextAlignment.Center);
            if (all.Count == 0) legend.Children.Add(new TextBlock { Text = "等待用量记录", FontSize = 10, Foreground = Muted });
        }
    }
}
