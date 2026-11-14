using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>How a finished screenshot is dressed up: a background (gradient, one color or none), space around it, rounded corners, a shadow.</summary>
    internal sealed class BeautifyOptions
    {
        public static readonly string[] Presets = { "ocean", "sunset", "forest", "graphite", "paper", "custom" };

        public bool Enabled;
        public string Background = "gradient";      // gradient | solid | transparent
        public string Style = "classic";             // the id of a look: a built-in one (BeautifyStyles.Ids) or one of the owner's looks
        public Dictionary<string, BeautifyLook> Looks;   // the owner's looks and changes to built-in ones (config: beautify.looks)

        /// <summary>The parameters of a look: the owner's version when there is one, else the built-in default.</summary>
        public BeautifyLook LookOf(string id)
        {
            BeautifyLook l;
            if (Looks != null && id != null && Looks.TryGetValue(id, out l)) return l;
            return BeautifyLook.Default(Array.IndexOf(BeautifyStyles.Ids, id) >= 0 ? id : "classic");
        }
        public string Preset = "ocean";
        public string Color1 = "#667EEA", Color2 = "#764BA2";   // used by "custom"
        public int Padding = 48, Radius = 14;
        public bool Shadow = true;
        public int ShadowBlur = 28, ShadowOpacity = 40;

        public static BeautifyOptions FromSettings(Settings s)
        {
            return new BeautifyOptions
            {
                Enabled = s.BeautifyEnabled, Style = s.BeautifyStyle, Looks = LookMap(s.BeautifyLooks), Background = s.BeautifyBackground, Preset = s.BeautifyPreset, Color1 = s.BeautifyColor1, Color2 = s.BeautifyColor2,
                Padding = s.BeautifyPadding, Radius = s.BeautifyRadius, Shadow = s.BeautifyShadow, ShadowBlur = s.BeautifyShadowBlur, ShadowOpacity = s.BeautifyShadowOpacity
            };
        }

        public static Dictionary<string, BeautifyLook> LookMap(IEnumerable<BeautifyLook> looks)
        {
            Dictionary<string, BeautifyLook> m = new Dictionary<string, BeautifyLook>();
            if (looks != null) foreach (BeautifyLook l in looks) m[l.Id] = l;
            return m;
        }

        private static WpfColor C(string hex, WpfColor fallback)
        {
            try { return (WpfColor)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
        }

        /// <summary>The two colors of the background: from the chosen preset, or the own two colors.</summary>
        public void Colors(out WpfColor a, out WpfColor b)
        {
            switch (Preset)
            {
                case "sunset": a = C("#FF9966", Colors_.Orange); b = C("#FF5E62", Colors_.Red); return;
                case "forest": a = C("#134E5E", Colors_.Teal); b = C("#71B280", Colors_.Green); return;
                case "graphite": a = C("#232526", Colors_.Dark); b = C("#414345", Colors_.Dark); return;
                case "paper": a = C("#F5F7FA", Colors_.Light); b = C("#C3CFE2", Colors_.Light); return;
                case "custom": a = C(Color1, Colors_.Blue); b = C(Color2, Colors_.Purple); return;
                default: a = C("#2193B0", Colors_.Blue); b = C("#6DD5ED", Colors_.Light); return;
            }
        }

        private static class Colors_
        {
            public static readonly WpfColor Orange = System.Windows.Media.Colors.Orange, Red = System.Windows.Media.Colors.Tomato, Teal = System.Windows.Media.Colors.Teal,
                Green = System.Windows.Media.Colors.SeaGreen, Dark = System.Windows.Media.Colors.DimGray, Light = System.Windows.Media.Colors.LightGray,
                Blue = System.Windows.Media.Colors.SteelBlue, Purple = System.Windows.Media.Colors.MediumPurple;
        }
    }

    internal static class Beautifier
    {
        /// <summary>The picture inside a frame of the background: Padding pixels around, rounded corners, a soft shadow. The source is not changed.</summary>
        public static System.Drawing.Bitmap Apply(System.Drawing.Bitmap src, BeautifyOptions o)
        {
            int pad = Math.Max(0, Math.Min(300, o.Padding));
            BitmapSource img = ToSource(src);
            BeautifyCard card = BeautifyStyles.Build(img, src.Width, src.Height, o);     // the picture in its frame: one of the ten looks (BeautifierStyles.cs)
            double rad = Math.Abs(card.Angle) * Math.PI / 180;
            double bw = card.W * Math.Cos(rad) + card.H * Math.Sin(rad), bh = card.W * Math.Sin(rad) + card.H * Math.Cos(rad);
            int w = (int)Math.Ceiling(bw) + 2 * pad, h = (int)Math.Ceiling(bh) + 2 * pad;
            Canvas root = new Canvas { Width = w, Height = h };
            if (o.Background != "transparent")
            {
                WpfColor a, b;
                if (card.Bg1.HasValue) { a = card.Bg1.Value; b = card.Bg2 ?? a; }       // neon and cinema bring their own background
                else o.Colors(out a, out b);
                Brush fill = o.Background == "solid" ? (Brush)new SolidColorBrush(a) : new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
                root.Children.Add(new Rectangle { Width = w, Height = h, Fill = fill });
            }
            Canvas.SetLeft(card.Element, (w - card.W) / 2); Canvas.SetTop(card.Element, (h - card.H) / 2);
            if (Math.Abs(card.Angle) > 0.01) card.Element.RenderTransform = new RotateTransform(card.Angle, card.W / 2, card.H / 2);
            root.Children.Add(card.Element);
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            return ToBitmap(rtb);
        }

        internal static BitmapSource ToSource(System.Drawing.Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                BitmapDecoder dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource f = dec.Frames[0];
                f.Freeze();
                return f;
            }
        }

        private static System.Drawing.Bitmap ToBitmap(BitmapSource s)
        {
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(s));
            using (MemoryStream ms = new MemoryStream())
            {
                enc.Save(ms);
                ms.Position = 0;
                using (System.Drawing.Bitmap tmp = new System.Drawing.Bitmap(ms))
                    return new System.Drawing.Bitmap(tmp);
            }
        }
    }
}
