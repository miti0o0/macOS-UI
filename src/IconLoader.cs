using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacUI
{
    // Loads large, high-quality icons for files, folders and Store apps.
    internal static class IconLoader
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource Get(string parsingName, int size)
        {
            if (string.IsNullOrEmpty(parsingName)) return null;
            string key = size + "|" + parsingName;
            ImageSource image;
            if (cache.TryGetValue(key, out image)) return image;
            image = FromShell(parsingName, size) ?? FromFile(parsingName);
            cache[key] = image;
            return image;
        }

        static ImageSource FromShell(string parsingName, int size)
        {
            IShellItemImageFactory factory = null;
            IntPtr hbitmap = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory) != 0 || factory == null) return null;
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY, out hbitmap) != 0) return null;
                return ToBitmapSource(hbitmap);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        static ImageSource FromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }

        static BitmapSource ToBitmapSource(IntPtr hbitmap)
        {
            var bm = new BITMAP();
            if (GetObject(hbitmap, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0) return null;
            int width = bm.bmWidth, height = Math.Abs(bm.bmHeight);

            var info = new BITMAPINFO();
            info.biSize = 40;
            info.biWidth = width;
            info.biHeight = -height; // top-down rows
            info.biPlanes = 1;
            info.biBitCount = 32;

            byte[] pixels = new byte[width * height * 4];
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(hdc, hbitmap, 0, (uint)height, pixels, ref info, 0) == 0) return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }

            // Old icons without an alpha channel would otherwise be invisible.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha)
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            source.Freeze();
            return source;
        }

        const int SIIGBF_ICONONLY = 0x4;

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public uint mask0, mask1, mask2, mask3;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bitmap);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    }
}
