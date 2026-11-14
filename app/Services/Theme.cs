using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>Colors and shape of one theme in one mode (light or dark), taken from config\themes\*.json.</summary>
    internal sealed class Theme
    {
        private static readonly string[] BuiltIn = { "slate", "slate-soft", "tide", "sage", "rose" };

        /// <summary>The built-in themes first, then every other file in config\themes (the user's own themes).</summary>
        public static string[] Ids
        {
            get
            {
                System.Collections.Generic.List<string> ids = new System.Collections.Generic.List<string>(BuiltIn);
                try
                {
                    if (Directory.Exists(Paths.Themes))
                    {
                        string[] files = Directory.GetFiles(Paths.Themes, "*.json");
                        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                        foreach (string f in files)
                        {
                            string id = Path.GetFileNameWithoutExtension(f);
                            if (!ids.Contains(id)) ids.Add(id);
                        }
                    }
                }
                catch (Exception ex) { Log.Write("theme list error: " + ex.Message); }
                return ids.ToArray();
            }
        }

        /// <summary>The name written in the theme file ("name"), or the id.</summary>
        public static string DisplayName(string id)
        {
            try
            {
                string path = Path.Combine(Paths.Themes, id + ".json");
                if (File.Exists(path))
                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)))
                    {
                        JsonElement n;
                        if (doc.RootElement.TryGetProperty("name", out n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())) return n.GetString();
                    }
            }
            catch { }
            return id;
        }

        /// <summary>Makes the user's own theme from an accent color: the shape and the neutral colors of Slate, the accent of the user.</summary>
        public static void Create(string id, string name, WpfColor accent)
        {
            string baseFile = Path.Combine(Paths.Themes, "slate.json");
            System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(baseFile, Encoding.UTF8)) as System.Text.Json.Nodes.JsonObject;
            root["id"] = id; root["name"] = name;
            root["description"] = new System.Text.Json.Nodes.JsonObject { ["ru"] = "Своя тема", ["en"] = "My own theme" };
            root["source"] = "created in SNAPter settings";
            foreach (string mode in new string[] { "light", "dark" })
            {
                System.Text.Json.Nodes.JsonObject m = root[mode] as System.Text.Json.Nodes.JsonObject;
                bool dark = mode == "dark";
                WpfColor a = dark ? Mix(accent, System.Windows.Media.Colors.White, 0.25) : accent;
                m["accent"] = Hex(a);
                m["accent_hover"] = Hex(dark ? Mix(a, System.Windows.Media.Colors.White, 0.2) : Mix(a, System.Windows.Media.Colors.Black, 0.18));
                m["accent_text"] = Luma(a) > 0.45 ? "#111827" : "#ffffff";
                m["accent_soft"] = Hex(dark ? Mix(a, Color("#181b24"), 0.8) : Mix(a, System.Windows.Media.Colors.White, 0.88));
                m["focus"] = Hex(a);
            }
            File.WriteAllText(Path.Combine(Paths.Themes, id + ".json"), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
        }

        private static WpfColor Mix(WpfColor a, WpfColor b, double t)
        {
            return WpfColor.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        private static string Hex(WpfColor c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

        private static double Luma(WpfColor c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

        private static WpfColor Color(string hex) { return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex); }

        public string Id;
        public string Mode;
        public WpfColor Bg = WpfColor.FromRgb(245, 245, 245);
        public WpfColor Surface = WpfColor.FromRgb(255, 255, 255);
        public WpfColor SurfaceMuted = WpfColor.FromRgb(238, 238, 238);
        public WpfColor Text = WpfColor.FromRgb(30, 30, 30);
        public WpfColor TextSecondary = WpfColor.FromRgb(100, 100, 100);
        public WpfColor Accent = WpfColor.FromRgb(36, 120, 214);
        public WpfColor AccentHover = WpfColor.FromRgb(20, 100, 190);
        public WpfColor AccentText = WpfColor.FromRgb(255, 255, 255);
        public WpfColor Border = WpfColor.FromRgb(200, 200, 200);
        public double RadiusMd = 10;

        public static string SystemMode()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                if (v is int && (int)v == 0) return "dark";
            }
            catch { }
            return "light";
        }

        public static string ResolveMode(string setting)
        {
            if (setting == "light" || setting == "dark") return setting;
            return SystemMode();
        }

        public static Theme Load(string themeId, string modeSetting)
        {
            Theme t = new Theme();
            t.Id = themeId;
            t.Mode = ResolveMode(modeSetting);
            string path = Path.Combine(Paths.Themes, themeId + ".json");
            if (!File.Exists(path)) return t;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement roles;
                    if (root.TryGetProperty(t.Mode, out roles))
                    {
                        t.Bg = Color(roles, "bg", t.Bg);
                        t.Surface = Color(roles, "surface", t.Surface);
                        t.SurfaceMuted = Color(roles, "surface_muted", t.SurfaceMuted);
                        t.Text = Color(roles, "text", t.Text);
                        t.TextSecondary = Color(roles, "text_secondary", t.TextSecondary);
                        t.Accent = Color(roles, "accent", t.Accent);
                        t.AccentHover = Color(roles, "accent_hover", t.AccentHover);
                        t.AccentText = Color(roles, "accent_text", t.AccentText);
                        t.Border = Color(roles, "border", t.Border);
                    }
                    JsonElement shape, r;
                    if (root.TryGetProperty("shape", out shape) && shape.TryGetProperty("radius_md", out r) && r.ValueKind == JsonValueKind.String)
                    {
                        string s = r.GetString().Replace("px", "").Trim();
                        double px;
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out px)) t.RadiusMd = px;
                    }
                }
            }
            catch (Exception ex) { Log.Write("theme error " + themeId + ": " + ex.Message); }
            return t;
        }

        private static WpfColor Color(JsonElement roles, string name, WpfColor fallback)
        {
            JsonElement v;
            if (!roles.TryGetProperty(name, out v) || v.ValueKind != JsonValueKind.String) return fallback;
            try { return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(v.GetString().Trim()); }
            catch { return fallback; }
        }
    }
}
