using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    internal enum EditorTool { Select, Arrow, Line, Rect, Ellipse, Brush, Marker, Text, Steps, Blur, Eraser, Picker, Fill, Spotlight, Magnifier }

    /// <summary>One remembered set of values of a tool (a preset): color, size, fill, rounded corners, text style.</summary>
    internal sealed class ToolStyle
    {
        public WpfColor Color = WpfColor.FromRgb(229, 50, 45);
        public double Size = 4;
        public int Fill;          // shapes: 0 none, 1 translucent, 2 solid
        public bool Rounded;      // rectangle: rounded corners
        public int TextStyle;     // text: 0 plain, 1 outline, 2 plate, 3 auto
        public int Opacity = 100; // percent, 10..100
        // text: font of this preset (empty / -1: the global settings of the Text page are used)
        public string FontName = "";
        public int Bold = -1;     // -1 global, 0 no, 1 yes
        public int Italic = -1;
        public string Align = "";   // left | center | right | empty = global
        // arrow: head of this preset (empty: global editor.arrow_head) and its size factor
        public string HeadStyle = "";
        public double HeadScale = 1.0;
        public bool HeadHollow;   // arrow head (triangle, dot): an outline of the line's thickness instead of a filled shape

        public ToolStyle Clone() { return (ToolStyle)MemberwiseClone(); }
    }

    /// <summary>Three presets (keys 1, 2, 3) for every drawing tool, and which one is active. Values are remembered
    /// between runs (config\settings.json, section "presets"). Changing a color or size edits the active preset.</summary>
    internal sealed class StyleStore
    {
        public int PresetCount { get; private set; }

        private readonly Dictionary<EditorTool, ToolStyle[]> _p = new Dictionary<EditorTool, ToolStyle[]>();
        private readonly Dictionary<EditorTool, int> _a = new Dictionary<EditorTool, int>();

        public static readonly EditorTool[] Drawing =
        {
            EditorTool.Arrow, EditorTool.Line, EditorTool.Rect, EditorTool.Ellipse, EditorTool.Brush,
            EditorTool.Marker, EditorTool.Text, EditorTool.Steps, EditorTool.Blur, EditorTool.Fill, EditorTool.Spotlight, EditorTool.Magnifier
        };

        public static string Key(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Arrow: return "arrow";
                case EditorTool.Line: return "line";
                case EditorTool.Rect: return "rectangle";
                case EditorTool.Ellipse: return "ellipse";
                case EditorTool.Brush: return "brush";
                case EditorTool.Marker: return "marker";
                case EditorTool.Text: return "text";
                case EditorTool.Steps: return "steps";
                case EditorTool.Blur: return "blur";
                case EditorTool.Fill: return "fill";
                case EditorTool.Spotlight: return "spotlight";
                case EditorTool.Magnifier: return "magnifier";
                case EditorTool.Eraser: return "eraser";
                case EditorTool.Picker: return "picker";
                default: return "select";
            }
        }

        private static WpfColor C(string hex)
        {
            return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }

        private static ToolStyle S(string hex, double size, int fill = 0, bool rounded = false, int textStyle = 0)
        {
            return new ToolStyle { Color = C(hex), Size = size, Fill = fill, Rounded = rounded, TextStyle = textStyle };
        }

        private static ToolStyle SO(string hex, double size, int opacity)
        {
            return new ToolStyle { Color = C(hex), Size = size, Opacity = opacity };
        }

        private static ToolStyle[] Defaults(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Arrow: return new[] { S("#E5322D", 4), S("#1E88E5", 5), S("#2FB24C", 6) };
                case EditorTool.Line: return new[] { S("#E5322D", 3), S("#1E88E5", 4), S("#111111", 6) };
                case EditorTool.Rect: return new[] { S("#E5322D", 3), S("#1E88E5", 3, 1), S("#111111", 3, 0, true) };
                case EditorTool.Ellipse: return new[] { S("#E5322D", 3), S("#1E88E5", 3, 1), S("#FFD400", 4) };
                case EditorTool.Brush: return new[] { S("#E5322D", 4), S("#1E88E5", 6), S("#111111", 3) };
                case EditorTool.Marker: return new[] { S("#FFD400", 14), S("#2FB24C", 14), S("#FF4DA6", 14) };
                case EditorTool.Text: return new[] { S("#FFFFFF", 18, 0, false, 3), S("#FFD400", 20, 0, false, 1), S("#E5322D", 18, 0, false, 2) };
                case EditorTool.Steps: return new[] { S("#E5322D", 28), S("#1E88E5", 28), S("#111111", 28) };
                case EditorTool.Blur: return new[] { S("#000000", 8), S("#000000", 14), S("#000000", 22) };
                // the paint bucket: size = how different a color may be (percent) and still be painted over; the opacity keeps the picture visible
                case EditorTool.Fill: return new[] { SO("#FFD400", 12, 60), SO("#E5322D", 12, 60), SO("#1E88E5", 20, 60) };
                // the spotlight: the color darkens everything around the frame, the opacity is how much
                case EditorTool.Spotlight: return new[] { SO("#000000", 1, 60), SO("#000000", 1, 40), SO("#000000", 1, 80) };
                // the magnifier: size = how many times it enlarges; the color is its ring
                case EditorTool.Magnifier: return new[] { SO("#FFFFFF", 2, 100), SO("#E5322D", 3, 100), SO("#1E88E5", 4, 100) };
                default: return new[] { S("#E5322D", 4), S("#1E88E5", 5), S("#2FB24C", 6) };
            }
        }

        public StyleStore(int count = 3)
        {
            PresetCount = Math.Max(2, Math.Min(9, count));
            foreach (EditorTool t in Drawing)
            {
                ToolStyle[] d = Defaults(t);
                ToolStyle[] all = new ToolStyle[PresetCount];
                for (int i = 0; i < PresetCount; i++) all[i] = i < d.Length ? d[i] : d[i % d.Length].Clone();
                _p[t] = all; _a[t] = 0;
            }
        }

        public static StyleStore Create(Settings s)
        {
            StyleStore st = new StyleStore(s == null ? 3 : s.PresetCount);
            JsonObject node = s == null ? null : s.PresetsNode;
            if (node == null) return st;
            JsonArray rec = node["recent_colors"] as JsonArray;
            if (rec != null)
                foreach (JsonNode n in rec)
                {
                    try { st.Recent.Add(C(n.GetValue<string>())); } catch { }
                }
            foreach (EditorTool t in Drawing)
            {
                try
                {
                    JsonObject o = node[Key(t)] as JsonObject;
                    if (o == null) continue;
                    JsonArray items = o["items"] as JsonArray;
                    if (items != null)
                        for (int i = 0; i < st.PresetCount && i < items.Count; i++)
                        {
                            JsonObject it = items[i] as JsonObject;
                            if (it == null) continue;
                            ToolStyle d = st._p[t][i];
                            string col = Read<string>(it, "color", null);
                            if (col != null) { try { d.Color = C(col); } catch { } }
                            d.Size = Read(it, "size", d.Size);
                            d.Fill = (int)Read(it, "fill", (double)d.Fill);
                            d.Rounded = Read(it, "rounded", d.Rounded);
                            d.TextStyle = (int)Read(it, "text_style", (double)d.TextStyle);
                            d.Opacity = Math.Max(10, Math.Min(100, (int)Read(it, "opacity", (double)d.Opacity)));
                            d.FontName = Read(it, "font", d.FontName) ?? "";
                            d.Bold = (int)Read(it, "bold", (double)d.Bold);
                            d.Italic = (int)Read(it, "italic", (double)d.Italic);
                            d.Align = Read(it, "align", d.Align) ?? "";
                            d.HeadStyle = Read(it, "head", d.HeadStyle) ?? "";
                            d.HeadScale = Math.Max(0.3, Math.Min(5, Read(it, "head_scale", d.HeadScale)));
                            d.HeadHollow = Read(it, "head_hollow", d.HeadHollow);
                        }
                    int act = (int)Read(o, "active", 0.0);
                    st._a[t] = Math.Max(0, Math.Min(st.PresetCount - 1, act));
                }
                catch (Exception ex) { Log.Write("presets read error " + t + ": " + ex.Message); }
            }
            return st;
        }

        private static T Read<T>(JsonObject o, string name, T def)
        {
            try { JsonNode n = o[name]; return n == null ? def : n.GetValue<T>(); } catch { return def; }
        }

        /// <summary>True for tools that have presets (everything that draws).</summary>
        public static bool HasStyle(EditorTool t) { return Array.IndexOf(Drawing, t) >= 0; }

        public ToolStyle Current(EditorTool t)
        {
            if (!HasStyle(t)) t = EditorTool.Arrow;
            return _p[t][_a[t]];
        }

        public ToolStyle Preset(EditorTool t, int i) { return _p[t][i]; }
        public int Active(EditorTool t) { return HasStyle(t) ? _a[t] : 0; }
        public void SetActive(EditorTool t, int i) { if (HasStyle(t)) _a[t] = Math.Max(0, Math.Min(PresetCount - 1, i)); }

        // ---- recently used custom colors (shown next to the palette) ----
        public readonly List<WpfColor> Recent = new List<WpfColor>();
        public const int RecentMax = 8;

        public void AddRecent(WpfColor c)
        {
            Recent.RemoveAll(x => x == c);
            Recent.Insert(0, c);
            if (Recent.Count > RecentMax) Recent.RemoveRange(RecentMax, Recent.Count - RecentMax);
        }

        private static string Hex(WpfColor c)
        {
            string rgb = c.R.ToString("X2", CultureInfo.InvariantCulture) + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);
            return c.A == 255 ? "#" + rgb : "#" + c.A.ToString("X2", CultureInfo.InvariantCulture) + rgb;   // #AARRGGBB when the color is translucent
        }

        public JsonObject ToJson()
        {
            JsonObject root = new JsonObject();
            foreach (EditorTool t in Drawing)
            {
                JsonArray items = new JsonArray();
                foreach (ToolStyle s in _p[t])
                {
                    items.Add(new JsonObject
                    {
                        ["color"] = Hex(s.Color),
                        ["size"] = s.Size,
                        ["fill"] = s.Fill,
                        ["rounded"] = s.Rounded,
                        ["text_style"] = s.TextStyle,
                        ["opacity"] = s.Opacity,
                        ["font"] = s.FontName,
                        ["bold"] = s.Bold,
                        ["italic"] = s.Italic,
                        ["align"] = s.Align,
                        ["head"] = s.HeadStyle,
                        ["head_scale"] = s.HeadScale,
                        ["head_hollow"] = s.HeadHollow
                    });
                }
                root[Key(t)] = new JsonObject { ["active"] = _a[t], ["items"] = items };
            }
            JsonArray rec = new JsonArray();
            foreach (WpfColor c in Recent) rec.Add(Hex(c));
            root["recent_colors"] = rec;
            return root;
        }
    }
}
