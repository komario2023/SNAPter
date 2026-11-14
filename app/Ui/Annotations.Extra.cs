using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>Paint bucket: finds the area of similar color around a point of the screenshot (the picture is not changed).</summary>
    internal static class FloodFiller
    {
        /// <summary>seed: a point of the screenshot; limit: the region; tolerance: percent of the largest color difference that still counts as the same color.
        /// mask has one byte per pixel of box (1 = painted). The painted area is made one pixel wider, so that soft edges are covered too.</summary>
        public static bool Fill(Point seed, Int32Rect limit, int tolerance, out Int32Rect box, out byte[] mask)
        {
            box = new Int32Rect(); mask = null;
            byte[] pix; int w, h;
            if (!Backdrop.Raw(out pix, out w, out h)) return false;
            int lx0 = Math.Max(0, limit.X), ly0 = Math.Max(0, limit.Y), lx1 = Math.Min(w, limit.X + limit.Width), ly1 = Math.Min(h, limit.Y + limit.Height);
            int sx = (int)Math.Floor(seed.X), sy = (int)Math.Floor(seed.Y);
            if (sx < lx0 || sx >= lx1 || sy < ly0 || sy >= ly1) return false;
            int lw = lx1 - lx0, lh = ly1 - ly0;
            byte[] seen = new byte[lw * lh];
            int si = (sy * w + sx) * 4;
            int sb = pix[si], sg = pix[si + 1], sr = pix[si + 2];
            int thr = Math.Max(0, Math.Min(255, (int)Math.Round(tolerance * 2.55)));
            Func<int, int, bool> match = delegate (int x, int y)
            {
                int i = (y * w + x) * 4;
                return Math.Abs(pix[i] - sb) <= thr && Math.Abs(pix[i + 1] - sg) <= thr && Math.Abs(pix[i + 2] - sr) <= thr;
            };
            Stack<long> stack = new Stack<long>();
            stack.Push(((long)sy << 32) | (uint)sx);
            while (stack.Count > 0)
            {
                long v = stack.Pop();
                int x = (int)(v & 0xFFFFFFFF), y = (int)(v >> 32);
                if (seen[(y - ly0) * lw + (x - lx0)] != 0 || !match(x, y)) continue;
                int xl = x, xr = x;
                while (xl - 1 >= lx0 && seen[(y - ly0) * lw + (xl - 1 - lx0)] == 0 && match(xl - 1, y)) xl--;
                while (xr + 1 < lx1 && seen[(y - ly0) * lw + (xr + 1 - lx0)] == 0 && match(xr + 1, y)) xr++;
                for (int i = xl; i <= xr; i++) seen[(y - ly0) * lw + (i - lx0)] = 1;
                foreach (int yy in new int[] { y - 1, y + 1 })
                {
                    if (yy < ly0 || yy >= ly1) continue;
                    bool run = false;
                    for (int i = xl; i <= xr; i++)
                    {
                        bool ok = seen[(yy - ly0) * lw + (i - lx0)] == 0 && match(i, yy);
                        if (ok && !run) { stack.Push(((long)yy << 32) | (uint)i); run = true; }
                        else if (!ok) run = false;
                    }
                }
            }
            // one pixel wider
            byte[] grown = new byte[seen.Length];
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < lh; y++)
                for (int x = 0; x < lw; x++)
                {
                    if (seen[y * lw + x] == 0) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx != 0 && dy != 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= lw || ny >= lh) continue;
                            grown[ny * lw + nx] = 1;
                        }
                }
            for (int y = 0; y < lh; y++)
                for (int x = 0; x < lw; x++)
                    if (grown[y * lw + x] != 0) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            if (maxX < 0) return false;
            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            byte[] cut = new byte[bw * bh];
            for (int y = 0; y < bh; y++) Array.Copy(grown, (minY + y) * lw + minX, cut, y * bw, bw);
            box = new Int32Rect(lx0 + minX, ly0 + minY, bw, bh);
            mask = cut;
            return true;
        }
    }

    /// <summary>The area of one color painted over (the paint bucket tool).</summary>
    internal sealed class FillAnnotation : Annotation
    {
        public Rect R;               // the box around the painted area
        public byte[] Mask;          // R.Width x R.Height bytes, 1 = painted; shared by the copies, never changed
        public int Tolerance = 12;   // what it was made with (percent)
        private BitmapSource _bmp;
        private WpfColor _bmpColor;

        public override Annotation Clone() { FillAnnotation c = new FillAnnotation(); c.Id = Id; c.CopyFrom(this); return c; }

        public override void CopyFrom(Annotation other)
        {
            FillAnnotation o = (FillAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; R = o.R; Mask = o.Mask; Tolerance = o.Tolerance;
        }

        public override FrameworkElement CreateVisual()
        {
            int w = (int)Math.Round(R.Width), h = (int)Math.Round(R.Height);
            Canvas holder = new Canvas { IsHitTestVisible = false };
            if (Mask == null || w < 1 || h < 1 || Mask.Length < w * h) return holder;
            if (_bmp == null || _bmpColor != Color)
            {
                byte a = Color.A, pr = (byte)(Color.R * a / 255), pg = (byte)(Color.G * a / 255), pb = (byte)(Color.B * a / 255);
                byte[] px = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++)
                    if (Mask[i] != 0) { px[i * 4] = pb; px[i * 4 + 1] = pg; px[i * 4 + 2] = pr; px[i * 4 + 3] = a; }
                BitmapSource bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
                bs.Freeze();
                _bmp = bs; _bmpColor = Color;
            }
            Image img = new Image { Source = _bmp, Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, R.X);
            Canvas.SetTop(img, R.Y);
            holder.Children.Add(img);
            return holder;
        }

        public override Rect Bounds() { return R; }

        public override bool Hit(Point p)
        {
            int w = (int)Math.Round(R.Width), h = (int)Math.Round(R.Height);
            int x = (int)Math.Floor(p.X - R.X), y = (int)Math.Floor(p.Y - R.Y);
            return Mask != null && x >= 0 && y >= 0 && x < w && y < h && Mask[y * w + x] != 0;
        }

        public override void Translate(double dx, double dy) { R = new Rect(R.X + dx, R.Y + dy, R.Width, R.Height); }
    }

    /// <summary>Everything around a rectangle is darkened, the rectangle stays bright (the opacity of the object is the strength).</summary>
    internal sealed class SpotlightAnnotation : RectAnnotation
    {
        public override Annotation Clone() { SpotlightAnnotation c = new SpotlightAnnotation(); c.Id = Id; c.CopyFrom(this); return c; }

        public override void CopyFrom(Annotation other)
        {
            SpotlightAnnotation o = (SpotlightAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; R = o.R;
        }

        public override FrameworkElement CreateVisual()
        {
            GeometryGroup g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new RectangleGeometry(new Rect(-20000, -20000, 40000, 40000)));
            g.Children.Add(new RectangleGeometry(R));
            Path p = new Path { Data = g, Fill = MakeBrush(Color), IsHitTestVisible = false };
            return p;
        }

        /// <summary>Only the frame of the bright area is clickable: the dark area and the objects inside stay free.</summary>
        public override bool Hit(Point p)
        {
            Rect o = R; o.Inflate(7, 7);
            Rect i = R; i.Inflate(-7, -7);
            return o.Contains(p) && (i.IsEmpty || i.Width <= 0 || i.Height <= 0 || !i.Contains(p));
        }
    }

    /// <summary>A round magnifier: the part of the screenshot under its center, shown larger (Zoom times) inside a ring.</summary>
    internal sealed class MagnifierAnnotation : RectAnnotation
    {
        public double Zoom = 2;
        public double Thickness = 3;

        public override Annotation Clone() { MagnifierAnnotation c = new MagnifierAnnotation(); c.Id = Id; c.CopyFrom(this); return c; }

        public override void CopyFrom(Annotation other)
        {
            MagnifierAnnotation o = (MagnifierAnnotation)other;
            Color = o.Color; Opacity = o.Opacity; R = o.R; Zoom = o.Zoom; Thickness = o.Thickness;
        }

        public override FrameworkElement CreateVisual()
        {
            Canvas holder = new Canvas { IsHitTestVisible = false };
            BitmapSource src = Backdrop.Source;
            if (src == null || R.Width < 4 || R.Height < 4) return holder;
            double z = Math.Max(1.1, Zoom);
            int w = Math.Max(2, Math.Min(src.PixelWidth, (int)Math.Ceiling(R.Width / z)));
            int h = Math.Max(2, Math.Min(src.PixelHeight, (int)Math.Ceiling(R.Height / z)));
            int x = (int)Math.Round(R.X + R.Width / 2 - w / 2.0), y = (int)Math.Round(R.Y + R.Height / 2 - h / 2.0);
            x = Math.Max(0, Math.Min(src.PixelWidth - w, x));
            y = Math.Max(0, Math.Min(src.PixelHeight - h, y));
            Image img = new Image { Source = new CroppedBitmap(src, new Int32Rect(x, y, w, h)), Stretch = Stretch.Fill, Width = R.Width, Height = R.Height, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
            img.Clip = new EllipseGeometry(new Rect(0, 0, R.Width, R.Height));
            Ellipse ring = new Ellipse { Width = R.Width, Height = R.Height, Stroke = MakeBrush(Color), StrokeThickness = Thickness, IsHitTestVisible = false };
            Canvas box = new Canvas { Width = R.Width, Height = R.Height, IsHitTestVisible = false };
            box.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.35, Color = Colors.Black };
            box.Children.Add(img); box.Children.Add(ring);
            Canvas.SetLeft(box, R.X); Canvas.SetTop(box, R.Y);
            holder.Children.Add(box);
            return holder;
        }

        public override bool Hit(Point p)
        {
            double a = Math.Max(1, R.Width / 2), b = Math.Max(1, R.Height / 2);
            double dx = (p.X - (R.X + a)) / a, dy = (p.Y - (R.Y + b)) / b;
            return Math.Sqrt(dx * dx + dy * dy) <= 1 + 6 / Math.Min(a, b);
        }
    }
}
