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
    /// <summary>The frozen screenshot as pixels: used by the blur tool and to pick a readable text style.</summary>
    internal static class Backdrop
    {
        public static BitmapSource Source;
        private static byte[] _pix;
        private static int _w, _h;

        public static void Set(BitmapSource s) { Source = s; _pix = null; }

        /// <summary>The raw pixels of the screenshot (Bgr32, 4 bytes each, no row padding).</summary>
        public static bool Raw(out byte[] pix, out int w, out int h)
        {
            pix = null; w = h = 0;
            if (!Ensure()) return false;
            pix = _pix; w = _w; h = _h;
            return true;
        }

        /// <summary>The color of one screen pixel of the frozen screenshot.</summary>
        public static bool Pixel(Point p, out WpfColor c)
        {
            c = Colors.Black;
            if (!Ensure()) return false;
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x < 0 || y < 0 || x >= _w || y >= _h) return false;
            int i = (y * _w + x) * 4;
            c = WpfColor.FromRgb(_pix[i + 2], _pix[i + 1], _pix[i]);
            return true;
        }

        private static bool Ensure()
        {
            if (_pix != null) return true;
            if (Source == null) return false;
            try
            {
                _w = Source.PixelWidth; _h = Source.PixelHeight;
                byte[] b = new byte[_w * 4 * _h];
                Source.CopyPixels(b, _w * 4, 0); // Bgr32
                _pix = b;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Average color and brightness spread (0..1) of the picture under a rectangle.</summary>
        public static bool Stats(Rect r, out WpfColor mean, out double spread)
        {
            mean = Colors.Gray; spread = 0;
            if (!Ensure()) return false;
            int x0 = Math.Max(0, (int)r.Left), y0 = Math.Max(0, (int)r.Top);
            int x1 = Math.Min(_w, (int)Math.Ceiling(r.Right)), y1 = Math.Min(_h, (int)Math.Ceiling(r.Bottom));
            if (x1 <= x0 || y1 <= y0) return false;
            int nx = Math.Min(28, x1 - x0), ny = Math.Min(14, y1 - y0);
            double sr = 0, sg = 0, sb = 0, sl = 0, sl2 = 0; int n = 0;
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int x = x0 + (int)((ix + 0.5) * (x1 - x0) / nx);
                    int y = y0 + (int)((iy + 0.5) * (y1 - y0) / ny);
                    int i = (y * _w + x) * 4;
                    byte b = _pix[i], g = _pix[i + 1], rr = _pix[i + 2];
                    sr += rr; sg += g; sb += b;
                    double l = Luma(WpfColor.FromRgb(rr, g, b));
                    sl += l; sl2 += l * l; n++;
                }
            if (n == 0) return false;
            mean = WpfColor.FromRgb((byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
            double m = sl / n;
            spread = Math.Sqrt(Math.Max(0, sl2 / n - m * m));
            return true;
        }

        public static double Luma(WpfColor c)
        {
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        private static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static double Contrast(WpfColor a, WpfColor b)
        {
            double la = Luma(a), lb = Luma(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Black or white, whichever reads better on the given color.</summary>
        public static WpfColor Ink(WpfColor on)
        {
            return Luma(on) > 0.4 ? Colors.Black : Colors.White;
        }
    }

    /// <summary>
    /// An object drawn over the screenshot. The picture is never changed; every object keeps its own
    /// parameters and can build a fresh WPF visual from them. The same factory feeds the screen and the
    /// export, so what you see is what you save. Coordinates are real pixels of the virtual screen.
    /// </summary>
    internal abstract class Annotation
    {
        private static int _nextId = 1;

        public int Id = _nextId++;
        public WpfColor Color = WpfColor.FromRgb(229, 50, 45);

        public static int NewId() { return _nextId++; }

        /// <summary>0.1 .. 1: how opaque the object is (tool setting "opacity").</summary>
        public double Opacity = 1.0;

        /// <summary>Degrees, clockwise, around the center of the object. Shapes and text can be rotated.</summary>
        public double Angle;
        public virtual bool CanRotate { get { return false; } }
        /// <summary>The rectangle of the object before it is rotated.</summary>
        public virtual Rect LocalRect() { return Bounds(); }

        public static Point RotatePoint(Point p, double deg, Point c)
        {
            double rad = deg * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            double x = p.X - c.X, y = p.Y - c.Y;
            return new Point(c.X + x * cos - y * sin, c.Y + x * sin + y * cos);
        }

        /// <summary>The smallest rectangle around r turned by deg degrees about its own center.</summary>
        public static Rect RotatedBounds(Rect r, double deg)
        {
            if (Math.Abs(deg) < 0.001) return r;
            Point c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            Point[] q = { RotatePoint(r.TopLeft, deg, c), RotatePoint(r.TopRight, deg, c), RotatePoint(r.BottomRight, deg, c), RotatePoint(r.BottomLeft, deg, c) };
            double l = q.Min(p => p.X), t = q.Min(p => p.Y), rr = q.Max(p => p.X), b = q.Max(p => p.Y);
            return new Rect(new Point(l, t), new Point(rr, b));
        }

        /// <summary>The visual with the object's opacity applied.</summary>
        public FrameworkElement BuildVisual()
        {
            FrameworkElement v = CreateVisual();
            if (Opacity < 0.999) v.Opacity = Opacity;
            return v;
        }

        public abstract Annotation Clone();
        public abstract void CopyFrom(Annotation other);
        public abstract FrameworkElement CreateVisual();
        public abstract Rect Bounds();
        public abstract bool Hit(Point p);
        public abstract void Translate(double dx, double dy);

        protected static SolidColorBrush MakeBrush(WpfColor c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static double DistanceToSegment(Point p, Point a, Point b)
        {
            Vector ab = b - a;
            double l2 = ab.LengthSquared;
            if (l2 < 1e-9) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / l2));
            Point proj = a + ab * t;
            return (p - proj).Length;
        }
    }

    /// <summary>Arrow (Head = true) or a plain line (Head = false).</summary>
    internal sealed class ArrowAnnotation : Annotation
    {
        public Point A;
        public Point B;
        public double Thickness = 4;
        public bool Head = true;
        public bool Head2;          // a second head at the start (Ctrl+Alt while drawing by default)
        public static string HeadStyle = "triangle";   // triangle | open | dot (settings editor.arrow_head)
        public string HeadKind = "";                   // the head of this arrow (its preset); empty = HeadStyle
        public double HeadScale = 1.0;                 // size factor of the head
        public bool HeadHollow;                        // the head is an outline (same thickness as the line), not filled
        private string Kind { get { return string.IsNullOrEmpty(HeadKind) ? HeadStyle : HeadKind; } }

        public double Length { get { return (B - A).Length; } }

        public override Annotation Clone()
        {
            ArrowAnnotation c = new ArrowAnnotation();
            c.Id = Id; c.Color = Color; c.Opacity = Opacity; c.A = A; c.B = B; c.Thickness = Thickness; c.Head = Head; c.Head2 = Head2; c.HeadKind = HeadKind; c.HeadScale = HeadScale; c.HeadHollow = HeadHollow;
            return c;
        }

        public override void CopyFrom(Annotation other)
        {
            ArrowAnnotation o = (ArrowAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; A = o.A; B = o.B; Thickness = o.Thickness; Head = o.Head; Head2 = o.Head2; HeadKind = o.HeadKind; HeadScale = o.HeadScale; HeadHollow = o.HeadHollow;
        }

        public double HeadLength { get { return Head ? Math.Max(12, Thickness * 4.5) * Math.Max(0.3, HeadScale) : 0; } }

        public override FrameworkElement CreateVisual()
        {
            Vector d = B - A;
            double len = d.Length;
            Path path = new Path();
            path.IsHitTestVisible = false;
            SolidColorBrush brush = MakeBrush(Color);
            path.Stroke = brush;
            path.Fill = HeadHollow ? null : brush;
            path.StrokeThickness = Thickness;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = Head ? PenLineCap.Flat : PenLineCap.Round;
            if (len < 0.5) return path;

            PathGeometry g = new PathGeometry();
            if (!Head)
            {
                PathFigure only = new PathFigure { StartPoint = A, IsClosed = false, IsFilled = false };
                only.Segments.Add(new LineSegment(B, true));
                g.Figures.Add(only);
                path.Data = g;
                return path;
            }

            Vector u = d / len;
            Vector n = new Vector(-u.Y, u.X);
            double hl = Math.Min(HeadLength, len * (Head2 ? 0.45 : 0.9));
            double hw = hl * 0.5;
            Point baseP = B - u * hl;
            Point startP = Head2 ? A + u * hl : A;

            if (Kind == "open")
            {
                // an open head: two strokes instead of a filled triangle
                path.Fill = null;
                PathFigure shaft = new PathFigure { StartPoint = startP, IsClosed = false, IsFilled = false };
                shaft.Segments.Add(new LineSegment(Head2 ? baseP : B, true));
                g.Figures.Add(shaft);
                foreach (bool atStart in Head2 ? new bool[] { false, true } : new bool[] { false })
                {
                    Point tip = atStart ? A : B;
                    Vector back = atStart ? u : -u;
                    PathFigure v = new PathFigure { StartPoint = tip + back * hl + n * hw, IsClosed = false, IsFilled = false };
                    v.Segments.Add(new LineSegment(tip, true));
                    v.Segments.Add(new LineSegment(tip + back * hl - n * hw, true));
                    g.Figures.Add(v);
                }
                path.Data = g;
                return path;
            }
            if (Kind == "dot")
            {
                // a round dot at the end
                double rr = Math.Max(3, Thickness * 1.8 * Math.Max(0.3, HeadScale));
                // a hollow dot is a ring: the line stops at its edge (otherwise it would show inside the ring)
                Vector ud = d / len;
                Point rodStart = (HeadHollow && Head2) ? A + ud * rr : A;
                Point rodEnd = HeadHollow ? B - ud * Math.Min(rr, len) : B;
                PathFigure rod = new PathFigure { StartPoint = rodStart, IsClosed = false, IsFilled = false };
                rod.Segments.Add(new LineSegment(rodEnd, true));
                g.Figures.Add(rod);
                GeometryGroup grp = new GeometryGroup();
                grp.Children.Add(g);
                grp.Children.Add(new EllipseGeometry(B, rr, rr));
                if (Head2) grp.Children.Add(new EllipseGeometry(A, rr, rr));
                path.Data = grp;
                return path;
            }

            PathFigure line = new PathFigure { StartPoint = startP, IsClosed = false, IsFilled = false };
            line.Segments.Add(new LineSegment(baseP + u * 0.5, true));

            PathFigure head = new PathFigure { StartPoint = B, IsClosed = true, IsFilled = !HeadHollow };
            head.Segments.Add(new LineSegment(baseP + n * hw, true));
            head.Segments.Add(new LineSegment(baseP - n * hw, true));

            g.Figures.Add(line);
            g.Figures.Add(head);
            if (Head2)
            {
                PathFigure head2 = new PathFigure { StartPoint = A, IsClosed = true, IsFilled = !HeadHollow };
                head2.Segments.Add(new LineSegment(startP + n * hw, true));
                head2.Segments.Add(new LineSegment(startP - n * hw, true));
                g.Figures.Add(head2);
            }
            path.Data = g;
            return path;
        }

        public override Rect Bounds()
        {
            Rect r = new Rect(A, B);
            double pad = Thickness + HeadLength * 0.5;
            r.Inflate(pad, pad);
            return r;
        }

        public override bool Hit(Point p)
        {
            return DistanceToSegment(p, A, B) <= Math.Max(7, Thickness / 2 + 5);
        }

        public override void Translate(double dx, double dy)
        {
            A = new Point(A.X + dx, A.Y + dy);
            B = new Point(B.X + dx, B.Y + dy);
        }
    }

    /// <summary>Anything defined by one rectangle (shapes, blur): can be resized by 8 handles.</summary>
    internal abstract class RectAnnotation : Annotation
    {
        public Rect R;

        public static Rect Norm(Point a, Point b)
        {
            return new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        }

        public override Rect Bounds() { return R; }

        public override void Translate(double dx, double dy)
        {
            R = new Rect(R.X + dx, R.Y + dy, R.Width, R.Height);
        }

        public Point[] HandlePoints()
        {
            double l = R.Left, t = R.Top, r = R.Right, b = R.Bottom, cx = (l + r) / 2, cy = (t + b) / 2;
            Point[] pts = { new Point(l, t), new Point(cx, t), new Point(r, t), new Point(r, cy), new Point(r, b), new Point(cx, b), new Point(l, b), new Point(l, cy) };
            if (Math.Abs(Angle) > 0.001) for (int i = 0; i < pts.Length; i++) pts[i] = RotatePoint(pts[i], Angle, new Point(cx, cy));
            return pts;
        }
    }

    /// <summary>Rectangle (optionally rounded) or ellipse; outline plus optional fill.</summary>
    internal sealed class ShapeAnnotation : RectAnnotation
    {
        public bool Ellipse;
        public double Thickness = 3;
        public int Fill;        // 0 none, 1 translucent, 2 solid
        public bool Rounded;
        public double RoundPct = 40;      // roundness of the corners in percent: 100 = as round as possible (a pill)

        public override Annotation Clone()
        {
            ShapeAnnotation c = new ShapeAnnotation();
            c.Id = Id; c.CopyFrom(this);
            return c;
        }

        public override void CopyFrom(Annotation other)
        {
            ShapeAnnotation o = (ShapeAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; R = o.R; Ellipse = o.Ellipse; Thickness = o.Thickness; Fill = o.Fill; Rounded = o.Rounded; RoundPct = o.RoundPct; Angle = o.Angle;
        }

        public override FrameworkElement CreateVisual()
        {
            Shape s;
            if (Ellipse) s = new System.Windows.Shapes.Ellipse();
            else
            {
                System.Windows.Shapes.Rectangle rc = new System.Windows.Shapes.Rectangle();
                if (Rounded)
                {
                    double rad = Math.Max(1, RoundPct / 100.0 * Math.Min(R.Width, R.Height) / 2);
                    rc.RadiusX = rad; rc.RadiusY = rad;
                }
                s = rc;
            }
            s.IsHitTestVisible = false;
            s.Width = Math.Max(1, R.Width);
            s.Height = Math.Max(1, R.Height);
            s.Stroke = MakeBrush(Color);
            s.StrokeThickness = Thickness;
            s.StrokeLineJoin = PenLineJoin.Round;
            if (Fill == 1) s.Fill = MakeBrush(WpfColor.FromArgb((byte)(80 * Color.A / 255), Color.R, Color.G, Color.B));
            else if (Fill == 2) s.Fill = MakeBrush(Color);
            else s.Fill = null;
            if (Math.Abs(Angle) > 0.001) s.RenderTransform = new RotateTransform(Angle, s.Width / 2, s.Height / 2);
            Canvas.SetLeft(s, R.X);
            Canvas.SetTop(s, R.Y);
            return s;
        }

        public override bool CanRotate { get { return true; } }

        public override Rect LocalRect()
        {
            Rect r = R;
            r.Inflate(Thickness / 2, Thickness / 2);
            return r;
        }

        public override Rect Bounds() { return RotatedBounds(LocalRect(), Angle); }

        public override bool Hit(Point p)
        {
            if (Math.Abs(Angle) > 0.001) p = RotatePoint(p, -Angle, new Point(R.X + R.Width / 2, R.Y + R.Height / 2));
            double pad = Math.Max(6, Thickness / 2 + 4);
            if (Ellipse)
            {
                double a = Math.Max(1, R.Width / 2), b = Math.Max(1, R.Height / 2);
                double cx = R.X + a, cy = R.Y + b;
                double dx = (p.X - cx) / a, dy = (p.Y - cy) / b;
                double rr = Math.Sqrt(dx * dx + dy * dy);
                double tol = pad / Math.Min(a, b);
                if (Fill > 0) return rr <= 1 + tol;
                return Math.Abs(rr - 1) <= tol;
            }
            Rect outer = R; outer.Inflate(pad, pad);
            if (!outer.Contains(p)) return false;
            if (Fill > 0) return true;
            Rect inner = R;
            inner.Inflate(-pad, -pad);
            return inner.IsEmpty || inner.Width <= 0 || inner.Height <= 0 || !inner.Contains(p);
        }
    }

    /// <summary>Pixelation of the picture under a rectangle (hides passwords, names...).</summary>
    internal sealed class BlurAnnotation : RectAnnotation
    {
        public double Block = 10;

        public override Annotation Clone()
        {
            BlurAnnotation c = new BlurAnnotation();
            c.Id = Id; c.CopyFrom(this);
            return c;
        }

        public override void CopyFrom(Annotation other)
        {
            BlurAnnotation o = (BlurAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; R = o.R; Block = o.Block;
        }

        public override FrameworkElement CreateVisual()
        {
            Canvas holder = new Canvas { IsHitTestVisible = false };
            BitmapSource src = Backdrop.Source;
            if (src == null) return holder;
            int x = Math.Max(0, (int)Math.Floor(R.X)), y = Math.Max(0, (int)Math.Floor(R.Y));
            int w = Math.Min(src.PixelWidth - x, (int)Math.Ceiling(R.Right) - x);
            int h = Math.Min(src.PixelHeight - y, (int)Math.Ceiling(R.Bottom) - y);
            if (w < 2 || h < 2) return holder;
            double block = Math.Max(2, Math.Min(Block, Math.Min(w, h)));
            CroppedBitmap crop = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
            double f = 1.0 / block;
            TransformedBitmap small = new TransformedBitmap(crop, new ScaleTransform(f, f));
            Image img = new Image();
            img.Source = small;
            img.Stretch = Stretch.Fill;
            img.Width = w; img.Height = h;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, x);
            Canvas.SetTop(img, y);
            holder.Children.Add(img);
            return holder;
        }

        public override bool Hit(Point p) { return R.Contains(p); }
    }

    /// <summary>Free drawing: brush, or a translucent marker.</summary>
    internal sealed class BrushAnnotation : Annotation
    {
        public List<Point> Pts = new List<Point>();
        public double Thickness = 4;
        public bool Marker;

        public override Annotation Clone()
        {
            BrushAnnotation c = new BrushAnnotation();
            c.Id = Id; c.CopyFrom(this);
            return c;
        }

        public override void CopyFrom(Annotation other)
        {
            BrushAnnotation o = (BrushAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; Thickness = o.Thickness; Marker = o.Marker; Pts = new List<Point>(o.Pts);
        }

        public override FrameworkElement CreateVisual()
        {
            Path path = new Path();
            path.IsHitTestVisible = false;
            path.Stroke = MakeBrush(Marker ? WpfColor.FromArgb((byte)(120 * Color.A / 255), Color.R, Color.G, Color.B) : Color);
            path.StrokeThickness = Thickness;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            if (Pts.Count == 0) return path;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(Pts[0], false, false);
                if (Pts.Count == 1) ctx.LineTo(new Point(Pts[0].X + 0.01, Pts[0].Y), true, true);
                else for (int i = 1; i < Pts.Count; i++) ctx.LineTo(Pts[i], true, true);
            }
            g.Freeze();
            path.Data = g;
            return path;
        }

        public override Rect Bounds()
        {
            if (Pts.Count == 0) return new Rect(0, 0, 0, 0);
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            foreach (Point p in Pts) { l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); b = Math.Max(b, p.Y); }
            Rect rc = new Rect(new Point(l, t), new Point(r, b));
            rc.Inflate(Thickness / 2 + 2, Thickness / 2 + 2);
            return rc;
        }

        public override bool Hit(Point p)
        {
            double tol = Math.Max(7, Thickness / 2 + 5);
            if (Pts.Count == 1) return (p - Pts[0]).Length <= tol;
            for (int i = 1; i < Pts.Count; i++)
                if (DistanceToSegment(p, Pts[i - 1], Pts[i]) <= tol) return true;
            return false;
        }

        public override void Translate(double dx, double dy)
        {
            for (int i = 0; i < Pts.Count; i++) Pts[i] = new Point(Pts[i].X + dx, Pts[i].Y + dy);
        }
    }

    /// <summary>A numbered circle (1, 2, 3...) to mark steps.</summary>
    internal sealed class StepAnnotation : Annotation
    {
        public Point Pos;          // center
        public int Number = 1;
        public double Diameter = 28;

        public static string LabelStyle = "number";   // number | upper | lower | roman (settings editor.step_style)
        public static int StartNumber = 1;            // settings editor.step_start

        /// <summary>The text inside the circle: 1, A, a or I according to the chosen style.</summary>
        public string Label()
        {
            int n = Math.Max(0, Number);
            switch (LabelStyle)
            {
                case "upper": return Letters(n, 'A');
                case "lower": return Letters(n, 'a');
                case "roman": return Roman(n);
                default: return n.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string Letters(int n, char a)
        {
            if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder();
            while (n > 0) { n--; sb.Insert(0, (char)(a + n % 26)); n /= 26; }
            return sb.ToString();
        }

        private static string Roman(int n)
        {
            if (n <= 0 || n >= 4000) return n.ToString(CultureInfo.InvariantCulture);
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] s = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
            return sb.ToString();
        }

        public override Annotation Clone()
        {
            StepAnnotation c = new StepAnnotation();
            c.Id = Id; c.CopyFrom(this);
            return c;
        }

        public override void CopyFrom(Annotation other)
        {
            StepAnnotation o = (StepAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; Pos = o.Pos; Number = o.Number; Diameter = o.Diameter;
        }

        public override FrameworkElement CreateVisual()
        {
            WpfColor ink = Backdrop.Ink(Color);
            TextBlock tb = new TextBlock();
            tb.Text = Label();
            tb.FontFamily = new FontFamily("Segoe UI");
            tb.FontSize = Diameter * 0.52;
            tb.FontWeight = FontWeights.Bold;
            tb.Foreground = MakeBrush(ink);
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.VerticalAlignment = VerticalAlignment.Center;
            Border b = new Border();
            b.Width = Diameter; b.Height = Diameter;
            b.CornerRadius = new CornerRadius(Diameter / 2);
            b.Background = MakeBrush(Color);
            b.BorderBrush = MakeBrush(ink);
            b.BorderThickness = new Thickness(Math.Max(1.5, Diameter * 0.06));
            b.Child = tb;
            b.IsHitTestVisible = false;
            Canvas.SetLeft(b, Pos.X - Diameter / 2);
            Canvas.SetTop(b, Pos.Y - Diameter / 2);
            return b;
        }

        public override Rect Bounds()
        {
            return new Rect(Pos.X - Diameter / 2, Pos.Y - Diameter / 2, Diameter, Diameter);
        }

        public override bool Hit(Point p) { return (p - Pos).Length <= Diameter / 2 + 3; }

        public override void Translate(double dx, double dy) { Pos = new Point(Pos.X + dx, Pos.Y + dy); }
    }
}
