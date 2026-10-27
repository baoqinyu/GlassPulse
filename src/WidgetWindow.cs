using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GlassPulse
{
    internal sealed class WidgetWindow : Window
    {
        internal readonly Grid View;
        internal readonly Settings Preferences;
        internal WeekSummary Current;
        private readonly string dataDirectory;
        private readonly string pricePath;
        private UsageEngine engine;
        private DispatcherTimer timer;
        private System.Windows.Forms.NotifyIcon tray;
        private System.Drawing.Icon trayIcon;
        private bool refreshing;
        private bool exiting;
        private readonly bool demo;
        private int weekOffset;
        private string fatalError = "";
        private bool hourly = true;
        private bool chartDollars;
        private string chartSignature;
        private WeekSummary loadedSummary;
        private bool updatingWeekSelector;

        internal WidgetWindow(Settings preferences, string dataDirectory, string pricePath, bool demo)
        {
            this.Preferences = preferences;
            this.dataDirectory = dataDirectory;
            this.pricePath = pricePath;
            this.demo = demo;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlassPulse.Widget.xaml"))
                View = (Grid)XamlReader.Load(stream);
            Content = View;
            Theme.Apply(View, preferences.Theme == "light");
            Title = "GlassPulse";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Topmost = preferences.AlwaysOnTop && !demo;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = double.IsNaN(preferences.Left) || double.IsInfinity(preferences.Left) ? -10000 : preferences.Left;
            Top = double.IsNaN(preferences.Top) || double.IsInfinity(preferences.Top) ? -10000 : preferences.Top;
            Get<Button>("PreviousButton").Click += delegate { ChangeWeek(-1); };
            Get<Button>("NextButton").Click += delegate { ChangeWeek(1); };
            Get<Button>("ThemeButton").Click += delegate { Preferences.Theme = Preferences.Theme == "light" ? "dark" : "light"; ApplyAppearance(); Save(); };
            Get<Button>("CodexBudget").Click += delegate { OpenSettings("Codex"); };
            Get<Button>("ClaudeBudget").Click += delegate { OpenSettings("Claude"); };
            Get<ComboBox>("WeekSelector").SelectionChanged += delegate {
                if (updatingWeekSelector) return;
                var item = Get<ComboBox>("WeekSelector").SelectedItem as ComboBoxItem;
                if (item == null) return;
                int target = (int)item.Tag;
                if (target == weekOffset) return;
                weekOffset = target;
                if (demo) ChangeWeek(0); else Refresh();
            };
            Get<Button>("SettingsButton").Click += delegate { OpenSettings(); };
            Get<Button>("HideButton").Click += delegate { Hide(); };
            Get<Button>("DetailsButton").Click += delegate { Preferences.Expanded = !Preferences.Expanded; ApplyAppearance(); Save(); };
            Get<Button>("ExportButton").Click += delegate { Export(); };
            Get<Button>("TimeModeButton").Click += delegate { hourly = !hourly; RedrawDashboard(); };
            Get<Button>("MetricButton").Click += delegate { chartDollars = !chartDollars; RedrawDashboard(); };
            View.MouseLeftButtonDown += Drag;
            View.ContextMenu = BuildMenu();
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
                if (e.Key == Key.F5) { Refresh(); e.Handled = true; }
            };
            SourceInitialized += delegate {
                if (!demo) { NativeGlass.Attach(this, ToggleVisibility); NativeGlass.ApplyBackdrop(this, Preferences.Expanded && !Preferences.Mini, Preferences.Theme == "light"); }
            };
            Loaded += delegate {
                if (Left <= -9999 || Top <= -9999)
                {
                    Left = SystemParameters.WorkArea.Right - ActualWidth - 22;
                    Top = SystemParameters.WorkArea.Top + 34;
                }
                if (!demo)
                {
                    NativeGlass.ClampToScreens(this, false);
                    CreateTray();
                    if (Preferences.ClickThrough) NativeGlass.SetClickThrough(this, true);
                    StartTimer();
                    Refresh();
                }
                else SetSummary(DemoSummary());
            };
            SizeChanged += delegate { if (IsLoaded && !demo) NativeGlass.ClampToScreens(this, false); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (!exiting && !demo) { e.Cancel = true; Hide(); }
            };
            Closed += delegate {
                if (timer != null) timer.Stop();
                if (tray != null) tray.Dispose();
                if (trayIcon != null) trayIcon.Dispose();
            };
            ApplyAppearance();
        }

        internal T Get<T>(string name) where T : FrameworkElement { return (T)View.FindName(name); }

        private ContextMenu BuildMenu()
        {
            ContextMenu menu = new ContextMenu { Background = (Brush)View.Resources["SettingsSurface"], Foreground = (Brush)View.Resources["PrimaryText"] };
            AddMenu(menu, "显示 / 隐藏    Ctrl+Alt+G", ToggleVisibility);
            AddMenu(menu, "显示 Codex + Claude", delegate { SelectProviders("all"); });
            AddMenu(menu, "仅显示 Codex", delegate { SelectProviders("codex"); });
            AddMenu(menu, "仅显示 Claude", delegate { SelectProviders("claude"); });
            AddMenu(menu, "切换浅色 / 深色", delegate { Preferences.Theme = Preferences.Theme == "light" ? "dark" : "light"; ApplyAppearance(); Save(); });
            AddMenu(menu, "极简横条 / 完整视图", delegate { Preferences.Mini = !Preferences.Mini; ApplyAppearance(); Save(); });
            AddMenu(menu, "保持置顶", delegate { Preferences.AlwaysOnTop = !Preferences.AlwaysOnTop; ApplyAppearance(); Save(); });
            AddMenu(menu, "鼠标穿透 / 恢复交互", ToggleClickThrough);
            menu.Items.Add(new Separator());
            AddMenu(menu, "返回本周", delegate { weekOffset = 0; Refresh(); });
            AddMenu(menu, "刷新    F5", Refresh);
            AddMenu(menu, "偏好设置…", OpenSettings);
            menu.Items.Add(new Separator());
            AddMenu(menu, "退出 GlassPulse", Quit);
            return menu;
        }

        private static void AddMenu(ContextMenu menu, string label, Action action)
        {
            MenuItem item = new MenuItem { Header = label };
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        private void CreateTray()
        {
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            tray = new System.Windows.Forms.NotifyIcon { Text = "GlassPulse · 本地每周用量", Icon = trayIcon, Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏  (Ctrl+Alt+G)", null, delegate { Dispatcher.Invoke((Action)ToggleVisibility); });
            menu.Items.Add("恢复鼠标交互", null, delegate { Dispatcher.Invoke((Action)delegate {
                Preferences.ClickThrough = false;
                NativeGlass.SetClickThrough(this, false);
                Show(); Activate(); Save();
            }); });
            menu.Items.Add("极简横条 / 完整视图", null, delegate { Dispatcher.Invoke((Action)delegate {
                Preferences.Mini = !Preferences.Mini; ApplyAppearance(); Save();
            }); });
            menu.Items.Add("偏好设置…", null, delegate { Dispatcher.Invoke((Action)OpenSettings); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Dispatcher.Invoke((Action)Quit); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke((Action)ToggleVisibility); };
        }

        private void SelectProviders(string providers)
        {
            Preferences.Providers = providers;
            ApplyAppearance(); Save();
        }

        private void ToggleClickThrough()
        {
            Preferences.ClickThrough = !Preferences.ClickThrough;
            NativeGlass.SetClickThrough(this, Preferences.ClickThrough);
            Save();
        }

        internal void ToggleVisibility()
        {
            if (!IsVisible || Preferences.ClickThrough)
            {
                Preferences.ClickThrough = false;
                if (!demo) NativeGlass.SetClickThrough(this, false);
                Show(); Activate();
            }
            else Hide();
        }

        private void Drag(object sender, MouseButtonEventArgs e)
        {
            DependencyObject origin = e.OriginalSource as DependencyObject;
            for (DependencyObject item = origin; item != null && item != View; item = VisualTreeHelper.GetParent(item))
                if (item is Button) return;
            if (e.ClickCount == 2)
            {
                if (Preferences.Mini) { Preferences.Mini = false; Preferences.Expanded = true; }
                else Preferences.Expanded = !Preferences.Expanded;
                ApplyAppearance(); Save();
                e.Handled = true;
                return;
            }
            try { DragMove(); }
            catch (InvalidOperationException) { return; }
            if (!demo) NativeGlass.ClampToScreens(this, true);
            Preferences.Left = Left;
            Preferences.Top = Top;
            Save();
        }

        internal void ApplyAppearance()
        {
            Topmost = Preferences.AlwaysOnTop && !demo;
            Theme.Apply(View, Preferences.Theme == "light");
            View.ContextMenu = BuildMenu();
            ApplyLayout();
        }

        internal void ApplyLayout()
        {
            bool opening = Preferences.Expanded && Get<StackPanel>("DetailPanel").Visibility != Visibility.Visible;
            bool codexVisible = Preferences.Providers != "claude";
            bool claudeVisible = Preferences.Providers != "codex";
            View.Width = Preferences.Mini && Preferences.Providers != "all" ? 288 : Preferences.Expanded && !Preferences.Mini ? 800 : 508;
            Get<StackPanel>("CodexLegend").Visibility = codexVisible ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("ClaudeLegend").Visibility = claudeVisible ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("CodexRow").Visibility = codexVisible ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("ClaudeRow").Visibility = claudeVisible ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("MiniCodexRow").Visibility = codexVisible ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("MiniClaudeRow").Visibility = claudeVisible ? Visibility.Visible : Visibility.Collapsed;
            Get<Border>("MiniDivider").Visibility = codexVisible && claudeVisible ? Visibility.Visible : Visibility.Collapsed;
            var mini = Get<Grid>("MiniView");
            mini.ColumnDefinitions[0].Width = codexVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            mini.ColumnDefinitions[1].Width = new GridLength(codexVisible && claudeVisible ? 25 : 0);
            mini.ColumnDefinitions[2].Width = claudeVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Get<TextBlock>("ProviderLegend").Text = Preferences.Providers == "codex" ? "Codex" : Preferences.Providers == "claude" ? "Claude" : "Codex / Claude";
            if (!demo) NativeGlass.ApplyBackdrop(this, Preferences.Expanded && !Preferences.Mini, Preferences.Theme == "light");
            Get<ScrollViewer>("DashboardScroll").MaxHeight = demo ? double.PositiveInfinity : Math.Max(100, SystemParameters.WorkArea.Height - 280);
            Get<StackPanel>("FullView").Visibility = Preferences.Mini ? Visibility.Collapsed : Visibility.Visible;
            Get<Grid>("MiniView").Visibility = Preferences.Mini ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("DetailPanel").Visibility = Preferences.Expanded ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("DetailsButton").Content = Preferences.Expanded ? "收起面板  ⌃" : "展开面板  ⌄";
            Get<Border>("GlassSurface").Opacity = SystemParameters.HighContrast ? 1 : Preferences.GlassOpacity;
            if (SystemParameters.HighContrast) Get<Border>("GlassSurface").Background = Brushes.Black;
            UpdateLayout();
            chartSignature = null;
            if (loadedSummary != null) SetSummary(loadedSummary);
            if (opening && !demo && IsLoaded && SystemParameters.ClientAreaAnimation)
            {
                StackPanel panel = Get<StackPanel>("DetailPanel");
                TranslateTransform movement = new TranslateTransform();
                panel.RenderTransform = movement;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                movement.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
                panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            }
        }

        private void Save()
        {
            if (demo) return;
            try { Preferences.Save(dataDirectory); }
            catch (IOException) { Get<TextBlock>("StatusText").Text = "设置保存失败：请检查目录权限"; }
            catch (UnauthorizedAccessException) { Get<TextBlock>("StatusText").Text = "设置保存失败：目录不可写"; }
        }

        private void StartTimer()
        {
            if (timer != null) timer.Stop();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Preferences.RefreshSeconds) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
        }

        private void ChangeWeek(int direction)
        {
            weekOffset = Math.Max(EarliestWeekOffset(), Math.Min(0, weekOffset + direction));
            if (demo)
            {
                WeekSummary value = DemoSummary();
                value.Start = value.Start.AddDays(7 * weekOffset);
                value.End = value.Start.AddDays(7);
                SetSummary(value);
            }
            else Refresh();
        }

        private int EarliestWeekOffset()
        {
            DateTime earliest;
            if (Current == null || !DateTime.TryParse(Current.EarliestDate(), out earliest)) return 0;
            DateTime current = demo ? new DateTime(2026, 10, 26) : WeekClock.StartOfWeek(DateTime.Now, Preferences.WeekStart);
            return Math.Min(0, (WeekClock.StartOfWeek(earliest, Preferences.WeekStart) - current).Days / 7);
        }

        private void UpdateWeekSelector()
        {
            var selector = Get<ComboBox>("WeekSelector");
            int earliest = EarliestWeekOffset();
            updatingWeekSelector = true;
            try
            {
                DateTime current = demo ? new DateTime(2026, 10, 26) : WeekClock.StartOfWeek(DateTime.Now, Preferences.WeekStart);
                if (selector.Items.Count != 1 - earliest || selector.Tag == null || (DateTime)selector.Tag != current)
                {
                    selector.Items.Clear(); selector.Tag = current;
                    for (int offset = 0; offset >= earliest; offset--)
                    {
                        DateTime start = current.AddDays(offset * 7);
                        selector.Items.Add(new ComboBoxItem { Content = WeekClock.RangeLabel(start), ToolTip = start.ToString("yyyy-MM-dd") + " – " + start.AddDays(6).ToString("yyyy-MM-dd"), Tag = offset });
                    }
                }
                selector.SelectedIndex = Math.Max(0, Math.Min(selector.Items.Count - 1, -weekOffset));
                selector.ToolTip = "选择所有可用历史周 · 最早记录 " + (Current.EarliestDate() ?? "暂无");
            }
            finally { updatingWeekSelector = false; }
        }

        internal async void Refresh()
        {
            if (demo || refreshing || exiting) return;
            refreshing = true;
            int selected = weekOffset;
            int start = Preferences.WeekStart;
            try
            {
                if (engine == null) engine = new UsageEngine(Preferences.CodexRoot, Preferences.ClaudeRoot, pricePath);
                UsageEngine reader = engine;
                WeekSummary summary = await Task.Run(() => reader.Refresh(DateTimeOffset.Now, TimeZoneInfo.Local, start, selected));
                if (exiting) return;
                fatalError = "";
                if (selected == weekOffset) SetSummary(summary);
            }
            catch (Exception ex)
            {
                // Surface the failure while retaining explicitly stale last-known numbers.
                fatalError = "读取失败，请检查日志目录和价格表";
                Get<TextBlock>("StatusText").Text = fatalError;
                Get<Ellipse>("LiveDot").Fill = new SolidColorBrush(Color.FromRgb(237, 168, 134));
                Get<TextBlock>("StatusText").ToolTip = ex.GetType().Name;
                if (Current != null) Get<TextBlock>("WeekBadge").Text = "数据过期";
            }
            finally
            {
                refreshing = false;
                if (selected != weekOffset && !exiting) Refresh();
            }
        }

        internal void SetSummary(WeekSummary summary)
        {
            loadedSummary = summary;
            summary = summary.SelectProviders(Preferences.Providers);
            Current = summary;
            if (weekOffset < EarliestWeekOffset())
            {
                weekOffset = EarliestWeekOffset();
                if (demo) ChangeWeek(0); else { Get<TextBlock>("StatusText").Text = "正在加载所选工具的历史"; Refresh(); }
                return;
            }
            UpdateWeekSelector();
            Get<TextBlock>("WeekBadge").Text = summary.Demo ? "演示" : weekOffset == 0 ? "本周" : "历史";
            Get<Button>("NextButton").IsEnabled = weekOffset < 0;
            Get<Button>("PreviousButton").IsEnabled = weekOffset > EarliestWeekOffset();
            UpdateProvider(summary.Codex, "Codex", Preferences.CodexBudget);
            UpdateProvider(summary.Claude, "Claude", Preferences.ClaudeBudget);
            long unknown = summary.Codex.UnpricedTokens + summary.Claude.UnpricedTokens;
            string status = summary.Demo ? "演示数据 · 2 秒刷新" : summary.Errors > 0 ? "部分日志未能读取" :
                !string.IsNullOrEmpty(summary.PriceWarning) ? "价格表需要检查" : unknown > 0 ? "部分模型未定价" :
                summary.Files == 0 ? "等待本地日志" : !string.IsNullOrEmpty(summary.SourceWarning) ? "部分数据源未连接" :
                "本地 · " + Preferences.RefreshSeconds + " 秒刷新";
            Get<TextBlock>("StatusText").Text = status;
            Get<TextBlock>("StatusText").ToolTip = string.Format("已发现 {0} 份日志 · 更新于 {1:HH:mm:ss} · {2} 个读取问题", summary.Files, summary.RefreshedAt.ToLocalTime(), summary.Errors);
            Get<Ellipse>("LiveDot").Fill = new SolidColorBrush(unknown > 0 || summary.Errors > 0 ? Color.FromRgb(237, 190, 144) : Color.FromRgb(166, 232, 203));
            Get<TextBlock>("TotalTokens").Text = Format.Tokens(summary.Codex.Tokens + summary.Claude.Tokens);
            Get<TextBlock>("TotalCost").Text = unknown > 0 && unknown == summary.Codex.Tokens + summary.Claude.Tokens ? "待定价" : (unknown > 0 ? "≥ " : "") + Format.Money(summary.Codex.Cost + summary.Claude.Cost);
            long prompt = summary.Codex.Input + summary.Claude.Input + summary.Codex.CacheRead + summary.Claude.CacheRead + summary.Codex.CacheWrite + summary.Claude.CacheWrite;
            Get<TextBlock>("CacheRatio").Text = (prompt == 0 ? 0 : 100d * (summary.Codex.CacheRead + summary.Claude.CacheRead) / prompt).ToString("0.#") + "%";
            Get<TextBlock>("RequestCount").Text = (summary.Codex.Requests + summary.Claude.Requests).ToString("N0");
            Get<TextBlock>("InputDetail").Text = Format.Tokens(summary.Codex.Input + summary.Claude.Input);
            Get<TextBlock>("CacheDetail").Text = Format.Tokens(summary.Codex.CacheRead + summary.Claude.CacheRead);
            Get<TextBlock>("WriteDetail").Text = Format.Tokens(summary.Codex.CacheWrite + summary.Claude.CacheWrite);
            Get<TextBlock>("OutputDetail").Text = Format.Tokens(summary.Codex.Output + summary.Claude.Output);
            Get<TextBlock>("HistoryInfo").Text = "可用记录自：" +
                (Preferences.Providers != "claude" ? "Codex " + (summary.Codex.FirstDate ?? "暂无") : "") +
                (Preferences.Providers == "all" ? "  ·  " : "") +
                (Preferences.Providers != "codex" ? "Claude " + (summary.Claude.FirstDate ?? "暂无") : "");
            Get<TextBlock>("ResetText").Text = "下一周 " + summary.End.ToString("MM.dd") + " 00:00 · 本地时间";
            string[] missing = summary.Codex.UnknownModels.Keys.Concat(summary.Claude.UnknownModels.Keys).Distinct().ToArray();
            string warning = missing.Length > 0 ? "待定价：" + string.Join("、", missing) + "。token 已计入，金额仅包含已知价格。" : "";
            if (summary.Codex.EstimatedModelTokens + summary.Claude.EstimatedModelTokens > 0) warning += " 内部模型别名按 ccusage 参考模型估算，实际路由可能不同。";
            if (summary.Codex.EstimatedCacheTtl + summary.Claude.EstimatedCacheTtl > 0)
                warning += " 部分旧日志未记录缓存时长，按短期缓存写入估算。";
            if (summary.Errors > 0) warning += " 部分日志无法读取或格式异常，用量可能不完整。";
            warning += summary.SourceWarning + summary.PriceWarning;
            Get<TextBlock>("WarningText").Text = warning;
            Get<TextBlock>("WarningText").Visibility = warning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (Preferences.Expanded && !Preferences.Mini) RedrawDashboard();
            if (tray != null)
            {
                string text = "GlassPulse · " + Format.Tokens(summary.Codex.Tokens + summary.Claude.Tokens) + " tokens · " +
                    (unknown > 0 ? "≥" : "") + Format.Money(summary.Codex.Cost + summary.Claude.Cost);
                tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }

        private void UpdateProvider(ProviderSummary summary, string prefix, decimal budget)
        {
            string tokens = Format.Tokens(summary.Tokens) + " tokens";
            string partial = summary.UnpricedTokens > 0 ? "≥ " : "";
            string money = summary.UnpricedTokens > 0 && summary.UnpricedTokens == summary.Tokens ? "待定价" : partial + Format.Money(summary.Cost);
            Get<TextBlock>(prefix + "Tokens").Text = tokens;
            Get<TextBlock>(prefix + "Tokens").ToolTip = summary.Tokens.ToString("N0") + " tokens · " + summary.Requests + " 条去重记录（含缓存）";
            Get<TextBlock>(prefix + "Cost").Text = money;
            decimal percentage = budget > 0 ? summary.Cost / budget * 100 : 0;
            Get<Button>(prefix + "Budget").Content = budget <= 0 ? "设置周预算  ↗" : "周预算 " + Format.Money(budget) + " · " + partial + percentage.ToString("0.#", CultureInfo.InvariantCulture) + "%  · 修改";
            Get<Button>(prefix + "Budget").ToolTip = "相对自己设置的 API 等值预算；套餐剩余额度需查看官方用量页。";
            SetBar(Get<ProgressBar>(prefix + "Bar"), (double)Math.Min(100, percentage));
            SetBar(Get<ProgressBar>("Mini" + prefix + "Bar"), (double)Math.Min(100, percentage));
            Get<TextBlock>("Mini" + prefix + "Cost").Text = money;
            Get<TextBlock>("Mini" + prefix + "Tokens").Text = (Current.Demo ? "演示 · " : weekOffset == 0 ? "本周 · " : "历史 · ") + tokens;
            AutomationProperties.SetHelpText(Get<ProgressBar>(prefix + "Bar"), percentage.ToString("0.#") + "% 自定周预算，" + tokens + "，API 折算 " + money);
        }

        private void SetBar(ProgressBar bar, double value)
        {
            double old = bar.Value;
            if (Math.Abs(old - value) < 0.001) return;
            bar.BeginAnimation(ProgressBar.ValueProperty, null);
            bar.Value = value;
            if (!demo && IsVisible && SystemParameters.ClientAreaAnimation)
                bar.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation(old, value, TimeSpan.FromMilliseconds(280)) { FillBehavior = FillBehavior.Stop });
        }

        internal void RedrawDashboard()
        {
            if (Current == null || !Preferences.Expanded || Preferences.Mini) return;
            string signature = hourly + ":" + chartDollars + ":" + Current.Start.Ticks + ":" + Get<Canvas>("Chart").ActualWidth + ":" +
                string.Join(",", Current.Codex.HourlyTokens.Concat(Current.Claude.HourlyTokens)) + ":" +
                string.Join(",", Current.Codex.DailyTokens.Concat(Current.Claude.DailyTokens)) + ":" +
                string.Join(",", Current.Codex.HourlyCosts.Concat(Current.Claude.HourlyCosts)) + ":" +
                string.Join(",", Current.Codex.DailyCosts.Concat(Current.Claude.DailyCosts)) + ":" +
                string.Join(",", Current.ModelTokens.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value));
            if (chartSignature == signature) return;
            chartSignature = signature;
            DashboardCharts.Draw(this, Current, hourly, chartDollars);
        }

        internal static void WriteCsv(WeekSummary week, string path)
        {
            StringBuilder csv = new StringBuilder("date,provider,tokens,standard_api_usd,week_unpriced_tokens\r\n");
            for (int day = 0; day < 7; day++)
                foreach (ProviderSummary provider in new[] { week.Codex, week.Claude }.Where(p => week.Providers == null || week.Providers == "all" || p.Provider == week.Providers))
                    csv.AppendFormat(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd},{1},{2},{3:0.000000},{4}\r\n",
                        week.Start.AddDays(day), provider.Provider, provider.DailyTokens[day], provider.DailyCosts[day], provider.UnpricedTokens);
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
        }

        private void Export()
        {
            if (Current == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV 表格 (*.csv)|*.csv", FileName = "GlassPulse-" + Current.Start.ToString("yyyy-MM-dd") + ".csv" };
            if (dialog.ShowDialog(this) != true) return;
            try { WriteCsv(Current, dialog.FileName); }
            catch (IOException) { MessageBox.Show(this, "无法写入文件，请选择其他位置。", "GlassPulse"); }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, "没有权限写入这个位置。", "GlassPulse"); }
        }

        private void OpenSettings() { OpenSettings(null); }
        private void OpenSettings(string focusBudget)
        {
            Preferences.ClickThrough = false;
            if (!demo) NativeGlass.SetClickThrough(this, false);
            Show();
            string oldCodexRoot = Preferences.CodexRoot, oldClaudeRoot = Preferences.ClaudeRoot;
            SettingsWindow dialog = new SettingsWindow(Preferences, dataDirectory, pricePath, demo, focusBudget) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (oldCodexRoot != Preferences.CodexRoot || oldClaudeRoot != Preferences.ClaudeRoot) engine = null;
                ApplyAppearance();
                Save();
                if (!demo) { StartTimer(); Refresh(); }
            }
        }

        internal void Quit()
        {
            exiting = true;
            if (IsVisible) { Preferences.Left = Left; Preferences.Top = Top; }
            Save();
            Close();
            if (Application.Current != null) Application.Current.Shutdown();
        }

        internal static WeekSummary DemoSummary()
        {
            WeekSummary value = new WeekSummary { Start = new DateTime(2026, 10, 26), End = new DateTime(2026, 11, 2),
                RefreshedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero), Demo = true, Files = 12 };
            value.Codex.Tokens = 8420000; value.Codex.Cost = 38.47m;
            value.Codex.Input = 1100000; value.Codex.CacheRead = 6900000; value.Codex.CacheWrite = 100000; value.Codex.Output = 320000;
            value.Claude.Tokens = 5160000; value.Claude.Cost = 62.18m;
            value.Claude.Input = 500000; value.Claude.CacheRead = 3900000; value.Claude.CacheWrite = 300000; value.Claude.Output = 460000;
            long[] codex = { 1200000, 2410000, 850000, 1780000, 1310000, 620000, 250000 };
            long[] claude = { 720000, 1510000, 480000, 980000, 860000, 430000, 180000 };
            value.Codex.DailyTokens = codex; value.Claude.DailyTokens = claude;
            value.Codex.FirstDate = value.Claude.FirstDate = "2026-09-07";
            value.Codex.ModelTokens["gpt-6.1-sol"] = 6800000;
            value.Codex.ModelTokens["gpt-6-sol"] = 1620000;
            value.Claude.ModelTokens["claude-opus-5-5"] = 3560000;
            value.Claude.ModelTokens["claude-sonnet-5-5"] = 1600000;
            value.Codex.Requests = 184; value.Claude.Requests = 126;
            value.ModelTokens["gpt-6.1-sol"] = 6800000;
            value.ModelTokens["claude-opus-5-5"] = 3560000;
            value.ModelTokens["gpt-6-sol"] = 1620000;
            value.ModelTokens["claude-sonnet-5-5"] = 1600000;
            for (int i = 0; i < 7; i++)
            {
                value.Codex.DailyCosts[i] = value.Codex.Cost * codex[i] / value.Codex.Tokens;
                value.Claude.DailyCosts[i] = value.Claude.Cost * claude[i] / value.Claude.Tokens;
            }
            double[] hours = { 1, 0.5, 0.2, 0.1, 0.1, 0.2, 1, 2, 5, 9, 14, 11, 6, 10, 15, 13, 8, 5, 2, 3, 4, 5, 3, 1 };
            double sum = hours.Sum();
            for (int i = 0; i < 24; i++)
            {
                value.Codex.HourlyTokens[i] = (long)(hours[i] / sum * value.Codex.Tokens);
                value.Claude.HourlyTokens[i] = (long)(hours[(i + 22) % 24] / sum * value.Claude.Tokens);
                value.Codex.HourlyCosts[i] = value.Codex.Cost * (decimal)(hours[i] / sum);
                value.Claude.HourlyCosts[i] = value.Claude.Cost * (decimal)(hours[(i + 22) % 24] / sum);
            }
            return value;
        }
    }
}
