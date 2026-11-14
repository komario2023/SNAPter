using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Snapter
{
    /// <summary>One look of the beautiful screenshot as a set of parameters: the base (window, browser, polaroid...) and what can be changed in it.
    /// A built-in look ("window") has its own default parameters; the owner's changes are stored under its id and replace them; new looks
    /// ("my1", "my2"...) are stored the same way. Everything lives in config\settings.json, section beautify.looks.</summary>
    internal sealed class BeautifyLook
    {
        public static readonly string[] Types = { "classic", "window", "windows", "browser", "polaroid", "glass", "neon", "sticker", "stack", "bezel", "cinema" };

        public string Id = "";
        public string Name = "";             // "" = the usual name of a built-in look
        public string Type = "classic";
        public double Angle = double.NaN;    // degrees; NaN = the base's own (polaroid -2.5, the others 0)
        public double Frame = -1;            // the frame around the picture, px; -1 = the base's own
        public string FrameColor = "";       // "" = the base's own
        public double Bar = -1;              // the title bar of window / windows / browser, px
        public string BarColor = "";
        public bool Dots = true;             // the three colored dots of the window and the browser
        public string Text = "";             // browser: the address; window / windows: the title; polaroid: the caption. "" = the base's own
        public double Bottom = -1;           // polaroid: the wide bottom, px
        public int Radius = -1;              // corners, px; -1 = from the settings
        public int Shadow = -1;              // -1 = from the settings, 0 = no shadow, 1 = shadow
        public string Glow = "";             // neon: the color of the glow; "" = the first color of the palette
        public string Bg1 = "", Bg2 = "";    // an own background (two colors); "" = the base's own or the palette
        public int Layers = 2;               // stack: sheets behind the picture
        public int BarsPct = 14;             // cinema: the black bars, percent of the height
        public int Alpha = 110;              // glass: how milky the frame is, 0-255
        public bool Touched;                 // changed by the owner (only such built-in looks are stored)

        public static bool IsBuiltIn(string id) { return Array.IndexOf(BeautifyStyles.Ids, id) >= 0; }

        public static BeautifyLook Default(string id)
        {
            return new BeautifyLook { Id = id, Type = Array.IndexOf(Types, id) >= 0 ? id : "classic" };
        }

        public BeautifyLook Clone() { return (BeautifyLook)MemberwiseClone(); }

        // ---- JSON ----
        public JsonObject ToJson()
        {
            JsonObject o = new JsonObject();
            if (Name.Length > 0) o["name"] = Name;
            o["type"] = Type;
            if (!double.IsNaN(Angle)) o["angle"] = Angle;
            if (Frame >= 0) o["frame"] = Frame;
            if (FrameColor.Length > 0) o["frame_color"] = FrameColor;
            if (Bar >= 0) o["bar"] = Bar;
            if (BarColor.Length > 0) o["bar_color"] = BarColor;
            if (!Dots) o["dots"] = false;
            if (Text.Length > 0) o["text"] = Text;
            if (Bottom >= 0) o["bottom"] = Bottom;
            if (Radius >= 0) o["radius"] = Radius;
            if (Shadow >= 0) o["shadow"] = Shadow;
            if (Glow.Length > 0) o["glow"] = Glow;
            if (Bg1.Length > 0) o["bg1"] = Bg1;
            if (Bg2.Length > 0) o["bg2"] = Bg2;
            if (Layers != 2) o["layers"] = Layers;
            if (BarsPct != 14) o["bars_pct"] = BarsPct;
            if (Alpha != 110) o["alpha"] = Alpha;
            return o;
        }

        private static string S(JsonObject o, string key, string def)
        {
            try { JsonNode n = o[key]; return n == null ? def : n.GetValue<string>(); } catch { return def; }
        }

        private static double D(JsonObject o, string key, double def)
        {
            try
            {
                JsonNode n = o[key];
                if (n == null) return def;
                return double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);   // works for whole numbers, decimals, in memory or from the file
            }
            catch { return def; }
        }

        public static BeautifyLook FromJson(string id, JsonObject o)
        {
            BeautifyLook l = Default(id);
            string t = S(o, "type", l.Type);
            l.Type = Array.IndexOf(Types, t) >= 0 ? t : l.Type;
            l.Name = S(o, "name", "");
            l.Angle = D(o, "angle", double.NaN);
            l.Frame = D(o, "frame", -1); l.FrameColor = S(o, "frame_color", "");
            l.Bar = D(o, "bar", -1); l.BarColor = S(o, "bar_color", "");
            try { l.Dots = o["dots"] == null || o["dots"].GetValue<bool>(); } catch { l.Dots = true; }
            l.Text = S(o, "text", ""); l.Bottom = D(o, "bottom", -1);
            l.Radius = (int)D(o, "radius", -1); l.Shadow = (int)D(o, "shadow", -1);
            l.Glow = S(o, "glow", ""); l.Bg1 = S(o, "bg1", ""); l.Bg2 = S(o, "bg2", "");
            l.Layers = (int)Math.Max(1, Math.Min(5, D(o, "layers", 2)));
            l.BarsPct = (int)Math.Max(2, Math.Min(30, D(o, "bars_pct", 14)));
            l.Alpha = (int)Math.Max(0, Math.Min(255, D(o, "alpha", 110)));
            l.Touched = true;
            return l;
        }

        /// <summary>The owner's looks from config (section beautify.looks), in the order they were written.</summary>
        public static List<BeautifyLook> ParseAll(JsonObject looks)
        {
            List<BeautifyLook> list = new List<BeautifyLook>();
            if (looks == null) return list;
            foreach (KeyValuePair<string, JsonNode> kv in looks)
            {
                JsonObject o = kv.Value as JsonObject;
                if (o == null || string.IsNullOrWhiteSpace(kv.Key)) continue;
                try { list.Add(FromJson(kv.Key, o)); } catch { }
            }
            return list;
        }

        // ---- the random generator ----
        private static readonly string[] BarLight = { "#E9ECF1", "#F3F3F3", "#DDEBFF", "#E7E0F5", "#FFF1D6", "#E2F3E6" };
        private static readonly string[] BarDark = { "#2B2D31", "#1F2937", "#2A2438", "#1E293B" };
        private static readonly string[] Titles = { "Untitled", "Screenshot", "Report - final", "Notes", "Preview", "Dashboard", "Terminal", "Settings" };
        private static readonly string[] Urls = { "example.com", "my.site/page", "docs.app/guide", "news.today/story", "shop.example/cart", "localhost:3000", "mail.example.com" };
        private static readonly string[] Captions = { "Summer 2026", "Look at this!", "New design", "Before / after", "Bug found", "Idea", "Today" };
        private static readonly string[] Glows = { "#22D3EE", "#F472B6", "#A3E635", "#FB923C", "#C084FC", "#38BDF8", "#FACC15" };
        private static readonly string[][] DarkBg = { new[] { "#0B1020", "#1B1440" }, new[] { "#0F172A", "#164E63" }, new[] { "#120A1F", "#3B0764" }, new[] { "#0A0A0F", "#1F1F2E" }, new[] { "#111827", "#1F2937" } };
        private static readonly string[][] BrightBg = { new[] { "#FDE68A", "#F472B6" }, new[] { "#A7F3D0", "#3B82F6" }, new[] { "#FBCFE8", "#C4B5FD" }, new[] { "#FED7AA", "#FCA5A5" }, new[] { "#BFDBFE", "#818CF8" } };
        private static readonly string[] PaperColors = { "#FCFCFA", "#F6EFE0", "#FFFFFF", "#F3F4F6" };
        private static readonly string[] StickerColors = { "#FFFFFF", "#FFF6C2", "#FFE0EC", "#D9F2FF" };
        private static readonly string[] BezelColors = { "#16181C", "#E8E8EC", "#B8A07E", "#2B3A67", "#3A1F2B" };

        private static T Pick<T>(Random r, T[] a) { return a[r.Next(a.Length)]; }

        /// <summary>A fresh look with a random base and random, but tasteful, parameters. "id" is kept (a built-in look can be replaced).
        /// opt limits what may fall out: no dark looks, only some bases, and how wild the extras (rotation, corners, own background) are.</summary>
        public static BeautifyLook Random(Random r, string id, BeautifyRandomOptions opt = null)
        {
            if (opt == null) opt = new BeautifyRandomOptions();
            List<string> types = opt.AllowedTypes();
            BeautifyLook l = new BeautifyLook { Id = id, Type = types[r.Next(types.Count)], Touched = true };
            double wild = Math.Max(0, Math.Min(100, opt.Wild)) / 100.0;
            if (r.NextDouble() < 0.10 + 0.5 * wild) l.Angle = r.Next(-5, 6);
            if (r.NextDouble() < 0.15 + 0.6 * wild) l.Radius = r.Next(4, 27);
            if (r.NextDouble() < 0.05 + 0.4 * wild) { string[] bg = Pick(r, BrightBg); l.Bg1 = bg[0]; l.Bg2 = bg[1]; }
            bool dark = opt.AllowDark;
            switch (l.Type)
            {
                case "window":
                    l.Bar = r.Next(26, 40); l.BarColor = (dark && r.NextDouble() < 0.3) ? Pick(r, BarDark) : Pick(r, BarLight);
                    l.Dots = r.NextDouble() < 0.85; if (r.NextDouble() < 0.2 + 0.5 * wild) l.Text = Pick(r, Titles); break;
                case "windows":
                    l.Bar = r.Next(30, 40); l.BarColor = (dark && r.NextDouble() < 0.3) ? Pick(r, BarDark) : Pick(r, BarLight);
                    l.Text = Pick(r, Titles); break;
                case "browser":
                    l.Bar = r.Next(38, 54); l.BarColor = (dark && r.NextDouble() < 0.3) ? Pick(r, BarDark) : Pick(r, BarLight);
                    l.Dots = r.NextDouble() < 0.85; l.Text = Pick(r, Urls); break;
                case "polaroid":
                    l.Frame = r.Next(12, 27); l.Bottom = r.Next(40, 81); l.FrameColor = Pick(r, PaperColors);
                    l.Angle = r.Next(-5, 6); if (r.NextDouble() < 0.7) l.Text = Pick(r, Captions); break;
                case "glass":
                    l.Frame = r.Next(8, 19); l.Alpha = r.Next(70, 171); break;
                case "neon":
                    l.Glow = Pick(r, Glows); l.Frame = r.Next(2, 6); { string[] bg = Pick(r, DarkBg); l.Bg1 = bg[0]; l.Bg2 = bg[1]; } break;
                case "sticker":
                    l.Frame = r.Next(6, 17); l.FrameColor = Pick(r, StickerColors); break;
                case "stack":
                    l.Layers = r.Next(2, 5); break;
                case "bezel":
                    l.Frame = r.Next(10, 23); l.FrameColor = Pick(r, dark ? BezelColors : BezelLight); break;
                case "cinema":
                    l.BarsPct = r.Next(8, 19); { string[] bg = Pick(r, DarkBg); l.Bg1 = bg[0]; l.Bg2 = bg[1]; } break;
            }
            return l;
        }

        private static readonly string[] BezelLight = { "#E8E8EC", "#B8A07E", "#D9DEE8", "#F1E4D0" };
    }

    /// <summary>What the random look may be: dark looks allowed or not (neon and cinema are dark by nature, as are the dark bars and bezels),
    /// which bases may fall out, and how wild the extras are (0-100). Settings: section beautify.random.</summary>
    internal sealed class BeautifyRandomOptions
    {
        public bool AllowDark = true;
        public string Types = "classic,window,windows,browser,polaroid,glass,neon,sticker,stack,bezel,cinema";
        public int Wild = 50;

        public List<string> AllowedTypes()
        {
            List<string> r = new List<string>();
            foreach (string t in (Types ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string k = t.Trim().ToLowerInvariant();
                if (Array.IndexOf(BeautifyLook.Types, k) < 0 || r.Contains(k)) continue;
                if (!AllowDark && (k == "neon" || k == "cinema")) continue;
                r.Add(k);
            }
            if (r.Count == 0) r.Add("window");
            return r;
        }

        public static BeautifyRandomOptions From(Settings s)
        {
            return new BeautifyRandomOptions { AllowDark = s.RandomAllowDark, Types = s.RandomTypes, Wild = s.RandomWild };
        }
    }
}