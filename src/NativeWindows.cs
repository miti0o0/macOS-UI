using System;
using System.Runtime.InteropServices;

namespace MacUI
{
    // Win32 / DWM calls for window buttons, animations and the app switcher.
    internal static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }

        public delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
                          SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
        public const int WS_SYSMENU = 0x80000, SW_MAXIMIZE = 3;
        public const int SC_DRAGMOVE = 0xF012;
        public const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;
        public const int GWL_STYLE = -16;
        public const int WS_CAPTION = 0xC00000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
        public const uint WM_SYSCOMMAND = 0x112;
        public const int SC_CLOSE = 0xF060, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_RESTORE = 0xF120;

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_CAPTION_BUTTON_BOUNDS = 5, DWMWA_EXTENDED_FRAME_BOUNDS = 9,
                         DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
        public const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4,
                         DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17,
                          EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x2;

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        public const uint LLKHF_ALTDOWN = 0x20;
        public const uint VK_TAB = 0x09, VK_SHIFT = 0x10, VK_ESCAPE = 0x1B, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_DOWN = 0x28,
                          VK_BACKTICK = 0xC0,
                          VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_UNASSIGNED = 0xE8;

        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr hdc, int x, int y);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO info, uint winIni);

        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS margins);
        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out POINT size);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DWM_THUMBNAIL_PROPERTIES props);

        public static void SetDwmInt(IntPtr hWnd, int attribute, int value)
        {
            DwmSetWindowAttribute(hWnd, attribute, ref value, sizeof(int));
        }

        // The visible window frame, without Windows' invisible resize borders.
        public static RECT GetFrame(IntPtr hWnd)
        {
            RECT frame;
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf(typeof(RECT))) != 0)
                GetWindowRect(hWnd, out frame);
            return frame;
        }

        public static MONITORINFO GetMonitor(IntPtr hWnd)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(MonitorFromWindow(hWnd, 2), ref info);
            return info;
        }
    }
}
