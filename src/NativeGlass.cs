using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GlassPulse
{
    internal static class NativeGlass
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy { public int State, Flags, Color, Animation; }
        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr handle, int index, int value);
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr handle, int id);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        private sealed class SurfaceState
        {
            public string Signature;
            public bool Rounded, Expanded, Light, MaterialFailed;
            public RoundedGlassLayer Material;
        }
        private static readonly ConditionalWeakTable<Window, SurfaceState> Surfaces = new ConditionalWeakTable<Window, SurfaceState>();
        private static SurfaceState Surface(Window window) { return Surfaces.GetValue(window, key => new SurfaceState()); }
        public static bool HotkeyAvailable { get; private set; }
        private const int HotkeyId = 0x4750;

        public static void Attach(Window window, Action toggle)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
                if (message == 0x0312 && wParam.ToInt32() == HotkeyId) { toggle(); handled = true; }
                return IntPtr.Zero;
            });
            // Ctrl+Alt+G restores the widget even while its surface ignores mouse input.
            HotkeyAvailable = RegisterHotKey(handle, HotkeyId, 0x4000 | 0x0002 | 0x0001, 0x47);
            window.Closed += delegate { UnregisterHotKey(handle, HotkeyId); var state = Surface(window); if (state.Material != null) state.Material.Dispose(); };
            window.LocationChanged += delegate { RefreshSurface(window); };
            window.StateChanged += delegate { RefreshSurface(window); };
            window.Activated += delegate { RefreshSurface(window); };
            window.IsVisibleChanged += delegate { RefreshSurface(window); };
            window.LayoutUpdated += delegate { RefreshSurface(window); };
            window.Loaded += delegate { RefreshSurface(window); };
            source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
                if (message == 0x02E0)
                {
                    Surface(window).Signature = null;
                    window.Dispatcher.BeginInvoke((Action)delegate { RefreshSurface(window); }, DispatcherPriority.Loaded);
                }
                return IntPtr.Zero;
            });
            ApplyBackdrop(window, false);
        }

        private static bool ClipSurface(Window window, out bool changed)
        {
            changed = false;
            var root = window.Content as FrameworkElement;
            var frame = root == null ? null : root.FindName("Frame") as Border;
            var source = PresentationSource.FromVisual(window);
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || source == null || frame == null || frame.ActualWidth <= 0 || frame.ActualHeight <= 0) return false;
            Rect bounds = frame.TransformToAncestor(window).TransformBounds(new Rect(frame.RenderSize));
            Matrix dpi = source.CompositionTarget.TransformToDevice;
            int left = (int)Math.Round(bounds.Left * dpi.M11), top = (int)Math.Round(bounds.Top * dpi.M22);
            int right = (int)Math.Ceiling(bounds.Right * dpi.M11), bottom = (int)Math.Ceiling(bounds.Bottom * dpi.M22);
            double radius = frame.CornerRadius.TopLeft;
            int ellipseX = (int)Math.Round(radius * 2 * dpi.M11), ellipseY = (int)Math.Round(radius * 2 * dpi.M22);
            SurfaceState state = Surface(window);
            string signature = string.Join(":", left, top, right, bottom, ellipseX, ellipseY);
            if (signature == state.Signature) return state.Rounded;
            IntPtr region = CreateRoundRectRgn(left, top, right, bottom, ellipseX, ellipseY);
            if (region == IntPtr.Zero) { state.Rounded = false; return false; }
            if (SetWindowRgn(handle, region, true) == 0)
            {
                DeleteObject(region); // Ownership transfers to Windows only on success.
                state.Rounded = false;
                return false;
            }
            // SetWindowRgn owns this HRGN. It clips controls and hit tests; material pixels have a separate visual clip.
            state.Signature = signature;
            state.Rounded = true;
            changed = true;
            return true;
        }

        private static void RefreshSurface(Window window)
        {
            SurfaceState state = Surface(window);
            bool changed;
            bool ready = ClipSurface(window, out changed);
            if (!ready || !window.IsVisible || window.WindowState == WindowState.Minimized || SystemParameters.HighContrast)
            {
                if (state.Material != null) state.Material.Hide();
                return;
            }
            if (state.MaterialFailed) return;
            try
            {
                if (state.Material == null) state.Material = new RoundedGlassLayer(window);
                var frame = ((FrameworkElement)window.Content).FindName("Frame") as Border;
                Rect card = frame.TransformToAncestor(window).TransformBounds(new Rect(frame.RenderSize));
                Matrix dpi = PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
                state.Material.Update(card, dpi, frame.CornerRadius.TopLeft, state.Expanded);
            }
            catch (Exception ex)
            {
                if (!(ex is COMException || ex is TypeLoadException || ex is MissingMethodException ||
                    ex is EntryPointNotFoundException || ex is DllNotFoundException || ex is NotSupportedException ||
                    ex is InvalidOperationException || ex is InvalidCastException || ex is System.ComponentModel.Win32Exception)) throw;
                if (state.Material != null) { state.Material.Dispose(); state.Material = null; }
                state.MaterialFailed = true;
                // Unsupported composition falls back to WPF transparency, never an unbounded native blur.
            }
        }

        public static void ApplyBackdrop(Window window, bool expanded, bool light = false)
        {
            SurfaceState state = Surface(window);
            state.Expanded = expanded; state.Light = light;
            SetAccent(window, 0, false);
            RefreshSurface(window);
        }

        public static bool IsMaterialActive(Window window)
        {
            SurfaceState state;
            return Surfaces.TryGetValue(window, out state) && state.Material != null && state.Material.Active;
        }

        private static void SetAccent(Window window, int mode, bool light)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            IntPtr memory = IntPtr.Zero;
            try
            {
                AccentPolicy accent = new AccentPolicy { State = mode, Flags = 2, Color = light ? unchecked((int)0x66F4F6F2) : unchecked((int)0x662C231D) };
                int size = Marshal.SizeOf(accent);
                memory = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, memory, false);
                CompositionData data = new CompositionData { Attribute = 19, Data = memory, Size = size };
                SetWindowCompositionAttribute(handle, ref data);
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            finally { if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory); }
        }

        public static void SetClickThrough(Window window, bool enabled)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            int style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, enabled ? style | 0x20 : style & ~0x20);
        }

        public static void ClampToScreens(Window window, bool snap)
        {
            var source = PresentationSource.FromVisual(window);
            Matrix transform = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            var pixelPoint = new System.Drawing.Point((int)(window.Left * transform.M11 + window.ActualWidth / 2 * transform.M11),
                (int)(window.Top * transform.M22 + window.ActualHeight / 2 * transform.M22));
            var area = System.Windows.Forms.Screen.FromPoint(pixelPoint).WorkingArea;
            double x = area.X / transform.M11;
            double y = area.Y / transform.M22;
            double right = area.Right / transform.M11 - window.ActualWidth;
            double bottom = area.Bottom / transform.M22 - window.ActualHeight;
            window.Left = Math.Max(x, Math.Min(window.Left, right));
            window.Top = Math.Max(y, Math.Min(window.Top, bottom));
            if (!snap) return;
            if (Math.Abs(window.Left - x) < 18) window.Left = x + 8;
            if (Math.Abs(window.Left - right) < 18) window.Left = right - 8;
            if (Math.Abs(window.Top - y) < 18) window.Top = y + 8;
            if (Math.Abs(window.Top - bottom) < 18) window.Top = bottom - 8;
        }
    }
}
