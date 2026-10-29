using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace MacUI
{
    internal sealed class DockItem
    {
        public string Kind = "app"; // app, launchpad, trash, separator
        public string Name;
        public string Target;
        public string Args;
        public string MatchExe;
        public bool Pinned;
        public readonly List<IntPtr> Windows = new List<IntPtr>();

        public Grid View;
        public TranslateTransform Bounce;
        public UIElement Dot;
        public double Size, TargetSize, BaseCenter;

        public bool IsApp { get { return Kind == "app"; } }
        public bool IsRunning { get { return Windows.Count > 0; } }

        public bool Matches(string exePath)
        {
            if (!IsApp || exePath == null) return false;
            string exeName = Path.GetFileName(exePath);
            if (!string.IsNullOrEmpty(MatchExe)) return string.Equals(MatchExe, exeName, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(Target, exePath, StringComparison.OrdinalIgnoreCase)) return true;
            return Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Path.GetFileName(Target), exeName, StringComparison.OrdinalIgnoreCase);
        }
    }

    // A Grid that shows up (with its name) in UI Automation, so screen readers and tests can find Dock icons.
    internal sealed class NamedGrid : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        {
            return new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
        }
    }

    // The macOS-style Dock: pinned apps, running apps, magnification and the Trash.
    // It hides itself and slides up when the mouse touches the bottom of the screen.
    internal sealed class DockWindow : Window
    {
        const double BaseSize = 48, MaxSize = 76, Spacing = 6, Pad = 7, BottomGap = 6, Range = 160, SepWidth = 13;
        const double HiddenOffset = BaseSize + Pad * 2 + BottomGap + 12;
        const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

        public static string ConfigPath { get { return Path.Combine(App.DataDir, "dock.txt"); } }
        static readonly string[] ConfigHeader =
        {
            "# MacUI Dock: one app per line, in Dock order.",
            "#   Name|Program path or shell:AppsFolder\\AppID|Arguments (optional)|Process.exe (optional, for the running dot)",
            "# Save this file and the Dock reloads by itself."
        };

        readonly Grid root;
        readonly Border background;
        readonly StackPanel panel;
        readonly Border label;
        readonly TextBlock labelText;
        readonly TranslateTransform slide = new TranslateTransform();
        readonly DispatcherTimer reloadTimer;
        readonly List<DockItem> runningOrder = new List<DockItem>();
        readonly DockItem launchpad = new DockItem { Kind = "launchpad", Name = "Launchpad" };
        readonly DockItem trash = new DockItem { Kind = "trash", Name = "Trash", Target = "shell:RecycleBinFolder" };
        readonly DockItem separator1 = new DockItem { Kind = "separator" };
        readonly DockItem separator2 = new DockItem { Kind = "separator" };
        List<DockItem> pinned = new List<DockItem>();
        List<DockItem> entries = new List<DockItem>();
        DockItem hovered;
        double totalBaseWidth;
        bool animating;
        bool revealed = true;
        bool menuOpen;
        IntPtr hwnd;
        DateTime hideAt = DateTime.Now.AddSeconds(1.5);
        DateTime ignoreConfigUntil;
        FileSystemWatcher watcher;

        public DockWindow()
        {
            Title = "MacUI Dock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = Theme.UiFont;

            root = new Grid { RenderTransform = slide };
            background = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Height = BaseSize + Pad * 2,
                Margin = new Thickness(0, 0, 0, BottomGap),
                CornerRadius = new CornerRadius(18),
                Background = Theme.B(Theme.DockBg),
                BorderBrush = Theme.B(Theme.DockBorder),
                BorderThickness = new Thickness(1)
            };
            // Almost-transparent background so the mouse is still tracked above small icons.
            panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, BottomGap + Pad),
                Background = Theme.B(Color.FromArgb(1, 0, 0, 0))
            };
            panel.SizeChanged += (s, e) => background.Width = panel.ActualWidth + Pad * 2;

            labelText = new TextBlock { Foreground = Theme.B(Theme.Text), FontSize = 13 };
            label = new Border
            {
                Background = Theme.B(Theme.PanelBg),
                BorderBrush = Theme.B(Theme.PanelBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 3, 10, 4),
                Child = labelText,
                Visibility = Visibility.Collapsed
            };
            var overlay = new Canvas { IsHitTestVisible = false };
            overlay.Children.Add(label);

            root.Children.Add(background);
            root.Children.Add(panel);
            root.Children.Add(overlay);
            Content = root;

            root.MouseMove += (s, e) => Magnify(e.GetPosition(root).X);
            root.MouseLeave += (s, e) =>
            {
                hovered = null;
                Magnify(double.NaN);
            };

            SourceInitialized += (s, e) =>
            {
                hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WindowStyles.MakeToolWindow(hwnd, true);
            };
            Reposition();

            var edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            edgeTimer.Tick += (s, e) => WatchMouse();
            edgeTimer.Start();

            reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            reloadTimer.Tick += (s, e) =>
            {
                reloadTimer.Stop();
                ReloadConfig();
            };

            LoadConfig();
            Refresh();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Refresh();
            timer.Start();
            WatchConfig();
        }

        public IntPtr Handle { get { return hwnd; } }

        public void Reposition()
        {
            Width = SystemParameters.PrimaryScreenWidth;
            Height = MaxSize + Pad + BottomGap + 44;
            Left = 0;
            Top = SystemParameters.PrimaryScreenHeight - Height;
        }

        // ---------- auto-hide ----------

        void WatchMouse()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            double s = Dpi.Scale;
            double screenWidth = SystemParameters.PrimaryScreenWidth * s, screenHeight = SystemParameters.PrimaryScreenHeight * s;

            if (!revealed)
            {
                if (p.Y >= screenHeight - 2 && p.X >= 0 && p.X < screenWidth) Reveal();
                return;
            }

            if (menuOpen || WindowPicker.Current != null || IsOverDock(p, s)) hideAt = DateTime.MaxValue;
            else if (hideAt == DateTime.MaxValue) hideAt = DateTime.Now.AddMilliseconds(350);
            else if (DateTime.Now >= hideAt) Conceal();
        }

        bool IsOverDock(Native.POINT p, double s)
        {
            if (PresentationSource.FromVisual(background) == null) return false;
            Point topLeft = background.PointToScreen(new Point(0, 0));
            double right = topLeft.X + background.ActualWidth * s;
            double top = topLeft.Y - (MaxSize - BaseSize + 10) * s; // room for magnified icons
            return p.X >= topLeft.X && p.X <= right && p.Y >= top;
        }

        void Reveal()
        {
            revealed = true;
            hideAt = DateTime.MaxValue;
            // Always come up above every other window.
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Slide(0, 180);
        }

        void Conceal()
        {
            revealed = false;
            hovered = null;
            Magnify(double.NaN);
            Slide(HiddenOffset, 220);
        }

        void Slide(double to, int milliseconds)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = to == 0 ? EasingMode.EaseOut : EasingMode.EaseIn }
            };
            slide.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        // Shows the Dock for a moment, e.g. while a window minimizes into it.
        public void Peek()
        {
            if (!revealed) Reveal();
            hideAt = DateTime.Now.AddSeconds(1.2);
        }

        DockItem ItemFor(string exePath)
        {
            return exePath == null ? null : entries.FirstOrDefault(e => e.IsApp && e.Matches(exePath));
        }

        // Screen rectangle (in pixels) of an element, as it is once the Dock has slid up.
        Rect ScreenRect(FrameworkElement element)
        {
            if (PresentationSource.FromVisual(element) == null) return Rect.Empty;
            Point topLeft = element.PointToScreen(new Point(0, 0));
            Point bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            Rect rect = new Rect(topLeft, bottomRight);
            rect.Offset(0, -slide.Y * Dpi.Scale);
            return rect;
        }

        // Screen rectangle (in pixels) of the app's Dock icon, where minimized windows fly to.
        public Rect IconTarget(string exePath)
        {
            DockItem item = ItemFor(exePath);
            FrameworkElement element = item != null && item.View != null ? (FrameworkElement)item.View : background;
            Rect rect = ScreenRect(element);
            if (rect.IsEmpty) return rect;
            if (item == null) rect = new Rect(rect.Left + rect.Width / 2 - rect.Height / 2, rect.Top, rect.Height, rect.Height);
            return rect;
        }

        // The nicest icon we know for an app (Store apps look better through their Dock entry).
        public ImageSource IconFor(string exePath)
        {
            DockItem item = ItemFor(exePath);
            return IconLoader.Get(item != null && item.Pinned ? item.Target : exePath, 128);
        }

        // ---------- running apps ----------

        void Refresh()
        {
            List<AppWindow> windows;
            try { windows = WindowTracker.GetAppWindows(); }
            catch (Exception ex) { App.Log(ex); return; }

            foreach (DockItem item in pinned) item.Windows.Clear();
            foreach (DockItem item in runningOrder) item.Windows.Clear();

            foreach (AppWindow w in windows)
            {
                DockItem owner = pinned.FirstOrDefault(p => p.Matches(w.ExePath)) ??
                                 runningOrder.FirstOrDefault(r => string.Equals(r.Target, w.ExePath, StringComparison.OrdinalIgnoreCase));
                if (owner == null)
                {
                    owner = new DockItem { Name = AppInfo.DisplayName(w.ExePath), Target = w.ExePath };
                    runningOrder.Add(owner);
                }
                owner.Windows.Add(w.Handle);
            }
            runningOrder.RemoveAll(r => !r.IsRunning);

            var list = new List<DockItem> { launchpad };
            list.AddRange(pinned);
            if (runningOrder.Count > 0)
            {
                list.Add(separator1);
                list.AddRange(runningOrder);
            }
            list.Add(separator2);
            list.Add(trash);

            if (!list.SequenceEqual(entries))
            {
                entries = list;
                Rebuild();
            }
            foreach (DockItem item in entries)
                if (item.Dot != null) item.Dot.Visibility = item.IsRunning ? Visibility.Visible : Visibility.Hidden;
        }

        void Rebuild()
        {
            panel.Children.Clear();
            double x = 0;
            foreach (DockItem item in entries)
            {
                EnsureView(item);
                panel.Children.Add(item.View);
                double width = item.Kind == "separator" ? SepWidth : BaseSize + Spacing;
                item.BaseCenter = x + width / 2;
                x += width;
            }
            totalBaseWidth = x;
            if (hovered != null && !entries.Contains(hovered)) hovered = null;
            UpdateLabel();
        }

        // ---------- visuals ----------

        void EnsureView(DockItem item)
        {
            if (item.View != null) return;
            if (item.Kind == "separator")
            {
                item.View = new Grid { Width = SepWidth, Height = BaseSize, VerticalAlignment = VerticalAlignment.Bottom };
                item.View.Children.Add(new Border { Width = 1, Margin = new Thickness(0, 4, 0, 4), Background = Theme.B(Theme.Separator) });
                return;
            }

            item.Size = item.TargetSize = BaseSize;
            var view = new NamedGrid
            {
                Width = BaseSize,
                Height = BaseSize,
                Margin = new Thickness(Spacing / 2, 0, Spacing / 2, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brushes.Transparent
            };
            item.Bounce = new TranslateTransform();
            view.RenderTransform = item.Bounce;
            view.Children.Add(CreateIcon(item));

            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Theme.B(Theme.Text),
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -6),
                Visibility = Visibility.Hidden
            };
            item.Dot = dot;
            view.Children.Add(dot);

            view.MouseEnter += (s, e) =>
            {
                hovered = item;
                UpdateLabel();
            };
            view.MouseLeave += (s, e) =>
            {
                if (hovered != item) return;
                hovered = null;
                UpdateLabel();
            };
            System.Windows.Automation.AutomationProperties.SetName(view, item.Name); // lets tests and screen readers find icons
            view.MouseLeftButtonUp += (s, e) => OnClick(item);
            view.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                ShowMenu(item);
            };
            item.View = view;
        }

        static FrameworkElement CreateIcon(DockItem item)
        {
            if (item.Kind == "launchpad") return LaunchpadIcon();
            ImageSource source = IconLoader.Get(item.Kind == "trash" ? RecycleBin : item.Target, 128);
            if (source == null) return Placeholder(item.Name);
            var image = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        static FrameworkElement LaunchpadIcon()
        {
            string[] colors = { "#FF5F57", "#FFBD2E", "#28C840", "#0A84FF", "#BF5AF2", "#FF9F0A", "#64D2FF", "#FF375F", "#30D158" };
            var grid = new UniformGrid { Rows = 3, Columns = 3, Margin = new Thickness(11) };
            foreach (string color in colors)
                grid.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(2.5),
                    Background = Theme.B((Color)ColorConverter.ConvertFromString(color))
                });
            var tile = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush(Color.FromRgb(0x5E, 0x67, 0x74), Color.FromRgb(0x2E, 0x33, 0x3B), 90),
                Child = grid
            };
            return new Viewbox { Child = new Border { Padding = new Thickness(4), Child = tile } };
        }

        static FrameworkElement Placeholder(string name)
        {
            var tile = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush(Color.FromRgb(0x8E, 0x8E, 0x93), Color.FromRgb(0x48, 0x48, 0x4A), 90),
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(),
                    FontSize = 32,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            return new Viewbox { Child = new Border { Padding = new Thickness(4), Child = tile } };
        }

        // ---------- magnification ----------

        void Magnify(double mouseX)
        {
            double offset = (root.ActualWidth - totalBaseWidth) / 2;
            foreach (DockItem item in entries)
            {
                if (item.Kind == "separator") continue;
                double factor = 0;
                if (!double.IsNaN(mouseX))
                {
                    double distance = Math.Abs(mouseX - (offset + item.BaseCenter));
                    if (distance < Range) factor = (Math.Cos(Math.PI * distance / Range) + 1) / 2;
                }
                item.TargetSize = BaseSize + (MaxSize - BaseSize) * factor;
            }
            if (animating) return;
            animating = true;
            CompositionTarget.Rendering += OnRendering;
        }

        void OnRendering(object sender, EventArgs e)
        {
            bool busy = false;
            foreach (DockItem item in entries)
            {
                if (item.Kind == "separator" || item.View == null) continue;
                double diff = item.TargetSize - item.Size;
                if (Math.Abs(diff) > 0.25)
                {
                    item.Size += diff * 0.35;
                    busy = true;
                }
                else item.Size = item.TargetSize;
                item.View.Width = item.Size;
                item.View.Height = item.Size;
            }
            UpdateLabel();
            if (busy) return;
            CompositionTarget.Rendering -= OnRendering;
            animating = false;
        }

        void UpdateLabel()
        {
            if (hovered == null || hovered.View == null || hovered.View.Parent != panel)
            {
                label.Visibility = Visibility.Collapsed;
                return;
            }
            labelText.Text = hovered.Name;
            label.Visibility = Visibility.Visible;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point top = hovered.View.TranslatePoint(new Point(hovered.View.Width / 2, 0), root);
            Canvas.SetLeft(label, top.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, top.Y - label.DesiredSize.Height - 10);
        }

        void BounceIcon(DockItem item)
        {
            var animation = new DoubleAnimation(0, -BaseSize * 0.4, TimeSpan.FromMilliseconds(280))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            item.Bounce.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        // ---------- clicks ----------

        void OnClick(DockItem item)
        {
            if (item.Kind == "launchpad")
            {
                Shell.StartMenu();
                return;
            }
            if (item.Kind == "trash")
            {
                Shell.Open(item.Target);
                return;
            }
            if (!item.IsRunning)
            {
                Shell.Open(item.Target, item.Args);
                BounceIcon(item);
                return;
            }

            // Several windows: show live previews of all of them to pick from.
            if (item.Windows.Count > 1)
            {
                WindowPicker.Toggle(item.Windows.ToList(), ScreenRect(item.View));
                return;
            }
            IntPtr foreground = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOTOWNER);
            if (item.Windows[0] == foreground) Native.ShowWindow(foreground, Native.SW_MINIMIZE);
            else Genie.Activate(item.Windows[0]);
        }

        void ShowMenu(DockItem item)
        {
            if (item.Kind == "separator") return;
            var menu = new ContextMenu { PlacementTarget = item.View, Placement = PlacementMode.Top };
            menuOpen = true;
            menu.Closed += (s, e) => menuOpen = false;

            if (item.Kind == "trash")
            {
                Menus.Add(menu, "Open", () => Shell.Open(item.Target));
                Menus.Add(menu, "Empty Trash", () => Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0));
            }
            else if (item.Kind == "launchpad")
            {
                Menus.Add(menu, "Open Start Menu", Shell.StartMenu);
                Menus.Add(menu, "Customize Dock…", () => Shell.Open("notepad.exe", "\"" + ConfigPath + "\""));
            }
            else
            {
                foreach (IntPtr handle in item.Windows)
                {
                    IntPtr h = handle;
                    string title = Native.GetTitle(h);
                    if (title.Length > 60) title = title.Substring(0, 57) + "…";
                    Menus.Add(menu, title, () => Genie.Activate(h));
                }
                if (item.IsRunning) menu.Items.Add(new Separator());

                bool running = item.IsRunning;
                Menus.Add(menu, running ? "New Window" : "Open", () =>
                {
                    Shell.Open(item.Target, item.Args);
                    if (!running) BounceIcon(item);
                });
                if (File.Exists(item.Target))
                    Menus.Add(menu, "Show in File Explorer", () => Shell.Open("explorer.exe", "/select,\"" + item.Target + "\""));
                Menus.Add(menu, "Keep in Dock", () => TogglePin(item)).IsChecked = item.Pinned;
                if (running)
                {
                    menu.Items.Add(new Separator());
                    Menus.Add(menu, "Quit", () =>
                    {
                        foreach (IntPtr h in item.Windows) TrafficLights.Close(h);
                    });
                }
            }
            menu.IsOpen = true;
        }

        void TogglePin(DockItem item)
        {
            if (item.Pinned)
            {
                pinned.Remove(item);
                item.Pinned = false;
            }
            else
            {
                runningOrder.Remove(item);
                item.Pinned = true;
                pinned.Add(item);
            }
            SaveConfig();
            Refresh();
        }

        // ---------- dock.txt ----------

        void LoadConfig()
        {
            if (!File.Exists(ConfigPath)) File.WriteAllLines(ConfigPath, DefaultConfig(), Encoding.UTF8);
            var list = new List<DockItem>();
            foreach (string raw in File.ReadAllLines(ConfigPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split('|');
                if (parts.Length < 2 || parts[1].Trim().Length == 0) continue;
                list.Add(new DockItem
                {
                    Name = parts[0].Trim(),
                    Target = Environment.ExpandEnvironmentVariables(parts[1].Trim()),
                    Args = parts.Length > 2 ? parts[2].Trim() : null,
                    MatchExe = parts.Length > 3 ? parts[3].Trim() : null,
                    Pinned = true
                });
            }
            pinned = list;
        }

        void SaveConfig()
        {
            ignoreConfigUntil = DateTime.Now.AddSeconds(2);
            var lines = new List<string>(ConfigHeader);
            foreach (DockItem item in pinned)
                lines.Add(string.Join("|", item.Name, item.Target, item.Args ?? "", item.MatchExe ?? ""));
            try { File.WriteAllLines(ConfigPath, lines, Encoding.UTF8); }
            catch (Exception ex) { App.Log(ex); }
        }

        void ReloadConfig()
        {
            try { LoadConfig(); }
            catch (IOException) { reloadTimer.Start(); return; } // still being saved, try again
            runningOrder.Clear();
            entries = new List<DockItem>();
            Refresh();
        }

        void WatchConfig()
        {
            watcher = new FileSystemWatcher(App.DataDir, "dock.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            FileSystemEventHandler changed = (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DateTime.Now < ignoreConfigUntil) return;
                reloadTimer.Stop();
                reloadTimer.Start();
            }));
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.EnableRaisingEvents = true;
        }

        static IEnumerable<string> DefaultConfig()
        {
            var lines = new List<string>(ConfigHeader);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            lines.Add("Finder|%WINDIR%\\explorer.exe||explorer.exe");
            AddFirstExisting(lines, "Microsoft Edge",
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            AddFirstExisting(lines, "Google Chrome",
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            AddFirstExisting(lines, "Firefox", Path.Combine(pf, @"Mozilla Firefox\firefox.exe"));
            lines.Add("Notepad|%WINDIR%\\System32\\notepad.exe||notepad.exe");
            lines.Add("Calculator|shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App||CalculatorApp.exe");
            if (File.Exists(Path.Combine(local, @"Microsoft\WindowsApps\wt.exe")))
                lines.Add("Terminal|shell:AppsFolder\\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App||WindowsTerminal.exe");
            lines.Add("App Store|shell:AppsFolder\\Microsoft.WindowsStore_8wekyb3d8bbwe!App||WinStore.App.exe");
            lines.Add("System Settings|shell:AppsFolder\\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel||SystemSettings.exe");
            return lines;
        }

        static void AddFirstExisting(List<string> lines, string name, params string[] paths)
        {
            string path = paths.FirstOrDefault(File.Exists);
            if (path != null) lines.Add(name + "|" + path + "||");
        }
    }
}
