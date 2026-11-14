using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Path = System.Windows.Shapes.Path;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
namespace Snapter
{
    internal sealed partial class RegionOverlay
    {
        // ================= rotating shapes and text, and the handles of a text block =================
        // A selected shape or text has a round handle above it: drag it to turn the object (Shift: in steps of editor.rotate_step;
        // near 0 / 90 / 180 degrees it sticks), double click it to make the object straight again.
        // A selected text (not turned) has handles: the sides change the width of the block (the text wraps), the corners change the font size.

        private static Point RectCenter(Rect r) { return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }

        /// <summary>The line start (on the frame) and the handle (24 px above it) for rotating, in the turned frame of the object.</summary>
        private static void RotateHandleSpots(Annotation a, out Point edge, out Point handle)
        {
            Rect lr = a.LocalRect();
            lr.Inflate(3, 3);
            Point c = RectCenter(lr);
            edge = Annotation.RotatePoint(new Point(c.X, lr.Top), a.Angle, c);
            handle = Annotation.RotatePoint(new Point(c.X, lr.Top - 24), a.Angle, c);
        }

        private static bool HitRotateHandle(Annotation a, Point p)
        {
            Point edge, h;
            RotateHandleSpots(a, out edge, out h);
            return (p - h).Length <= 10;
        }

        private static Point[] TextHandlePoints(TextAnnotation t)
        {
            Rect r = t.LocalRect();
            double l = r.Left, tp = r.Top, rt = r.Right, b = r.Bottom, cx = (l + rt) / 2, cy = (tp + b) / 2;
            return new Point[] { new Point(l, tp), new Point(cx, tp), new Point(rt, tp), new Point(rt, cy), new Point(rt, b), new Point(cx, b), new Point(l, b), new Point(l, cy) };
        }

        /// <summary>The handles of a text block that do something: corners (0, 2, 4, 6) and the left / right sides (3, 7).</summary>
        private static bool TextHandleActive(int i) { return i != 1 && i != 5; }

        private static int HitTextHandle(TextAnnotation t, Point p)
        {
            if (Math.Abs(t.Angle) > 0.001) return -1;
            Point[] pts = TextHandlePoints(t);
            for (int i = 0; i < 8; i++)
                if (TextHandleActive(i) && Math.Abs(p.X - pts[i].X) <= 7 && Math.Abs(p.Y - pts[i].Y) <= 7) return i;
            return -1;
        }

        /// <summary>The extra handles of the selected object: the round rotate handle and the handles of a text block.</summary>
        private void DrawEditHandles(Brush accent)
        {
            Annotation a = _selected;
            if (a == null) return;
            TextAnnotation tx = a as TextAnnotation;
            if (tx != null && Math.Abs(tx.Angle) < 0.001)
            {
                Point[] pts = TextHandlePoints(tx);
                for (int i = 0; i < 8; i++)
                {
                    if (!TextHandleActive(i)) continue;
                    Rectangle h = new Rectangle { Width = 9, Height = 9, Fill = Brush(_theme.Surface), Stroke = accent, StrokeThickness = 1.5 };
                    Canvas.SetLeft(h, pts[i].X - 4.5); Canvas.SetTop(h, pts[i].Y - 4.5);
                    _chrome.Children.Add(h);
                }
            }
            if (a.CanRotate)
            {
                Point edge, hp;
                RotateHandleSpots(a, out edge, out hp);
                Line ln = new Line { X1 = edge.X, Y1 = edge.Y, X2 = hp.X, Y2 = hp.Y, Stroke = accent, StrokeThickness = 1.2 };
                _chrome.Children.Add(ln);
                Ellipse e = new Ellipse { Width = 14, Height = 14, Fill = Brush(_theme.Surface), Stroke = accent, StrokeThickness = 2, ToolTip = _i18n.T("tip.rotate_handle") };
                Canvas.SetLeft(e, hp.X - 7); Canvas.SetTop(e, hp.Y - 7);
                _chrome.Children.Add(e);
            }
        }

        private void RotateDrag(Point p, bool snap)
        {
            Annotation a = _selected;
            if (a == null || _orig == null) return;
            Point cen = RectCenter(_orig.LocalRect());
            double ang = Math.Atan2(p.Y - cen.Y, p.X - cen.X) * 180 / Math.PI + 90;
            if (snap) { double st = Math.Max(1, _settings.RotateStep); ang = Math.Round(ang / st) * st; }
            else { double q = Math.Round(ang / 90) * 90; if (Math.Abs(ang - q) < 3) ang = q; }      // it sticks to the straight positions
            while (ang > 180) ang -= 360;
            while (ang <= -180) ang += 360;
            a.Angle = ang;
            RebuildAnnotations();
        }

        /// <summary>The rectangle of a turned shape that is resized by a handle: the opposite handle stays where it is on the screen.</summary>
        private static Rect ResizeRectRotated(Rect orig, double angle, int handle, Point world)
        {
            if (Math.Abs(angle) < 0.001) return ResizeRect(orig, handle, world);
            Point c0 = RectCenter(orig);
            Point local = Annotation.RotatePoint(world, -angle, c0);          // the mouse in the frame of the shape
            Rect nr = ResizeRect(orig, handle, local);
            Point cw = Annotation.RotatePoint(RectCenter(nr), angle, c0);     // where the new center is on the screen
            return new Rect(cw.X - nr.Width / 2, cw.Y - nr.Height / 2, nr.Width, nr.Height);
        }

        /// <summary>Drag of a text handle: the sides set the width of the block (the text wraps), the corners scale the font.</summary>
        private void TextResizeDrag(Point c)
        {
            TextAnnotation t = _selected as TextAnnotation, o = _orig as TextAnnotation;
            if (t == null || o == null) return;
            Rect r0 = o.LocalRect();
            c = ClampToSel(c);
            int h = _annoHandle;
            if (h == 3 || h == 7)
            {
                double w = h == 3 ? c.X - r0.Left : r0.Right - c.X;
                w = Math.Max(24, w);
                t.BoxWidth = w;
                t.Pos = new Point(h == 3 ? r0.Left : r0.Right - w, r0.Top);
                return;
            }
            // a corner: the opposite corner stays, the font grows or shrinks by the new width
            Point opp = new Point(h == 0 || h == 6 ? r0.Right : r0.Left, h == 0 || h == 2 ? r0.Bottom : r0.Top);
            double k = Math.Max(0.2, Math.Min(8, Math.Abs(c.X - opp.X) / Math.Max(1, r0.Width)));
            t.FontSize = Math.Max(6, Math.Min(500, o.FontSize * k));
            if (o.BoxWidth > 0) t.BoxWidth = o.BoxWidth * k;
            Rect nr = t.LocalRect();
            double nx = (h == 0 || h == 6) ? opp.X - nr.Width : opp.X;
            double ny = (h == 0 || h == 2) ? opp.Y - nr.Height : opp.Y;
            t.Pos = new Point(nx, ny);
        }

        // ---- self test hooks ----
        internal bool TestHitRotate(Point p) { return _selected != null && _selected.CanRotate && HitRotateHandle(_selected, p); }
        internal Point TestRotateHandle() { Point e, h; RotateHandleSpots(_selected, out e, out h); return h; }
        internal int TestTextHandle(Point p) { TextAnnotation t = _selected as TextAnnotation; return t == null ? -1 : HitTextHandle(t, p); }
        internal Point TestTextHandlePoint(int i) { return TextHandlePoints((TextAnnotation)_selected)[i]; }
    }
}
