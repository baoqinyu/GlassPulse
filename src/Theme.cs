using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GlassPulse
{
    internal static class Theme
    {
        internal static void Apply(FrameworkElement root, bool light)
        {
            if (SystemParameters.HighContrast) light = false;
            var colors = new Dictionary<string, string> {
                { "PrimaryText", light ? "#20373A" : "#F2F6F5" },
                { "Muted", light ? "#526C70" : "#A9B6B6" },
                { "SecondaryText", light ? "#425F62" : "#AFC0BF" },
                { "CodexText", light ? "#226A56" : "#D9FAEB" },
                { "ClaudeText", light ? "#8B4D32" : "#F7DDCB" },
                { "MintText", light ? "#2D715B" : "#BCE7D8" },
                { "PeachText", light ? "#9B5F3E" : "#F1BDA0" },
                { "MeterTrack", light ? "#203A6863" : "#18FFFFFF" },
                { "HoverSurface", light ? "#243D7167" : "#18FFFFFF" },
                { "DashboardCard", light ? "#58FFFFFF" : "#0BFFFFFF" },
                { "Divider", light ? "#244A6C68" : "#18FFFFFF" },
                { "SettingsSurface", light ? "#EDF4F3" : "#1C252C" },
                { "InputSurface", light ? "#E4EEEB" : "#29343C" },
                { "InputBorder", light ? "#9CB6AE" : "#4E646B" },
                { "SwitchOff", light ? "#B4C5C0" : "#425355" }
            };
            foreach (var pair in colors) root.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
            root.Resources["Mint"] = new LinearGradientBrush(light ? Color.FromRgb(45,137,111) : Color.FromRgb(85,187,166), light ? Color.FromRgb(111,188,155) : Color.FromRgb(185,249,220), 0);
            root.Resources["Peach"] = new LinearGradientBrush(light ? Color.FromRgb(164,96,60) : Color.FromRgb(182,128,106), light ? Color.FromRgb(210,157,115) : Color.FromRgb(244,198,166), 0);
            var surface = root.FindName("GlassSurface") as Border;
            if (surface != null)
            {
                var brush = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(1,1) };
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(light ? "#DAFFFFFF" : "#B5798F9C"),0));
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(light ? "#C4D8E9E3" : "#BA263846"),.4));
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(light ? "#CBE5DBEB" : "#B252485B"),1));
                surface.Background = brush;
            }
        }
    }
}
