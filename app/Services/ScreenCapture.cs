using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Snapter
{
    /// <summary>Screen capture in real pixels (the app is per-monitor DPI aware, see app.manifest).
    /// Uses BitBlt with CAPTUREBLT so semi-transparent (layered) windows are captured too.</summary>
    internal static class ScreenCapture
    {
        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        /// <summary>All monitors as one rectangle (can start at negative coordinates).</summary>
        public static Rectangle VirtualScreen()
        {
            return SystemInformation.VirtualScreen;
        }

        public static Rectangle CursorMonitor()
        {
            return Screen.FromPoint(Cursor.Position).Bounds;
        }

        public static Rectangle PrimaryMonitor()
        {
            return Screen.PrimaryScreen.Bounds;
        }

        /// <summary>The monitor with the number Windows gives it in the display settings (1, 2, ...);
        /// when there is none, the n-th monitor in the list; when still none, the main one.</summary>
        public static Rectangle MonitorNumber(int n)
        {
            Screen[] all = Screen.AllScreens;
            foreach (Screen s in all)
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(s.DeviceName ?? "", @"DISPLAY(\d+)$");
                if (m.Success && int.Parse(m.Groups[1].Value) == n) return s.Bounds;
            }
            if (n >= 1 && n <= all.Length) return all[n - 1].Bounds;
            return PrimaryMonitor();
        }

        /// <summary>The first monitor that is not the main one (the "additional" monitor); the main one when there is only one.</summary>
        public static Rectangle SecondaryMonitor()
        {
            foreach (Screen s in Screen.AllScreens) if (!s.Primary) return s.Bounds;
            return PrimaryMonitor();
        }

        [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int cbSize; public int flags; public IntPtr hCursor; public int x, y; }
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo pci);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo info);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

        /// <summary>The picture of the screen; with includeCursor the mouse pointer is drawn into it (capture.include_cursor).</summary>
        public static Bitmap Capture(Rectangle area, bool includeCursor)
        {
            Bitmap bmp = Capture(area);
            if (!includeCursor) return bmp;
            try
            {
                CursorInfo ci = new CursorInfo { cbSize = Marshal.SizeOf(typeof(CursorInfo)) };
                if (GetCursorInfo(ref ci) && (ci.flags & 1) != 0 && ci.hCursor != IntPtr.Zero)
                {
                    IconInfo ii;
                    int hx = 0, hy = 0;
                    if (GetIconInfo(ci.hCursor, out ii))
                    {
                        hx = ii.xHotspot; hy = ii.yHotspot;
                        if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                        if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                    }
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr dc = g.GetHdc();
                        try { DrawIconEx(dc, ci.x - hx - area.X, ci.y - hy - area.Y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); } // DI_NORMAL
                        finally { g.ReleaseHdc(dc); }
                    }
                }
            }
            catch (Exception ex) { Log.Write("cursor capture error: " + ex.Message); }
            return bmp;
        }

        public static Bitmap Capture(Rectangle area)
        {
            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr destDc = g.GetHdc();
                    try
                    {
                        if (!BitBlt(destDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, SRCCOPY | CAPTUREBLT))
                            throw new InvalidOperationException("BitBlt failed (error " + Marshal.GetLastWin32Error() + ")");
                    }
                    finally { g.ReleaseHdc(destDc); }
                }
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); }
            return bmp;
        }
    }
}
