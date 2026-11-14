using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Snapter
{
    /// <summary>Tray icon (placeholder quill drawn in code, accent color of the theme) and its menu.</summary>
    internal sealed class TrayService : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        private readonly AppController _app;
        private readonly NotifyIcon _notify = new NotifyIcon();
        private Icon _icon;
        private IntPtr _hIcon = IntPtr.Zero;
        private string _balloonPath;   // a click on the message opens Explorer with this file selected

        public TrayService(AppController app)
        {
            _app = app;
            _notify.Visible = true;
            _notify.BalloonTipClicked += delegate { OpenBalloonPath(); };
            // one left click, like Lightshot (what it does is the setting ui.tray_click: region | menu | none)
            _notify.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                // a plain click: the region; Shift + click: the settings (what each does is set in Settings - General)
                string what = (Control.ModifierKeys & Keys.Shift) != 0 ? _app.Settings.TrayShiftClick : _app.Settings.TrayClick;
                TrayAction(what);
            };
        }

        private void TrayAction(string what)
        {
            switch (what)
            {
                case "none": break;
                case "menu": ShowMenu(); break;
                case "settings": _app.OpenSettings(); break;
                default: _app.CaptureRegion(false, 220); break; // a short pause so the icon and the menu are not in the picture
            }
        }

        private void ShowMenu()
        {
            try
            {
                System.Reflection.MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (mi != null) mi.Invoke(_notify, null);
            }
            catch (Exception ex) { Log.Write("tray menu error: " + ex.Message); }
        }

        public void Rebuild()
        {
            Theme theme = _app.Theme;
            I18n t = _app.I18n;
            Color bg = ToDrawing(theme.Surface), fg = ToDrawing(theme.Text);

            // icon: its brackets follow the TASKBAR theme (light or dark), not the theme of the apps
            // (Settings - Appearance: one of ten icons; the ink either follows the taskbar or the program theme)
            ShowIcon(_app.Settings, theme);
            _notify.Text = Truncate(t.T("tray.tooltip") + " v" + AppVersion.Text, 60);

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem title = new ToolStripMenuItem(t.T("app.name") + " - " + t.T("tray.title"));
            title.Enabled = false;
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());

            AddAction(menu, t.T("capture.region"), "capture.region", delegate { _app.CaptureRegion(false); });
            AddAction(menu, t.T("capture.region_to_clip"), "capture.region_to_clip", delegate { _app.CaptureRegion(true); });
            AddAction(menu, t.T("capture.region_delay"), "capture.region_delay", delegate { _app.RegionWithDelay(); });
            AddAction(menu, t.T("capture.repeat_last"), "capture.repeat_last", delegate { _app.RepeatLastRegion(); });
            AddAction(menu, t.T("capture.fullscreen_to_file"), "capture.fullscreen_to_file", delegate { _app.FullscreenToFile(); });
            AddAction(menu, t.T("capture.fullscreen_to_clip"), "capture.fullscreen_to_clip", delegate { _app.FullscreenToClipboard(); });
            AddAction(menu, _app.IsRecording ? t.T("menu.rec_stop") : t.T("capture.record_gif"), "capture.record_gif", delegate { _app.StartRecordFlow("gif"); });
            AddAction(menu, t.T("capture.record_video"), "capture.record_video", delegate { _app.StartRecordFlow("video"); });
            AddAction(menu, t.T("capture.scroll"), "capture.scroll", delegate { _app.StartRecordFlow("scroll"); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem neural = new ToolStripMenuItem(t.T("menu.neural_safe"));
            neural.Checked = _app.Settings.NeuralSafe;
            neural.Click += delegate { _app.ApplyChoice("neural", _app.Settings.NeuralSafe ? "off" : "on"); };
            menu.Items.Add(neural);
            ToolStripMenuItem profiles = new ToolStripMenuItem(t.T("menu.profiles"));
            foreach (string pn in _app.ProfileNames())
            {
                string pname = pn;
                ToolStripMenuItem pi = new ToolStripMenuItem(_app.ProfileLabel(pn));
                pi.Click += delegate { _app.ApplyProfile(pname); };
                profiles.DropDownItems.Add(pi);
            }
            menu.Items.Add(profiles);
            ToolStripMenuItem settingsItem = new ToolStripMenuItem(t.T("settings.title") + "...");
            settingsItem.Click += delegate { _app.OpenSettings(); };
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());

            // appearance
            ToolStripMenuItem appearance = new ToolStripMenuItem(t.T("menu.appearance"));
            appearance.DropDownItems.Add(Header(t.T("menu.theme_mode")));
            foreach (string m in new string[] { "system", "light", "dark" })
                appearance.DropDownItems.Add(Choice(t.T("theme.mode." + m), "mode", m, _app.Settings.ThemeMode == m));
            appearance.DropDownItems.Add(new ToolStripSeparator());
            appearance.DropDownItems.Add(Header(t.T("menu.accent_theme")));
            foreach (string id in Theme.Ids)
                appearance.DropDownItems.Add(Choice(ThemeLabel(id), "theme", id, _app.Settings.AccentTheme == id));
            menu.Items.Add(appearance);

            // language
            ToolStripMenuItem lang = new ToolStripMenuItem(t.T("menu.language"));
            lang.DropDownItems.Add(Choice(t.T("lang.auto"), "lang", "auto", _app.Settings.Language == "auto"));
            foreach (LanguageInfo l in I18n.Languages())
                lang.DropDownItems.Add(Choice(l.Native, "lang", l.Code, _app.Settings.Language == l.Code));
            menu.Items.Add(lang);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem shots = new ToolStripMenuItem(t.T("menu.open_shots"));
            shots.Click += delegate { _app.OpenShotsFolder(); };
            menu.Items.Add(shots);

            ToolStripMenuItem folder = new ToolStripMenuItem(t.T("menu.open_folder"));
            folder.Click += delegate { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.Root + "\"") { UseShellExecute = true }); };
            menu.Items.Add(folder);

            ToolStripMenuItem log = new ToolStripMenuItem(t.T("menu.open_log"));
            log.Click += delegate { _app.OpenLog(); };
            menu.Items.Add(log);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem(t.T("menu.exit"));
            exit.Click += delegate { _app.Quit(); };
            menu.Items.Add(exit);

            menu.BackColor = bg;
            menu.ForeColor = fg;
            Paint(menu.Items, bg, fg);

            ContextMenuStrip old = _notify.ContextMenuStrip;
            _notify.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
        }

        /// <summary>Puts an icon into the tray at once. Used by Rebuild and by the preview in the settings window.</summary>
        public void ShowIcon(Settings s, Theme theme)
        {
            // the outline: follows the Windows taskbar (light / dark) or is the chosen ink;
            // the accent: follows the program theme or is the chosen color of the ten
            Color ink = s.TrayIconFollowTaskbar ? IconFactory.TaskbarInk() : IconFactory.InkColor(s.TrayIconInk);
            Color accent = s.TrayIconFollowTheme ? ToDrawing(theme.Accent) : IconFactory.AccentColor(s.TrayIconColor);
            SetIcon(IconFactory.Draw(s.TrayIcon, 32, ink, accent));
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState; public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        /// <summary>The message near the clock. It is always silent (NIIF_NOSOUND): Windows would play its own notification sound on top of the
        /// program sound (Settings - After a capture), which gave two sounds. The program plays the one the user chose, or none.</summary>
        public void Balloon(string title, string text, int ms = 3000, string openPath = null)
        {
            _balloonPath = openPath;
            try
            {
                const int NIM_MODIFY = 1, NIF_INFO = 0x10, NIIF_INFO = 1, NIIF_NOSOUND = 0x10;
                System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                object window = typeof(NotifyIcon).GetField("window", bf)?.GetValue(_notify);
                object id = typeof(NotifyIcon).GetField("id", bf)?.GetValue(_notify);
                NativeWindow nw = window as NativeWindow;
                if (nw != null && id is int && nw.Handle != IntPtr.Zero)
                {
                    NOTIFYICONDATA d = new NOTIFYICONDATA();
                    d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                    d.hWnd = nw.Handle; d.uID = (int)id; d.uFlags = NIF_INFO;
                    d.szInfo = text.Length > 255 ? text.Substring(0, 255) : text;
                    d.szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title;
                    d.uTimeoutOrVersion = ms; d.dwInfoFlags = NIIF_INFO | NIIF_NOSOUND;
                    if (Shell_NotifyIcon(NIM_MODIFY, ref d)) return;
                }
            }
            catch (Exception ex) { Log.Write("silent balloon error: " + ex.Message); }
            try { _notify.ShowBalloonTip(ms, title, text, ToolTipIcon.Info); } catch { }
        }

        private void OpenBalloonPath()
        {
            string p = _balloonPath;
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                if (System.IO.File.Exists(p)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + p + "\"") { UseShellExecute = true });
                else if (System.IO.Directory.Exists(p)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + p + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("open from message error: " + ex.Message); }
        }

        private string ThemeLabel(string id)
        {
            string key = "theme.name." + id;
            string v = _app.I18n.T(key);
            return v == key ? Theme.DisplayName(id) : v;
        }

        private void AddAction(ContextMenuStrip menu, string text, string action, Action onClick)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            string keys = _app.KeysFor(action);
            if (!string.IsNullOrEmpty(keys)) mi.ShortcutKeyDisplayString = keys;
            mi.Click += delegate { onClick(); };
            menu.Items.Add(mi);
        }

        private static ToolStripMenuItem Header(string text)
        {
            ToolStripMenuItem h = new ToolStripMenuItem(text);
            h.Enabled = false;
            return h;
        }

        private ToolStripMenuItem Choice(string text, string kind, string value, bool isChecked)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.Checked = isChecked;
            mi.Click += delegate { _app.ApplyChoice(kind, value); };
            return mi;
        }

        private static void Paint(ToolStripItemCollection items, Color bg, Color fg)
        {
            foreach (ToolStripItem it in items)
            {
                it.BackColor = bg;
                it.ForeColor = fg;
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems)
                {
                    mi.DropDown.BackColor = bg;
                    Paint(mi.DropDownItems, bg, fg);
                }
            }
        }

        private static Color ToDrawing(System.Windows.Media.Color c)
        {
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        private static string Truncate(string s, int n)
        {
            return s.Length <= n ? s : s.Substring(0, n);
        }

        private static Bitmap DrawIcon(Color accent, Color ink)
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                PointF[] vane = { new PointF(6, 27), new PointF(9, 15), new PointF(16, 7), new PointF(27, 3), new PointF(25, 13), new PointF(18, 21), new PointF(9, 26) };
                using (SolidBrush fill = new SolidBrush(accent)) g.FillPolygon(fill, vane);
                using (Pen shaft = new Pen(ink, 2)) g.DrawLine(shaft, 4, 29, 24, 7);
            }
            return bmp;
        }

        private void SetIcon(Bitmap bmp)
        {
            Icon oldIcon = _icon; IntPtr oldH = _hIcon;
            _hIcon = bmp.GetHicon();
            _icon = Icon.FromHandle(_hIcon);
            _notify.Icon = _icon;
            bmp.Dispose();
            if (oldIcon != null) oldIcon.Dispose();
            if (oldH != IntPtr.Zero) DestroyIcon(oldH);
        }

        public void Dispose()
        {
            _notify.Visible = false;
            _notify.Dispose();
            if (_icon != null) _icon.Dispose();
            if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        }
    }
}
