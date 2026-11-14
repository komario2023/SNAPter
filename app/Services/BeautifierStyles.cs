using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>The finished card of a screenshot in one of the looks: the picture with its frame, in a box of its own (W x H) that may be turned.</summary>
    internal sealed class BeautifyCard
    {
        public FrameworkElement Element;
        public double W, H, Angle;
        public WpfColor? Bg1, Bg2;     // a look with its own background (neon, cinema, or the owner's colors): it replaces the colors of the preset
    }

    /// <summary>The looks of the beautiful screenshot. Eleven are built in (their ids are in Ids); every look is drawn from its BeautifyLook parameters, so the
    /// owner can change them, replace a built-in look or add new ones. The background, the space around, the corner radius and the shadow still come from
    /// the settings unless the look sets its own.</summary>
    internal static class BeautifyStyles
    {
        public static readonly string[] Ids = { "classic", "window", "windows", "browser", "polaroid", "glass", "neon", "sticker", "stack", "bezel", "cinema" };

        /// <summary>The built-in ids and then the owner's own looks, in order.</summary>
        public static List<string> AllIds(IEnumerable<BeautifyLook> looks)
        {
            List<string> r = new List<string>(Ids);
            if (looks != null) foreach (BeautifyLook l in looks) if (!r.Contains(l.Id)) r.Add(l.Id);
            return r;
        }

        public static bool IsKnown(string id, IEnumerable<BeautifyLook> looks)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (Array.IndexOf(Ids, id) >= 0) return true;
            if (looks != null) foreach (BeautifyLook l in looks) if (l.Id == id) return true;
            return false;
        }

        private static Brush Br(byte a, byte r, byte g, byte b) { return new SolidColorBrush(WpfColor.FromArgb(a, r, g, b)); }

        private static WpfColor Col(string hex, WpfColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try { return (WpfColor)ColorConverter.ConvertFromString(hex.Trim()); } catch { return fallback; }
        }

        private static double Num(double v, double def) { return v < 0 ? def : v; }

        private static Image Pic(BitmapSource img, int w, int h, double radius, double x, double y)
        {
            Image p = new Image { Source = img, Width = w, Height = h, Stretch = Stretch.Fill };
            p.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
            RenderOptions.SetBitmapScalingMode(p, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(p, x); Canvas.SetTop(p, y);
            return p;
        }

        private static Rectangle Box(double w, double h, double radius, Brush fill, Brush stroke, double strokeW, double x, double y)
        {
            Rectangle r = new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = fill, Stroke = stroke, StrokeThickness = strokeW };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
            return r;
        }

        private static TextBlock Label(string text, double size, Brush color, double x, double y, string family = "Segoe UI")
        {
            TextBlock t = new TextBlock { Text = text, FontSize = size, Foreground = color, FontFamily = new FontFamily(family) };
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            return t;
        }

        private static bool ShadowOn(BeautifyLook l, BeautifyOptions o) { return l.Shadow < 0 ? o.Shadow : l.Shadow == 1; }

        private static void Shadow(FrameworkElement e, BeautifyLook l, BeautifyOptions o)
        {
            if (ShadowOn(l, o) && o.ShadowOpacity > 0)
                e.Effect = new DropShadowEffect { BlurRadius = Math.Max(1, o.ShadowBlur), ShadowDepth = Math.Max(2, o.ShadowBlur / 5.0), Direction = 270, Opacity = Math.Min(1, o.ShadowOpacity / 100.0), Color = Colors.Black };
        }

        private static BeautifyCard Card(Canvas c, double w, double h, BeautifyLook l, BeautifyOptions o, double ownAngle)
        {
            c.Width = w; c.Height = h;
            Shadow(c, l, o);
            BeautifyCard card = new BeautifyCard { Element = c, W = w, H = h, Angle = double.IsNaN(l.Angle) ? ownAngle : l.Angle };
            if (!string.IsNullOrWhiteSpace(l.Bg1)) { card.Bg1 = Col(l.Bg1, Colors.Black); card.Bg2 = string.IsNullOrWhiteSpace(l.Bg2) ? card.Bg1 : Col(l.Bg2, card.Bg1.Value); }
            return card;
        }

        private static readonly WpfColor[] DotColors = { WpfColor.FromRgb(237, 106, 94), WpfColor.FromRgb(245, 191, 79), WpfColor.FromRgb(97, 197, 84) };

        public static BeautifyCard Build(BitmapSource img, int w, int h, BeautifyOptions o)
        {
            BeautifyLook l = o.LookOf(o.Style);
            double r = l.Radius >= 0 ? l.Radius : o.Radius;
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2.0));
            Canvas c = new Canvas();
            switch (l.Type)
            {
                case "window":
                    {
                        double bar = Num(l.Bar, 30);
                        WpfColor bc = Col(l.BarColor, WpfColor.FromRgb(233, 236, 241));
                        c.Children.Add(Box(w, bar + 2, 0, new SolidColorBrush(bc), null, 0, 0, 0));
                        if (l.Dots) for (int k = 0; k < 3; k++) c.Children.Add(Box(12, 12, 6, new SolidColorBrush(DotColors[k]), null, 0, 12 + k * 19, (bar - 12) / 2));
                        if (l.Text.Length > 0)
                        {
                            bool dark = (bc.R + bc.G + bc.B) < 384;
                            TextBlock t = new TextBlock { Text = l.Text, FontSize = Math.Max(10, bar * 0.42), FontFamily = new FontFamily("Segoe UI"), Foreground = Br(255, dark ? (byte)230 : (byte)80, dark ? (byte)230 : (byte)86, dark ? (byte)235 : (byte)96), Width = w, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                            Canvas.SetLeft(t, 0); Canvas.SetTop(t, (bar - t.FontSize * 1.3) / 2); c.Children.Add(t);
                        }
                        c.Children.Add(Pic(img, w, h, 0, 0, bar));
                        return Wrap(Card(c, w, h + bar, l, o, 0), c, r, l, o);
                    }
                case "windows":
                    {
                        double bar = Num(l.Bar, 34);
                        WpfColor bc = Col(l.BarColor, WpfColor.FromRgb(243, 243, 243));
                        bool dark = (bc.R + bc.G + bc.B) < 384;
                        Brush ink = Br(255, dark ? (byte)235 : (byte)40, dark ? (byte)235 : (byte)40, dark ? (byte)235 : (byte)44);
                        c.Children.Add(Box(w, bar + 2, 0, new SolidColorBrush(bc), null, 0, 0, 0));
                        c.Children.Add(Box(14, 14, 3, Br(255, 70, 130, 230), null, 0, 12, (bar - 14) / 2));
                        c.Children.Add(Label(l.Text.Length > 0 ? l.Text : "Untitled", Math.Max(10, bar * 0.36), ink, 34, (bar - bar * 0.36 * 1.35) / 2));
                        TextBlock caps = new TextBlock { Text = "—      □      ✕", FontSize = Math.Max(10, bar * 0.34), Foreground = ink, FontFamily = new FontFamily("Segoe UI Symbol") };
                        caps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Canvas.SetLeft(caps, w - caps.DesiredSize.Width - 16); Canvas.SetTop(caps, (bar - caps.DesiredSize.Height) / 2); c.Children.Add(caps);
                        c.Children.Add(Pic(img, w, h, 0, 0, bar));
                        return Wrap(Card(c, w, h + bar, l, o, 0), c, Math.Min(r, l.Radius >= 0 ? r : 8), l, o);
                    }
                case "browser":
                    {
                        double bar = Num(l.Bar, 44);
                        WpfColor bc = Col(l.BarColor, WpfColor.FromRgb(222, 226, 233));
                        bool dark = (bc.R + bc.G + bc.B) < 384;
                        c.Children.Add(Box(w, bar + 2, 0, new SolidColorBrush(bc), null, 0, 0, 0));
                        double left = 14;
                        if (l.Dots) { for (int k = 0; k < 3; k++) c.Children.Add(Box(12, 12, 6, new SolidColorBrush(DotColors[k]), null, 0, 14 + k * 19, (bar - 12) / 2)); left = 86; }
                        double ph = Math.Min(28, bar - 12), pw = Math.Max(80, Math.Min(w - left - 24, 440));
                        c.Children.Add(Box(pw, ph, ph / 2, dark ? Br(255, 60, 64, 72) : Br(255, 255, 255, 255), null, 0, left, (bar - ph) / 2));
                        c.Children.Add(Label("🔒 " + (l.Text.Length > 0 ? l.Text : "example.com"), Math.Max(10, ph * 0.44), dark ? Br(255, 205, 210, 220) : Br(255, 110, 118, 130), left + 14, (bar - ph * 0.44 * 1.35) / 2));
                        c.Children.Add(Pic(img, w, h, 0, 0, bar));
                        return Wrap(Card(c, w, h + bar, l, o, 0), c, r, l, o);
                    }
                case "polaroid":
                    {
                        double side = Num(l.Frame, 18), bottom = Num(l.Bottom, 64);
                        WpfColor fc = Col(l.FrameColor, WpfColor.FromRgb(252, 252, 250));
                        c.Children.Add(Box(w + 2 * side, h + side + bottom, 3, new SolidColorBrush(fc), Br(255, 226, 226, 222), 1, 0, 0));
                        c.Children.Add(Pic(img, w, h, 0, side, side));
                        if (l.Text.Length > 0)
                        {
                            bool dark = (fc.R + fc.G + fc.B) < 384;
                            TextBlock t = new TextBlock { Text = l.Text, FontSize = Math.Max(10, Math.Min(bottom * 0.4, 30)), FontFamily = new FontFamily("Segoe Print, Ink Free, Segoe UI"), Foreground = dark ? Br(255, 235, 235, 235) : Br(255, 60, 60, 70), Width = w + 2 * side, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                            Canvas.SetLeft(t, 0); Canvas.SetTop(t, side + h + (bottom - t.FontSize * 1.4) / 2); c.Children.Add(t);
                        }
                        return Card(c, w + 2 * side, h + side + bottom, l, o, -2.5);
                    }
                case "glass":
                    {
                        double f = Num(l.Frame, 12), rr = r + f;
                        c.Children.Add(Box(w + 2 * f, h + 2 * f, rr, Br((byte)Math.Max(0, Math.Min(255, l.Alpha)), 255, 255, 255), Br(190, 255, 255, 255), 1.5, 0, 0));
                        c.Children.Add(Pic(img, w, h, r, f, f));
                        return Card(c, w + 2 * f, h + 2 * f, l, o, 0);
                    }
                case "neon":
                    {
                        WpfColor a, b; o.Colors(out a, out b);
                        if (o.Preset == "graphite" || o.Preset == "paper") a = WpfColor.FromRgb(34, 211, 238);
                        if (!string.IsNullOrWhiteSpace(l.Glow)) a = Col(l.Glow, a);
                        double e = Num(l.Frame, 3);
                        c.Children.Add(Pic(img, w, h, r, e, e));
                        c.Children.Add(Box(w + 2 * e, h + 2 * e, r + e, null, new SolidColorBrush(a), e, 0, 0));
                        c.Width = w + 2 * e; c.Height = h + 2 * e;
                        c.Effect = new DropShadowEffect { BlurRadius = Math.Max(20, o.ShadowBlur + 10), ShadowDepth = 0, Opacity = ShadowOn(l, o) ? 0.95 : 0.0, Color = a };
                        BeautifyCard card = new BeautifyCard { Element = c, W = w + 2 * e, H = h + 2 * e, Angle = double.IsNaN(l.Angle) ? 0 : l.Angle, Bg1 = WpfColor.FromRgb(11, 16, 32), Bg2 = WpfColor.FromRgb(27, 20, 64) };
                        if (!string.IsNullOrWhiteSpace(l.Bg1)) { card.Bg1 = Col(l.Bg1, card.Bg1.Value); card.Bg2 = string.IsNullOrWhiteSpace(l.Bg2) ? card.Bg1 : Col(l.Bg2, card.Bg2.Value); }
                        return card;
                    }
                case "sticker":
                    {
                        double e = Num(l.Frame, 10);
                        c.Children.Add(Box(w + 2 * e, h + 2 * e, r + e, new SolidColorBrush(Col(l.FrameColor, Colors.White)), null, 0, 0, 0));
                        c.Children.Add(Pic(img, w, h, r, e, e));
                        return Card(c, w + 2 * e, h + 2 * e, l, o, 0);
                    }
                case "stack":
                    {
                        int layers = Math.Max(1, Math.Min(5, l.Layers));
                        double m = Math.Round(0.07 * Math.Max(w, h)) + 12;
                        double[] angles = { -5, 3.5, -2, 6, -7 };
                        for (int k = layers - 1; k >= 0; k--)
                        {
                            byte a = (byte)(210 - k * 55);
                            Rectangle back = Box(w, h, r, Br(a, 255, 255, 255), Br((byte)Math.Max(40, a - 60), 255, 255, 255), 1, m, m);
                            back.RenderTransform = new RotateTransform(angles[k % angles.Length], w / 2.0, h / 2.0);
                            c.Children.Add(back);
                        }
                        c.Children.Add(Pic(img, w, h, r, m, m));
                        return Card(c, w + 2 * m, h + 2 * m, l, o, 0);
                    }
                case "bezel":
                    {
                        double f = Num(l.Frame, 16), rr = Math.Max(r, 8) + f;
                        WpfColor fc = Col(l.FrameColor, WpfColor.FromRgb(22, 24, 28));
                        bool dark = (fc.R + fc.G + fc.B) < 384;
                        c.Children.Add(Box(w + 2 * f, h + 2 * f, rr, new SolidColorBrush(fc), dark ? Br(255, 70, 76, 86) : Br(255, 150, 150, 156), 1.5, 0, 0));
                        c.Children.Add(Pic(img, w, h, Math.Max(4, r / 2), f, f));
                        return Card(c, w + 2 * f, h + 2 * f, l, o, 0);
                    }
                case "cinema":
                    {
                        double bars = Math.Round(h * Math.Max(2, Math.Min(30, l.BarsPct)) / 100.0);
                        c.Children.Add(Box(w, h + 2 * bars, 0, new SolidColorBrush(Col(l.BarColor, Colors.Black)), null, 0, 0, 0));
                        c.Children.Add(Pic(img, w, h, 0, 0, bars));
                        BeautifyCard card = Card(c, w, h + 2 * bars, l, o, 0);
                        if (!card.Bg1.HasValue) { card.Bg1 = WpfColor.FromRgb(8, 8, 10); card.Bg2 = WpfColor.FromRgb(26, 26, 32); }
                        return card;
                    }
                default:   // classic: the picture with rounded corners and a soft shadow
                    {
                        c.Children.Add(Pic(img, w, h, r, 0, 0));
                        return Card(c, w, h, l, o, 0);
                    }
            }
        }

        private static BeautifyCard Wrap(BeautifyCard card, Canvas clipped, double radius, BeautifyLook l, BeautifyOptions o)
        {
            // a clipped canvas loses its shadow: the clip is set here and the shadow goes to an outer box of the same size
            clipped.Clip = new RectangleGeometry(new Rect(0, 0, card.W, card.H), radius, radius);
            Canvas outer = new Canvas { Width = card.W, Height = card.H };
            clipped.Effect = null;
            outer.Children.Add(clipped);
            Shadow(outer, l, o);
            card.Element = outer;
            return card;
        }
    }
}
