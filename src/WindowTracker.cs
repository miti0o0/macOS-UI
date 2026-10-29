using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MacUI
{
    internal sealed class AppWindow
    {
        public IntPtr Handle;
        public string ExePath;
    }

    // Finds the same windows Alt+Tab would show, and which program owns each.
    internal static class WindowTracker
    {
        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        static readonly Dictionary<IntPtr, string> storeAppCache = new Dictionary<IntPtr, string>();
        static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TextInputHost.exe", "SearchHost.exe", "SearchApp.exe", "StartMenuExperienceHost.exe",
            "ShellExperienceHost.exe", "LockApp.exe"
        };

        public static List<AppWindow> GetAppWindows()
        {
            var result = new List<AppWindow>();
            Native.EnumWindows((h, l) =>
            {
                AppWindow w = Inspect(h);
                if (w != null) result.Add(w);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        // Returns null unless this is a normal app window (one that Alt+Tab would show).
        public static AppWindow Inspect(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return null;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return null;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return null;
            if (IsCloaked(h)) return null;
            if (Native.GetWindowTextLength(h) == 0) return null;
            if (IsShellWindow(Native.GetClassName(h))) return null;

            string path = GetExePath(h);
            if (path == null || Ignored.Contains(Path.GetFileName(path))) return null;
            return new AppWindow { Handle = h, ExePath = path };
        }

        public static bool IsShellWindow(string className)
        {
            return className == "Progman" || className == "WorkerW" ||
                   className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd";
        }

        public static bool IsOwnWindow(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid == OwnPid;
        }

        public static string GetExePath(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return null;
            string path = ProcessPath(pid);

            // Store apps run inside ApplicationFrameHost; the real app is a child window.
            if (path != null && path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr core = Native.FindWindowEx(h, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (core != IntPtr.Zero)
                {
                    uint corePid;
                    Native.GetWindowThreadProcessId(core, out corePid);
                    string real = ProcessPath(corePid);
                    if (real != null)
                    {
                        storeAppCache[h] = real;
                        return real;
                    }
                }
                string cached;
                if (storeAppCache.TryGetValue(h, out cached)) return cached;
            }
            return path;
        }

        static string ProcessPath(uint pid)
        {
            IntPtr process = Native.OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return Native.QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally
            {
                Native.CloseHandle(process);
            }
        }

        public static bool IsCloaked(IntPtr h)
        {
            int cloaked;
            return Native.DwmGetWindowAttribute(h, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            if (Native.SetForegroundWindow(h) && Native.GetForegroundWindow() == h) return;

            uint unused;
            uint foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out unused);
            uint thisThread = Native.GetCurrentThreadId();
            bool attach = foregroundThread != thisThread;
            if (attach) Native.AttachThreadInput(thisThread, foregroundThread, true);
            Native.BringWindowToTop(h);
            Native.SetForegroundWindow(h);
            if (attach) Native.AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    internal static class AppInfo
    {
        static readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string DisplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Finder";
            string name;
            if (names.TryGetValue(path, out name)) return name;

            string file = Path.GetFileNameWithoutExtension(path);
            if (file.Equals("explorer", StringComparison.OrdinalIgnoreCase)) name = "Finder";
            else
            {
                try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                catch { name = null; }
                if (string.IsNullOrWhiteSpace(name)) name = file;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            }
            name = name.Trim();
            names[path] = name;
            return name;
        }
    }
}
