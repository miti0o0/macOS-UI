using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MacUI
{
    // Hides the Windows taskbar while MacUI runs and puts it back afterwards.
    internal static class Taskbar
    {
        static string StateFile { get { return Path.Combine(App.DataDir, "taskbar-state.txt"); } }
        static bool applied;

        public static void Hide()
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return; // right after sign-in the taskbar may not exist yet; KeepHidden retries
            applied = true;

            Native.APPBARDATA abd = NewData(tray);
            int state = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd).ToUInt32();

            // Only remember the original state once, so a crash never overwrites it with ours.
            if (!File.Exists(StateFile)) File.WriteAllText(StateFile, state.ToString());

            if ((state & Native.ABS_AUTOHIDE) == 0)
            {
                abd.lParam = (IntPtr)(state | Native.ABS_AUTOHIDE);
                Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
            }
            foreach (IntPtr h in TrayWindows()) Native.ShowWindow(h, Native.SW_HIDE);
        }

        public static void KeepHidden()
        {
            if (!applied)
            {
                Hide();
                return;
            }
            foreach (IntPtr h in TrayWindows())
                if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
        }

        public static void Restore()
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (File.Exists(StateFile))
            {
                int state;
                if (tray != IntPtr.Zero && int.TryParse(File.ReadAllText(StateFile).Trim(), out state))
                {
                    Native.APPBARDATA abd = NewData(tray);
                    abd.lParam = (IntPtr)state;
                    Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
                }
                File.Delete(StateFile);
            }
            foreach (IntPtr h in TrayWindows()) Native.ShowWindow(h, Native.SW_SHOWNA);
        }

        static List<IntPtr> TrayWindows()
        {
            var list = new List<IntPtr>();
            IntPtr main = Native.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) list.Add(main);
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                list.Add(h);
            return list;
        }

        static Native.APPBARDATA NewData(IntPtr tray)
        {
            var abd = new Native.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            abd.hWnd = tray;
            return abd;
        }
    }
}
