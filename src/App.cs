using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MacUI
{
    internal static class App
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacUI");
        public static Spotlight Spotlight;
        public static DockWindow Dock;
        static MenuBar menuBar;
        static AppSwitcher switcher;
        static bool cleanedUp;

        [STAThread]
        static int Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            // MacUI.exe --quit     closes a running MacUI
            // MacUI.exe --restore  also makes sure the Windows taskbar is back
            if (mode == "--quit" || mode == "--restore")
            {
                EventWaitHandle running;
                if (EventWaitHandle.TryOpenExisting("MacUI.Quit", out running))
                {
                    running.Set();
                    Thread.Sleep(1500);
                }
                if (mode == "--restore")
                {
                    Taskbar.Restore();
                    SystemAnimation.Restore();
                }
                return 0;
            }

            bool createdNew;
            var mutex = new Mutex(true, "MacUI.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("MacUI is already running.\nUse the Apple menu → Quit MacUI to go back to normal Windows.", "MacUI");
                return 1;
            }
            var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "MacUI.Quit");

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = Theme.CreateResources();
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log(e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log(e.ExceptionObject as Exception);
                Cleanup();
            };
            app.SessionEnding += (s, e) => Cleanup();
            app.Exit += (s, e) => Cleanup();

            Taskbar.Hide();
            SystemAnimation.DisableMinimizeAnimation();
            menuBar = new MenuBar();
            menuBar.Show();
            Dock = new DockWindow();
            Dock.Show();
            Spotlight = new Spotlight();
            Spotlight.Initialize();
            switcher = new AppSwitcher();
            switcher.Install();
            Decorations.Start();
            WindowEvents.Install();

            var keepHidden = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            keepHidden.Tick += (s, e) =>
            {
                Taskbar.KeepHidden();
                CaptionButtons.Sweep(Decorations.IsExcluded);
            };
            keepHidden.Start();

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    menuBar.Reposition();
                    Dock.Reposition();
                }));

            var waiter = new Thread(() =>
            {
                quitEvent.WaitOne();
                app.Dispatcher.BeginInvoke(new Action(Quit));
            });
            waiter.IsBackground = true;
            waiter.Start();

            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }

        public static void Quit()
        {
            Cleanup();
            Application.Current.Shutdown();
        }

        public static void SetFullscreen(bool fullscreen)
        {
            // Games and full-screen videos cover the menu bar, like on a Mac.
            // Windows sometimes reports this wrongly, so double-check the active window really fills the screen.
            if (fullscreen && !TrafficLights.IsFullscreen(Native.GetForegroundWindow())) fullscreen = false;
            menuBar.Topmost = !fullscreen;
        }

        public static void OnShellRestarted()
        {
            Taskbar.Hide();
        }

        static void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;
            try { WindowEvents.Uninstall(); } catch { }
            try { if (switcher != null) switcher.Uninstall(); } catch { }
            try { if (menuBar != null) menuBar.RemoveAppBar(); } catch { }
            try { if (Spotlight != null) Spotlight.UnregisterHotkey(); } catch { }
            try { Taskbar.Restore(); } catch { }
            try { SystemAnimation.Restore(); } catch { }
            try { Decorations.Stop(); } catch { }
            try { CaptionButtons.RestoreAll(); } catch { }
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(DataDir, "log.txt"), DateTime.Now + "  " + ex + Environment.NewLine); }
            catch { }
        }
    }
}
