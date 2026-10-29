using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacUI
{
    // Removes Windows' own minimize / maximize / close buttons from normal title bars
    // by switching off the window's system menu (put back when MacUI quits).
    internal static class CaptionButtons
    {
        static readonly HashSet<IntPtr> stripped = new HashSet<IntPtr>();
        static readonly HashSet<IntPtr> seen = new HashSet<IntPtr>();
        static readonly Dictionary<IntPtr, int> captionHeights = new Dictionary<IntPtr, int>();

        // Browsers and Electron apps draw their own buttons; those get a ButtonCover instead.
        public static bool DrawsOwnTitleBar(IntPtr h)
        {
            string cls = Native.GetClassName(h);
            return cls.StartsWith("Chrome_WidgetWin_") || cls == "MozillaWindowClass" || cls == "CASCADIA_HOSTING_WINDOW_CLASS";
        }

        public static void Sweep(Func<string, bool> isExcluded)
        {
            foreach (AppWindow window in WindowTracker.GetAppWindows())
                if (!isExcluded(window.ExePath)) Strip(window.Handle);
            seen.RemoveWhere(h => !Native.IsWindow(h));
            stripped.RemoveWhere(h => !Native.IsWindow(h));
            foreach (IntPtr h in captionHeights.Keys.Where(h => !Native.IsWindow(h)).ToList()) captionHeights.Remove(h);
        }

        // Safe to call repeatedly: some apps switch their system menu back on (e.g. when maximized),
        // so this is checked again on every refresh.
        public static void Strip(IntPtr h)
        {
            if (DrawsOwnTitleBar(h)) return;
            int style = Native.GetWindowLong(h, Native.GWL_STYLE);
            if ((style & Native.WS_SYSMENU) == 0) return;

            // Remember how tall the old buttons were, so the new ones sit in the middle of the title bar.
            Native.RECT bounds;
            if (seen.Add(h) && Native.DwmGetWindowAttribute(h, Native.DWMWA_CAPTION_BUTTON_BOUNDS, out bounds, 16) == 0 && bounds.Bottom > bounds.Top)
                captionHeights[h] = bounds.Bottom - bounds.Top;

            Native.SetWindowLong(h, Native.GWL_STYLE, style & ~Native.WS_SYSMENU);
            Redraw(h);
            stripped.Add(h);
        }

        public static void Unstrip(IntPtr h)
        {
            seen.Remove(h);
            if (!stripped.Remove(h) || !Native.IsWindow(h)) return;
            Native.SetWindowLong(h, Native.GWL_STYLE, Native.GetWindowLong(h, Native.GWL_STYLE) | Native.WS_SYSMENU);
            Redraw(h);
        }

        public static void RestoreAll()
        {
            foreach (IntPtr h in stripped.ToList()) Unstrip(h);
            seen.Clear();
        }

        public static int CaptionHeight(IntPtr h)
        {
            int height;
            int minimum = (int)Math.Round(28 * Dpi.Scale);
            return captionHeights.TryGetValue(h, out height) ? Math.Max(height, minimum) : (int)Math.Round(32 * Dpi.Scale);
        }

        static void Redraw(IntPtr h)
        {
            Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }

    // A patch in the title bar's own color, laid over buttons that apps draw themselves.
    internal sealed class ButtonCover : Window
    {
        readonly IntPtr target;
        readonly Border patch;
        IntPtr hwnd;

        public ButtonCover(IntPtr target)
        {
            this.target = target;
            Title = "MacUI Button Cover " + target;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Left = -32000;
            Top = -32000;
            Width = 10;
            Height = 10;

            patch = new Border();
            Content = patch;
            // The patch still behaves like title bar: drag to move, double-click to zoom.
            patch.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) Native.ShowWindowAsync(target, Native.IsZoomed(target) ? Native.SW_RESTORE : Native.SW_MAXIMIZE);
                else
                {
                    WindowTracker.Activate(target);
                    Native.PostMessage(target, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_DRAGMOVE, IntPtr.Zero);
                }
            };

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                WindowStyles.MakeToolWindow(hwnd, true);
                Native.SetDwmInt(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            };
        }

        public void Park()
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public void PlaceAt(Native.RECT me, IntPtr insertAfter)
        {
            if (hwnd == IntPtr.Zero) return;
            Color? color = SampleTitleBar(me.Left - (int)Math.Round(5 * Dpi.Scale), (me.Top + me.Bottom) / 2);
            if (color == null)
            {
                Park();
                return;
            }
            patch.Background = Theme.B(color.Value);
            patch.CornerRadius = new CornerRadius(0, Native.IsZoomed(target) ? 0 : 8, 0, 0);
            Native.SetWindowPos(hwnd, insertAfter, me.Left, me.Top, me.Right - me.Left, me.Bottom - me.Top, Native.SWP_NOACTIVATE);
        }

        static Color? SampleTitleBar(int x, int y)
        {
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try
            {
                uint c = Native.GetPixel(dc, x, y);
                if (c == 0xFFFFFFFF) return null;
                return Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, dc);
            }
        }
    }

    // Gives every app window macOS buttons at its top-right corner and hides Windows' own buttons.
    internal static class Decorations
    {
        sealed class Decoration
        {
            public TrafficLights Lights;
            public ButtonCover Cover;
        }

        sealed class Probe
        {
            public bool Found;
            public int FrameWidth;
            public DateTime Time = DateTime.Now;
            public int FromRight, FromTop, Height; // where the app's own buttons are, relative to the window frame
        }

        static string ExcludePath { get { return Path.Combine(App.DataDir, "no-window-buttons.txt"); } }

        static readonly Dictionary<IntPtr, Decoration> decorations = new Dictionary<IntPtr, Decoration>();
        static readonly Dictionary<IntPtr, Probe> probes = new Dictionary<IntPtr, Probe>();
        static readonly HashSet<IntPtr> probing = new HashSet<IntPtr>();
        static readonly HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static Dispatcher dispatcher;

        public static void Start()
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            LoadExcluded();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => Refresh();
            timer.Start();
            Refresh();
        }

        public static void Stop()
        {
            foreach (IntPtr h in decorations.Keys.ToList()) Remove(h);
        }

        public static bool IsExcluded(string exePath)
        {
            return exePath != null && excluded.Contains(Path.GetFileName(exePath));
        }

        static bool refreshing;

        public static void Refresh()
        {
            // Showing or closing a WPF window pumps messages, so window events can arrive in the middle of a
            // refresh. Running a second refresh inside the first one used to create duplicate buttons for a window.
            if (refreshing)
            {
                RefreshSoon();
                return;
            }
            refreshing = true;
            try { RefreshNow(); }
            finally { refreshing = false; }
        }

        static void RefreshNow()
        {
            IntPtr foreground = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOTOWNER);
            var alive = new HashSet<IntPtr>();
            foreach (AppWindow window in WindowTracker.GetAppWindows())
            {
                IntPtr h = window.Handle;
                if (IsExcluded(window.ExePath) || Native.IsIconic(h)) continue;
                // Fills the whole monitor: a full-screen game/video gets no buttons, but a maximized browser on a
                // monitor without taskbar looks the same, so only skip it when its own title-bar buttons aren't showing.
                if (TrafficLights.IsFullscreen(h) && !ShowsOwnButtons(h)) continue;
                alive.Add(h);
                CaptionButtons.Strip(h);
                Decoration decoration;
                if (!decorations.TryGetValue(h, out decoration))
                {
                    decoration = new Decoration { Lights = new TrafficLights(h, window.ExePath), Cover = new ButtonCover(h) };
                    decorations[h] = decoration; // registered before Show(), which can pump messages
                    decoration.Cover.Show();
                    decoration.Lights.Show();
                }
                decoration.Lights.SetActive(h == foreground);
                Place(h, decoration);
            }
            foreach (IntPtr h in decorations.Keys.Where(h => !alive.Contains(h)).ToList()) Remove(h);
        }

        public static void OnMoved(IntPtr h)
        {
            Decoration decoration;
            if (!decorations.TryGetValue(h, out decoration)) return;
            if (Native.IsIconic(h) || !Native.IsWindowVisible(h)) Park(decoration);
            else Place(h, decoration);
            // A window that moved or got maximized may now cover (or uncover) other windows' buttons.
            RefreshSoon();
        }

        static DispatcherTimer soon;

        static void RefreshSoon()
        {
            if (soon == null)
            {
                soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                soon.Tick += (s, e) =>
                {
                    soon.Stop();
                    Refresh();
                };
            }
            if (!soon.IsEnabled) soon.Start();
        }

        public static void OnGone(IntPtr h)
        {
            Decoration decoration;
            if (decorations.TryGetValue(h, out decoration)) Park(decoration);
        }

        public static void OnDestroyed(IntPtr h)
        {
            Remove(h);
            probes.Remove(h);
        }

        static void Remove(IntPtr h)
        {
            Decoration decoration;
            if (!decorations.TryGetValue(h, out decoration)) return;
            decorations.Remove(h);
            decoration.Lights.Close();
            decoration.Cover.Close();
        }

        static void Park(Decoration decoration)
        {
            decoration.Lights.Park();
            decoration.Cover.Park();
        }

        static void Place(IntPtr h, Decoration decoration)
        {
            Native.RECT frame = Native.GetFrame(h);
            if (Native.IsZoomed(h))
            {
                // Windows reports maximized windows a few pixels past the usable screen area (under the menu bar).
                Native.RECT work = Native.GetMonitor(h).rcWork;
                frame.Left = Math.Max(frame.Left, work.Left);
                frame.Top = Math.Max(frame.Top, work.Top);
                frame.Right = Math.Min(frame.Right, work.Right);
            }
            double s = Dpi.Scale;
            // Just below the Dock in the "always on top" layer, so the Dock and menus stay above them.
            IntPtr after = App.Dock != null && App.Dock.Handle != IntPtr.Zero ? App.Dock.Handle : Native.HWND_TOPMOST;

            int centerY;
            Native.RECT cover;
            if (FindOwnButtons(h, frame, out cover))
            {
                if (TrafficLights.IsCovered(h, cover)) decoration.Cover.Park();
                else decoration.Cover.PlaceAt(cover, after);
                centerY = (cover.Top + cover.Bottom) / 2;
            }
            else
            {
                decoration.Cover.Park();
                centerY = frame.Top + CaptionButtons.CaptionHeight(h) / 2;
            }

            int width = (int)Math.Round(decoration.Lights.Width * s), height = (int)Math.Round(decoration.Lights.Height * s);
            var me = new Native.RECT { Left = frame.Right - width - (int)Math.Round(6 * s), Top = centerY - height / 2 };
            me.Right = me.Left + width;
            me.Bottom = me.Top + height;
            if (TrafficLights.IsCovered(h, me)) decoration.Lights.Park();
            else decoration.Lights.MoveTo(me.Left, me.Top, after);
        }

        // ---------- finding buttons that apps draw themselves ----------

        // Screen area of the app's own minimize/maximize/close buttons, if it draws them itself.
        static bool FindOwnButtons(IntPtr h, Native.RECT frame, out Native.RECT cover)
        {
            double s = Dpi.Scale;
            cover = new Native.RECT { Top = frame.Top, Right = frame.Right };

            // Best source: the exact buttons, from UI Automation.
            Probe probe = EnsureProbe(h, frame);
            if (probe != null && probe.Found)
            {
                cover.Left = frame.Right - probe.FromRight - (int)Math.Round(2 * s);
                cover.Bottom = frame.Top + probe.FromTop + probe.Height;
                return true;
            }

            // Fallback for apps like Windows Terminal: where Windows says the caption buttons are.
            if (!CaptionButtons.DrawsOwnTitleBar(h)) return false;
            Native.RECT bounds, window;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CAPTION_BUTTON_BOUNDS, out bounds, 16) != 0 ||
                bounds.Right - bounds.Left < 20 || !Native.GetWindowRect(h, out window)) return false;
            cover.Left = window.Left + bounds.Left - (int)Math.Round(2 * s);
            cover.Bottom = window.Top + bounds.Bottom;
            return true;
        }

        static bool ShowsOwnButtons(IntPtr h)
        {
            // Re-checked every 2 s: going in and out of a full-screen video doesn't change the window's size.
            Probe probe = EnsureProbe(h, Native.GetFrame(h), TimeSpan.FromSeconds(2));
            return probe != null && probe.Found;
        }

        static Probe EnsureProbe(IntPtr h, Native.RECT frame)
        {
            return EnsureProbe(h, frame, TimeSpan.MaxValue);
        }

        static Probe EnsureProbe(IntPtr h, Native.RECT frame, TimeSpan maxAge)
        {
            int width = frame.Right - frame.Left;
            Probe probe;
            bool fresh = probes.TryGetValue(h, out probe) && probe.FrameWidth == width &&
                         (maxAge == TimeSpan.MaxValue || DateTime.Now - probe.Time < maxAge);
            if (!fresh && !probing.Contains(h))
            {
                probing.Add(h);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Probe result = FindButtons(h, frame);
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        probing.Remove(h);
                        probes[h] = result;
                        OnMoved(h);
                    }));
                });
            }
            return probe;
        }

        // Finds the row of 2-3 equal buttons flush with the window's top-right corner.
        static Probe FindButtons(IntPtr h, Native.RECT frame)
        {
            var probe = new Probe { FrameWidth = frame.Right - frame.Left };
            try
            {
                double s = Dpi.Scale;
                AutomationElement window = AutomationElement.FromHandle(h);
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var candidates = new List<Rect>();
                foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, condition))
                {
                    Rect r = button.Current.BoundingRectangle;
                    if (r.IsEmpty || r.Width < 8 || r.Height < 8) continue;
                    // Only buttons in the title strip (scrolled-away content can report positions far above the window;
                    // maximized apps may put their buttons a few pixels above the visible frame).
                    if (r.Bottom > frame.Top && r.Top >= frame.Top - 16 * s && r.Bottom <= frame.Top + 80 * s &&
                        r.Right > frame.Right - 400 * s) candidates.Add(r);
                }

                List<Rect> row = FindButtonRow(candidates, frame.Right, s);
                if (row.Count < 2) return probe;

                probe.Found = true;
                probe.FromRight = frame.Right - (int)Math.Floor(row.Min(r => r.Left));
                probe.FromTop = Math.Max(0, (int)Math.Floor(row.Min(r => r.Top)) - frame.Top);
                probe.Height = (int)Math.Ceiling(row.Max(r => r.Bottom) - row.Min(r => r.Top));
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
            return probe;
        }

        // Starting at the right edge, chains leftwards through same-sized neighbours (close, maximize, minimize).
        // Other buttons nearby (a panel's own "Close", toolbar icons) are skipped, and when an app reports
        // the same button twice (Firefox does, in two heights) the tallest one wins.
        public static List<Rect> FindButtonRow(List<Rect> candidates, double frameRight, double s)
        {
            var row = new List<Rect>();
            var first = candidates.Where(r => r.Right >= frameRight - 4 * s)
                                  .OrderByDescending(r => r.Right).ThenByDescending(r => r.Height).ToList();
            if (first.Count == 0) return row;
            row.Add(first[0]);

            while (row.Count < 3)
            {
                Rect previous = row[row.Count - 1];
                var next = candidates.Where(r =>
                        Math.Abs(r.Right - previous.Left) <= 8 * s &&
                        Math.Abs(r.Top - previous.Top) <= 4 * s &&
                        Math.Abs(r.Height - previous.Height) <= 4 * s &&
                        Math.Abs(r.Width - previous.Width) <= 6 * s)
                    .OrderByDescending(r => r.Height).ToList();
                if (next.Count == 0) break;
                row.Add(next[0]);
            }
            return row;
        }

        // ---------- per-app opt-out ----------

        public static void ShowAppMenu(FrameworkElement anchor, string exePath)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            Menus.Add(menu, "Use Windows buttons in " + AppInfo.DisplayName(exePath), () =>
            {
                excluded.Add(Path.GetFileName(exePath));
                SaveExcluded();
                foreach (AppWindow w in WindowTracker.GetAppWindows())
                    if (string.Equals(w.ExePath, exePath, StringComparison.OrdinalIgnoreCase)) CaptionButtons.Unstrip(w.Handle);
                Refresh();
            });
            if (excluded.Count > 0)
                Menus.Add(menu, "Use macOS buttons in all apps again", () =>
                {
                    excluded.Clear();
                    SaveExcluded();
                    Refresh();
                });
            menu.IsOpen = true;
        }

        static void LoadExcluded()
        {
            try
            {
                if (!File.Exists(ExcludePath)) return;
                foreach (string line in File.ReadAllLines(ExcludePath))
                    if (line.Trim().Length > 0 && !line.StartsWith("#")) excluded.Add(line.Trim());
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void SaveExcluded()
        {
            try
            {
                var lines = new List<string> { "# Apps (process names) that keep the normal Windows buttons." };
                lines.AddRange(excluded.OrderBy(x => x));
                File.WriteAllLines(ExcludePath, lines);
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}
