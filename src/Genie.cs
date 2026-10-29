using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MacUI
{
    // Remembers where each window was, so it can be animated after it has already minimized.
    internal static class WindowBounds
    {
        public struct Bounds
        {
            public Native.RECT Window;
            public Native.RECT Frame;
        }

        static readonly Dictionary<IntPtr, Bounds> cache = new Dictionary<IntPtr, Bounds>();

        public static void Update(IntPtr h)
        {
            if (Native.IsIconic(h)) return;
            Native.RECT window;
            if (!Native.GetWindowRect(h, out window) || window.Left <= -30000) return;
            cache[h] = new Bounds { Window = window, Frame = Native.GetFrame(h) };
        }

        public static void UpdateIfTracked(IntPtr h)
        {
            if (cache.ContainsKey(h) || h == Native.GetForegroundWindow()) Update(h);
        }

        public static void Forget(IntPtr h)
        {
            cache.Remove(h);
        }

        public static bool TryGet(IntPtr h, out Bounds bounds)
        {
            if (cache.TryGetValue(h, out bounds)) return true;

            var placement = new Native.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT)) };
            if (!Native.GetWindowPlacement(h, ref placement)) return false;
            Native.MONITORINFO monitor = Native.GetMonitor(h);
            Native.RECT r = placement.rcNormalPosition;
            // rcNormalPosition is relative to the work area, not the screen.
            int dx = monitor.rcWork.Left - monitor.rcMonitor.Left, dy = monitor.rcWork.Top - monitor.rcMonitor.Top;
            r.Left += dx; r.Right += dx; r.Top += dy; r.Bottom += dy;
            if ((placement.flags & 2) != 0) r = monitor.rcWork; // will restore maximized
            bounds = new Bounds { Window = r, Frame = r };
            return true;
        }
    }

    // MacUI draws its own minimize animation, so Windows' one is switched off while it runs.
    internal static class SystemAnimation
    {
        const uint SPI_GETANIMATION = 0x48, SPI_SETANIMATION = 0x49;

        public static void DisableMinimizeAnimation()
        {
            Set(false);
        }

        // Puts back the user's saved setting (the change above is never written to their profile).
        public static void Restore()
        {
            bool enabled = true;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics"))
                    enabled = key == null || !"0".Equals(key.GetValue("MinAnimate") as string);
            }
            catch { }
            Set(enabled);
        }

        static void Set(bool enabled)
        {
            var info = new Native.ANIMATIONINFO { cbSize = 8, iMinAnimate = enabled ? 1 : 0 };
            Native.SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, 0);
        }
    }

    // The macOS "genie" effect, drawn with live DWM thumbnails of the real window cut into strips.
    internal static class Genie
    {
        public const int MaxRunning = 4;
        public static int Running;

        public static void PlayMinimize(IntPtr hwnd)
        {
            WindowBounds.Bounds bounds;
            if (Running >= MaxRunning || !WindowBounds.TryGet(hwnd, out bounds)) return;
            new GenieOverlay(hwnd, bounds, Target(hwnd), false, null).Begin();
        }

        // Brings a window forward; minimized windows fly out of the Dock first.
        public static void Activate(IntPtr hwnd)
        {
            WindowBounds.Bounds bounds;
            if (!Native.IsIconic(hwnd) || Running >= MaxRunning || !WindowBounds.TryGet(hwnd, out bounds))
            {
                WindowTracker.Activate(hwnd);
                return;
            }
            new GenieOverlay(hwnd, bounds, Target(hwnd), true, () => WindowTracker.Activate(hwnd)).Begin();
        }

        static Rect Target(IntPtr hwnd)
        {
            Rect icon = Rect.Empty;
            if (App.Dock != null)
            {
                App.Dock.Peek();
                icon = App.Dock.IconTarget(WindowTracker.GetExePath(hwnd));
            }
            if (!icon.IsEmpty) return icon;
            double s = Dpi.Scale;
            double width = SystemParameters.PrimaryScreenWidth * s, height = SystemParameters.PrimaryScreenHeight * s;
            return new Rect(width / 2 - 24 * s, height - 60 * s, 48 * s, 48 * s);
        }
    }

    internal sealed class GenieOverlay : Window
    {
        const int Strips = 40;
        const double Duration = 0.5;

        readonly IntPtr source;
        readonly WindowBounds.Bounds bounds;
        readonly Rect icon;
        readonly bool reverse;
        readonly Action done;
        readonly List<IntPtr> thumbs = new List<IntPtr>();
        readonly Stopwatch clock = new Stopwatch();
        readonly Native.RECT area;
        IntPtr hwnd;
        bool finished;

        public GenieOverlay(IntPtr source, WindowBounds.Bounds bounds, Rect icon, bool reverse, Action done)
        {
            this.source = source;
            this.bounds = bounds;
            this.icon = icon;
            this.reverse = reverse;
            this.done = done;

            Title = "MacUI Animation";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Transparent;
            Left = -32000;
            Top = -32000;
            Width = 1;
            Height = 1;

            Native.RECT f = bounds.Frame;
            area.Left = (int)Math.Min(f.Left, icon.Left) - 2;
            area.Top = (int)Math.Min(f.Top, icon.Top) - 2;
            area.Right = (int)Math.Max(f.Right, icon.Right) + 2;
            area.Bottom = (int)Math.Max(f.Bottom, icon.Bottom) + 2;

            SourceInitialized += OnSourceInitialized;
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // A see-through window: transparent WPF background over DWM "glass" covering everything.
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            Native.SetDwmInt(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            Native.SetDwmInt(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, 1);
            Native.SetDwmInt(hwnd, Native.DWMWA_BORDER_COLOR, unchecked((int)0xFFFFFFFE));
            WindowStyles.MakeToolWindow(hwnd, true);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, area.Left, area.Top,
                area.Right - area.Left, area.Bottom - area.Top, Native.SWP_NOACTIVATE);
        }

        public void Begin()
        {
            Genie.Running++;
            Show();
            for (int i = 0; i < Strips; i++)
            {
                IntPtr thumb;
                if (Native.DwmRegisterThumbnail(hwnd, source, out thumb) != 0) break;
                thumbs.Add(thumb);
            }
            if (thumbs.Count == 0)
            {
                Finish();
                return;
            }
            Apply(reverse ? 1 : 0);
            clock.Start();
            CompositionTarget.Rendering += OnRendering;
        }

        void OnRendering(object sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds / Duration;
            if (t >= 1)
            {
                Apply(reverse ? 0 : 1);
                Finish();
                return;
            }
            Apply(reverse ? 1 - t : t);
        }

        // t = 0: the window as it was; t = 1: squeezed into the Dock icon.
        void Apply(double t)
        {
            Native.RECT f = bounds.Frame, w = bounds.Window;
            double fx = f.Left, fy = f.Top, fw = f.Right - f.Left, fh = f.Bottom - f.Top;
            int ox = f.Left - w.Left, oy = f.Top - w.Top;
            double size = Math.Min(icon.Width, icon.Height);
            double tx = icon.Left + icon.Width / 2, ty = icon.Top;

            // The bottom edge races towards the Dock first, the top follows.
            double top = Lerp(fy, ty, Ease(Clamp((t - 0.25) / 0.75)));
            double bottom = Lerp(fy + fh, ty + size, Ease(Clamp(t / 0.6)));
            double squeeze = Ease(Clamp(t / 0.45));
            double span = Math.Max(1, ty - fy);
            byte opacity = (byte)(255 * (1 - 0.3 * Clamp((t - 0.75) / 0.25)));

            int n = thumbs.Count;
            for (int i = 0; i < n; i++)
            {
                double v0 = (double)i / n, v1 = (double)(i + 1) / n;
                double y0 = Lerp(top, bottom, v0), y1 = Lerp(top, bottom, v1);
                // Rows closer to the Dock are pinched closer to the icon's width: the funnel shape.
                double k = Smooth(Clamp(((y0 + y1) / 2 - fy) / span)) * squeeze;
                double left = Lerp(fx, tx - size / 2, k), right = Lerp(fx + fw, tx + size / 2, k);

                var props = new Native.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_RECTSOURCE | Native.DWM_TNP_OPACITY |
                              Native.DWM_TNP_VISIBLE | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                    opacity = opacity,
                    fVisible = true,
                    fSourceClientAreaOnly = false
                };
                props.rcSource.Left = ox;
                props.rcSource.Right = ox + (int)fw;
                props.rcSource.Top = oy + (int)Math.Round(v0 * fh);
                props.rcSource.Bottom = oy + (int)Math.Round(v1 * fh);
                props.rcDestination.Left = (int)Math.Round(left) - area.Left;
                props.rcDestination.Right = (int)Math.Round(right) - area.Left;
                props.rcDestination.Top = (int)Math.Round(y0) - area.Top;
                props.rcDestination.Bottom = (int)Math.Round(y1) - area.Top;
                Native.DwmUpdateThumbnailProperties(thumbs[i], ref props);
            }
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            CompositionTarget.Rendering -= OnRendering;
            if (done != null)
            {
                try { done(); }
                catch (Exception ex) { App.Log(ex); }
            }
            // When restoring, give the real window a moment to paint before the copy disappears.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(reverse ? 60 : 1) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                foreach (IntPtr thumb in thumbs) Native.DwmUnregisterThumbnail(thumb);
                thumbs.Clear();
                Close();
                Genie.Running--;
            };
            timer.Start();
        }

        static double Clamp(double x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Smooth(double x) { return x * x * (3 - 2 * x); }
        static double Ease(double x) { return x < 0.5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2; }
    }
}
