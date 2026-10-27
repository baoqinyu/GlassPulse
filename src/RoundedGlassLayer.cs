using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace GlassPulse
{
    // The material owns a composition-only HWND behind the WPF controls. No framebuffer is read or saved.
    internal sealed class RoundedGlassLayer : IDisposable
    {
        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface DesktopInterop
        {
            void CreateDesktopWindowTarget(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
            void EnsureOnThread(uint threadId);
        }
        [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, Thread, Apartment; }
        [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Accent { public int State, Flags, Color, Animation; }
        [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RectI rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        // A dispatcher queue belongs to this UI thread and remains available until it exits.
        private static IntPtr dispatcherQueue;
        private static bool queueRequested;
        private readonly Window owner;
        private IntPtr host;
        private Compositor compositor;
        private DesktopWindowTarget target;
        private SpriteVisual sprite;
        private CompositionRoundedRectangleGeometry geometry;
        private bool disposed;
        private string regionSignature;
        internal bool Active { get { return !disposed && host != IntPtr.Zero && target != null; } }

        internal RoundedGlassLayer(Window owner)
        {
            this.owner = owner;
            try
            {
                if (!queueRequested)
                {
                    int result = CreateDispatcherQueueController(new QueueOptions { Size = 12, Thread = 2, Apartment = 2 }, out dispatcherQueue);
                    if (result < 0) Marshal.ThrowExceptionForHR(result);
                    queueRequested = true;
                }
                RectI rect;
                IntPtr foreground = new WindowInteropHelper(owner).Handle;
                if (!GetWindowRect(foreground, out rect)) throw new InvalidOperationException("Window bounds are unavailable");
                // No redirection bitmap, activation, taskbar button, or mouse input. Only the clipped visual draws.
                host = CreateWindowEx(0x082000A0, "STATIC", "", 0x80000000u, rect.Left, rect.Top,
                    rect.Right - rect.Left, rect.Bottom - rect.Top, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (host == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                EnableHostSampler(host);
                compositor = new Compositor();
                IntPtr pointer;
                ((DesktopInterop)(object)compositor).CreateDesktopWindowTarget(host, false, out pointer);
                try { target = (DesktopWindowTarget)Marshal.GetObjectForIUnknown(pointer); }
                finally { Marshal.Release(pointer); }
                var root = compositor.CreateContainerVisual();
                target.Root = root;
                sprite = compositor.CreateSpriteVisual();
                geometry = compositor.CreateRoundedRectangleGeometry();
                sprite.Clip = compositor.CreateGeometricClip(geometry);
                sprite.Brush = compositor.CreateHostBackdropBrush();
                root.Children.InsertAtTop(sprite);
            }
            catch { Dispose(); throw; }
        }

        private static void EnableHostSampler(IntPtr window)
        {
            // Host-backdrop mode enables sampling without painting the old unbounded blur rectangle.
            Accent accent = new Accent { State = 5 };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            try
            {
                Marshal.StructureToPtr(accent, memory, false);
                CompositionData data = new CompositionData { Attribute = 19, Data = memory, Size = Marshal.SizeOf(accent) };
                SetWindowCompositionAttribute(window, ref data);
            }
            finally { Marshal.FreeHGlobal(memory); }
            int enabled = 1;
            DwmSetWindowAttribute(window, 17, ref enabled, 4);
        }

        internal void Update(Rect card, System.Windows.Media.Matrix dpi, double radius, bool expanded)
        {
            if (disposed) return;
            if (!owner.IsVisible || SystemParameters.HighContrast) { Hide(); return; }
            IntPtr foreground = new WindowInteropHelper(owner).Handle;
            RectI bounds;
            if (!GetWindowRect(foreground, out bounds)) { Hide(); return; }
            SetWindowPos(host, foreground, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x50);
            int left = (int)Math.Round(card.Left * dpi.M11), top = (int)Math.Round(card.Top * dpi.M22);
            int right = (int)Math.Ceiling(card.Right * dpi.M11), bottom = (int)Math.Ceiling(card.Bottom * dpi.M22);
            int ellipseX = (int)Math.Round(radius * 2 * dpi.M11), ellipseY = (int)Math.Round(radius * 2 * dpi.M22);
            string signature = string.Join(":", left, top, right, bottom, ellipseX, ellipseY);
            if (signature != regionSignature)
            {
                IntPtr region = CreateRoundRectRgn(left, top, right, bottom, ellipseX, ellipseY);
                if (region == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (SetWindowRgn(host, region, true) == 0) { DeleteObject(region); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
                regionSignature = signature; // Windows owns the successfully assigned region.
            }
            sprite.Offset = new Vector3((float)(card.Left * dpi.M11), (float)(card.Top * dpi.M22), 0);
            sprite.Size = new Vector2((float)(card.Width * dpi.M11), (float)(card.Height * dpi.M22));
            geometry.Size = sprite.Size;
            geometry.CornerRadius = new Vector2((float)(radius * dpi.M11), (float)(radius * dpi.M22));
            sprite.Opacity = expanded ? 1f : .9f;
        }

        internal void Hide() { if (host != IntPtr.Zero) ShowWindow(host, 0); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (host != IntPtr.Zero) ShowWindow(host, 0);
            if (target != null) { target.Root = null; target.Dispose(); target = null; }
            if (sprite != null) { sprite.Dispose(); sprite = null; }
            if (geometry != null) { geometry.Dispose(); geometry = null; }
            if (compositor != null) { compositor.Dispose(); compositor = null; }
            if (host != IntPtr.Zero) { DestroyWindow(host); host = IntPtr.Zero; }
        }
    }
}