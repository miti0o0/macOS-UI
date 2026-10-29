using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MacUI
{
    // The macOS app switcher: hold Alt, press Tab to move, let go of Alt to switch.
    // While it is open: Shift+Tab or arrows move, Q quits the selected app, Esc cancels.
    internal sealed class AppSwitcher : Window
    {
        sealed class Entry
        {
            public string ExePath;
            public string Name;
            public ImageSource Icon;
            public readonly List<IntPtr> Windows = new List<IntPtr>();
        }

        readonly StackPanel row;
        readonly DispatcherTimer altWatch;
        readonly Brush highlight = Theme.B(Theme.Dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0, 0, 0));
        Native.LowLevelKeyboardProc hookProc;
        IntPtr hook;
        List<Entry> entries = new List<Entry>();
        int selected;
        bool open;

        public AppSwitcher()
        {
            Title = "MacUI App Switcher";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Theme.UiFont;
            Foreground = Theme.B(Theme.Text);

            row = new StackPanel { Orientation = Orientation.Horizontal };
            Content = new Border
            {
                CornerRadius = new CornerRadius(24),
                Background = Theme.B(Theme.Dark ? Color.FromArgb(0xE0, 0x1E, 0x1E, 0x20) : Color.FromArgb(0xE8, 0xF0, 0xF0, 0xF2)),
                BorderBrush = Theme.B(Theme.PanelBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 14, 14, 8),
                Margin = new Thickness(30),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35 },
                Child = row
            };

            SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                WindowStyles.MakeToolWindow(h, true);
                Native.SetDwmInt(h, Native.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            };
            SizeChanged += (s, e) => Center();

            // Safety net in case the Alt release was never seen (e.g. it happened in an admin window).
            altWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            altWatch.Tick += (s, e) =>
            {
                if (open && (Native.GetAsyncKeyState(0x12) & 0x8000) == 0) Commit();
            };
        }

        public void Install()
        {
            hookProc = HookProc;
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc, Native.GetModuleHandle(null), 0);
        }

        public void Uninstall()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var key = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int message = wParam.ToInt32();
                if (HandleKey(key.vkCode, key.flags, message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN))
                    return (IntPtr)1; // swallow the key
            }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        // Runs inside the keyboard hook, so it must decide quickly; UI work is queued.
        bool HandleKey(uint vk, uint flags, bool down)
        {
            if (vk == Native.VK_UNASSIGNED) return false;

            // Alt+` switches between the windows of the app you are in (Cmd+` on a Mac).
            if (vk == Native.VK_BACKTICK && !open && (flags & Native.LLKHF_ALTDOWN) != 0)
            {
                if (down)
                {
                    Native.SendKeys((byte)Native.VK_UNASSIGNED);
                    bool back = (Native.GetAsyncKeyState((int)Native.VK_SHIFT) & 0x8000) != 0;
                    Dispatcher.BeginInvoke(new Action(() => CycleWindows(back)));
                }
                return true;
            }

            if (vk == Native.VK_TAB)
            {
                if (down && (flags & Native.LLKHF_ALTDOWN) != 0)
                {
                    bool back = (Native.GetAsyncKeyState((int)Native.VK_SHIFT) & 0x8000) != 0;
                    if (!open)
                    {
                        open = true;
                        // A harmless extra key so releasing Alt doesn't open the app's menu bar.
                        Native.SendKeys((byte)Native.VK_UNASSIGNED);
                        Dispatcher.BeginInvoke(new Action(() => Open(back)));
                    }
                    else Dispatcher.BeginInvoke(new Action(() => Step(back ? -1 : 1)));
                    return true;
                }
                return open;
            }

            if (!open) return false;
            if (!down)
            {
                if (vk == Native.VK_LMENU || vk == Native.VK_RMENU || vk == 0x12)
                    Dispatcher.BeginInvoke(new Action(Commit));
                return false;
            }
            switch (vk)
            {
                case Native.VK_ESCAPE:
                    Dispatcher.BeginInvoke(new Action(Cancel));
                    return true;
                case Native.VK_LEFT:
                    Dispatcher.BeginInvoke(new Action(() => Step(-1)));
                    return true;
                case Native.VK_RIGHT:
                    Dispatcher.BeginInvoke(new Action(() => Step(1)));
                    return true;
                case Native.VK_DOWN:
                    Dispatcher.BeginInvoke(new Action(ShowWindowsOfSelected));
                    return true;
                case 0x51: // Q
                    Dispatcher.BeginInvoke(new Action(QuitSelected));
                    return true;
            }
            return false;
        }

        void Open(bool back)
        {
            if (!open) return;
            entries = BuildEntries();
            if (entries.Count == 0)
            {
                open = false;
                return;
            }
            selected = entries.Count == 1 ? 0 : back ? entries.Count - 1 : 1;
            Render();
            Show();
            Center();
            altWatch.Start();
        }

        static List<Entry> BuildEntries()
        {
            var result = new List<Entry>();
            foreach (AppWindow window in WindowTracker.GetAppWindows()) // most recently used first
            {
                Entry entry = result.FirstOrDefault(e => string.Equals(e.ExePath, window.ExePath, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entry = new Entry
                    {
                        ExePath = window.ExePath,
                        Name = AppInfo.DisplayName(window.ExePath),
                        Icon = App.Dock.IconFor(window.ExePath)
                    };
                    result.Add(entry);
                }
                entry.Windows.Add(window.Handle);
            }
            return result;
        }

        void Step(int delta)
        {
            if (!open || entries.Count == 0) return;
            selected = (selected + delta + entries.Count) % entries.Count;
            Render();
        }

        void Render()
        {
            row.Children.Clear();
            double maxWidth = SystemParameters.PrimaryScreenWidth * 0.9 - 90;
            double cell = Math.Min(124, maxWidth / Math.Max(1, entries.Count));
            double iconSize = cell * 0.8;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                int index = i;
                var tile = new Border
                {
                    Width = cell,
                    Height = cell,
                    CornerRadius = new CornerRadius(cell * 0.14),
                    Background = i == selected ? highlight : Brushes.Transparent
                };
                if (entry.Icon != null)
                {
                    var image = new Image { Source = entry.Icon, Width = iconSize, Height = iconSize };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    tile.Child = image;
                }
                else tile.Child = new TextBlock
                {
                    Text = entry.Name.Substring(0, 1),
                    FontSize = iconSize / 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var name = new TextBlock
                {
                    Text = entry.Name,
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Width = cell + 40,
                    Margin = new Thickness(-20, 6, -20, 0),
                    Visibility = i == selected ? Visibility.Visible : Visibility.Hidden
                };

                var column = new StackPanel { Background = Brushes.Transparent };
                column.Children.Add(tile);
                column.Children.Add(name);
                column.MouseEnter += (s, e) =>
                {
                    selected = index;
                    Render();
                };
                column.MouseLeftButtonUp += (s, e) =>
                {
                    selected = index;
                    Commit();
                };
                row.Children.Add(column);
            }
        }

        void Center()
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - ActualHeight) / 2;
        }

        void Commit()
        {
            if (!open) return;
            open = false;
            altWatch.Stop();
            Hide();
            if (selected < 0 || selected >= entries.Count) return;

            Entry entry = entries[selected];
            IntPtr visible = entry.Windows.FirstOrDefault(h => !Native.IsIconic(h));
            if (visible != IntPtr.Zero) WindowTracker.Activate(visible);
            else Genie.Activate(entry.Windows[0]);
        }

        // Down arrow in the switcher: previews of every window of the selected app.
        void ShowWindowsOfSelected()
        {
            if (!open || selected >= entries.Count) return;
            List<IntPtr> windows = entries[selected].Windows;
            Cancel();
            WindowPicker.Open(windows, Rect.Empty);
        }

        static void CycleWindows(bool back)
        {
            IntPtr foreground = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOTOWNER);
            string exe = WindowTracker.GetExePath(foreground);
            if (exe == null) return;
            List<IntPtr> windows = WindowTracker.GetAppWindows()
                .Where(w => string.Equals(w.ExePath, exe, StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Handle).ToList();
            if (windows.Count < 2) return;
            // Windows are listed front to back, so the back-most one is "next".
            Genie.Activate(back ? windows[1] : windows[windows.Count - 1]);
        }

        void Cancel()
        {
            open = false;
            altWatch.Stop();
            Hide();
        }

        void QuitSelected()
        {
            if (!open || selected >= entries.Count) return;
            foreach (IntPtr h in entries[selected].Windows) TrafficLights.Close(h);
            entries.RemoveAt(selected);
            if (entries.Count == 0)
            {
                Cancel();
                return;
            }
            selected = Math.Min(selected, entries.Count - 1);
            Render();
        }
    }

    // Listens to system-wide window events (no code is injected into other apps).
    internal static class WindowEvents
    {
        static Native.WinEventDelegate proc;
        static IntPtr systemHook, objectHook;

        public static void Install()
        {
            proc = OnEvent;
            systemHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, proc, 0, 0, Native.WINEVENT_SKIPOWNPROCESS);
            objectHook = Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, proc, 0, 0, Native.WINEVENT_SKIPOWNPROCESS);
        }

        public static void Uninstall()
        {
            if (systemHook != IntPtr.Zero) Native.UnhookWinEvent(systemHook);
            if (objectHook != IntPtr.Zero) Native.UnhookWinEvent(objectHook);
            systemHook = objectHook = IntPtr.Zero;
        }

        static void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
            try
            {
                switch (eventType)
                {
                    case Native.EVENT_SYSTEM_FOREGROUND:
                    case Native.EVENT_SYSTEM_MINIMIZEEND:
                        WindowBounds.Update(hwnd);
                        Decorations.Refresh();
                        break;
                    case Native.EVENT_SYSTEM_MINIMIZESTART:
                        Decorations.OnGone(hwnd);
                        Genie.PlayMinimize(hwnd);
                        break;
                    case Native.EVENT_OBJECT_LOCATIONCHANGE:
                        WindowBounds.UpdateIfTracked(hwnd);
                        Decorations.OnMoved(hwnd);
                        break;
                    case Native.EVENT_OBJECT_HIDE:
                        Decorations.OnGone(hwnd);
                        break;
                    case Native.EVENT_OBJECT_DESTROY:
                        WindowBounds.Forget(hwnd);
                        Decorations.OnDestroyed(hwnd);
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }
    }
}
