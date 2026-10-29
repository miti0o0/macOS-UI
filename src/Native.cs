using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace MacUI
{
    internal static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        public const uint ABM_NEW = 0x0, ABM_REMOVE = 0x1, ABM_QUERYPOS = 0x2, ABM_SETPOS = 0x3,
                          ABM_GETSTATE = 0x4, ABM_SETSTATE = 0xA;
        public const uint ABE_TOP = 1, ABE_BOTTOM = 3;
        public const int ABS_AUTOHIDE = 0x1;
        public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

        public const int SW_HIDE = 0, SW_SHOW = 5, SW_MINIMIZE = 6, SW_SHOWNA = 8, SW_RESTORE = 9;
        public const uint GW_OWNER = 4, GA_ROOTOWNER = 3;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
        public const uint WM_CLOSE = 0x10, WM_HOTKEY = 0x312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        public const byte VK_MENU = 0x12, VK_LWIN = 0x5B, VK_SPACE = 0x20;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("shell32.dll")]
        public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

        public static string GetClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // Presses the keys in order, then releases them in reverse order.
        public static void SendKeys(params byte[] keys)
        {
            foreach (byte key in keys)
                keybd_event(key, 0, key == VK_LWIN ? 1u : 0u, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--)
                keybd_event(keys[i], 0, (keys[i] == VK_LWIN ? 1u : 0u) | 2u, UIntPtr.Zero);
        }
    }

    internal static class WindowStyles
    {
        // Hides the window from Alt+Tab and, optionally, stops it from taking focus when clicked.
        public static void MakeToolWindow(IntPtr hwnd, bool noActivate)
        {
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_TOOLWINDOW;
            ex &= ~Native.WS_EX_APPWINDOW;
            if (noActivate) ex |= Native.WS_EX_NOACTIVATE;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
        }

        public static void MakeToolWindow(Window window, bool noActivate)
        {
            MakeToolWindow(new WindowInteropHelper(window).Handle, noActivate);
        }
    }

    internal static class Dpi
    {
        static double scale;

        public static double Scale
        {
            get
            {
                if (scale == 0)
                    using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                        scale = g.DpiX / 96.0;
                return scale;
            }
        }
    }
}
