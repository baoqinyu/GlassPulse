using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace GlassPulse
{
    internal sealed class SettingsWindow : Window
    {
        private readonly Grid view;
        private readonly Settings preferences;
        private readonly string dataDirectory;
        private readonly string pricePath;
        private readonly bool demo;
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal SettingsWindow(Settings settings, string dataDirectory, string pricePath, bool demo, string focusBudget = null)
        {
            preferences = settings;
            this.dataDirectory = dataDirectory;
            this.pricePath = pricePath;
            this.demo = demo;
            Title = "GlassPulse · 偏好设置";
            Width = 480; Height = 660;
            MinWidth = 460; MinHeight = 500;
            MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 40);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(28, 37, 44));
            ResizeMode = ResizeMode.CanResizeWithGrip;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlassPulse.Settings.xaml"))
                view = (Grid)XamlReader.Load(stream);
            Content = view;
            Theme.Apply(view, settings.Theme == "light");
            Background = (Brush)view.Resources["SettingsSurface"];
            Get<ComboBox>("ThemeInput").SelectedIndex = settings.Theme == "light" ? 1 : 0;
            Get<ComboBox>("ProvidersInput").SelectedIndex = settings.Providers == "codex" ? 1 : settings.Providers == "claude" ? 2 : 0;
            if (focusBudget != null) Loaded += delegate { var input = Get<TextBox>(focusBudget + "BudgetInput"); input.Focus(); input.SelectAll(); };
            if (!demo && !NativeGlass.HotkeyAvailable) Get<TextBlock>("HotkeyHint").Text = "快捷键不可用 · 可从托盘唤回";
            Get<TextBox>("CodexBudgetInput").Text = settings.CodexBudget.ToString(CultureInfo.InvariantCulture);
            Get<TextBox>("ClaudeBudgetInput").Text = settings.ClaudeBudget.ToString(CultureInfo.InvariantCulture);
            Get<TextBox>("RefreshInput").Text = settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture);
            Get<ComboBox>("WeekStartInput").SelectedIndex = settings.WeekStart == 1 ? 0 : 1;
            Get<Slider>("OpacityInput").Value = settings.GlassOpacity;
            Get<CheckBox>("TopmostInput").IsChecked = settings.AlwaysOnTop;
            Get<CheckBox>("MiniInput").IsChecked = settings.Mini;
            Get<TextBox>("CodexRootInput").Text = settings.CodexRoot;
            Get<TextBox>("ClaudeRootInput").Text = settings.ClaudeRoot;
            if (!demo)
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey))
                    Get<CheckBox>("StartupInput").IsChecked = key != null && key.GetValue("GlassPulse") != null;
            }
            try
            {
                PriceCatalog prices = PriceCatalog.Load(pricePath);
                Get<TextBlock>("PriceDate").Text = "内置单价核验：" + prices.VerifiedAt + " · 修改后自动重算";
            }
            catch (Exception) { Get<TextBlock>("PriceDate").Text = "价格表需要检查，请使用有效的 JSON。"; }
            Get<Button>("SaveButton").Click += Save;
            Get<Button>("CancelButton").Click += delegate { Close(); };
            Get<Button>("PricingButton").Click += delegate { OpenFile(pricePath, true); };
            Get<Button>("DataButton").Click += delegate { OpenFile(dataDirectory, false); };
        }

        internal T Get<T>(string name) where T : FrameworkElement { return (T)view.FindName(name); }

        private void OpenFile(string path, bool edit)
        {
            if (demo) return;
            try
            {
                Process.Start(edit ? new ProcessStartInfo("notepad.exe", "\"" + path + "\"") :
                    new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception) { Get<TextBlock>("ErrorText").Text = "无法打开，请在配置目录中手动编辑 pricing.json。"; }
        }

        private void Save(object sender, RoutedEventArgs args)
        {
            decimal codex, claude;
            int seconds;
            if (!decimal.TryParse(Get<TextBox>("CodexBudgetInput").Text, NumberStyles.Number, CultureInfo.InvariantCulture, out codex) ||
                !decimal.TryParse(Get<TextBox>("ClaudeBudgetInput").Text, NumberStyles.Number, CultureInfo.InvariantCulture, out claude) ||
                codex < 0 || claude < 0 || codex > 100000000 || claude > 100000000)
            {
                Get<TextBlock>("ErrorText").Text = "请输入 0 至 100,000,000 之间的周预算，0 表示不设置上限。";
                return;
            }
            if (!int.TryParse(Get<TextBox>("RefreshInput").Text, out seconds) || seconds < 1 || seconds > 60)
            {
                Get<TextBlock>("ErrorText").Text = "刷新间隔请输入 1 至 60 秒。";
                return;
            }
            string codexRoot = Get<TextBox>("CodexRootInput").Text.Trim();
            string claudeRoot = Get<TextBox>("ClaudeRootInput").Text.Trim();
            if (!Path.IsPathRooted(codexRoot) || !Path.IsPathRooted(claudeRoot))
            {
                Get<TextBlock>("ErrorText").Text = "请填写日志主目录的绝对路径。";
                return;
            }
            try
            {
                if (!demo)
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
                    {
                        if (Get<CheckBox>("StartupInput").IsChecked == true)
                            key.SetValue("GlassPulse", "\"" + Assembly.GetExecutingAssembly().Location + "\" --data-dir \"" + dataDirectory + "\"");
                        else key.DeleteValue("GlassPulse", false);
                    }
                }
                preferences.Theme = Get<ComboBox>("ThemeInput").SelectedIndex == 1 ? "light" : "dark";
                preferences.Providers = Get<ComboBox>("ProvidersInput").SelectedIndex == 1 ? "codex" : Get<ComboBox>("ProvidersInput").SelectedIndex == 2 ? "claude" : "all";
                preferences.CodexBudget = codex; preferences.ClaudeBudget = claude;
                preferences.RefreshSeconds = seconds;
                preferences.WeekStart = Get<ComboBox>("WeekStartInput").SelectedIndex == 0 ? 1 : 0;
                preferences.GlassOpacity = Get<Slider>("OpacityInput").Value;
                preferences.AlwaysOnTop = Get<CheckBox>("TopmostInput").IsChecked == true;
                preferences.Mini = Get<CheckBox>("MiniInput").IsChecked == true;
                preferences.CodexRoot = codexRoot; preferences.ClaudeRoot = claudeRoot;
                if (!demo) preferences.Save(dataDirectory);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                if (!(ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)) throw;
                Get<TextBlock>("ErrorText").Text = "无法保存配置，请检查目录权限或登录启动设置权限。";
            }
        }
    }
}
