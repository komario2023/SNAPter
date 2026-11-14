using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Microsoft.Win32;

namespace Snapter
{
    /// <summary>The program icon, drawn in code: four corner brackets of a viewfinder around a selected area.
    /// The same drawing feeds the tray icon (ink follows the taskbar theme) and the .ico of the exe.</summary>
    internal static class IconFactory
    {
        public static readonly Color Accent = Color.FromArgb(45, 140, 255);

        /// <summary>True when the Windows taskbar is light (it has its own setting, separate from the apps).</summary>
        public static bool TaskbarIsLight()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                return v is int && (int)v != 0;
            }
            catch { return false; }
        }

        /// <summary>Ten accent colors for the tray icon (Settings - Appearance) and five inks (the outline).</summary>
        public static readonly string[] AccentKeys = { "blue", "sky", "teal", "green", "lime", "yellow", "orange", "red", "pink", "purple" };
        public static readonly string[] InkKeys = { "black", "graphite", "navy", "white", "gray" };

        public static Color AccentColor(string key)
        {
            switch (key)
            {
                case "sky": return Color.FromArgb(56, 189, 248);
                case "teal": return Color.FromArgb(20, 184, 166);
                case "green": return Color.FromArgb(34, 197, 94);
                case "lime": return Color.FromArgb(132, 204, 22);
                case "yellow": return Color.FromArgb(250, 204, 21);
                case "orange": return Color.FromArgb(251, 146, 60);
                case "red": return Color.FromArgb(239, 68, 68);
                case "pink": return Color.FromArgb(236, 72, 153);
                case "purple": return Color.FromArgb(168, 85, 247);
                default: return Accent;
            }
        }

        public static Color InkColor(string key)
        {
            switch (key)
            {
                case "graphite": return Color.FromArgb(55, 65, 81);
                case "navy": return Color.FromArgb(30, 41, 59);
                case "white": return Color.FromArgb(245, 245, 247);
                case "gray": return Color.FromArgb(120, 126, 138);
                default: return Color.FromArgb(17, 17, 17);
            }
        }

        public static Color TaskbarInk()
        {
            return TaskbarIsLight() ? Color.FromArgb(32, 32, 36) : Color.FromArgb(245, 245, 247);
        }

        /// <summary>The ten tray icons the user can choose from (Settings - Appearance). "quill" is the first pen icon.</summary>
        public static readonly string[] Kinds =
        {
            "viewfinder", "quill", "camera", "crop", "target", "pin", "bolt", "select", "lens", "star",
            "frame", "arrow", "scissors", "pencil", "window", "clipboard", "eye", "grid", "bubble", "monitor"
        };

        public static Bitmap Draw(string kind, int size, Color ink, Color accent)
        {
            if (kind == null || kind == "viewfinder" || Array.IndexOf(Kinds, kind) < 0) return Draw(size, ink, accent);
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float s = size / 32f;
                using (SolidBrush fill = new SolidBrush(accent))
                using (SolidBrush inkFill = new SolidBrush(ink))
                using (Pen pen = new Pen(ink, Math.Max(1.4f, 2.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    switch (kind)
                    {
                        case "quill":
                            g.FillPolygon(fill, P(s, 6, 27, 9, 15, 16, 7, 27, 3, 25, 13, 18, 21, 9, 26));
                            g.DrawLine(pen, 4 * s, 29 * s, 24 * s, 7 * s);
                            break;
                        case "camera":
                            using (GraphicsPath body = Round(new RectangleF(3 * s, 9 * s, 26 * s, 19 * s), 4 * s)) g.DrawPath(pen, body);
                            g.FillPolygon(inkFill, P(s, 11, 9, 13, 5, 19, 5, 21, 9));
                            g.FillEllipse(fill, 10 * s, 12 * s, 12 * s, 12 * s);
                            using (SolidBrush hi = new SolidBrush(Color.FromArgb(160, 255, 255, 255))) g.FillEllipse(hi, 13 * s, 14.5f * s, 3.5f * s, 3.5f * s);
                            break;
                        case "crop":
                            using (Pen thick = new Pen(ink, Math.Max(1.6f, 3.2f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            {
                                g.DrawLines(thick, new PointF[] { new PointF(9 * s, 3 * s), new PointF(9 * s, 23 * s), new PointF(29 * s, 23 * s) });
                                g.DrawLines(thick, new PointF[] { new PointF(3 * s, 9 * s), new PointF(23 * s, 9 * s), new PointF(23 * s, 29 * s) });
                            }
                            g.FillRectangle(fill, 12 * s, 12 * s, 8 * s, 8 * s);
                            break;
                        case "target":
                            g.DrawEllipse(pen, 4 * s, 4 * s, 24 * s, 24 * s);
                            g.FillEllipse(fill, 11 * s, 11 * s, 10 * s, 10 * s);
                            g.DrawLine(pen, 16 * s, 1 * s, 16 * s, 8 * s); g.DrawLine(pen, 16 * s, 24 * s, 16 * s, 31 * s);
                            g.DrawLine(pen, 1 * s, 16 * s, 8 * s, 16 * s); g.DrawLine(pen, 24 * s, 16 * s, 31 * s, 16 * s);
                            break;
                        case "pin":
                            g.FillEllipse(fill, 8 * s, 4 * s, 16 * s, 16 * s);
                            g.FillPolygon(fill, P(s, 9.5f, 16, 22.5f, 16, 16, 30));
                            g.FillEllipse(inkFill, 12.5f * s, 8.5f * s, 7 * s, 7 * s);
                            break;
                        case "bolt":
                            g.FillPolygon(fill, P(s, 18, 2, 7, 18, 15, 18, 12, 30, 25, 13, 17, 13));
                            g.DrawPolygon(pen, P(s, 18, 2, 7, 18, 15, 18, 12, 30, 25, 13, 17, 13));
                            break;
                        case "select":
                            using (Pen dash = new Pen(ink, Math.Max(1.4f, 2.4f * s)) { DashPattern = new float[] { 2.5f, 1.8f } })
                            using (GraphicsPath box = Round(new RectangleF(3 * s, 3 * s, 21 * s, 21 * s), 3 * s)) g.DrawPath(dash, box);
                            g.FillPolygon(fill, P(s, 17, 15, 17, 29, 21, 25, 24, 31, 27, 29.5f, 24, 23.5f, 29, 23.5f));
                            break;
                        case "lens":
                            using (SolidBrush glass = new SolidBrush(Color.FromArgb(110, accent))) g.FillEllipse(glass, 4 * s, 4 * s, 18 * s, 18 * s);
                            using (Pen thick = new Pen(ink, Math.Max(1.6f, 3f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            {
                                g.DrawEllipse(thick, 4 * s, 4 * s, 18 * s, 18 * s);
                                g.DrawLine(thick, 20 * s, 20 * s, 29 * s, 29 * s);
                            }
                            break;
                        case "star":
                            PointF[] st = new PointF[10];
                            for (int i = 0; i < 10; i++)
                            {
                                double a = -Math.PI / 2 + i * Math.PI / 5;
                                float r = (i % 2 == 0 ? 14.5f : 6.2f) * s;
                                st[i] = new PointF(16 * s + (float)Math.Cos(a) * r, 16.8f * s + (float)Math.Sin(a) * r);
                            }
                            g.FillPolygon(fill, st);
                            g.DrawPolygon(pen, st);
                            break;
                        case "frame":   // a picture: frame, mountain and sun
                            using (GraphicsPath fr = Round(new RectangleF(3 * s, 5 * s, 26 * s, 22 * s), 4 * s)) g.DrawPath(pen, fr);
                            g.FillPolygon(fill, P(s, 7, 24, 13, 14, 18, 20, 21, 16, 26, 24));
                            g.FillEllipse(inkFill, 20 * s, 9 * s, 4.5f * s, 4.5f * s);
                            break;
                        case "arrow":   // an arrow, the main tool of the editor
                            g.DrawLine(pen, 5 * s, 27 * s, 20 * s, 12 * s);
                            g.FillPolygon(fill, P(s, 28, 4, 14, 7, 25, 18));
                            g.DrawPolygon(pen, P(s, 28, 4, 14, 7, 25, 18));
                            break;
                        case "scissors":
                            g.DrawLine(pen, 9 * s, 11 * s, 29 * s, 23 * s);
                            g.DrawLine(pen, 9 * s, 21 * s, 29 * s, 9 * s);
                            g.FillEllipse(fill, 3 * s, 4 * s, 8 * s, 8 * s); g.DrawEllipse(pen, 3 * s, 4 * s, 8 * s, 8 * s);
                            g.FillEllipse(fill, 3 * s, 20 * s, 8 * s, 8 * s); g.DrawEllipse(pen, 3 * s, 20 * s, 8 * s, 8 * s);
                            break;
                        case "pencil":
                            g.FillPolygon(fill, P(s, 8, 21, 22, 6, 26, 10, 12, 25));
                            g.DrawPolygon(pen, P(s, 8, 21, 22, 6, 26, 10, 12, 25));
                            g.FillPolygon(inkFill, P(s, 5, 28, 8, 21, 12, 25));
                            break;
                        case "window":
                            using (GraphicsPath wp = Round(new RectangleF(3 * s, 5 * s, 26 * s, 22 * s), 4 * s)) g.DrawPath(pen, wp);
                            g.DrawLine(pen, 3 * s, 11.5f * s, 29 * s, 11.5f * s);
                            using (GraphicsPath body2 = Round(new RectangleF(7 * s, 15 * s, 18 * s, 8 * s), 2 * s)) g.FillPath(fill, body2);
                            g.FillEllipse(inkFill, 6 * s, 7.3f * s, 2.2f * s, 2.2f * s); g.FillEllipse(inkFill, 10 * s, 7.3f * s, 2.2f * s, 2.2f * s);
                            break;
                        case "clipboard":
                            using (GraphicsPath cb = Round(new RectangleF(6 * s, 6 * s, 20 * s, 24 * s), 3 * s)) g.DrawPath(pen, cb);
                            using (GraphicsPath clip = Round(new RectangleF(11 * s, 3 * s, 10 * s, 6 * s), 2 * s)) g.FillPath(inkFill, clip);
                            using (Pen lines = new Pen(accent, Math.Max(1.6f, 3f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            {
                                g.DrawLine(lines, 11 * s, 15 * s, 21 * s, 15 * s); g.DrawLine(lines, 11 * s, 20 * s, 21 * s, 20 * s); g.DrawLine(lines, 11 * s, 25 * s, 17 * s, 25 * s);
                            }
                            break;
                        case "eye":
                            g.DrawEllipse(pen, 2 * s, 8 * s, 28 * s, 16 * s);
                            g.FillEllipse(fill, 11 * s, 10.5f * s, 11 * s, 11 * s);
                            g.FillEllipse(inkFill, 14.5f * s, 14 * s, 4 * s, 4 * s);
                            break;
                        case "grid":
                            using (GraphicsPath g1 = Round(new RectangleF(4 * s, 4 * s, 11 * s, 11 * s), 2.5f * s)) g.DrawPath(pen, g1);
                            using (GraphicsPath g2 = Round(new RectangleF(17 * s, 4 * s, 11 * s, 11 * s), 2.5f * s)) g.FillPath(fill, g2);
                            using (GraphicsPath g3 = Round(new RectangleF(4 * s, 17 * s, 11 * s, 11 * s), 2.5f * s)) g.FillPath(fill, g3);
                            using (GraphicsPath g4 = Round(new RectangleF(17 * s, 17 * s, 11 * s, 11 * s), 2.5f * s)) g.DrawPath(pen, g4);
                            break;
                        case "bubble":
                            using (GraphicsPath bb = Round(new RectangleF(3 * s, 4 * s, 26 * s, 18 * s), 5 * s)) { g.FillPath(fill, bb); g.DrawPath(pen, bb); }
                            g.FillPolygon(fill, P(s, 9, 21, 8, 29, 16, 22));
                            g.DrawLines(pen, new PointF[] { new PointF(9 * s, 21 * s), new PointF(8 * s, 29 * s), new PointF(16 * s, 22 * s) });
                            g.FillEllipse(inkFill, 8 * s, 11.5f * s, 3.2f * s, 3.2f * s); g.FillEllipse(inkFill, 14.4f * s, 11.5f * s, 3.2f * s, 3.2f * s); g.FillEllipse(inkFill, 20.8f * s, 11.5f * s, 3.2f * s, 3.2f * s);
                            break;
                        case "monitor":
                            using (GraphicsPath mn = Round(new RectangleF(3 * s, 4 * s, 26 * s, 18 * s), 3 * s)) g.DrawPath(pen, mn);
                            using (GraphicsPath scr = Round(new RectangleF(7 * s, 8 * s, 18 * s, 10 * s), 1.5f * s)) g.FillPath(fill, scr);
                            g.DrawLine(pen, 16 * s, 22 * s, 16 * s, 27 * s);
                            g.DrawLine(pen, 10 * s, 28 * s, 22 * s, 28 * s);
                            break;
                    }
                }
            }
            return bmp;
        }

        private static PointF[] P(float s, params float[] xy)
        {
            PointF[] p = new PointF[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new PointF(xy[2 * i] * s, xy[2 * i + 1] * s);
            return p;
        }

        public static Bitmap Draw(int size, Color ink, Color accent)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float s = size / 32f;

                // selected area (filled), slightly off center, with a light highlight
                RectangleF block = new RectangleF(10 * s, 10 * s, 14 * s, 14 * s);
                using (GraphicsPath path = Round(block, 3 * s))
                using (SolidBrush fill = new SolidBrush(accent))
                    g.FillPath(fill, path);
                using (SolidBrush hi = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                    g.FillEllipse(hi, 12.5f * s, 12.5f * s, 3.5f * s, 3.5f * s);

                // four corner brackets
                float m = 3f * s, arm = 9f * s, w = Math.Max(1.6f, 3f * s);
                using (Pen pen = new Pen(ink, w))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                    float a = m, b = size - m;
                    g.DrawLines(pen, new PointF[] { new PointF(a, a + arm), new PointF(a, a), new PointF(a + arm, a) });
                    g.DrawLines(pen, new PointF[] { new PointF(b - arm, a), new PointF(b, a), new PointF(b, a + arm) });
                    g.DrawLines(pen, new PointF[] { new PointF(a, b - arm), new PointF(a, b), new PointF(a + arm, b) });
                    g.DrawLines(pen, new PointF[] { new PointF(b - arm, b), new PointF(b, b), new PointF(b, b - arm) });
                }
            }
            return bmp;
        }

        private static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Writes an .ico with PNG frames (16 to 256 px). Used once: Snapter.exe --make-icon file.ico</summary>
        public static void WriteIco(string path)
        {
            WriteIcoWith(path, null, Color.FromArgb(17, 17, 17), Accent, true);   // the icon of the exe: on a white rounded plate
        }

        /// <summary>The icon of the tray as chosen in the settings (its shape and colors), written as an .ico (for a shortcut).
        /// The outline is the chosen ink (it does not follow the taskbar: Explorer is usually light).</summary>
        public static void WriteIcoFor(string path, Settings s)
        {
            Color ink = InkColor(s.TrayIconInk);
            if (ink.R > 200 && ink.G > 200) ink = InkColor("graphite");   // a white outline would vanish on the white plate
            WriteIcoWith(path, s.TrayIcon, ink, AccentColor(s.TrayIconColor), true);
        }

        /// <summary>All tray icons on a grid, ten in a row (a developer tool: Snapter.exe --icon-sheet file.png).</summary>
        public static void WriteSheet(string path)
        {
            int cell = 80;
            using (Bitmap sheet = new Bitmap(cell * 10, cell * ((Kinds.Length + 9) / 10), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(244, 244, 244));
                for (int i = 0; i < Kinds.Length; i++)
                    using (Bitmap b = Draw(Kinds[i], 64, Color.FromArgb(17, 17, 17), Accent))
                        g.DrawImage(b, (i % 10) * cell + 8, (i / 10) * cell + 8);
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>The icon on a white rounded plate (like the icons of other programs on the desktop).</summary>
        private static Bitmap OnPlate(Bitmap icon, int size)
        {
            Bitmap res = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(res))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float pad = size * 0.02f;
                using (GraphicsPath plate = Round(new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad), size * 0.22f))
                using (SolidBrush white = new SolidBrush(Color.White))
                    g.FillPath(white, plate);
                float inner = size * 0.74f, off = (size - inner) / 2f;
                g.DrawImage(icon, new RectangleF(off, off, inner, inner));
            }
            return res;
        }

        private static void WriteIcoWith(string path, string kind, Color ink, Color accent, bool plate = false)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            byte[][] frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                int ds = plate ? Math.Max(sizes[i], 128) : sizes[i];   // on a plate the picture is drawn big and scaled down smoothly
                using (Bitmap raw = kind == null ? Draw(ds, ink, accent) : Draw(kind, ds, ink, accent))
                using (Bitmap bmp = plate ? OnPlate(raw, sizes[i]) : (Bitmap)raw.Clone())
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    frames[i] = ms.ToArray();
                }
            }
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                for (int i = 0; i < sizes.Length; i++) w.Write(frames[i]);
            }
        }
    }

    /// <summary>"Start with Windows": a value in HKCU\...\Run.</summary>
    internal static class AutoStart
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Snapter";

        /// <summary>Removes legacy autostart registry value QuillPane from an older project name.</summary>
        private static void MigrateOld()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k.GetValue("QuillPane") == null) return;
                    k.DeleteValue("QuillPane", false);
                    string exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe) && !exe.Contains("publish_new")) k.SetValue(ValueName, "\"" + exe + "\"");
                    Log.Write("autostart: removed legacy QuillPane entry");
                }
            }
            catch { }
        }

        public static bool IsOn()
        {
            MigrateOld();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void Apply(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (on)
                    {
                        string exe = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exe)) k.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else k.DeleteValue(ValueName, false);
                }
                Log.Write("autostart " + (on ? "on" : "off"));
            }
            catch (Exception ex) { Log.Write("autostart error: " + ex.Message); }
        }
    }
}
