using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    internal sealed class TextAnnotation : Annotation
    {
        public const int Plain = 0, Outline = 1, Plate = 2, Auto = 3;

        public Point Pos;
        public string Text = "";
        public double FontSize = 18;
        public int Style = Auto;
        // font of this text (the preset it was drawn with); empty / -1 = the global settings (Text page)
        public string Font = "";
        public int BoldMode = -1;
        public int ItalicMode = -1;
        public string AlignMode = "";
        public double BoxWidth;          // 0 = as wide as the text; else the text wraps at this width (the handles of the text block)

        public override Annotation Clone()
        {
            TextAnnotation c = new TextAnnotation();
            c.Id = Id; c.CopyFrom(this);
            return c;
        }

        public override void CopyFrom(Annotation other)
        {
            TextAnnotation o = (TextAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; Pos = o.Pos; Text = o.Text; FontSize = o.FontSize; Style = o.Style;
            Font = o.Font; BoldMode = o.BoldMode; ItalicMode = o.ItalicMode; AlignMode = o.AlignMode; BoxWidth = o.BoxWidth; Angle = o.Angle;
        }

        // text look (Settings - Text): font, bold, italic, alignment, plate and outline
        public static string FontName = "Segoe UI";
        public static bool Bold = true;
        public static bool Italic = false;
        public static string Align = "left";           // left | center | right
        public static string PlateColor = "auto";      // auto or #RRGGBB
        public static double PlateOpacity = 0.8;
        public static double OutlineWidth = 0.2;       // of the font size

        public static bool TryColor(string hex, out WpfColor c)
        {
            try { c = (WpfColor)ColorConverter.ConvertFromString(hex); return true; }
            catch { c = Colors.Black; return false; }
        }

        public FontWeight Weight { get { bool b = BoldMode < 0 ? Bold : BoldMode == 1; return b ? FontWeights.SemiBold : FontWeights.Normal; } }
        public FontStyle Slant { get { bool i = ItalicMode < 0 ? Italic : ItalicMode == 1; return i ? FontStyles.Italic : FontStyles.Normal; } }
        public string FontFamilyName { get { return string.IsNullOrWhiteSpace(Font) ? FontName : Font; } }

        public TextAlignment Alignment()
        {
            string a = string.IsNullOrEmpty(AlignMode) ? Align : AlignMode;
            return a == "center" ? TextAlignment.Center : a == "right" ? TextAlignment.Right : a == "justify" ? TextAlignment.Justify : TextAlignment.Left;   // justify: both edges straight (wrapped text, like in Word)
        }

        private TextBlock MakeBlock()
        {
            TextBlock tb = new TextBlock();
            tb.Text = Text;
            tb.FontFamily = new FontFamily(FontFamilyName);
            tb.FontSize = FontSize;
            tb.FontWeight = Weight;
            tb.FontStyle = Slant;
            tb.TextAlignment = Alignment();
            tb.Foreground = MakeBrush(Color);
            tb.IsHitTestVisible = false;
            if (BoxWidth > 0) { tb.TextWrapping = TextWrapping.Wrap; tb.Width = BoxWidth; }
            return tb;
        }

        /// <summary>When true a plate under the text is drawn as an outline instead (settings: text.neural_safe).</summary>
        public static bool NeuralSafe;

        /// <summary>What is really drawn. "Auto" looks at the picture under the text: readable already - plain;
        /// an even background - the text takes black or white, whichever reads better; a busy photo - outline.</summary>
        public void Resolve(out int style, out WpfColor color)
        {
            style = Style; color = Color;
            if (Style == Auto)
            {
                WpfColor mean; double spread;
                if (!Backdrop.Stats(Bounds(), out mean, out spread)) style = Outline;
                else if (Backdrop.Contrast(Color, mean) >= 4.5 && spread < 0.16) style = Plain;
                else if (spread < 0.12)
                {
                    color = Backdrop.Ink(mean);
                    style = Backdrop.Contrast(color, mean) >= 4.5 ? Plain : Outline;
                }
                else style = Outline;
            }
            if (NeuralSafe && style == Plate) style = Outline;
        }

        public override FrameworkElement CreateVisual()
        {
            Canvas host = new Canvas();
            host.IsHitTestVisible = false;
            Canvas.SetLeft(host, Pos.X);
            Canvas.SetTop(host, Pos.Y);
            if (Math.Abs(Angle) > 0.001) { Rect lr0 = LocalRect(); host.RenderTransform = new RotateTransform(Angle, lr0.Width / 2, lr0.Height / 2); }
            int st; WpfColor col;
            Resolve(out st, out col);
            WpfColor ring = Backdrop.Ink(col);

            if (st == Outline && !string.IsNullOrEmpty(Text))
            {
                FormattedText ft = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(FontFamilyName), Slant, Weight, FontStretches.Normal),
                    FontSize, Brushes.Black, 1.0);
                ft.TextAlignment = Alignment();
                ft.MaxTextWidth = BoxWidth > 0 ? BoxWidth : Math.Max(1, ft.Width);
                Geometry g = ft.BuildGeometry(new Point(0, 0));
                SolidColorBrush ringBrush = MakeBrush(ring);
                Path under = new Path();
                under.Data = g;
                under.Fill = ringBrush;
                under.Stroke = ringBrush;
                under.StrokeThickness = Math.Max(2, FontSize * OutlineWidth);
                under.StrokeLineJoin = PenLineJoin.Round;
                Path over = new Path();
                over.Data = g;
                over.Fill = MakeBrush(col);
                host.Children.Add(under);
                host.Children.Add(over);
                return host;
            }

            TextBlock tb = MakeBlock();
            tb.Foreground = MakeBrush(col);
            if (st == Plate)
            {
                double padX = FontSize * 0.4, padY = FontSize * 0.15;
                Border plate = new Border();
                byte pa = (byte)Math.Max(25, Math.Min(255, (int)Math.Round(255 * PlateOpacity)));
                WpfColor pc;
                if (PlateColor != "auto" && TryColor(PlateColor, out pc)) plate.Background = MakeBrush(WpfColor.FromArgb(pa, pc.R, pc.G, pc.B));
                else plate.Background = MakeBrush(Backdrop.Luma(col) > 0.4 ? WpfColor.FromArgb(pa, 0, 0, 0) : WpfColor.FromArgb(pa, 255, 255, 255));
                plate.CornerRadius = new CornerRadius(FontSize * 0.35);
                plate.Padding = new Thickness(padX, padY, padX, padY);
                plate.Child = tb;
                Canvas.SetLeft(plate, -padX);
                Canvas.SetTop(plate, -padY);
                host.Children.Add(plate);
            }
            else host.Children.Add(tb);
            return host;
        }

        public override bool CanRotate { get { return true; } }

        public override Rect LocalRect()
        {
            TextBlock tb = MakeBlock();
            if (string.IsNullOrEmpty(Text)) tb.Text = " ";
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Rect(Pos.X, Pos.Y, Math.Max(12, tb.DesiredSize.Width), Math.Max(12, tb.DesiredSize.Height));
        }

        public override Rect Bounds() { return RotatedBounds(LocalRect(), Angle); }

        public override bool Hit(Point p)
        {
            Rect r = LocalRect();
            if (Math.Abs(Angle) > 0.001) p = RotatePoint(p, -Angle, new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
            r.Inflate(4, 4);
            return r.Contains(p);
        }

        public override void Translate(double dx, double dy)
        {
            Pos = new Point(Pos.X + dx, Pos.Y + dy);
        }
    }
}
