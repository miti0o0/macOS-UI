using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MacUI
{
    // Live previews of all windows of one app (like macOS App Exposé). Click one to bring it forward.
    internal sealed class WindowPicker : Window
    {
        const double TileWidth = 240, ThumbHeight = 150, ThumbPad = 6;

        sealed class Tile
        {
            public IntPtr Window;
            public Border Frame;
            public Border ThumbArea;
            public UIElement CloseButton;
            public IntPtr Thumb;
        }

        public static WindowPicker Current { get; private set; }

        readonly List<Tile> tiles = new List<Tile>();
        readonly WrapPanel panel;
        readonly Rect anchor; // screen pixels; empty = centre of the screen
        readonly Brush selectedBrush = Theme.B(Color.FromArgb(0x66, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
        IntPtr hwnd;
        int selected;
        bool closing;

        public static void Toggle(IList<IntPtr> windows, Rect anchor)
        {
            if (Current != null)
            {
                Current.Dismiss();
                return;
            }
            Open(windows, anchor);
        }

        public static void Open(IList<IntPtr> windows, Rect anchor)
        {
            if (Current != null) Current.Dismiss();
            if (windows.Count == 0) return;
            Current = new WindowPicker(windows, anchor);
            Current.Show();
            WindowTracker.Activate(Current.hwnd);
            Current.Activate();
        }

        WindowPicker(IList<IntPtr> windows, Rect anchor)
        {
            this.anchor = anchor;
            Title = "MacUI Windows";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Theme.UiFont;
            Foreground = Theme.B(Theme.Text);
            Left = -32000;
            Top = -32000;

            panel = new WrapPanel { MaxWidth = SystemParameters.PrimaryScreenWidth * 0.85 };
            Content = new Border
            {
                Margin = new Thickness(24),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(16),
                Background = Theme.B(Theme.PanelBg),
                BorderBrush = Theme.B(Theme.PanelBorder),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35 },
                Child = panel
            };
            foreach (IntPtr w in windows) AddTile(w);
            Select(0);

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                // See-through window (like the genie overlay) so DWM can draw the live previews on it.
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
                Native.SetDwmInt(hwnd, Native.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
                Native.SetDwmInt(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, 1);
                Native.SetDwmInt(hwnd, Native.DWMWA_BORDER_COLOR, unchecked((int)0xFFFFFFFE));
                WindowStyles.MakeToolWindow(hwnd, false);
            };
            SizeChanged += (s, e) =>
            {
                Position();
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdateThumbs));
            };
            Deactivated += (s, e) => Dismiss();
            PreviewKeyDown += OnKey;
            Closed += (s, e) =>
            {
                foreach (Tile tile in tiles)
                    if (tile.Thumb != IntPtr.Zero) Native.DwmUnregisterThumbnail(tile.Thumb);
                if (Current == this) Current = null;
            };
        }

        void AddTile(IntPtr window)
        {
            var tile = new Tile { Window = window };
            tile.ThumbArea = new Border
            {
                Width = TileWidth,
                Height = ThumbHeight,
                CornerRadius = new CornerRadius(8),
                Background = Theme.B(Theme.Hover)
            };

            string text = Native.GetTitle(window);
            var title = new TextBlock
            {
                Text = text.Length > 0 ? text : AppInfo.DisplayName(WindowTracker.GetExePath(window)),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var close = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = Theme.B(Color.FromRgb(0xFF, 0x5F, 0x57)),
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Hidden,
                Cursor = Cursors.Hand,
                ToolTip = "Close window",
                Child = new TextBlock
                {
                    Text = "",
                    FontFamily = Theme.IconFont,
                    FontSize = 8,
                    Foreground = Theme.B(Color.FromRgb(0x4D, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            tile.CloseButton = close;
            var titleRow = new DockPanel { Margin = new Thickness(2, 6, 2, 0), Width = TileWidth };
            DockPanel.SetDock(close, Dock.Right);
            titleRow.Children.Add(close);
            titleRow.Children.Add(title);

            var stack = new StackPanel();
            stack.Children.Add(tile.ThumbArea);
            stack.Children.Add(titleRow);
            tile.Frame = new Border
            {
                Padding = new Thickness(6),
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                Child = stack
            };

            tile.Frame.MouseEnter += (s, e) => Select(tiles.IndexOf(tile));
            tile.Frame.MouseLeftButtonUp += (s, e) => Choose(tile);
            close.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                TrafficLights.Close(tile.Window);
                RemoveTile(tile);
            };
            tiles.Add(tile);
            panel.Children.Add(tile.Frame);
        }

        void RemoveTile(Tile tile)
        {
            if (tile.Thumb != IntPtr.Zero) Native.DwmUnregisterThumbnail(tile.Thumb);
            tiles.Remove(tile);
            panel.Children.Remove(tile.Frame);
            if (tiles.Count == 0)
            {
                Dismiss();
                return;
            }
            Select(Math.Min(selected, tiles.Count - 1));
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdateThumbs));
        }

        void Select(int index)
        {
            if (index < 0 || index >= tiles.Count) return;
            selected = index;
            for (int i = 0; i < tiles.Count; i++)
            {
                tiles[i].Frame.Background = i == index ? selectedBrush : Brushes.Transparent;
                tiles[i].CloseButton.Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
            }
        }

        void Choose(Tile tile)
        {
            IntPtr window = tile.Window;
            Dismiss();
            Genie.Activate(window);
        }

        public void Dismiss()
        {
            if (closing) return;
            closing = true;
            if (Current == this) Current = null;
            Close();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            int columns = Math.Max(1, (int)(panel.ActualWidth / (TileWidth + 20)));
            switch (e.Key)
            {
                case Key.Escape: Dismiss(); break;
                case Key.Enter: if (selected < tiles.Count) Choose(tiles[selected]); break;
                case Key.Left: Select(selected - 1); break;
                case Key.Right: case Key.Tab: Select(selected + 1); break;
                case Key.Up: Select(selected - columns); break;
                case Key.Down: Select(selected + columns); break;
                default: return;
            }
            e.Handled = true;
        }

        // Above the Dock icon it came from, or in the middle of the screen.
        void Position()
        {
            double s = Dpi.Scale;
            double screenWidth = SystemParameters.PrimaryScreenWidth, screenHeight = SystemParameters.PrimaryScreenHeight;
            double left, top;
            if (anchor.IsEmpty)
            {
                left = (screenWidth - ActualWidth) / 2;
                top = (screenHeight - ActualHeight) / 2;
            }
            else
            {
                left = (anchor.Left + anchor.Width / 2) / s - ActualWidth / 2;
                top = anchor.Top / s - ActualHeight + 10;
            }
            Left = Math.Max(0, Math.Min(screenWidth - ActualWidth, left));
            Top = Math.Max(MenuBar.BarHeight, top);
        }

        // Places a live DWM thumbnail of each window inside its tile, keeping the window's shape.
        void UpdateThumbs()
        {
            if (hwnd == IntPtr.Zero || closing) return;
            double s = Dpi.Scale;
            foreach (Tile tile in tiles)
            {
                if (tile.Thumb == IntPtr.Zero && Native.DwmRegisterThumbnail(hwnd, tile.Window, out tile.Thumb) != 0) continue;

                Native.POINT size;
                Native.DwmQueryThumbnailSourceSize(tile.Thumb, out size);
                double boxWidth = TileWidth - ThumbPad * 2, boxHeight = ThumbHeight - ThumbPad * 2;
                double scale = size.X > 0 && size.Y > 0 ? Math.Min(boxWidth / size.X, boxHeight / size.Y) : 1;
                double width = size.X * scale, height = size.Y * scale;
                Point origin = tile.ThumbArea.TranslatePoint(new Point(0, 0), this);
                double x = origin.X + (TileWidth - width) / 2, y = origin.Y + (ThumbHeight - height) / 2;

                var props = new Native.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = false
                };
                props.rcDestination.Left = (int)Math.Round(x * s);
                props.rcDestination.Top = (int)Math.Round(y * s);
                props.rcDestination.Right = (int)Math.Round((x + width) * s);
                props.rcDestination.Bottom = (int)Math.Round((y + height) * s);
                Native.DwmUpdateThumbnailProperties(tile.Thumb, ref props);
            }
        }
    }
}
