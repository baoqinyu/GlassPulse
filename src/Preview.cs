using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassPulse
{
    internal static class Preview
    {
        internal static BitmapSource Render(FrameworkElement element)
        {
            element.UpdateLayout();
            double width = element.ActualWidth;
            double height = element.ActualHeight;
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(element);
            return bitmap;
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }

        internal static void Write(WidgetWindow widget, string directory, string pricePath, string dataDirectory)
        {
            Directory.CreateDirectory(directory);
            widget.Preferences.Mini = false; widget.Preferences.Expanded = false;
            widget.ApplyAppearance();
            BitmapSource compact = Render(widget.View);
            Save(compact, Path.Combine(directory, "compact.png"));
            widget.Preferences.Expanded = true; widget.ApplyAppearance();
            BitmapSource expanded = Render(widget.View);
            Save(expanded, Path.Combine(directory, "expanded.png"));
            widget.Preferences.Mini = true; widget.ApplyAppearance();
            BitmapSource mini = Render(widget.View);
            Save(mini, Path.Combine(directory, "mini.png"));

            DrawingVisual stage = new DrawingVisual();
            using (DrawingContext dc = stage.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(19, 33, 41), Color.FromRgb(34, 30, 43), 30), null, new Rect(0, 0, 1500, 1040));
                var mintGlow = new RadialGradientBrush(Color.FromArgb(70, 99, 189, 153), Colors.Transparent);
                var peachGlow = new RadialGradientBrush(Color.FromArgb(55, 196, 137, 104), Colors.Transparent);
                dc.DrawEllipse(mintGlow, null, new Point(240, 340), 360, 310);
                dc.DrawEllipse(peachGlow, null, new Point(1160, 580), 440, 430);
                DrawText(dc, "G L A S S P U L S E", 15, Color.FromRgb(187, 218, 204), 74, 60);
                DrawText(dc, "你的编码周，一眼即见。", 31, Color.FromRgb(234, 241, 236), 74, 99);
                DrawText(dc, "Codex + Claude Code   /   每周用量 · API 等值 · 本地实时统计", 13, Color.FromRgb(156, 180, 178), 76, 151);
                double compactWidth = compact.PixelWidth / 2d;
                double compactHeight = compact.PixelHeight / 2d;
                dc.DrawImage(compact, new Rect(58, 260, compactWidth, compactHeight));
                double detailHeight = expanded.PixelHeight / 2d;
                dc.DrawImage(expanded, new Rect(630, 218, expanded.PixelWidth / 2d, detailHeight));
                dc.DrawImage(mini, new Rect(58, 565, mini.PixelWidth / 2d, mini.PixelHeight / 2d));
                DrawText(dc, "双击展开 dashboard · 简洁与细节，随时切换", 12, Color.FromRgb(163, 186, 180), 83, 523);
                DrawText(dc, "极简横条", 11, Color.FromRgb(163, 186, 180), 81, 663);
                DrawText(dc, "WINDOWS 10 / 11     ·     无浏览器内核     ·     MIT 开源", 11, Color.FromRgb(163, 182, 179), 76, 973);
                DrawText(dc, "界面预览 · 演示数据", 10, Color.FromRgb(143, 159, 158), 1280, 975);
            }
            RenderTargetBitmap hero = new RenderTargetBitmap(3000, 2080, 192, 192, PixelFormats.Pbgra32);
            hero.Render(stage);
            Save(hero, Path.Combine(directory, "preview.png"));

            SettingsWindow settings = new SettingsWindow(new Settings { CodexRoot = "[demo directory]", ClaudeRoot = "[demo directory]" }, dataDirectory, pricePath, true) { ShowInTaskbar = false };
            settings.Show(); settings.UpdateLayout();
            Save(Render((FrameworkElement)settings.Content), Path.Combine(directory, "settings.png"));
            settings.Close();
            File.WriteAllText(Path.Combine(dataDirectory, "preview-result.json"), Json.Serializer().Serialize(new {
                CompactWidth = compact.PixelWidth / 2d, CompactHeight = compact.PixelHeight / 2d,
                ExpandedHeight = expanded.PixelHeight / 2d, MiniHeight = mini.PixelHeight / 2d,
                DemoOnly = true, Passed = true
            }));
        }

        private static void DrawText(DrawingContext dc, string value, double size, Color color, double x, double y)
        {
            FormattedText text = new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, new SolidColorBrush(color), 2.0);
            dc.DrawText(text, new Point(x, y));
        }
    }
}
