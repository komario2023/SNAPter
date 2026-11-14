using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;

namespace Snapter
{
    /// <summary>Glue: settings, language, theme, hotkeys, tray and the capture actions.</summary>
    internal sealed partial class AppController : IDisposable
    {
        private readonly System.Windows.Application _app;
        private HotkeyManager _hotkeys;
        private TrayService _tray;
        private RegionOverlay _overlay;
        private SettingsWindow _settingsWindow;
        private Rectangle? _lastRegion;

        public Settings Settings { get; private set; }
        public I18n I18n { get; private set; }
        public Theme Theme { get; private set; }

        public AppController(System.Windows.Application app)
        {
            _app = app;
        }

        public void Start()
        {
            Settings = Settings.Load();
            I18n = new I18n();
            I18n.Load(Settings.Language);
            Theme = Theme.Load(Settings.AccentTheme, Settings.ThemeMode);
            Log.Write("start: language=" + I18n.Code + " mode=" + Theme.Mode + " theme=" + Settings.AccentTheme + " root=" + Paths.Root);

            _lastRegion = Settings.LastRegion;   // the region of the last time also survives a restart
            _tray = new TrayService(this);
            _tray.Rebuild();
            RegisterHotkeys();
        }

        /// <summary>For the self test: load settings, language and theme without a tray icon and hotkeys.</summary>
        internal void TestInit()
        {
            Settings = Settings.Load();
            I18n = new I18n();
            I18n.Load(Settings.Language);
            Theme = Theme.Load(Settings.AccentTheme, Settings.ThemeMode);
        }

        private void RegisterHotkeys()
        {
            StopRestartListener();
            if (_hotkeys != null) { _hotkeys.Dispose(); _hotkeys = null; }
            _hotkeys = new HotkeyManager();
            _hotkeys.Pressed += OnHotkey;
            List<string> failed = new List<string>();
            foreach (HotkeyDef d in Settings.Hotkeys)
            {
                if (!d.Enabled) continue;
                string error;
                if (_hotkeys.Register(d.Action, d.Keys, out error)) Log.Write("hotkey ok: " + d.Keys + " -> " + d.Action);
                else
                {
                    Log.Write("hotkey FAILED: " + d.Keys + " -> " + d.Action + " (" + error + ")");
                    failed.Add(d.Keys);
                }
            }
            if (failed.Count > 0)
                _tray.Balloon(I18n.T("app.name"), I18n.T("msg.hotkeys_failed", string.Join(", ", failed)) + "\n" + I18n.T("msg.hotkey_busy"));
        }

        /// <summary>Reads the settings files again (after the settings window changed them) and applies everything.</summary>
        public void ReloadSettings()
        {
            Settings = Settings.Load();
            I18n.Load(Settings.Language);
            Theme = Theme.Load(Settings.AccentTheme, Settings.ThemeMode);
            AutoStart.Apply(Settings.RunWithWindows);
            _tray.Rebuild();
            RegisterHotkeys();
            Log.Write("settings reloaded: language=" + I18n.Code + " mode=" + Theme.Mode + " theme=" + Settings.AccentTheme);
        }

        internal SettingsWindow TestCurrentSettingsWindow { get { return _settingsWindow; } }

        public void OpenSettings(int page = 0)
        {
            try
            {
                if (_settingsWindow != null)
                {
                    _settingsWindow.Activate();
                    return;
                }
                SettingsWindow w = new SettingsWindow(this, page);
                AttachSettingsWindow(w);
                w.Show();
                w.Activate();
            }
            catch (Exception ex) { _settingsWindow = null; Fail(ex); }
        }

        /// <summary>Remembers the window; when it closes (and is still the current one) the reference goes away.</summary>
        public void AttachSettingsWindow(SettingsWindow w)
        {
            _settingsWindow = w;
            w.Closed += delegate { if (_settingsWindow == w) _settingsWindow = null; };
        }

        /// <summary>Preview in the settings window: the tray icon changes at once, without saving anything.</summary>
        public void PreviewTrayIcon(Settings pending, Theme theme)
        {
            if (_tray != null) _tray.ShowIcon(pending, theme);
        }

        /// <summary>The settings window closed without Apply: the tray icon returns to the saved one.</summary>
        public void RestoreTray()
        {
            if (_tray != null) _tray.Rebuild();
        }

        public string KeysFor(string action)
        {
            foreach (HotkeyDef d in Settings.Hotkeys)
                if (d.Enabled && d.Action == action) return d.Keys;
            return null;
        }

        private void OnHotkey(string action)
        {
            try
            {
                switch (action)
                {
                    case "capture.region": CaptureRegion(false); break;
                    case "capture.region_to_clip": CaptureRegion(true); break;
                    case "capture.region_delay": RegionWithDelay(); break;
                    case "capture.repeat_last": RepeatLastRegion(); break;
                    case "capture.record_gif": StartRecordFlow("gif"); break;
                    case "capture.record_video": StartRecordFlow("video"); break;
                    case "capture.scroll": StartRecordFlow("scroll"); break;
                    case "capture.fullscreen_to_file": FullscreenToFile(); break;
                    case "capture.fullscreen_to_clip": FullscreenToClipboard(); break;
                    default: Log.Write("unknown action: " + action); break;
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        // ---------- profiles: "full", "ascetic", "minimal" and the user's own ----------
        public static readonly string[] BuiltInProfiles = { "full", "ascetic", "minimal" };
        private static readonly string[] AllTools = { "arrow", "line", "rectangle", "ellipse", "brush", "marker", "text", "steps", "blur", "eraser", "picker" };

        public List<string> ProfileNames()
        {
            List<string> names = new List<string>(BuiltInProfiles);
            if (Settings.ProfilesNode != null)
                foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode> kv in Settings.ProfilesNode)
                    if (!names.Contains(kv.Key) && kv.Value is System.Text.Json.Nodes.JsonObject) names.Add(kv.Key);
            return names;
        }

        /// <summary>The profiles (the built-in and the user's) in the order the key of the editor walks through them (settings: profile_cycle.order);
        /// the ones missing from the saved order go to the end.</summary>
        public List<string> ProfileCycleOrder()
        {
            List<string> all = ProfileNames();
            List<string> r = new List<string>();
            foreach (string part in (Settings.ProfileOrder ?? "").Split(','))
            {
                string p = part.Trim();
                if (p.Length > 0 && all.Contains(p) && !r.Contains(p)) r.Add(p);
            }
            foreach (string p in all) if (!r.Contains(p)) r.Add(p);
            return r;
        }

        /// <summary>True when the order of the profiles was set by the owner (then every capture starts with the first profile of the list).</summary>
        public bool ProfileOrderIsOwn()
        {
            List<string> all = ProfileNames();
            foreach (string part in (Settings.ProfileOrder ?? "").Split(','))
                if (all.Contains(part.Trim())) return true;
            return false;
        }

        /// <summary>The list for the key of the editor and where a capture starts in it. The order is the owner's: the first profile is the start (and is applied).
        /// Otherwise the capture starts with the saved setup: it is the profile that matches it, or, when none matches, an extra "current" step at the front.</summary>
        public List<string> ProfileCycleFor(out int start, out bool applyStart)
        {
            List<string> r = ProfileCycleOrder();
            if (ProfileOrderIsOwn()) { start = 0; applyStart = true; return r; }
            string match = ProfileKeyOf(Settings);
            int mi = match == null ? -1 : r.IndexOf(match);
            applyStart = false;
            if (mi >= 0) { start = mi; return r; }
            r.Insert(0, "current"); start = 0; return r;
        }

        /// <summary>The profile that the tools and panel look of these settings match, or null (an own setup).</summary>
        public string ProfileKeyOf(Settings s)
        {
            try
            {
                if (s.MinimalUi) return "minimal";
                if (AllTools.All(k => s.ToolEnabled(k))) return "full";
                if (AllTools.All(k => s.ToolEnabled(k) == (k == "arrow" || k == "rectangle" || k == "text"))) return "ascetic";
                if (s.ProfilesNode != null)
                    foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode> kv in s.ProfilesNode)
                    {
                        System.Text.Json.Nodes.JsonObject p = kv.Value as System.Text.Json.Nodes.JsonObject, pt = p == null ? null : p["tools"] as System.Text.Json.Nodes.JsonObject;
                        if (pt == null) continue;
                        bool same = AllTools.All(k => pt[k] == null || pt[k].GetValue<bool>() == s.ToolEnabled(k));
                        if (same && p["minimal_ui_mode"] != null && p["minimal_ui_mode"].GetValue<bool>() == s.MinimalUi
                            && (p["toolbar_position"] == null || p["toolbar_position"].GetValue<string>() == s.ToolbarPosition)
                            && (p["button_size"] == null || p["button_size"].GetValue<string>() == s.ButtonSize)
                            && (p["tool_labels"] == null || p["tool_labels"].GetValue<bool>() == s.ToolLabels)) return kv.Key;
                    }
                return null;
            }
            catch { return null; }
        }

        /// <summary>The settings as they are while this profile is on: the saved ones with the tools and the look of the profile on top. Nothing is saved.</summary>
        public Settings ProfileSettings(string name)
        {
            if (name == "current" || string.IsNullOrEmpty(name)) return Settings;
            try
            {
                System.Text.Json.Nodes.JsonObject ov = new System.Text.Json.Nodes.JsonObject();
                System.Text.Json.Nodes.JsonObject prof = null;
                if (Settings.ProfilesNode != null) prof = Settings.ProfilesNode[name] as System.Text.Json.Nodes.JsonObject;
                System.Text.Json.Nodes.JsonObject tools = new System.Text.Json.Nodes.JsonObject();
                if (prof != null)
                {
                    System.Text.Json.Nodes.JsonObject pt = prof["tools"] as System.Text.Json.Nodes.JsonObject;
                    if (pt != null) foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode> kv in pt) tools[kv.Key] = kv.Value == null ? null : kv.Value.DeepClone();
                    System.Text.Json.Nodes.JsonNode mn = prof["minimal_ui_mode"];
                    ov["startup"] = new System.Text.Json.Nodes.JsonObject { ["minimal_ui_mode"] = mn == null ? false : mn.GetValue<bool>() };
                    System.Text.Json.Nodes.JsonObject lay = new System.Text.Json.Nodes.JsonObject();
                    foreach (string k in new string[] { "toolbar_position", "button_size", "tool_labels" })
                        if (prof[k] != null) lay[k] = prof[k].DeepClone();
                    ov["layout"] = lay;
                }
                else
                {
                    bool full = name == "full", asc = name == "ascetic", min = name == "minimal";
                    if (full || asc) foreach (string k in AllTools) tools[k] = full || k == "arrow" || k == "rectangle" || k == "text";
                    ov["startup"] = new System.Text.Json.Nodes.JsonObject { ["minimal_ui_mode"] = min };
                }
                ov["tools"] = tools;
                if (Settings.PropsMode == "profile")        // the mode "own bottom panel for each profile": this profile brings its layout
                {
                    System.Text.Json.Nodes.JsonObject lay2 = ov["layout"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
                    lay2["props_layout"] = Settings.ProfileLayoutOf(name);
                    ov["layout"] = lay2;
                }
                return Settings.WithOverrides(ov);
            }
            catch (Exception ex) { Log.Write("profile settings error: " + ex.Message); return Settings; }
        }

        /// <summary>A profile is a set of tools plus the look of the panels. Built-in: full (everything), ascetic (arrow, rectangle, text), minimal (only copy, save, close).</summary>
        public void ApplyProfile(string name)
        {
            try
            {
                System.Text.Json.Nodes.JsonObject prof = null;
                if (Settings.ProfilesNode != null) prof = Settings.ProfilesNode[name] as System.Text.Json.Nodes.JsonObject;
                Settings.PatchUser(root =>
                {
                    System.Text.Json.Nodes.JsonObject tools = Settings.Section(root, "tools");
                    if (prof != null)
                    {
                        System.Text.Json.Nodes.JsonObject pt = prof["tools"] as System.Text.Json.Nodes.JsonObject;
                        if (pt != null) foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode> kv in pt) tools[kv.Key] = kv.Value == null ? null : kv.Value.DeepClone();
                        System.Text.Json.Nodes.JsonNode mn = prof["minimal_ui_mode"];
                        Settings.Section(root, "startup")["minimal_ui_mode"] = mn == null ? false : mn.GetValue<bool>();
                        System.Text.Json.Nodes.JsonObject lay = Settings.Section(root, "layout");
                        foreach (string k in new string[] { "toolbar_position", "button_size", "tool_labels" })
                            if (prof[k] != null) lay[k] = prof[k].DeepClone();
                        return;
                    }
                    bool full = name == "full", asc = name == "ascetic", min = name == "minimal";
                    if (full || asc)
                        foreach (string k in AllTools) tools[k] = full || k == "arrow" || k == "rectangle" || k == "text";
                    Settings.Section(root, "startup")["minimal_ui_mode"] = min;
                });
                ReloadSettings();
                Notify(I18n.T("msg.profile_applied", ProfileLabel(name)));
            }
            catch (Exception ex) { Fail(ex); }
        }

        public string ProfileLabel(string name)
        {
            string key = "profile." + name;
            string v = I18n.T(key);
            return v == key ? name : v;
        }

        /// <summary>Remembers the present tools and panel look as a profile with this name.</summary>
        public void SaveProfile(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;                           // a built-in name is allowed: the profile is then rewritten with your own tools
            Settings s = Settings;
            Settings.PatchUser(root =>
            {
                System.Text.Json.Nodes.JsonObject tools = new System.Text.Json.Nodes.JsonObject();
                foreach (string k in AllTools) tools[k] = s.ToolEnabled(k);
                System.Text.Json.Nodes.JsonObject p = new System.Text.Json.Nodes.JsonObject
                {
                    ["tools"] = tools,
                    ["minimal_ui_mode"] = s.MinimalUi,
                    ["toolbar_position"] = s.ToolbarPosition,
                    ["button_size"] = s.ButtonSize,
                    ["tool_labels"] = s.ToolLabels
                };
                Settings.Section(root, "profiles")[name] = p;
            });
            ReloadSettings();
        }

        public void DeleteProfile(string name)
        {
            // a built-in profile can only lose your rewrite (it returns to the original); an own profile is deleted
            Settings.PatchUser(root =>
            {
                Settings.Section(root, "profiles").Remove(name);
                Settings.Section(Settings.Section(root, "layout"), "profile_layouts").Remove(name);     // its bottom panel layout goes back to the built-in one too
            });
            ReloadSettings();
        }

        // ---------- messages, sound, what happens after a save ----------
        /// <summary>A message near the clock (settings: after.notifications and after.notification_seconds).</summary>
        public void Notify(string text, bool always = false, string openPath = null)
        {
            if (_tray == null || (!always && !Settings.NotifyEnabled)) return;
            // openPath: a click on the message opens Explorer with this file selected
            _tray.Balloon(I18n.T("app.name"), text, Math.Max(1, Settings.NotifySeconds) * 1000, openPath);
        }

        private void PlaySound()
        {
            if (!Settings.Sound) return;
            NotifySound.Play(Settings.SoundName, Settings.SoundVolume);
        }

        /// <summary>After a file was saved: also copy it, play a sound, show it in Explorer - as the settings say.</summary>
        private void AfterSaved(Bitmap bmp, string path)
        {
            PlaySound();
            if (Settings.CopyAndSave)
            {
                try { ImageOutput.CopyToClipboard(bmp); } catch (Exception ex) { Log.Write("copy after save error: " + ex.Message); }
            }
            if (Settings.OpenFolderAfterSave)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("open folder error: " + ex.Message); }
            }
            if (Settings.OpenInViewer && File.Exists(path))
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }   // the program that Windows uses for this picture type
                catch (Exception ex) { Log.Write("open in viewer error: " + ex.Message); }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        /// <summary>The active window and its program, for the file name tokens {window} and {process}.</summary>
        private static void RememberWindow()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
                GetWindowText(h, sb, 256);
                string title = sb.ToString();
                foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
                title = title.Trim();
                if (title.Length > 60) title = title.Substring(0, 60).Trim();
                uint pid; GetWindowThreadProcessId(h, out pid);
                string proc = "";
                try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
                ImageOutput.WindowTitle = title;
                ImageOutput.ProcessName = proc;
            }
            catch { }
        }

        // ---------- actions ----------
        public void CaptureRegion(bool autoCopy, int delayMs = 0, Rectangle? initial = null, string pickMode = null)
        {
            if (_overlay != null) return; // already open
            if (delayMs > 0)
            {
                System.Windows.Threading.DispatcherTimer wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
                wait.Tick += delegate { wait.Stop(); CaptureRegion(autoCopy, 0, initial, pickMode); };
                wait.Start();
                return;
            }
            try
            {
                RememberWindow();
                Rectangle bounds = ScreenCapture.VirtualScreen();
                Bitmap shot = ScreenCapture.Capture(bounds, Settings.IncludeCursor);
                RegionOverlay ov = new RegionOverlay(shot, bounds, Theme, I18n, Settings, autoCopy);
                if (initial.HasValue)
                    ov.InitialSelection = new System.Windows.Rect(initial.Value.X - bounds.X, initial.Value.Y - bounds.Y, initial.Value.Width, initial.Value.Height);
                if (!autoCopy && pickMode == null) { int ps; bool pa; List<string> pl = ProfileCycleFor(out ps, out pa); if (Settings.PropsMode == "profile" && pl[ps] != "current") pa = true; ov.SetProfiles(pl, ProfileSettings, ProfileLabel, ps, pa); }   // the key of the editor walks through the profiles
                ov.CopyRequested += delegate (Bitmap bmp)
                {
                    ImageOutput.CopyToClipboard(bmp);
                    Notify(I18n.T("msg.copied"));
                    PlaySound();
                    Log.Write("region copied " + bmp.Width + "x" + bmp.Height);
                };
                ov.SaveRequested += delegate (Bitmap bmp)
                {
                    string path = ImageOutput.Save(bmp, Settings);
                    Notify(I18n.T("msg.saved_to", path), false, path);
                    Log.Write("region saved " + path);
                    AfterSaved(bmp, path);
                    if (Settings.UploadAuto && UploadService.Enabled(Settings)) UploadBitmap(bmp);
                };
                ov.UploadRequested += UploadBitmap;
                ov.RecordRequested += delegate (string kind, Rectangle rect) { OnRegionChosen(kind, rect); };
                if (pickMode != null)
                {
                    // only an area is wanted (GIF, video, scrolling screenshot): the overlay reports it and closes
                    ov.PickMode = pickMode;
                    ov.SetHint(I18n.T("overlay.hint_pick_" + pickMode));
                    ov.RegionPicked += delegate (Rectangle rect) { OnRegionChosen(pickMode, rect); };
                }
                ov.SaveAsRequested += SaveAs;
                ov.PinRequested += delegate (Bitmap bmp, Rectangle where)
                {
                    PinWindow pin = new PinWindow(bmp, where, Theme);
                    pin.Show();
                    Log.Write("pinned " + bmp.Width + "x" + bmp.Height);
                };
                ov.Closed += delegate
                {
                    if (ov.LastRegion.HasValue)
                    {
                        _lastRegion = ov.LastRegion;
                        System.Drawing.Rectangle lr = ov.LastRegion.Value;
                        try { Settings.PatchUser(r => { JsonObject o = Settings.Section(Settings.Section(r, "state"), "last_region"); o["x"] = lr.X; o["y"] = lr.Y; o["w"] = lr.Width; o["h"] = lr.Height; }); }
                        catch (Exception ex) { Log.Write("last region save error: " + ex.Message); }
                    }
                    shot.Dispose(); _overlay = null; Log.Write("overlay closed");
                };
                _overlay = ov;
                ov.Show();
                Log.Write("overlay opened " + bounds.Width + "x" + bounds.Height + " at " + bounds.X + "," + bounds.Y + " autoCopy=" + autoCopy);
            }
            catch (Exception ex) { _overlay = null; Fail(ex); }
        }

        /// <summary>The region tool after a pause (to catch menus and tooltips that disappear when a key is pressed).</summary>
        public void RegionWithDelay()
        {
            int sec = Math.Max(1, Settings.CaptureDelay);
            Notify(I18n.T("msg.delay_started", sec));
            CaptureRegion(false, sec * 1000);
        }

        public void RepeatLastRegion()
        {
            if (!_lastRegion.HasValue)
            {
                Notify(I18n.T("msg.no_last_region"));
                return;
            }
            CaptureRegion(false, 0, _lastRegion);
        }

        // ---------- recording (GIF, video) and the scrolling screenshot ----------
        private GifRecorder _gif;
        private FfmpegRecorder _ff;
        private RecorderWindow _recWin;
        private System.Windows.Threading.DispatcherTimer _recTimer;
        private string _recKind;                 // gif | video while a recording is running
        private DateTime _recStarted;
        private bool _recBusy;                   // the file is being made, or a scroll capture runs

        public bool IsRecording { get { return _recKind != null; } }

        /// <summary>The key or menu item of a recording: the first press asks for an area, the next press (or the Stop button) ends it.</summary>
        public void StartRecordFlow(string kind)
        {
            try
            {
                if (_recKind != null && kind != "scroll") { StopRecording(); return; }
                if (_recBusy || _overlay != null) return;
                if (kind == "video" && FfmpegRecorder.Find(Settings) == null) { Notify(I18n.T("msg.ffmpeg_missing"), true); return; }
                CaptureRegion(true, 0, null, kind);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void OnRegionChosen(string kind, Rectangle rect)
        {
            if (rect.Width < 8 || rect.Height < 8) return;
            // a short pause: the overlay must be gone before the first picture is taken
            System.Windows.Threading.DispatcherTimer t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += delegate { t.Stop(); BeginRecord(kind, rect); };
            t.Start();
        }

        private void BeginRecord(string kind, Rectangle rect)
        {
            try
            {
                if (kind == "scroll") { RunScroll(rect); return; }
                _recKind = kind;
                if (Settings.RecordFrame)
                {
                    _recWin = new RecorderWindow(rect, I18n, Theme);
                    _recWin.StopClicked += StopRecording;
                    _recWin.Show();
                }
                int left = kind == "gif" ? Settings.GifCountdown : Settings.VideoCountdown;
                System.Windows.Threading.DispatcherTimer t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                Action tick = delegate
                {
                    if (_recKind == null) { t.Stop(); return; }
                    if (left > 0) { if (_recWin != null) _recWin.ShowCountdown(left); left--; }
                    else { t.Stop(); StartRecorderNow(kind, rect); }
                };
                t.Tick += delegate { tick(); };
                tick();
                if (left >= 0) t.Start();
            }
            catch (Exception ex) { Fail(ex); CleanupRecording(); }
        }

        private void StartRecorderNow(string kind, Rectangle rect)
        {
            _recStarted = DateTime.Now;
            if (kind == "gif")
            {
                _gif = new GifRecorder(rect, Settings);
                _gif.Start();
            }
            else
            {
                _ff = new FfmpegRecorder();
                string error;
                if (!_ff.Start(Settings, rect, RecordPath(".mp4"), out error))
                {
                    Notify(I18n.T("msg.rec_failed", error), true);
                    CleanupRecording();
                    return;
                }
            }
            Log.Write("recording started: " + kind + " " + rect);
            _recTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _recTimer.Tick += delegate
            {
                if (_recKind == null) return;
                TimeSpan el = DateTime.Now - _recStarted;
                if (_recWin != null) _recWin.SetElapsed(el);
                bool ended = (_gif != null && !_gif.Running) || (_ff != null && !_ff.Running);
                if (ended) StopRecording();
            };
            _recTimer.Start();
        }

        private void CleanupRecording()
        {
            if (_recTimer != null) { _recTimer.Stop(); _recTimer = null; }
            if (_recWin != null) { try { _recWin.Close(); } catch { } _recWin = null; }
            _recKind = null;
        }

        /// <summary>Ends the recording and makes the file (the GIF is encoded in the background, FFmpeg finishes its file).</summary>
        public async void StopRecording()
        {
            string kind = _recKind;
            if (kind == null) return;
            GifRecorder gif = _gif; FfmpegRecorder ff = _ff;
            _gif = null; _ff = null;
            CleanupRecording();
            _recBusy = true;
            Notify(I18n.T("msg.rec_finishing"));
            string path = null, error = null; int frames = 0;
            try
            {
                if (kind == "gif" && gif != null)
                {
                    string target = RecordPath(".gif");
                    await Task.Run(() => { gif.Stop(); frames = gif.Finish(target); });
                    path = target;
                }
                else if (ff != null)
                {
                    string e2 = null;
                    bool ok = await Task.Run(() => ff.Stop(out e2));
                    if (ok) path = ff.OutputPath; else error = e2;
                }
            }
            catch (Exception ex) { error = ex.Message; Log.Write("recording error: " + ex); }
            _recBusy = false;
            if (path == null) { Notify(I18n.T("msg.rec_failed", error ?? "?"), true); return; }
            long size = new FileInfo(path).Length;
            Notify(I18n.T("msg.rec_saved", path, (size / 1024.0 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
            Log.Write("recording saved " + path + " frames=" + frames + " bytes=" + size);
            bool copy = kind == "gif" ? Settings.GifCopyFile : Settings.VideoCopyFile;
            if (copy)
            {
                // the FILE goes to the clipboard: it can be pasted into Telegram, a mail, Explorer
                try { System.Windows.Forms.Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { path }); }
                catch (Exception ex) { Log.Write("clipboard file error: " + ex.Message); }
            }
            PlaySound();
            if (Settings.OpenFolderAfterSave)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); } catch { }
            }
        }

        private string RecordPath(string ext)
        {
            string folder = string.IsNullOrWhiteSpace(Settings.RecordFolder) ? Settings.SaveFolder : Settings.RecordFolder;
            folder = Environment.ExpandEnvironmentVariables(folder);
            Directory.CreateDirectory(folder);
            string name = ImageOutput.ExpandTemplate(Settings.FileNameTemplate, DateTime.Now);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string path = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, name + "_" + n + ext);
            return path;
        }

        private async void RunScroll(Rectangle rect)
        {
            if (_recBusy) return;
            _recBusy = true;
            Notify(I18n.T("msg.scroll_started"));
            ScrollCapture.Result res = null; string error = null;
            Settings st = Settings;
            try { res = await Task.Run(() => ScrollCapture.Run(rect, st, null)); }
            catch (Exception ex) { error = ex.Message; Log.Write("scroll capture error: " + ex); }
            _recBusy = false;
            if (res == null || res.Image == null) { Notify(I18n.T("msg.rec_failed", error ?? "?"), true); return; }
            try
            {
                using (res.Image)
                {
                    string path = null;
                    Log.Write("scroll capture: " + res.Image.Width + "x" + res.Image.Height + ", pieces " + res.Frames + ", ended: " + res.Why);
                    if (st.ScrollSave) { path = ImageOutput.Save(res.Image, st); Log.Write("scroll saved " + path); }
                    if (st.ScrollCopy) { try { ImageOutput.CopyToClipboard(res.Image); } catch (Exception ex) { Log.Write("scroll copy error: " + ex.Message); } }
                    Notify(I18n.T("msg.scroll_done", res.Image.Width + "x" + res.Image.Height, res.Frames, res.Why, path ?? ""), false, path);
                    PlaySound();
                    if (st.ScrollShow)
                    {
                        PinWindow pin = new PinWindow(res.Image, new Rectangle(rect.X, rect.Y, res.Image.Width, res.Image.Height), Theme);
                        pin.FitToScreen();
                        pin.Show();
                    }
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        // ---------- upload ("cloud") ----------
        private Rectangle FullscreenBounds()
        {
            switch (Settings.FullscreenScope)
            {
                case "all_monitors": return ScreenCapture.VirtualScreen();
                case "primary_monitor": return ScreenCapture.PrimaryMonitor();
                case "secondary_monitor": return ScreenCapture.SecondaryMonitor();
                case "monitor_1": return ScreenCapture.MonitorNumber(1);
                case "monitor_2": return ScreenCapture.MonitorNumber(2);
                default: return ScreenCapture.CursorMonitor();
            }
        }

        public void FullscreenToFile()
        {
            try
            {
                if (Settings.FullscreenInEditor) { CaptureRegion(false, 0, FullscreenBounds()); return; }
                RememberWindow();
                using (Bitmap shot = ScreenCapture.Capture(FullscreenBounds(), Settings.IncludeCursor))
                {
                    string path = ImageOutput.Save(shot, Settings, true);
                    Notify(I18n.T("msg.saved_to", path), false, path);
                    Log.Write("fullscreen saved " + path);
                    AfterSaved(shot, path);
                    if (Settings.UploadAuto && UploadService.Enabled(Settings)) UploadBitmap(shot);
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        public void FullscreenToClipboard()
        {
            try
            {
                if (Settings.FullscreenInEditor) { CaptureRegion(false, 0, FullscreenBounds()); return; }
                using (Bitmap shot = ScreenCapture.Capture(FullscreenBounds(), Settings.IncludeCursor))
                {
                    ImageOutput.CopyToClipboard(shot);
                    Notify(I18n.T("msg.copied"));
                    PlaySound();
                    Log.Write("fullscreen copied " + shot.Width + "x" + shot.Height);
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        // ---------- menu helpers ----------
        public void ApplyChoice(string kind, string value)
        {
            if (kind == "mode") Settings.ThemeMode = value;
            else if (kind == "theme") Settings.AccentTheme = value;
            else if (kind == "lang") Settings.Language = value;
            else if (kind == "neural")
            {
                Settings.NeuralSafe = value == "on";
                try { Settings.PatchUser(r => { Settings.Section(r, "text")["neural_safe"] = Settings.NeuralSafe; }); }
                catch (Exception ex) { Log.Write("save settings error: " + ex.Message); }
                _tray.Rebuild();
                return;
            }
            try { Settings.SaveUi(); } catch (Exception ex) { Log.Write("save settings error: " + ex.Message); }
            I18n.Load(Settings.Language);
            Theme = Theme.Load(Settings.AccentTheme, Settings.ThemeMode);
            Log.Write("choice " + kind + "=" + value + " (language=" + I18n.Code + " mode=" + Theme.Mode + " theme=" + Settings.AccentTheme + ")");
            _tray.Rebuild();
        }

        public void OpenShotsFolder()
        {
            try
            {
                string folder = Environment.ExpandEnvironmentVariables(Settings.SaveFolder);
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Fail(ex); }
        }

        public void OpenLog()
        {
            try
            {
                string log = Path.Combine(Paths.Logs, "app.log");
                if (File.Exists(log)) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + log + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Fail(Exception ex)
        {
            Log.Write("ERROR: " + ex);
            if (_tray != null) _tray.Balloon(I18n.T("app.name"), I18n.T("msg.error") + ": " + ex.Message);
        }

        public void Quit()
        {
            Log.Write("exit");
            Dispose();
            _app.Shutdown();
        }

        public void Dispose()
        {
            if (_overlay != null) { try { _overlay.Close(); } catch { } _overlay = null; }
            if (_settingsWindow != null) { try { _settingsWindow.Close(); } catch { } _settingsWindow = null; }
            if (_hotkeys != null) { _hotkeys.Dispose(); _hotkeys = null; }
            if (_tray != null) { _tray.Dispose(); _tray = null; }
        }
    }
}
