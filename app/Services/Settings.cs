using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Snapter
{
    internal sealed class HotkeyDef
    {
        public string Id;
        public string Keys;
        public bool Enabled;
        public string Action;
    }

    /// <summary>Defaults (config\settings.default.json) overlaid with the user's choices
    /// (config\settings.json, created on the first change).</summary>
    internal sealed class Settings
    {
        public string Language = "auto";
        public string ThemeMode = "system";
        public string AccentTheme = "slate";

        public string TrayClick = "region";   // region | menu | none
        public JsonObject PresetsNode;        // remembered presets of the tools (see ToolStyles.cs)
        public JsonObject ProfilesNode;       // the user's own profiles (section "profiles")

        public bool RunWithWindows = false;
        public int CaptureDelay = 3;            // seconds, for "region after a delay"
        public bool WindowSnap = true;          // a plain click on a window selects the whole window
        public bool NeuralSafe = false;         // plates under text are replaced by outlines (a plate can be mistaken for an interface element by a neural net)
        public int JpgQuality = 92;
        // ---- tray icon ----
        public string TrayIcon = "viewfinder";        // one of IconFactory.Kinds
        public bool TrayIconFollowTaskbar = true;     // the ink follows the light/dark taskbar; off = the ink of the program theme

        public bool TrayIconFollowTheme = false;      // the accent color of the icon follows the program theme
        public string TrayIconColor = "blue";         // one of IconFactory.AccentKeys (used when it does not follow the theme)
        public string TrayIconInk = "black";          // one of IconFactory.InkKeys (used when it does not follow the taskbar)

        // ---- Shift / Ctrl / Alt switches ----
        public bool ModEnabled = true;                // all of it on or off
        public bool ModHold = true;                   // the keys act while a region or a shape is drawn
        public bool ModTap = true;                    // a plain tap of the key acts on the region or the selected object
        public bool ModRegion = true;                 // ... on the selection region
        public bool ModShapes = true;                 // ... on shapes and arrows
        public string ModShownActions = "none,constrain,center,rounded,axis,double,fill,constrain+center";   // which choices the settings list shows

        // ---- windows ----
        public double SettingsLeft = double.NaN, SettingsTop = double.NaN, SettingsWidth = 880, SettingsHeight = 640, SettingsNavWidth = 190;
        public bool SettingsMaximized = false;
        public bool AutoRebuild = true;               // the exe rebuilds itself at the start when the source files changed (AutoUpdate.cs)
        public bool RememberSettingsTab = false;      // the settings open on the page where they were closed
        public int SettingsLastPage = 0;

        // ---- look of the two panels ----
        public string RatioList = "free,16:9,4:3,3:2,1:1,21:9,4:5,A4";   // Tab walks through these: the first is used first; "free" means no lock
        public bool RatioFlip = true;                 // a 16:9 region pulled upwards/downwards becomes 9:16 (and 4:3 becomes 3:4) by itself
        public int ButtonGap = 2;                   // the outer margin between the tool buttons (0-8)
        public int IconFill = 0;                      // 0: the icon keeps its own size; 40-100: the icon takes this percent of the button (big icons, thin panel)
        public int PanelPad = 4;                      // the inner padding of both panels (0-12)
        public int ButtonPx = 0;                    // 0: by layout.button_size; else the size in pixels (22-64)
        public bool ToolPanelFill = true;
        public int ToolPanelOpacity = 100;
        public bool ToolPanelBorder = true;
        public int ToolPanelRadius = -1;              // -1: from the theme
        public bool ToolPanelShadow = true;
        public bool PropsPanelFill = true;
        public int PropsPanelOpacity = 100;
        public bool PropsPanelBorder = true;
        public int PropsPanelRadius = -1;
        public bool PropsPanelShadow = true;

        // ---- panels ----
        public string ButtonSize = "medium";          // small | medium | large
        public bool ToolLabels = false;               // a text label next to each tool icon
        public string ToolOrder = "";                 // comma separated tool keys; empty = the default order
        // panels when the region is big (layout.fit_*): the program rearranges, shrinks, hides or moves them inside the region
        public bool FitEnabled = true;
        public bool FitShrink = true;
        public int FitMinScale = 70;                  // percent: the smallest size the panels may shrink to
        public bool FitHide = true;                   // hide the less important groups when it is still too tight
        public string FitHideItems = "recent,actions2";
        public string FitFallback = "inside";         // inside | clamp: what to do when nothing fits outside the region
        public string PropsLayout = "presets,colors,recent,shape,sliders,extra|copy,save,actions2,close";   // groups of the bottom panel; "|" starts a new line
        public string PropsMode = "shared";       // "shared": one bottom panel layout for every profile; "profile": every profile has its own (layout.profile_layouts)
        public JsonObject ProfileLayouts;         // profile name -> bottom panel layout text, only for the layouts that differ from the built-in one

        /// <summary>The bottom panel layout a built-in profile has until the owner changes it: full = everything, ascetic = one line without the extra buttons, minimal = Copy / Save and Close.</summary>
        public static string BuiltInPropsLayout(string profile)
        {
            if (profile == "ascetic") return "presets,colors,shape,sliders,extra,copy,save,close";
            if (profile == "minimal") return "copy,save,close";
            return "presets,colors,recent,shape,sliders,extra|copy,save,actions2,close";
        }

        /// <summary>The layout of this profile (the owner's own text, or the built-in one). It does not look at PropsMode.</summary>
        public string ProfileLayoutOf(string profile)
        {
            try
            {
                JsonNode n = ProfileLayouts == null ? null : ProfileLayouts[profile];
                string v = n == null ? null : n.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            catch { }
            return BuiltInPropsLayout(profile);
        }

        /// <summary>The bottom panel layout while this profile is on: its own in the mode "profile", otherwise the common one.</summary>
        public string PropsLayoutFor(string profile)
        {
            return PropsMode == "profile" && !string.IsNullOrEmpty(profile) && profile != "current" ? ProfileLayoutOf(profile) : PropsLayout;
        }
        public string CornerKey = "Shift+Tab";        // inside the region this key moves the panels to the next corner (CapsLock | Shift+Tab | Tab | F1 | none)
        public string StartCorner = "bottom_right";
        public string RatioKey = "Tab";               // the key that switches the aspect ratio of the region (with Shift: back); any key name, none = off
        public string ProfileKey = "Oem3";            // in the editor this key switches to the next profile (Oem3 = the key left of 1: Ё on a Russian keyboard, ` / ~ on an English one; "none" = off)
        public string ProfileOrder = "";              // the order of the profiles the key walks through (names separated by commas); empty = the standard order and a capture starts with the saved setup; once set, the first profile is used when a region is started
        // beautiful screenshot (section "beautify"): background, space around, rounded corners, shadow
        public bool BeautifyEnabled = false;
        public bool RandomAllowDark = true;           // the random look may be dark (neon, cinema, dark bars); the owner may switch it off
        public string RandomTypes = "classic,window,windows,browser,polaroid,glass,neon,sticker,stack,bezel,cinema";   // the bases that may fall out
        public int RandomWild = 50;                   // how unexpected the extras are (0-100)
        public List<BeautifyLook> BeautifyLooks = new List<BeautifyLook>();   // the owner's looks and changes to built-in ones (section beautify.looks)
        public string BeautifyStyle = "classic";      // one of ten looks: classic, window, browser, polaroid, glass, neon, sticker, stack, bezel, cinema
        public string BeautifyBackground = "gradient", BeautifyPreset = "ocean", BeautifyColor1 = "#667EEA", BeautifyColor2 = "#764BA2";
        public int BeautifyPadding = 48, BeautifyRadius = 14, BeautifyShadowBlur = 28, BeautifyShadowOpacity = 40;
        public bool BeautifyShadow = true;
        public bool PanelsDraggable = false;          // the panels get a grip: drag them where you like

        // ---- selection frame and dimming ----
        public double FrameThickness = 2;
        public double HandleSize = 9;
        public string DimColor = "#000000";
        public bool ShowCrosshair = false;
        public bool ShowMagnifier = false;
        public bool ShowSizeLabelAbove = true;        // size label above the frame (false: below)
        public string SizeLabelFormat = "{w} x {h}";

        // ---- after a capture ----
        public bool CopyAndSave = false;              // saving also copies to the clipboard
        public bool OpenFolderAfterSave = false;
        public bool OpenInViewer = false;             // after a file is saved, it opens in the standard image viewer of Windows
        public bool Sound = false;
        public string SoundName = "sys:Asterisk";     // after.sound_name: "sys:Asterisk" ... or "file:Windows Ding.wav"
        public int SoundVolume = 100;                 // after.sound_volume, percent
        public bool NotifyEnabled = true;
        public int NotifySeconds = 3;
        public bool FullscreenInEditor = false;       // "whole screen" opens the editor with the whole monitor selected

        // ---- editor ----
        public Dictionary<string, string> EditorKeys = new Dictionary<string, string>();
        public int PresetCount = 3;                   // 2..9, keys 1..9
        public bool SharedColor = false;              // one color for all tools
        public int EraserRadius = 6;
        public double RotateStep = 15;                // degrees of one click on the rotate buttons of an arrow
        public bool ArrowPanel = true;                // the panel of directions and rotation next to the tool panel
        public int SizeMin = 4, SizeMax = 48;         // the range of the size slider (a typed value may go beyond it, for one object)
        public int OpacityMin = 20;                   // the left end of the opacity slider
        public string PctBrush = "100,50,30";         // quick sizes as percents of the region height: big, medium, small
        public string PctMarker = "100,50,25";
        public string PctLine = "100,50,25";
        public string PctEraser = "100,50,25";

        // ---- keys of the editor: Enter ----
        public Dictionary<string, string> EnterActions = new Dictionary<string, string>();   // none | shift | ctrl | alt ... -> save | save_as | pin | copy | upload | close | none

        // ---- snapping to the axes of other objects (a held key while dragging) ----
        public bool SnapEnabled = true;
        public string SnapKey = "right_alt";          // right_alt | right_ctrl | right_shift | left_alt | left_ctrl | left_shift
        public int SnapThreshold = 10;                // pixels
        public bool SnapGuides = true;                // dashed guide lines while snapped

        // ---- strength of Shift / Ctrl / Alt actions (mouse wheel while the key is held) ----
        public int StrengthRound = 40, StrengthCenter = 100, StrengthConstrain = 100, StrengthAxis = 100, StrengthStep = 5;

        // ---- tray ----
        public string TrayShiftClick = "settings";    // settings | region | menu | none: Shift + click on the tray icon

        // ---- last region (kept between runs) ----
        public System.Drawing.Rectangle? LastRegion;

        // ---- recording: GIF ----
        public int GifFps = 10, GifMaxSeconds = 15, GifScale = 100, GifColors = 256, GifLoop = 0, GifCountdown = 3;
        public bool GifDither = true, GifCursor = true, GifSkipDuplicates = true, GifCopyFile = true;
        // ---- recording: video through FFmpeg ----
        public string FfmpegPath = "";                // empty: search PATH and the usual places
        public int VideoFps = 30, VideoCrf = 23, VideoBitrateKbps = 6000, VideoMaxrateKbps = 0, VideoScale = 100, VideoMaxSeconds = 0, VideoCountdown = 3;
        public string VideoCodec = "libx264", VideoPreset = "veryfast", VideoRateMode = "crf", VideoAudio = "none", VideoAudioDevice = "", VideoPixFmt = "yuv420p", VideoExtra = "";
        public bool VideoCursor = true, VideoCopyFile = true;
        // ---- recording: both ----
        public string RecordFolder = "";              // empty: the screenshots folder
        public bool RecordFrame = true;               // a frame and a stop button around the recorded region
        public bool RecordEditorButtons = true;       // the GIF / Video / Scroll buttons in the bottom panel of the editor
        // ---- scrolling screenshot ----
        public int ScrollDelayMs = 350, ScrollNotches = 3, ScrollMaxHeight = 20000, ScrollSkipTop = 0, ScrollSkipBottom = 0, ScrollTolerance = 3, ScrollStartDelay = 800, ScrollMaxFrames = 80;
        public bool ScrollCopy = true, ScrollSave = true, ScrollShow = false;
        public string TextFont = "Segoe UI";
        public bool TextAllCaps = false;              // everything typed becomes CAPITAL letters (easier for a neural network to read)
        public bool TextBold = true;
        public bool TextItalic = false;
        public string FontList = "Segoe UI,Verdana,Arial,Tahoma,Calibri,Consolas,Georgia";   // your own short list of fonts: the font menus show only these (and "all fonts" below)
        public string TextAlign = "left";           // left | center | right
        public string PlateColor = "auto";            // auto (dark/light by the text) or #RRGGBB
        public int PlateOpacity = 80;                 // percent
        public double OutlineWidth = 0.2;             // of the font size
        public string StepStyle = "number";           // number | upper | lower | roman
        public int StepStart = 1;
        public string ArrowHead = "triangle";         // triangle | open | dot

        // ---- capture ----
        public bool IncludeCursor = false;
        public string FixedSize = "";                 // "800x600": a plain click makes a region of this size
        public string SubfolderTemplate = "";         // for example {yyyy-MM}: screenshots go into sub-folders

        // ---- memory ----
        public bool RememberTool = true;
        public bool RememberFolder = true;
        public string LastTool = "";
        public string LastFolder = "";

        // upload ("cloud")
        public string UploadTarget = "none";        // none | folder | gdrive
        public string UploadFolderPath = "";
        public bool UploadCopyLink = true;          // put the link (or the file path) into the clipboard
        public bool UploadAuto = false;             // upload every saved file too
        public string GdClientId = "";
        public string GdClientSecret = "";
        public string GdFolderName = "SNAPter";
        public string GdFolderId = "";              // overrides the folder name
        public bool GdPublic = true;                // "anyone with the link can view"

        public int WebpQuality = 85;
        public bool WebpLossless = false;
        public string FullscreenFormat = "same";   // same | png | jpg | webp | bmp
        public Dictionary<string, string> ModActionMap = new Dictionary<string, string>();

        public double DimOpacity = 0.45;
        public string FrameColor = "accent";
        public bool ShowSizeLabel = true;

        public string SaveFolder = "%USERPROFILE%\\Pictures\\SNAPter";
        public string FileNameTemplate = "SNAPter_{yyyy-MM-dd}_{HH-mm-ss}";
        public string Format = "png";
        public string FullscreenScope = "cursor_monitor";

        public List<HotkeyDef> Hotkeys = new List<HotkeyDef>();

        // editor
        public bool EditorEnabled = true;
        public bool MinimalUi = false;
        public string ToolbarPosition = "right";
        public string PropertiesPosition = "bottom";
        public string DefaultColor = "#E5322D";
        public double DefaultThickness = 4;
        public double DefaultTextSize = 18;
        public Dictionary<string, bool> Tools = new Dictionary<string, bool>();

        public bool ToolEnabled(string id)
        {
            bool v;
            return !Tools.TryGetValue(id, out v) || v;
        }

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static Settings Load()
        {
            return FromRoot(LoadRoot());
        }

        /// <summary>The defaults overlaid with the user file, as a JSON tree.</summary>
        public static JsonObject LoadRoot()
        {
            JsonObject root = ReadObject(Paths.DefaultSettings) ?? new JsonObject();
            JsonObject user = ReadObject(Paths.UserSettings);
            if (user != null) Merge(root, user);
            return root;
        }

        /// <summary>EVERY setting of the program as JSON: the defaults overlaid with the user's changes, including presets, profiles and
        /// remembered state. Anything added to the defaults later appears here by itself.</summary>
        public static string ExportJson()
        {
            return LoadRoot().ToJsonString(WriteOptions);
        }

        /// <summary>The saved settings with a pending, not yet saved change on top (the settings window shows it at once).</summary>
        public static Settings WithOverrides(JsonObject pending)
        {
            JsonObject root = LoadRoot();
            if (pending != null) Merge(root, pending);
            return FromRoot(root);
        }

        public static Settings FromRoot(JsonObject root)
        {
            Settings s = new Settings();
            s.Language = Str(root, s.Language, "ui", "language");
            s.ThemeMode = Str(root, s.ThemeMode, "ui", "theme_mode");
            s.AccentTheme = Str(root, s.AccentTheme, "ui", "accent_theme");
            s.TrayClick = Str(root, s.TrayClick, "ui", "tray_click");
            s.RunWithWindows = Bool(root, s.RunWithWindows, "startup", "run_with_windows");
            s.CaptureDelay = (int)Num(root, s.CaptureDelay, "capture", "delay_seconds");
            s.WindowSnap = Bool(root, s.WindowSnap, "capture", "window_snap");
            s.NeuralSafe = Bool(root, s.NeuralSafe, "text", "neural_safe");
            s.JpgQuality = (int)Num(root, s.JpgQuality, "output", "jpg_quality");
            s.TrayIcon = Str(root, s.TrayIcon, "ui", "tray_icon");
            s.TrayIconFollowTaskbar = Bool(root, s.TrayIconFollowTaskbar, "ui", "tray_icon_follow_taskbar");
            s.TrayIconFollowTheme = Bool(root, s.TrayIconFollowTheme, "ui", "tray_icon_follow_theme");
            s.TrayIconColor = Str(root, s.TrayIconColor, "ui", "tray_icon_color");
            s.TrayIconInk = Str(root, s.TrayIconInk, "ui", "tray_icon_ink");
            s.ModEnabled = Bool(root, s.ModEnabled, "modifiers", "enabled");
            s.ModHold = Bool(root, s.ModHold, "modifiers", "while_drawing");
            s.ModTap = Bool(root, s.ModTap, "modifiers", "tap");
            s.ModRegion = Bool(root, s.ModRegion, "modifiers", "for_region");
            s.ModShapes = Bool(root, s.ModShapes, "modifiers", "for_shapes");
            s.ModShownActions = Str(root, s.ModShownActions, "modifiers", "shown_actions");
            s.SettingsLeft = Num(root, double.NaN, "state", "settings_window", "left");
            s.SettingsTop = Num(root, double.NaN, "state", "settings_window", "top");
            s.SettingsWidth = Num(root, s.SettingsWidth, "state", "settings_window", "width");
            s.SettingsNavWidth = Math.Max(110, Math.Min(500, Num(root, s.SettingsNavWidth, "state", "settings_window", "nav_width")));
            s.SettingsHeight = Num(root, s.SettingsHeight, "state", "settings_window", "height");
            s.SettingsMaximized = Bool(root, s.SettingsMaximized, "state", "settings_window", "maximized");
            s.AutoRebuild = Bool(root, s.AutoRebuild, "startup", "auto_rebuild");
            s.RememberSettingsTab = Bool(root, s.RememberSettingsTab, "startup", "remember_settings_tab");
            s.SettingsLastPage = (int)Math.Max(0, Num(root, s.SettingsLastPage, "state", "settings_window", "page"));
            s.ButtonPx = (int)Num(root, s.ButtonPx, "layout", "button_px");
            s.RatioList = Str(root, s.RatioList, "ratio", "list");
            s.RatioFlip = Bool(root, s.RatioFlip, "ratio", "flip");
            s.RatioKey = Str(root, s.RatioKey, "ratio", "key");
            s.ButtonGap = (int)Math.Max(0, Math.Min(8, Num(root, s.ButtonGap, "layout", "button_gap")));
            int fill = (int)Num(root, s.IconFill, "layout", "icon_fill"); s.IconFill = fill <= 0 ? 0 : Math.Max(40, Math.Min(100, fill));
            s.PanelPad = (int)Math.Max(0, Math.Min(12, Num(root, s.PanelPad, "layout", "panel_pad")));
            s.ToolPanelFill = Bool(root, s.ToolPanelFill, "layout", "tool_panel", "fill");
            s.ToolPanelOpacity = Math.Max(0, Math.Min(100, (int)Num(root, s.ToolPanelOpacity, "layout", "tool_panel", "opacity")));
            s.ToolPanelBorder = Bool(root, s.ToolPanelBorder, "layout", "tool_panel", "border");
            s.ToolPanelRadius = (int)Num(root, s.ToolPanelRadius, "layout", "tool_panel", "radius");
            s.ToolPanelShadow = Bool(root, s.ToolPanelShadow, "layout", "tool_panel", "shadow");
            s.PropsPanelFill = Bool(root, s.PropsPanelFill, "layout", "props_panel", "fill");
            s.PropsPanelOpacity = Math.Max(0, Math.Min(100, (int)Num(root, s.PropsPanelOpacity, "layout", "props_panel", "opacity")));
            s.PropsPanelBorder = Bool(root, s.PropsPanelBorder, "layout", "props_panel", "border");
            s.PropsPanelRadius = (int)Num(root, s.PropsPanelRadius, "layout", "props_panel", "radius");
            s.PropsPanelShadow = Bool(root, s.PropsPanelShadow, "layout", "props_panel", "shadow");
            s.ButtonSize = Str(root, s.ButtonSize, "layout", "button_size");
            s.ToolLabels = Bool(root, s.ToolLabels, "layout", "tool_labels");
            s.ToolOrder = Str(root, s.ToolOrder, "layout", "tool_order");
            s.PanelsDraggable = Bool(root, s.PanelsDraggable, "layout", "panels_draggable");
            s.BeautifyEnabled = Bool(root, s.BeautifyEnabled, "beautify", "enabled");
            s.RandomAllowDark = Bool(root, s.RandomAllowDark, "beautify", "random", "allow_dark");
            s.RandomTypes = Str(root, s.RandomTypes, "beautify", "random", "types");
            s.RandomWild = (int)Math.Max(0, Math.Min(100, Num(root, s.RandomWild, "beautify", "random", "wild")));
            JsonNode looksNode = Walk(root, new string[] { "beautify", "looks" });
            s.BeautifyLooks = BeautifyLook.ParseAll(looksNode as JsonObject);
            string bst = Str(root, s.BeautifyStyle, "beautify", "style");
            s.BeautifyStyle = BeautifyStyles.IsKnown(bst, s.BeautifyLooks) ? bst : "classic";
            string bgk = Str(root, s.BeautifyBackground, "beautify", "background");
            s.BeautifyBackground = bgk == "solid" || bgk == "transparent" ? bgk : "gradient";
            string bpr = Str(root, s.BeautifyPreset, "beautify", "preset");
            s.BeautifyPreset = Array.IndexOf(BeautifyOptions.Presets, bpr) >= 0 ? bpr : "ocean";
            s.BeautifyColor1 = Str(root, s.BeautifyColor1, "beautify", "color1");
            s.BeautifyColor2 = Str(root, s.BeautifyColor2, "beautify", "color2");
            s.BeautifyPadding = (int)Math.Max(0, Math.Min(300, Num(root, s.BeautifyPadding, "beautify", "padding")));
            s.BeautifyRadius = (int)Math.Max(0, Math.Min(100, Num(root, s.BeautifyRadius, "beautify", "radius")));
            s.BeautifyShadow = Bool(root, s.BeautifyShadow, "beautify", "shadow");
            s.BeautifyShadowBlur = (int)Math.Max(0, Math.Min(120, Num(root, s.BeautifyShadowBlur, "beautify", "shadow_blur")));
            s.BeautifyShadowOpacity = (int)Math.Max(0, Math.Min(100, Num(root, s.BeautifyShadowOpacity, "beautify", "shadow_opacity")));
            s.FitEnabled = Bool(root, s.FitEnabled, "layout", "fit_enabled");
            s.FitShrink = Bool(root, s.FitShrink, "layout", "fit_shrink");
            s.FitMinScale = (int)Math.Max(50, Math.Min(100, Num(root, s.FitMinScale, "layout", "fit_min_scale")));
            s.FitHide = Bool(root, s.FitHide, "layout", "fit_hide");
            s.FitHideItems = Str(root, s.FitHideItems, "layout", "fit_hide_items");
            s.FitFallback = Str(root, s.FitFallback, "layout", "fit_fallback") == "clamp" ? "clamp" : "inside";
            s.PropsLayout = Str(root, s.PropsLayout, "layout", "props_layout");
            s.PropsMode = Str(root, s.PropsMode, "layout", "props_mode") == "profile" ? "profile" : "shared";
            s.ProfileLayouts = (root["layout"] as JsonObject)?["profile_layouts"] as JsonObject;
            s.CornerKey = Str(root, s.CornerKey, "layout", "corner_key");
            s.StartCorner = Str(root, s.StartCorner, "layout", "start_corner");
            s.ProfileKey = Str(root, s.ProfileKey, "profile_cycle", "key");
            s.ProfileOrder = Str(root, s.ProfileOrder, "profile_cycle", "order");
            s.FrameThickness = Num(root, s.FrameThickness, "overlay", "frame_thickness");
            s.HandleSize = Num(root, s.HandleSize, "overlay", "handle_size");
            s.DimColor = Str(root, s.DimColor, "overlay", "dim_color");
            s.ShowCrosshair = Bool(root, s.ShowCrosshair, "overlay", "show_crosshair");
            s.ShowMagnifier = Bool(root, s.ShowMagnifier, "overlay", "show_magnifier");
            s.ShowSizeLabelAbove = Bool(root, s.ShowSizeLabelAbove, "overlay", "size_label_above");
            s.SizeLabelFormat = Str(root, s.SizeLabelFormat, "overlay", "size_label_format");
            s.CopyAndSave = Bool(root, s.CopyAndSave, "after", "copy_and_save");
            s.OpenFolderAfterSave = Bool(root, s.OpenFolderAfterSave, "after", "open_folder");
            s.OpenInViewer = Bool(root, s.OpenInViewer, "after", "open_in_viewer");
            s.Sound = Bool(root, s.Sound, "after", "sound");
            s.SoundName = NotifySound.Normalize(Str(root, s.SoundName, "after", "sound_name"));
            s.SoundVolume = Math.Max(0, Math.Min(100, (int)Num(root, s.SoundVolume, "after", "sound_volume")));
            s.NotifyEnabled = Bool(root, s.NotifyEnabled, "after", "notifications");
            s.NotifySeconds = (int)Num(root, s.NotifySeconds, "after", "notification_seconds");
            s.FullscreenInEditor = Bool(root, s.FullscreenInEditor, "after", "fullscreen_in_editor");
            JsonObject ek = root["editor_keys"] as JsonObject;
            if (ek != null)
                foreach (KeyValuePair<string, JsonNode> kv in ek)
                    s.EditorKeys[kv.Key.ToLowerInvariant()] = Str(ek, "", kv.Key);
            s.PresetCount = Math.Max(2, Math.Min(9, (int)Num(root, s.PresetCount, "editor", "preset_count")));
            s.SharedColor = Bool(root, s.SharedColor, "editor", "shared_color");
            s.EraserRadius = Math.Max(1, Math.Min(40, (int)Num(root, s.EraserRadius, "editor", "eraser_radius")));
            s.SizeMin = Math.Max(1, (int)Num(root, s.SizeMin, "editor", "size_min"));
            s.SizeMax = Math.Max(s.SizeMin + 1, (int)Num(root, s.SizeMax, "editor", "size_max"));
            s.OpacityMin = Math.Max(5, Math.Min(95, (int)Num(root, s.OpacityMin, "editor", "opacity_min")));
            s.PctBrush = Str(root, s.PctBrush, "editor", "pct_brush");
            s.PctMarker = Str(root, s.PctMarker, "editor", "pct_marker");
            s.PctLine = Str(root, s.PctLine, "editor", "pct_line");
            s.PctEraser = Str(root, s.PctEraser, "editor", "pct_eraser");
            JsonObject enter = root["enter_actions"] as JsonObject;
            if (enter != null)
                foreach (KeyValuePair<string, JsonNode> kv in enter)
                    s.EnterActions[kv.Key.ToLowerInvariant()] = Str(enter, "", kv.Key);
            s.SnapEnabled = Bool(root, s.SnapEnabled, "snap", "enabled");
            s.SnapKey = Str(root, s.SnapKey, "snap", "key");
            s.SnapThreshold = Math.Max(1, Math.Min(80, (int)Num(root, s.SnapThreshold, "snap", "threshold_px")));
            s.SnapGuides = Bool(root, s.SnapGuides, "snap", "show_guides");
            s.StrengthRound = (int)Math.Max(0, Math.Min(100, Num(root, s.StrengthRound, "modifiers", "default_round_pct")));
            s.StrengthCenter = (int)Math.Max(0, Math.Min(100, Num(root, s.StrengthCenter, "modifiers", "default_center_pct")));
            s.StrengthConstrain = (int)Math.Max(0, Math.Min(100, Num(root, s.StrengthConstrain, "modifiers", "default_constrain_pct")));
            s.StrengthAxis = (int)Math.Max(0, Math.Min(100, Num(root, s.StrengthAxis, "modifiers", "default_axis_pct")));
            s.StrengthStep = (int)Math.Max(1, Math.Min(25, Num(root, s.StrengthStep, "modifiers", "wheel_step_pct")));
            s.TrayShiftClick = Str(root, s.TrayShiftClick, "ui", "tray_shift_click");
            if (root["state"] is JsonObject stObj && stObj["last_region"] is JsonObject lr)
            {
                try { s.LastRegion = new System.Drawing.Rectangle(lr["x"].GetValue<int>(), lr["y"].GetValue<int>(), lr["w"].GetValue<int>(), lr["h"].GetValue<int>()); } catch { }
            }
            s.GifFps = Math.Max(1, Math.Min(60, (int)Num(root, s.GifFps, "record", "gif", "fps")));
            s.GifMaxSeconds = Math.Max(1, Math.Min(600, (int)Num(root, s.GifMaxSeconds, "record", "gif", "max_seconds")));
            s.GifScale = Math.Max(10, Math.Min(100, (int)Num(root, s.GifScale, "record", "gif", "scale_pct")));
            s.GifColors = Math.Max(4, Math.Min(256, (int)Num(root, s.GifColors, "record", "gif", "colors")));
            s.GifLoop = Math.Max(0, (int)Num(root, s.GifLoop, "record", "gif", "loop"));
            s.GifCountdown = Math.Max(0, Math.Min(30, (int)Num(root, s.GifCountdown, "record", "gif", "countdown_seconds")));
            s.GifDither = Bool(root, s.GifDither, "record", "gif", "dither");
            s.GifCursor = Bool(root, s.GifCursor, "record", "gif", "cursor");
            s.GifSkipDuplicates = Bool(root, s.GifSkipDuplicates, "record", "gif", "merge_identical_frames");
            s.GifCopyFile = Bool(root, s.GifCopyFile, "record", "gif", "copy_file_to_clipboard");
            s.FfmpegPath = Str(root, s.FfmpegPath, "record", "video", "ffmpeg_path");
            s.VideoFps = Math.Max(1, Math.Min(120, (int)Num(root, s.VideoFps, "record", "video", "fps")));
            s.VideoCrf = Math.Max(0, Math.Min(51, (int)Num(root, s.VideoCrf, "record", "video", "crf")));
            s.VideoBitrateKbps = Math.Max(100, (int)Num(root, s.VideoBitrateKbps, "record", "video", "bitrate_kbps"));
            s.VideoMaxrateKbps = Math.Max(0, (int)Num(root, s.VideoMaxrateKbps, "record", "video", "maxrate_kbps"));
            s.VideoScale = Math.Max(10, Math.Min(100, (int)Num(root, s.VideoScale, "record", "video", "scale_pct")));
            s.VideoMaxSeconds = Math.Max(0, (int)Num(root, s.VideoMaxSeconds, "record", "video", "max_seconds"));
            s.VideoCountdown = Math.Max(0, Math.Min(30, (int)Num(root, s.VideoCountdown, "record", "video", "countdown_seconds")));
            s.VideoCodec = Str(root, s.VideoCodec, "record", "video", "codec");
            s.VideoPreset = Str(root, s.VideoPreset, "record", "video", "preset");
            s.VideoRateMode = Str(root, s.VideoRateMode, "record", "video", "rate_mode");
            s.VideoAudio = Str(root, s.VideoAudio, "record", "video", "audio");
            s.VideoAudioDevice = Str(root, s.VideoAudioDevice, "record", "video", "audio_device");
            s.VideoPixFmt = Str(root, s.VideoPixFmt, "record", "video", "pixel_format");
            s.VideoExtra = Str(root, s.VideoExtra, "record", "video", "extra_args");
            s.VideoCursor = Bool(root, s.VideoCursor, "record", "video", "cursor");
            s.VideoCopyFile = Bool(root, s.VideoCopyFile, "record", "video", "copy_file_to_clipboard");
            s.RecordFolder = Str(root, s.RecordFolder, "record", "folder");
            s.RecordFrame = Bool(root, s.RecordFrame, "record", "show_frame");
            s.RecordEditorButtons = Bool(root, s.RecordEditorButtons, "record", "editor_buttons");
            s.ScrollDelayMs = Math.Max(50, (int)Num(root, s.ScrollDelayMs, "scroll", "delay_ms"));
            s.ScrollNotches = Math.Max(1, Math.Min(30, (int)Num(root, s.ScrollNotches, "scroll", "wheel_notches")));
            s.ScrollMaxHeight = Math.Max(500, (int)Num(root, s.ScrollMaxHeight, "scroll", "max_height_px"));
            s.ScrollSkipTop = Math.Max(0, (int)Num(root, s.ScrollSkipTop, "scroll", "skip_top_px"));
            s.ScrollSkipBottom = Math.Max(0, (int)Num(root, s.ScrollSkipBottom, "scroll", "skip_bottom_px"));
            s.ScrollTolerance = Math.Max(0, Math.Min(50, (int)Num(root, s.ScrollTolerance, "scroll", "tolerance_pct")));
            s.ScrollStartDelay = Math.Max(0, (int)Num(root, s.ScrollStartDelay, "scroll", "start_delay_ms"));
            s.ScrollMaxFrames = Math.Max(2, (int)Num(root, s.ScrollMaxFrames, "scroll", "max_frames"));
            s.ScrollCopy = Bool(root, s.ScrollCopy, "scroll", "copy_to_clipboard");
            s.ScrollSave = Bool(root, s.ScrollSave, "scroll", "save_file");
            s.ScrollShow = Bool(root, s.ScrollShow, "scroll", "show_result");
            s.RotateStep = Math.Max(1, Math.Min(90, Num(root, s.RotateStep, "editor", "rotate_step")));
            s.ArrowPanel = Bool(root, s.ArrowPanel, "editor", "arrow_panel");
            s.TextFont = Str(root, s.TextFont, "text", "font");
            s.TextAllCaps = Bool(root, s.TextAllCaps, "text", "all_caps");
            s.TextBold = Bool(root, s.TextBold, "text", "bold");
            s.TextItalic = Bool(root, s.TextItalic, "text", "italic");
            s.TextAlign = Str(root, s.TextAlign, "text", "align");
            s.FontList = Str(root, s.FontList, "text", "font_list");
            s.PlateColor = Str(root, s.PlateColor, "text", "plate_color");
            s.PlateOpacity = Math.Max(10, Math.Min(100, (int)Num(root, s.PlateOpacity, "text", "plate_opacity")));
            s.OutlineWidth = Math.Max(0.05, Math.Min(0.5, Num(root, s.OutlineWidth, "text", "outline_width")));
            s.StepStyle = Str(root, s.StepStyle, "editor", "step_style");
            s.StepStart = (int)Num(root, s.StepStart, "editor", "step_start");
            s.ArrowHead = Str(root, s.ArrowHead, "editor", "arrow_head");
            s.IncludeCursor = Bool(root, s.IncludeCursor, "capture", "include_cursor");
            s.FixedSize = Str(root, s.FixedSize, "capture", "fixed_size");
            s.SubfolderTemplate = Str(root, s.SubfolderTemplate, "output", "subfolder_template");
            s.RememberTool = Bool(root, s.RememberTool, "startup", "remember_last_tool");
            s.RememberFolder = Bool(root, s.RememberFolder, "startup", "remember_last_folder");
            s.LastTool = Str(root, s.LastTool, "state", "last_tool");
            s.LastFolder = Str(root, s.LastFolder, "state", "last_folder");
            s.UploadTarget = Str(root, s.UploadTarget, "upload", "target");
            s.UploadFolderPath = Str(root, s.UploadFolderPath, "upload", "folder_path");
            s.UploadCopyLink = Bool(root, s.UploadCopyLink, "upload", "copy_link");
            s.UploadAuto = Bool(root, s.UploadAuto, "upload", "auto_after_save");
            s.GdClientId = Str(root, s.GdClientId, "upload", "gdrive", "client_id");
            s.GdClientSecret = Str(root, s.GdClientSecret, "upload", "gdrive", "client_secret");
            s.GdFolderName = Str(root, s.GdFolderName, "upload", "gdrive", "folder_name");
            s.GdFolderId = Str(root, s.GdFolderId, "upload", "gdrive", "folder_id");
            s.GdPublic = Bool(root, s.GdPublic, "upload", "gdrive", "public_link");
            s.WebpQuality = (int)Num(root, s.WebpQuality, "output", "webp_quality");
            s.WebpLossless = Bool(root, s.WebpLossless, "output", "webp_lossless");
            s.FullscreenFormat = Str(root, s.FullscreenFormat, "output", "fullscreen_format");
            JsonObject ma = root["modifier_actions"] as JsonObject;
            if (ma != null)
                foreach (KeyValuePair<string, JsonNode> kv in ma)
                    s.ModActionMap[kv.Key.ToLowerInvariant()] = Str(ma, "", kv.Key);
            s.PresetsNode = root["presets"] as JsonObject;
            s.ProfilesNode = root["profiles"] as JsonObject;
            s.DimOpacity = Num(root, s.DimOpacity, "overlay", "dim_opacity");
            s.FrameColor = Str(root, s.FrameColor, "overlay", "frame_color");
            s.ShowSizeLabel = Bool(root, s.ShowSizeLabel, "overlay", "show_size_label");
            s.SaveFolder = Str(root, s.SaveFolder, "output", "save_folder");
            s.FileNameTemplate = Str(root, s.FileNameTemplate, "output", "file_name_template");
            s.Format = Str(root, s.Format, "output", "format");
            s.FullscreenScope = Str(root, s.FullscreenScope, "output", "fullscreen_scope");

            s.EditorEnabled = Bool(root, s.EditorEnabled, "modules", "editor");
            s.MinimalUi = Bool(root, s.MinimalUi, "startup", "minimal_ui_mode");
            s.ToolbarPosition = Str(root, s.ToolbarPosition, "layout", "toolbar_position");
            s.PropertiesPosition = Str(root, s.PropertiesPosition, "layout", "properties_panel_position");
            s.DefaultColor = Str(root, s.DefaultColor, "tool_defaults", "color");
            s.DefaultThickness = Num(root, s.DefaultThickness, "tool_defaults", "thickness");
            s.DefaultTextSize = Num(root, s.DefaultTextSize, "tool_defaults", "text_size");
            JsonObject tools = root["tools"] as JsonObject;
            if (tools != null)
                foreach (KeyValuePair<string, JsonNode> kv in tools)
                    s.Tools[kv.Key] = Bool(tools, true, kv.Key);

            JsonObject hk = root["hotkeys"] as JsonObject;
            if (hk != null)
            {
                foreach (KeyValuePair<string, JsonNode> kv in hk)
                {
                    JsonObject o = kv.Value as JsonObject;
                    if (o == null) continue;
                    HotkeyDef d = new HotkeyDef();
                    d.Id = kv.Key;
                    d.Keys = Str(o, "", "keys");
                    d.Enabled = Bool(o, true, "enabled");
                    d.Action = Str(o, kv.Key, "action");
                    s.Hotkeys.Add(d);
                }
            }
            return s;
        }

        /// <summary>Saves only the "ui" section into config\settings.json, keeping other user changes.</summary>
        public void SaveUi()
        {
            JsonObject user = ReadObject(Paths.UserSettings) ?? new JsonObject();
            user["ui"] = new JsonObject
            {
                ["language"] = Language,
                ["theme_mode"] = ThemeMode,
                ["accent_theme"] = AccentTheme,
                ["tray_click"] = TrayClick
            };
            File.WriteAllText(Paths.UserSettings, user.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        /// <summary>The actions assigned to the held Shift / Ctrl / Alt combination (see modifier_actions in the settings).</summary>
        public HashSet<string> ModActions(System.Windows.Input.ModifierKeys mods)
        {
            List<string> parts = new List<string>();
            if ((mods & System.Windows.Input.ModifierKeys.Control) != 0) parts.Add("ctrl");
            if ((mods & System.Windows.Input.ModifierKeys.Shift) != 0) parts.Add("shift");
            if ((mods & System.Windows.Input.ModifierKeys.Alt) != 0) parts.Add("alt");
            HashSet<string> set = new HashSet<string>();
            if (parts.Count == 0 || !ModEnabled) return set;
            string v;
            if (ModActionMap.TryGetValue(string.Join("+", parts), out v) && !string.IsNullOrWhiteSpace(v))
                foreach (string a in v.Split(','))
                {
                    string t = a.Trim().ToLowerInvariant();
                    if (t.Length > 0 && t != "none") set.Add(t);
                }
            return set;
        }

        /// <summary>Changes the user file (config\settings.json) through a callback and writes it back; other user changes are kept.</summary>
        public static void PatchUser(Action<JsonObject> patch)
        {
            JsonObject user = ReadObject(Paths.UserSettings) ?? new JsonObject();
            patch(user);
            File.WriteAllText(Paths.UserSettings, user.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        public static JsonObject Section(JsonObject root, string name)
        {
            JsonObject o = root[name] as JsonObject;
            if (o == null) { o = new JsonObject(); root[name] = o; }
            return o;
        }

        /// <summary>Saves the presets of the tools (section "presets") into config\settings.json.</summary>
        public void SavePresets(JsonObject presets)
        {
            JsonObject user = ReadObject(Paths.UserSettings) ?? new JsonObject();
            user["presets"] = presets;
            File.WriteAllText(Paths.UserSettings, user.ToJsonString(WriteOptions), new UTF8Encoding(false));
            PresetsNode = JsonNode.Parse(presets.ToJsonString()) as JsonObject;
        }

        // ---- helpers ----
        private static JsonObject ReadObject(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path, Encoding.UTF8);
                JsonDocumentOptions opt = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                return JsonNode.Parse(text, null, opt) as JsonObject;
            }
            catch (Exception ex)
            {
                Log.Write("settings read error " + path + ": " + ex.Message);
                return null;
            }
        }

        private static void Merge(JsonObject target, JsonObject source)
        {
            foreach (KeyValuePair<string, JsonNode> kv in source)
            {
                JsonObject src = kv.Value as JsonObject;
                JsonObject dst = target[kv.Key] as JsonObject;
                if (src != null && dst != null) Merge(dst, src);
                else target[kv.Key] = kv.Value == null ? null : kv.Value.DeepClone();
            }
        }

        private static JsonNode Walk(JsonNode node, string[] path)
        {
            JsonNode cur = node;
            foreach (string p in path)
            {
                JsonObject o = cur as JsonObject;
                if (o == null) return null;
                cur = o[p];
            }
            return cur;
        }

        private static string Str(JsonNode n, string def, params string[] path)
        {
            JsonNode v = Walk(n, path);
            try { return v == null ? def : v.GetValue<string>(); } catch { return def; }
        }

        private static double Num(JsonNode n, double def, params string[] path)
        {
            JsonNode v = Walk(n, path);
            if (v == null) return def;
            // a value read from the file is a number; one built in memory (a pending change) may be an int
            try { return v.GetValue<double>(); } catch { }
            try { return v.GetValue<int>(); } catch { }
            try { return v.GetValue<long>(); } catch { }
            try { return double.Parse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture); } catch { return def; }
        }

        private static bool Bool(JsonNode n, bool def, params string[] path)
        {
            JsonNode v = Walk(n, path);
            try { return v == null ? def : v.GetValue<bool>(); } catch { return def; }
        }
    }
}
