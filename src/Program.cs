using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;

namespace GlassPulse
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            string previewDirectory = null;
            string reportPath = null;
            bool demo = false;
            bool smoke = false;
            int quitAfter = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data-dir" && i + 1 < args.Length) dataDirectory = Path.GetFullPath(args[++i]);
                else if (args[i] == "--preview" && i + 1 < args.Length) { previewDirectory = Path.GetFullPath(args[++i]); demo = true; }
                else if (args[i] == "--report" && i + 1 < args.Length) reportPath = Path.GetFullPath(args[++i]);
                else if (args[i] == "--demo") demo = true;
                else if (args[i] == "--smoke") { smoke = true; demo = true; }
                else if (args[i] == "--quit-after" && i + 1 < args.Length) quitAfter = Math.Max(2, Math.Min(300, int.Parse(args[++i])));
            }
            try
            {
                Directory.CreateDirectory(dataDirectory);
                string pricePath = Path.Combine(dataDirectory, "pricing.json");
                if (!File.Exists(pricePath))
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlassPulse.pricing.json"))
                    using (FileStream output = File.Create(pricePath)) stream.CopyTo(output);
                }
                Settings preferences = Settings.Load(dataDirectory);
                if (reportPath != null)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    UsageEngine engine = new UsageEngine(preferences.CodexRoot, preferences.ClaudeRoot, pricePath);
                    WeekSummary result = engine.Refresh(DateTimeOffset.Now, TimeZoneInfo.Local, preferences.WeekStart, 0);
                    long firstRead = engine.TotalBytesRead;
                    WeekSummary second = engine.Refresh(DateTimeOffset.Now, TimeZoneInfo.Local, preferences.WeekStart, 0);
                    var report = new { WeekStart = result.Start.ToString("yyyy-MM-dd"),
                        Codex = result.Codex, Claude = result.Claude, Files = result.Files, ParseErrors = result.Errors,
                        InitialBytes = firstRead, UnchangedScanBytes = engine.TotalBytesRead - firstRead,
                        StableTotals = result.Codex.Tokens == second.Codex.Tokens && result.Claude.Tokens == second.Claude.Tokens,
                        ScanMilliseconds = watch.ElapsedMilliseconds, PriceWarning = result.PriceWarning,
                        SourceWarning = result.SourceWarning };
                    File.WriteAllText(reportPath, Json.Serializer().Serialize(report), new UTF8Encoding(false));
                    return 0;
                }
                bool created;
                string identity = "Local\\GlassPulse-" + StableHash(dataDirectory.ToLowerInvariant());
                using (Mutex mutex = new Mutex(true, identity, out created))
                {
                    if (!created && !demo)
                    {
                        MessageBox.Show("GlassPulse 已在运行。请双击托盘图标，或按 Ctrl+Alt+G 唤回。", "GlassPulse");
                        return 0;
                    }
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    WidgetWindow widget = new WidgetWindow(preferences, dataDirectory, pricePath, demo);
                    app.MainWindow = widget;
                    if (quitAfter > 0)
                    {
                        var stopTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(quitAfter) };
                        stopTimer.Tick += delegate { stopTimer.Stop(); widget.Quit(); };
                        stopTimer.Start();
                    }
                    if (previewDirectory != null)
                    {
                        widget.Loaded += delegate { widget.Dispatcher.BeginInvoke((Action)delegate {
                            try { Preview.Write(widget, previewDirectory, pricePath, dataDirectory); }
                            catch (Exception ex) { File.WriteAllText(Path.Combine(dataDirectory, "preview-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                            finally { widget.Quit(); }
                        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); };
                    }
                    if (smoke)
                    {
                        widget.Loaded += delegate {
                            NativeGlass.Attach(widget, widget.ToggleVisibility);
                            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                            timer.Tick += delegate {
                                timer.Stop();
                                try
                                {
                                    widget.Get<System.Windows.Controls.Button>("DetailsButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    widget.Preferences.Mini = true; widget.ApplyAppearance();
                                    widget.Preferences.Mini = false; widget.ApplyAppearance();
                                    widget.Get<System.Windows.Controls.Button>("TimeModeButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    widget.Get<System.Windows.Controls.Button>("MetricButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    widget.UpdateLayout(); widget.RedrawDashboard();
                                    if (widget.Get<System.Windows.Controls.Canvas>("TrendChart").Children.Count < 10 ||
                                        widget.Get<System.Windows.Controls.StackPanel>("ModelLegend").Children.Count != 4)
                                        throw new InvalidOperationException("Dashboard chart rendering failed");
                                    string csvPath = Path.Combine(dataDirectory, "smoke-export.csv");
                                    WidgetWindow.WriteCsv(widget.Current, csvPath);
                                    if (File.ReadAllLines(csvPath).Length != 15) throw new InvalidOperationException("CSV row count mismatch");
                                    Settings fixtureSettings = new Settings();
                                    SettingsWindow settingsDialog = new SettingsWindow(fixtureSettings, dataDirectory, pricePath, true) { Owner = widget };
                                    settingsDialog.Loaded += delegate { settingsDialog.Dispatcher.BeginInvoke((Action)delegate {
                                        settingsDialog.Get<System.Windows.Controls.TextBox>("CodexBudgetInput").Text = "0";
                                        settingsDialog.Get<System.Windows.Controls.Button>("SaveButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                        if (settingsDialog.Get<System.Windows.Controls.TextBlock>("ErrorText").Text.Length == 0)
                                            throw new InvalidOperationException("Budget validation did not reject zero");
                                        settingsDialog.Get<System.Windows.Controls.TextBox>("CodexBudgetInput").Text = "240";
                                        settingsDialog.Get<System.Windows.Controls.Button>("SaveButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    }); };
                                    if (settingsDialog.ShowDialog() != true || fixtureSettings.CodexBudget != 240)
                                        throw new InvalidOperationException("Settings save failed");
                                    widget.Hide(); widget.ToggleVisibility();
                                    NativeGlass.ApplyBackdrop(widget, true);
                                    NativeGlass.ApplyBackdrop(widget, false);
                                    NativeGlass.SetClickThrough(widget, true);
                                    NativeGlass.SetClickThrough(widget, false);
                                    File.WriteAllText(Path.Combine(dataDirectory, "ui-smoke.json"),
                                        Json.Serializer().Serialize(new { Passed = true, Width = widget.ActualWidth,
                                            Height = widget.ActualHeight, Expanded = widget.Preferences.Expanded,
                                            Visible = widget.IsVisible, HotkeyAvailable = NativeGlass.HotkeyAvailable,
                                            ChartModes = true, CsvRows = 15, SettingsValidation = true }));
                                }
                                catch (Exception ex) { File.WriteAllText(Path.Combine(dataDirectory, "smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                                finally { widget.Quit(); }
                            };
                            timer.Start();
                        };
                    }
                    app.Run(widget);
                    if (created) mutex.ReleaseMutex();
                    return Environment.ExitCode;
                }
            }
            catch (Exception ex)
            {
                string detail = ex.GetType().Name + ": " + ex.Message;
                try { File.WriteAllText(Path.Combine(dataDirectory, "crash.log"), detail, new UTF8Encoding(false)); }
                catch (Exception) { }
                if (previewDirectory == null && !smoke && reportPath == null)
                    MessageBox.Show("GlassPulse 无法启动。请检查配置目录中的 settings.json 和 pricing.json。\n\n" + detail,
                        "GlassPulse", MessageBoxButton.OK, MessageBoxImage.Information);
                return 1;
            }
        }

        private static string StableHash(string value)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 20);
        }
    }
}
