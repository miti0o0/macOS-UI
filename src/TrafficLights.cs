using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;

namespace MacUI
{
    // Red / yellow / green buttons for one app window, sitting where Windows' own buttons were (top-right).
    // Windows you aren't using show grey buttons that light up when the mouse is over them, like on a Mac.
    internal sealed class TrafficLights : Window
    {
        const double Diameter = 12, Gap = 8, Pad = 4;

        sealed class Light
        {
            public Grid View;
            public Ellipse Circle;
            public UIElement Glyph;
            public Brush Fill, Rim;
            public bool Enabled = true;
        }

        public readonly IntPtr Target;
        public readonly string ExePath;
        readonly Light close, minimize, zoom;
        readonly Brush greyFill = Theme.B(Theme.Dark ? Color.FromRgb(0x4D, 0x4D, 0x4F) : Color.FromRgb(0xD0, 0xD0, 0xD2));
        readonly Brush greyRim = Theme.B(Theme.Dark ? Color.FromRgb(0x5A, 0x5A, 0x5C) : Color.FromRgb(0xBC, 0xBC, 0xBE));
        IntPtr hwnd;
        bool active, hovering;

        public TrafficLights(IntPtr target, string exePath)
        {
            Target = target;
            ExePath = exePath;
            Title = "MacUI Window Buttons " + target;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Width = Diameter * 3 + Gap * 2 + Pad * 2;
            Height = Diameter + Pad * 2;
            Left = -32000;
            Top = -32000;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(Pad),
                Background = Theme.B(Color.FromArgb(1, 0, 0, 0))
            };
            // Talk to the window directly: once Windows' buttons are removed, the system menu commands may be ignored.
            close = CreateLight("#FF5F57", "#E0443E", Glyph("M3.5,3.5 L8.5,8.5 M8.5,3.5 L3.5,8.5", "#4D0000", false),
                () => Close(Target));
            minimize = CreateLight("#FEBC2E", "#DEA123", Glyph("M2.8,6 L9.2,6", "#995700", false),
                () => Native.ShowWindowAsync(Target, Native.SW_MINIMIZE));
            zoom = CreateLight("#28C840", "#1AAB29", Glyph("M3.4,8 L3.4,3.4 L8,3.4 Z M8.6,4 L8.6,8.6 L4,8.6 Z", "#006500", true),
                () => Native.ShowWindowAsync(Target, Native.IsZoomed(Target) ? Native.SW_RESTORE : Native.SW_MAXIMIZE));
            close.View.Margin = new Thickness(0, 0, Gap, 0);
            minimize.View.Margin = new Thickness(0, 0, Gap, 0);
            row.Children.Add(close.View);
            row.Children.Add(minimize.View);
            row.Children.Add(zoom.View);

            row.MouseEnter += (s, e) =>
            {
                hovering = true;
                UpdateLook();
            };
            row.MouseLeave += (s, e) =>
            {
                hovering = false;
                UpdateLook();
            };
            row.MouseRightButtonUp += (s, e) => Decorations.ShowAppMenu(row, ExePath);
            Content = row;

            int style = Native.GetWindowLong(target, Native.GWL_STYLE);
            minimize.Enabled = (style & Native.WS_MINIMIZEBOX) != 0;
            zoom.Enabled = (style & Native.WS_MAXIMIZEBOX) != 0;
            UpdateLook();

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                WindowStyles.MakeToolWindow(hwnd, true);
                Native.SetDwmInt(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            };
        }

        Light CreateLight(string fill, string rim, UIElement glyph, Action action)
        {
            var light = new Light
            {
                Fill = Theme.B((Color)ColorConverter.ConvertFromString(fill)),
                Rim = Theme.B((Color)ColorConverter.ConvertFromString(rim)),
                Glyph = glyph
            };
            light.Circle = new Ellipse { Width = Diameter, Height = Diameter, StrokeThickness = 0.5 };
            light.View = new Grid { Width = Diameter, Height = Diameter };
            light.View.Children.Add(light.Circle);
            light.View.Children.Add(glyph);
            light.View.MouseLeftButtonUp += (s, e) =>
            {
                if (light.Enabled) action();
            };
            return light;
        }

        static UIElement Glyph(string data, string color, bool filled)
        {
            Brush brush = Theme.B((Color)ColorConverter.ConvertFromString(color));
            var path = new ShapePath
            {
                Data = Geometry.Parse(data),
                Width = Diameter,
                Height = Diameter,
                Visibility = Visibility.Hidden,
                IsHitTestVisible = false
            };
            if (filled) path.Fill = brush;
            else
            {
                path.Stroke = brush;
                path.StrokeThickness = 1.3;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
            }
            return path;
        }

        public void SetActive(bool value)
        {
            if (active == value) return;
            active = value;
            UpdateLook();
        }

        void UpdateLook()
        {
            bool colored = active || hovering;
            foreach (Light light in new[] { close, minimize, zoom })
            {
                bool lit = colored && light.Enabled;
                light.Circle.Fill = lit ? light.Fill : greyFill;
                light.Circle.Stroke = lit ? light.Rim : greyRim;
                light.Glyph.Visibility = hovering && light.Enabled ? Visibility.Visible : Visibility.Hidden;
            }
        }

        public void MoveTo(int x, int y, IntPtr insertAfter)
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, insertAfter, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // Parked off-screen rather than hidden: a hidden see-through WPF window stops drawing.
        public void Park()
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        // ---------- helpers shared by the Dock, the app switcher and the window picker ----------

        public static void Close(IntPtr h)
        {
            if ((Native.GetWindowLong(h, Native.GWL_STYLE) & Native.WS_SYSMENU) != 0)
                Native.PostMessage(h, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_CLOSE, IntPtr.Zero);
            else
                Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        // True when another normal window sits on top of this part of the target window.
        public static bool IsCovered(IntPtr target, Native.RECT me)
        {
            for (IntPtr h = Native.GetWindow(target, Native.GW_HWNDPREV); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDPREV))
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || WindowTracker.IsOwnWindow(h)) continue;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & (Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT)) != 0 || WindowTracker.IsCloaked(h)) continue;
                Native.RECT r = Native.GetFrame(h);
                if (r.Left < me.Right && r.Right > me.Left && r.Top < me.Bottom && r.Bottom > me.Top) return true;
            }
            return false;
        }

        public static bool IsFullscreen(IntPtr h)
        {
            if ((Native.GetWindowLong(h, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION) return false;
            Native.MONITORINFO monitor = Native.GetMonitor(h);
            Native.RECT r;
            Native.GetWindowRect(h, out r);
            return r.Left <= monitor.rcMonitor.Left && r.Top <= monitor.rcMonitor.Top &&
                   r.Right >= monitor.rcMonitor.Right && r.Bottom >= monitor.rcMonitor.Bottom;
        }
    }
}
