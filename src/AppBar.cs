using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MacUI
{
    // Registers a window as a Windows "app bar" so maximized windows leave room for it.
    internal sealed class AppBar
    {
        static readonly uint CallbackMessage = Native.RegisterWindowMessage("MacUI.AppBarMessage");
        static readonly uint TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

        readonly Window window;
        readonly uint edge;
        readonly double thickness;
        IntPtr hwnd;
        bool registered;
        Native.RECT lastRect;

        public event Action<bool> FullscreenChanged;
        public event Action ShellRestarted;

        public AppBar(Window window, uint edge, double thickness)
        {
            this.window = window;
            this.edge = edge;
            this.thickness = thickness;
        }

        public void Attach()
        {
            hwnd = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            Register();
        }

        void Register()
        {
            Native.APPBARDATA abd = NewData();
            abd.uCallbackMessage = CallbackMessage;
            Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
            registered = true;
            lastRect = new Native.RECT();
            UpdatePosition();
        }

        public void UpdatePosition()
        {
            if (!registered) return;
            double scale = Dpi.Scale;
            int width = (int)Math.Round(SystemParameters.PrimaryScreenWidth * scale);
            int height = (int)Math.Round(SystemParameters.PrimaryScreenHeight * scale);
            int size = (int)Math.Round(thickness * scale);

            Native.APPBARDATA abd = NewData();
            abd.rc.Left = 0;
            abd.rc.Right = width;
            if (edge == Native.ABE_TOP) { abd.rc.Top = 0; abd.rc.Bottom = size; }
            else { abd.rc.Top = height - size; abd.rc.Bottom = height; }

            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            if (edge == Native.ABE_TOP) abd.rc.Bottom = abd.rc.Top + size;
            else abd.rc.Top = abd.rc.Bottom - size;

            if (abd.rc.Left == lastRect.Left && abd.rc.Top == lastRect.Top &&
                abd.rc.Right == lastRect.Right && abd.rc.Bottom == lastRect.Bottom) return;
            lastRect = abd.rc;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
        }

        public void Remove()
        {
            if (!registered) return;
            Native.APPBARDATA abd = NewData();
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
            registered = false;
        }

        Native.APPBARDATA NewData()
        {
            var abd = new Native.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            abd.hWnd = hwnd;
            abd.uEdge = edge;
            return abd;
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)msg == CallbackMessage)
            {
                int code = wParam.ToInt32();
                if (code == Native.ABN_POSCHANGED) UpdatePosition();
                else if (code == Native.ABN_FULLSCREENAPP && FullscreenChanged != null) FullscreenChanged(lParam != IntPtr.Zero);
            }
            else if ((uint)msg == TaskbarCreated)
            {
                // Explorer restarted and forgot every app bar.
                registered = false;
                Register();
                if (ShellRestarted != null) ShellRestarted();
            }
            return IntPtr.Zero;
        }
    }
}
