using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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
            window.Closed += delegate { UnregisterHotKey(handle, HotkeyId); };
            ApplyBackdrop(window, false);
        }

        public static void ApplyBackdrop(Window window, bool expanded)
        {
            if (SystemParameters.HighContrast) return;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            IntPtr memory = IntPtr.Zero;
            try
            {
                // Acrylic adds a stronger frosted backdrop for the expanded dashboard.
                AccentPolicy accent = new AccentPolicy { State = expanded ? 4 : 3, Flags = 2, Color = unchecked((int)0x662C231D) };
                int size = Marshal.SizeOf(accent);
                memory = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, memory, false);
                CompositionData data = new CompositionData { Attribute = 19, Data = memory, Size = size };
                SetWindowCompositionAttribute(handle, ref data);
            }
            // The translucent WPF surface remains usable when a Windows build rejects native blur.
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
