using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace MacUI
{
    // The macOS-style bar across the top of the screen.
    internal sealed class MenuBar : Window
    {
        public const double BarHeight = 26;

        const string AppleLogoPath =
            "M8,4.5 C6.8,3.6 4.8,3.4 3.5,4.6 C1.8,6.2 2.3,9.6 3.6,11.8 C4.6,13.5 5.7,14.6 6.8,14.2 " +
            "C7.4,14 7.7,13.8 8,13.8 C8.3,13.8 8.6,14 9.2,14.2 C10.3,14.6 11.4,13.5 12.4,11.8 " +
            "C12.9,11 13.2,10.2 13.4,9.4 C12.2,8.9 11.6,7.8 11.6,6.7 C11.6,5.7 12.1,4.9 12.8,4.4 " +
            "C11.4,3.3 9.3,3.5 8,4.5 Z M8.2,3.6 C8.2,2.3 9.2,1.2 10.6,1 C10.7,2.4 9.6,3.6 8.2,3.6 Z";

        readonly AppBar appBar;
        readonly TextBlock appName;
        readonly TextBlock clock;
        readonly TextBlock batteryText;
        readonly Border batteryFill;
        readonly FrameworkElement batteryView;
        readonly TextBlock networkIcon;
        readonly Brush textBrush = Theme.B(Theme.Text);
        int ticks;

        public MenuBar()
        {
            Title = "MacUI Menu Bar";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Theme.B(Theme.MenuBarBg);
            Foreground = textBrush;
            FontFamily = Theme.UiFont;
            FontSize = 13;
            Reposition();

            var root = new DockPanel { LastChildFill = false, Margin = new Thickness(8, 0, 8, 0) };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(left, Dock.Left);
            var logo = new Path
            {
                Data = Geometry.Parse(AppleLogoPath),
                Fill = textBrush,
                Width = 14,
                Height = 16,
                Stretch = Stretch.Uniform
            };
            left.Children.Add(Item(logo, OpenAppleMenu));
            appName = new TextBlock { Text = "Finder", FontWeight = FontWeights.Bold };
            left.Children.Add(Item(appName, null));
            AddAppMenu(left, "File", 'F');
            AddAppMenu(left, "Edit", 'E');
            AddAppMenu(left, "View", 'V');
            AddAppMenu(left, "Window", 'W');
            AddAppMenu(left, "Help", 'H');
            root.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(right, Dock.Right);

            batteryText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            batteryFill = new Border { CornerRadius = new CornerRadius(1.5), Background = textBrush, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var batteryPanel = new StackPanel { Orientation = Orientation.Horizontal };
            batteryPanel.Children.Add(batteryText);
            batteryPanel.Children.Add(BatteryIcon(batteryFill));
            batteryView = Item(batteryPanel, el => Shell.Open("ms-settings:batterysaver"));
            right.Children.Add(batteryView);

            networkIcon = Glyph("");
            right.Children.Add(Item(networkIcon, el => Shell.QuickSettings()));
            right.Children.Add(Item(Glyph(""), el => Shell.QuickSettings()));
            right.Children.Add(Item(Glyph(""), el => App.Spotlight.Toggle()));
            right.Children.Add(Item(ControlCenterIcon(), el => Shell.QuickSettings()));
            clock = new TextBlock();
            right.Children.Add(Item(clock, el => Shell.NotificationCenter()));
            root.Children.Add(right);

            Content = root;

            appBar = new AppBar(this, Native.ABE_TOP, BarHeight);
            appBar.FullscreenChanged += App.SetFullscreen;
            appBar.ShellRestarted += App.OnShellRestarted;
            SourceInitialized += (s, e) =>
            {
                WindowStyles.MakeToolWindow(this, true);
                appBar.Attach();
            };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Tick();
            timer.Start();
            Tick();
        }

        public void Reposition()
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = BarHeight;
            if (appBar != null) appBar.UpdatePosition();
        }

        public void RemoveAppBar()
        {
            appBar.Remove();
        }

        FrameworkElement Item(FrameworkElement content, Action<FrameworkElement> click)
        {
            content.VerticalAlignment = VerticalAlignment.Center;
            var border = new Border
            {
                Child = content,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(1, 3, 1, 3),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent
            };
            if (click != null)
            {
                Brush hover = Theme.B(Theme.Hover);
                border.MouseEnter += (s, e) => border.Background = hover;
                border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
                border.MouseLeftButtonUp += (s, e) => click(border);
            }
            return border;
        }

        // Clicking File/Edit/... opens that menu in the active app (Alt+letter).
        void AddAppMenu(Panel panel, string text, char key)
        {
            panel.Children.Add(Item(new TextBlock { Text = text }, el => Native.SendKeys(Native.VK_MENU, (byte)key)));
        }

        TextBlock Glyph(string glyph)
        {
            return new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 14 };
        }

        FrameworkElement BatteryIcon(Border fill)
        {
            var body = new Border
            {
                Width = 24,
                Height = 12,
                BorderThickness = new Thickness(1),
                BorderBrush = textBrush,
                CornerRadius = new CornerRadius(3.5),
                Padding = new Thickness(2),
                Child = fill,
                Opacity = 0.9
            };
            var nub = new Border
            {
                Width = 2,
                Height = 4,
                Background = textBrush,
                CornerRadius = new CornerRadius(0, 1, 1, 0),
                Margin = new Thickness(1, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.5
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(body);
            panel.Children.Add(nub);
            return panel;
        }

        FrameworkElement ControlCenterIcon()
        {
            var grid = new Grid { Width = 16, Height = 14 };
            grid.Children.Add(Toggle(VerticalAlignment.Top, HorizontalAlignment.Right));
            grid.Children.Add(Toggle(VerticalAlignment.Bottom, HorizontalAlignment.Left));
            return grid;
        }

        FrameworkElement Toggle(VerticalAlignment row, HorizontalAlignment knob)
        {
            return new Border
            {
                Width = 15,
                Height = 6.5,
                CornerRadius = new CornerRadius(3.25),
                BorderBrush = textBrush,
                BorderThickness = new Thickness(1.2),
                VerticalAlignment = row,
                Child = new Ellipse { Width = 3, Height = 3, Fill = textBrush, HorizontalAlignment = knob, Margin = new Thickness(0.6, 0, 0.6, 0) }
            };
        }

        void OpenAppleMenu(FrameworkElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, HorizontalOffset = -10, VerticalOffset = -6 };
            Menus.Add(menu, "About This PC", () => Shell.Open("ms-settings:about"));
            menu.Items.Add(new Separator());
            Menus.Add(menu, "System Settings…", () => Shell.Open("ms-settings:"));
            Menus.Add(menu, "App Store…", () => Shell.Open("ms-windows-store:"));
            menu.Items.Add(new Separator());
            Menus.Add(menu, "Force Quit…", () => Shell.Open("taskmgr.exe"), "Ctrl+Shift+Esc");
            menu.Items.Add(new Separator());
            Menus.Add(menu, "Sleep", () => Forms.Application.SetSuspendState(Forms.PowerState.Suspend, false, false));
            Menus.Add(menu, "Restart…", () => Power("restart your computer", "/r /t 0"));
            Menus.Add(menu, "Shut Down…", () => Power("shut down your computer", "/s /t 0"));
            menu.Items.Add(new Separator());
            Menus.Add(menu, "Lock Screen", () => Native.LockWorkStation(), "Win+L");
            Menus.Add(menu, "Log Out " + Environment.UserName + "…", () => Power("log out", "/l"));
            menu.Items.Add(new Separator());
            Menus.Add(menu, "Customize Dock…", () => Shell.Open("notepad.exe", "\"" + DockWindow.ConfigPath + "\""));
            Menus.Add(menu, "Open MacUI at Login", () => Startup.Enabled = !Startup.Enabled).IsChecked = Startup.Enabled;
            Menus.Add(menu, "Quit MacUI", App.Quit);
            menu.IsOpen = true;
        }

        static void Power(string what, string args)
        {
            if (MessageBox.Show("Are you sure you want to " + what + " now?", "MacUI",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
        }

        void Tick()
        {
            DateTime now = DateTime.Now;
            CultureInfo culture = CultureInfo.CurrentCulture;
            clock.Text = now.ToString("ddd d MMM", culture) + "   " + now.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
            UpdateActiveApp();
            if (ticks++ % 5 == 0)
            {
                UpdateBattery();
                UpdateNetwork();
            }
        }

        void UpdateActiveApp()
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;
            IntPtr root = Native.GetAncestor(foreground, Native.GA_ROOTOWNER);
            if (root == IntPtr.Zero) root = foreground;
            if (WindowTracker.IsOwnWindow(root)) return;

            if (WindowTracker.IsShellWindow(Native.GetClassName(root))) appName.Text = "Finder";
            else
            {
                string path = WindowTracker.GetExePath(root);
                if (path != null) appName.Text = AppInfo.DisplayName(path);
            }
        }

        void UpdateBattery()
        {
            Forms.PowerStatus status = Forms.SystemInformation.PowerStatus;
            if ((status.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) != 0)
            {
                batteryView.Visibility = Visibility.Collapsed;
                return;
            }
            batteryView.Visibility = Visibility.Visible;
            float level = Math.Max(0f, Math.Min(1f, status.BatteryLifePercent));
            bool charging = status.PowerLineStatus == Forms.PowerLineStatus.Online;
            batteryText.Text = (int)Math.Round(level * 100) + "%";
            batteryFill.Width = Math.Max(1, 18 * level);
            batteryFill.Background = charging ? Theme.B(Color.FromRgb(0x30, 0xD1, 0x58))
                : level < 0.2 ? Theme.B(Color.FromRgb(0xFF, 0x45, 0x3A)) : textBrush;
        }

        void UpdateNetwork()
        {
            bool any = false, wifi = false;
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    any = true;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) wifi = true;
                }
            }
            catch { }
            networkIcon.Text = wifi || !any ? "" : "";
            networkIcon.Opacity = any ? 1 : 0.35;
        }
    }
}
