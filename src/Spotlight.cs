using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MacUI
{
    public sealed class SpotlightResult
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public ImageSource Icon { get; set; }
        public string Glyph { get; set; }
        internal Action Run;
    }

    // Spotlight search (Alt+Space): apps, quick maths, websites and web search.
    internal sealed class Spotlight : Window
    {
        const int HotkeyId = 0x4D55;

        sealed class AppEntry
        {
            public string Name;
            public string Parsing;
        }

        readonly TextBox box;
        readonly TextBlock hint;
        readonly ListBox list;
        readonly Border divider;
        readonly object sync = new object();
        List<AppEntry> apps = new List<AppEntry>();
        DateTime indexedAt = DateTime.MinValue;
        bool indexing;
        IntPtr handle;

        public string HotkeyName { get; private set; }

        public Spotlight()
        {
            Title = "Spotlight";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 720;
            FontFamily = Theme.UiFont;
            Foreground = Theme.B(Theme.Text);

            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = Theme.B(Theme.PanelBg),
                BorderBrush = Theme.B(Theme.PanelBorder),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(30),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35 }
            };
            var stack = new StackPanel();

            var row = new Grid { Margin = new Thickness(18, 12, 18, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock
            {
                Text = "",
                FontFamily = Theme.IconFont,
                FontSize = 22,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            });
            hint = new TextBlock
            {
                Text = "Spotlight Search",
                FontSize = 24,
                Opacity = 0.4,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Margin = new Thickness(2, 0, 0, 0)
            };
            Grid.SetColumn(hint, 1);
            row.Children.Add(hint);
            box = new TextBox
            {
                FontSize = 24,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Foreground,
                CaretBrush = Foreground,
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            stack.Children.Add(row);

            divider = new Border { Height = 1, Background = Theme.B(Theme.Separator), Visibility = Visibility.Collapsed };
            stack.Children.Add(divider);

            list = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Margin = new Thickness(8),
                MaxHeight = 480,
                Focusable = false,
                Visibility = Visibility.Collapsed,
                ItemContainerStyle = (Style)Application.Current.Resources["SpotItem"],
                ItemTemplate = (DataTemplate)Application.Current.Resources["SpotTemplate"]
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            stack.Children.Add(list);

            card.Child = stack;
            Content = card;

            box.TextChanged += (s, e) => UpdateResults();
            box.PreviewKeyDown += OnKey;
            list.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var result = list.SelectedItem as SpotlightResult;
                if (result != null) Execute(result);
            };
            Deactivated += (s, e) => Hide();
        }

        public void Initialize()
        {
            handle = new WindowInteropHelper(this).EnsureHandle();
            WindowStyles.MakeToolWindow(handle, false);
            HwndSource.FromHwnd(handle).AddHook(WndProc);

            HotkeyName = "Alt+Space";
            if (!Native.RegisterHotKey(handle, HotkeyId, Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_SPACE))
            {
                HotkeyName = "Ctrl+Space";
                if (!Native.RegisterHotKey(handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_NOREPEAT, Native.VK_SPACE))
                    HotkeyName = null;
            }
            BuildIndex();
        }

        public void UnregisterHotkey()
        {
            Native.UnregisterHotKey(handle, HotkeyId);
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Open();
        }

        void Open()
        {
            if (DateTime.Now - indexedAt > TimeSpan.FromMinutes(5)) BuildIndex();
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = SystemParameters.PrimaryScreenHeight * 0.2;
            box.Text = "";
            UpdateResults();
            Show();
            // Opened from a key press that went to another app, so Windows needs the stronger way to hand over focus.
            WindowTracker.Activate(handle);
            Activate();
            box.Focus();
            Keyboard.Focus(box);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Hide();
                    e.Handled = true;
                    break;
                case Key.Down:
                    Move(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Move(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    var result = list.SelectedItem as SpotlightResult;
                    if (result != null) Execute(result);
                    e.Handled = true;
                    break;
            }
        }

        void Move(int delta)
        {
            if (list.Items.Count == 0) return;
            list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + delta));
            list.ScrollIntoView(list.SelectedItem);
        }

        void Execute(SpotlightResult result)
        {
            Hide();
            try { result.Run(); }
            catch (Exception ex) { App.Log(ex); }
        }

        void UpdateResults()
        {
            string query = box.Text.Trim();
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var results = new List<SpotlightResult>();

            if (query.Length > 0)
            {
                string answer = TryCalculate(query);
                if (answer != null)
                    results.Add(new SpotlightResult
                    {
                        Title = "= " + answer,
                        Subtitle = "Calculator — press Enter to copy",
                        Glyph = "",
                        Run = () => Clipboard.SetText(answer)
                    });

                List<AppEntry> snapshot;
                lock (sync) snapshot = apps;
                string lower = query.ToLowerInvariant();
                var matches = snapshot
                    .Select(a => new { App = a, Score = Score(a.Name, lower) })
                    .Where(m => m.Score >= 0)
                    .OrderBy(m => m.Score)
                    .ThenBy(m => m.App.Name.Length)
                    .Take(8);
                foreach (var match in matches)
                {
                    AppEntry app = match.App;
                    results.Add(new SpotlightResult
                    {
                        Title = app.Name,
                        Subtitle = "Application",
                        Icon = IconLoader.Get(app.Parsing, 64),
                        Run = () => Shell.Open(app.Parsing)
                    });
                }

                if (Directory.Exists(query) || File.Exists(query))
                    results.Add(new SpotlightResult { Title = query, Subtitle = "Open", Icon = IconLoader.Get(query, 64), Run = () => Shell.Open(query) });
                else if (LooksLikeUrl(query))
                {
                    string url = query.Contains("://") ? query : "https://" + query;
                    results.Add(new SpotlightResult { Title = url, Subtitle = "Open website", Glyph = "", Run = () => Shell.Open(url) });
                }

                results.Add(new SpotlightResult
                {
                    Title = "Search the web for “" + query + "”",
                    Subtitle = "Google",
                    Glyph = "",
                    Run = () => Shell.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(query))
                });
            }

            list.ItemsSource = results;
            Visibility visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = visibility;
            divider.Visibility = visibility;
            if (results.Count > 0) list.SelectedIndex = 0;
        }

        static int Score(string name, string query)
        {
            string n = name.ToLowerInvariant();
            if (n.StartsWith(query)) return 0;
            if (n.Contains(" " + query)) return 1;
            if (n.Contains(query)) return 2;
            string initials = new string(n.Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).ToArray());
            if (query.Length > 1 && initials.StartsWith(query)) return 3;
            return -1;
        }

        static string TryCalculate(string query)
        {
            if (!Regex.IsMatch(query, @"^[\d\s\.,\+\-\*/\(\)%]+$") || !Regex.IsMatch(query, @"\d\s*[\+\-\*/%]")) return null;
            try
            {
                object value = new DataTable().Compute(query.Replace(',', '.'), null);
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number)) return null;
                return number.ToString("G12", CultureInfo.CurrentCulture);
            }
            catch
            {
                return null;
            }
        }

        static bool LooksLikeUrl(string query)
        {
            if (query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
            return !query.Contains(" ") && Regex.IsMatch(query, @"^[\w-]+(\.[\w-]+)*\.[a-zA-Z]{2,}(/.*)?$");
        }

        // Reads every installed app (desktop and Store) from the shell's AppsFolder.
        void BuildIndex()
        {
            if (indexing) return;
            indexing = true;
            var thread = new Thread(() =>
            {
                try
                {
                    var found = new List<AppEntry>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    dynamic folder = shell.NameSpace("shell:AppsFolder");
                    foreach (dynamic item in folder.Items())
                    {
                        string name = item.Name;
                        string path = item.Path;
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;
                        if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) || !seen.Add(name)) continue;
                        found.Add(new AppEntry { Name = name, Parsing = "shell:AppsFolder\\" + path });
                    }
                    lock (sync) apps = found;
                    indexedAt = DateTime.Now;
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                }
                finally
                {
                    indexing = false;
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
