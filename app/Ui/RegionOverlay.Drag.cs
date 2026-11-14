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
        // ---- dragging: geometry helpers, mouse move and mouse up of the overlay ----
        /// <summary>Only horizontal or vertical, whichever is closer.</summary>
        private static Point SnapAxis(Point origin, Point p)
        {
            return Math.Abs(p.X - origin.X) >= Math.Abs(p.Y - origin.Y) ? new Point(p.X, origin.Y) : new Point(origin.X, p.Y);
        }

        /// <summary>The rectangle of a drag from "a" to "c" inside "limit": optionally a square, optionally from its center.</summary>
        private static Rect DragRect(Point a, Point c, bool square, bool fromCenter, Rect limit)
        {
            Vector d = c - a;
            double sx = d.X < 0 ? -1 : 1, sy = d.Y < 0 ? -1 : 1;
            double w = Math.Abs(d.X), h = Math.Abs(d.Y);
            double roomX = fromCenter ? Math.Min(a.X - limit.Left, limit.Right - a.X) : (sx < 0 ? a.X - limit.Left : limit.Right - a.X);
            double roomY = fromCenter ? Math.Min(a.Y - limit.Top, limit.Bottom - a.Y) : (sy < 0 ? a.Y - limit.Top : limit.Bottom - a.Y);
            roomX = Math.Max(0, roomX); roomY = Math.Max(0, roomY);
            w = Math.Min(w, roomX); h = Math.Min(h, roomY);
            if (square) { double s = Math.Min(Math.Max(w, h), Math.Min(roomX, roomY)); w = s; h = s; }
            if (fromCenter) return new Rect(a.X - w, a.Y - h, 2 * w, 2 * h);
            return RectAnnotation.Norm(a, new Point(a.X + sx * w, a.Y + sy * h));
        }

        /// <summary>Like DragRect, but the effects are not all-or-nothing: constrainT (0..1) is how far the rectangle is pulled
        /// towards a square, centerT (0..1) how far its start is moved towards the center (the mouse wheel changes these while a key is held).</summary>
        private static Rect DragRectBlend(Point a, Point c, double constrainT, double centerT, Rect limit)
        {
            Vector d = c - a;
            if (constrainT > 0)
            {
                Rect sq = DragRect(a, c, true, false, limit);   // the largest square this drag allows
                Vector sd = new Vector((d.X < 0 ? -1 : 1) * sq.Width, (d.Y < 0 ? -1 : 1) * sq.Height);
                d = d + (sd - d) * constrainT;
            }
            Point end = a + d;
            Point start = centerT > 0 ? new Point(a.X - d.X * centerT, a.Y - d.Y * centerT) : a;
            Rect r = RectAnnotation.Norm(start, end);
            r.Intersect(limit);
            return r.IsEmpty ? new Rect(a, a) : r;
        }

        private static Point Lerp(Point from, Point to, double t)
        {
            return new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }

        private static Point SnapAngle(Point origin, Point p)
        {
            Vector v = p - origin;
            double len = v.Length;
            if (len < 1) return p;
            double step = Math.PI / 4;
            double ang = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
            return new Point(origin.X + Math.Cos(ang) * len, origin.Y + Math.Sin(ang) * len);
        }

        private long _liveTick;

        /// <summary>While the region is being drawn, moved or stretched the panels follow it live (not more often than every ~30 ms).</summary>
        private void LiveLayoutPanels()
        {
            if (_autoCopy || !_hasSel || _sel.Width < 40 || _sel.Height < 40) { if (_panelsBuilt) HidePanels(); return; }
            long now = Environment.TickCount64;
            if (now - _liveTick < 30) return;
            _liveTick = now;
            if (!_panelsBuilt) BuildPanels();
            LayoutPanels();
        }

        private void HandleMove(Point p, ModifierKeys mods)
        {
            Point prev = _lastPt;
            _lastPt = p;
            UpdatePointerAids(p);
            if (_mode == DragMode.None)
            {
                UpdateCursor(p);
                UpdatePickChip(p);
                return;
            }
            // what the held Shift / Ctrl / Alt keys mean is set in the settings (modifier_actions)
            HashSet<string> allActs = (_settings.ModEnabled && _settings.ModHold) ? _settings.ModActions(mods) : new HashSet<string>();
            HashSet<string> acts = _settings.ModShapes ? allActs : new HashSet<string>();          // for shapes and arrows
            HashSet<string> regionActs = _settings.ModRegion ? allActs : new HashSet<string>();    // for the selection region
            bool constrain = acts.Contains("constrain");
            bool center = acts.Contains("center");
            Point c = Clamp(p);

            switch (_mode)
            {
                case DragMode.New:
                    if (Keyboard.IsKeyDown(Key.Space))
                    {
                        // Space: move the whole frame while dragging
                        Vector dv = c - Clamp(prev);
                        _start = Clamp(new Point(_start.X + dv.X, _start.Y + dv.Y));
                    }
                    if (RatioActive) _sel = RatioDrag(_start, c, regionActs.Contains("center") ? _strCenter / 100 : 0, new Rect(0, 0, _canvas.Width, _canvas.Height));   // a locked aspect ratio (Tab)
                    else _sel = DragRectBlend(_start, c, regionActs.Contains("constrain") ? _strConstrain / 100 : 0, regionActs.Contains("center") ? _strCenter / 100 : 0, new Rect(0, 0, _canvas.Width, _canvas.Height));
                    _regionRoundPct = regionActs.Contains("rounded") ? _strRound : 0;
                    UpdateVisuals();
                    LiveLayoutPanels();
                    break;

                case DragMode.Erase:
                    EraseAt(c);
                    break;

                case DragMode.Marquee:
                    MarqueeMove(c);
                    break;

                case DragMode.Rotate:
                    RotateDrag(p, constrain);
                    break;

                case DragMode.TextResize:
                    TextResizeDrag(c);
                    RebuildAnnotations();
                    break;

                case DragMode.Move:
                    {
                        double dx = c.X - _start.X, dy = c.Y - _start.Y;
                        double x = Math.Max(0, Math.Min(_canvas.Width - _selStart.Width, _selStart.X + dx));
                        double y = Math.Max(0, Math.Min(_canvas.Height - _selStart.Height, _selStart.Y + dy));
                        _sel = new Rect(x, y, _selStart.Width, _selStart.Height);
                        UpdateVisuals();
                        LiveLayoutPanels();
                        break;
                    }

                case DragMode.Resize:
                    _sel = RatioActive ? RatioResize(_selStart, _handle, c) : ResizeRect(_selStart, _handle, c);
                    UpdateVisuals();
                    LiveLayoutPanels();
                    break;

                case DragMode.AnnoResize:
                    {
                        RectAnnotation ra = _selected as RectAnnotation;
                        if (ra == null) break;
                        ra.R = ResizeRectRotated(((RectAnnotation)_orig).R, ra.Angle, _annoHandle, ClampToSel(c));
                        RebuildAnnotations();
                        break;
                    }

                case DragMode.DrawRect:
                    {
                        RectAnnotation ra = _selected as RectAnnotation;
                        if (ra == null) break;
                        Point q = ClampToSel(c);
                        Point a = new Point(_rectAnchor.X, _rectAnchor.Y);
                        // a square or a circle, from the center - never beyond the region
                        ra.R = DragRectBlend(a, q, constrain ? _strConstrain / 100 : 0, center ? _strCenter / 100 : 0, _sel);
                        ShapeAnnotation sh = ra as ShapeAnnotation;
                        if (sh != null)
                        {
                            ToolStyle st0 = _styles.Current(sh.Ellipse ? EditorTool.Ellipse : EditorTool.Rect);
                            if (!sh.Ellipse) { sh.Rounded = st0.Rounded ^ acts.Contains("rounded"); sh.RoundPct = _strRound; }
                            sh.Fill = acts.Contains("fill") ? (st0.Fill == 2 ? 1 : 2) : st0.Fill;
                        }
                        RebuildAnnotations();
                        break;
                    }

                case DragMode.DrawBrush:
                    {
                        BrushAnnotation br = _selected as BrushAnnotation;
                        if (br == null) break;
                        Point q = ClampToSel(c);
                        if (constrain || acts.Contains("axis"))
                        {
                            // a straight line: 45 degree steps, or only horizontal / vertical
                            br.Pts.Clear();
                            br.Pts.Add(_brushFirst);
                            Point snappedQ = acts.Contains("axis") ? Lerp(q, SnapAxis(_brushFirst, q), _strAxis / 100) : Lerp(q, SnapAngle(_brushFirst, q), _strConstrain / 100);
                            br.Pts.Add(ClampToSel(snappedQ));
                        }
                        else
                        {
                            if (br.Pts.Count > 0 && (q - br.Pts[br.Pts.Count - 1]).Length < 1.5) break;
                            br.Pts.Add(q);
                        }
                        RebuildAnnotations();
                        break;
                    }

                case DragMode.EditMove:
                    if (_editing != null && _editBorder != null)
                    {
                        _editing.CopyFrom(_orig);
                        _editing.Translate(c.X - _start.X, c.Y - _start.Y);
                        Canvas.SetLeft(_editBorder, _editing.Pos.X - EditPad);
                        Canvas.SetTop(_editBorder, _editing.Pos.Y - EditPad);
                    }
                    break;

                case DragMode.AnnoMove:
                    if (_selected != null)
                    {
                        _selected.CopyFrom(_orig);
                        _selected.Translate(c.X - _start.X, c.Y - _start.Y);
                        if (SnapHeld())
                        {
                            // the held snap key: the centre sticks to the vertical or horizontal axis of another object when it is near
                            Point cen = CenterOf(_selected);
                            Point sn = SnapCenter(cen, _selected);
                            _selected.Translate(sn.X - cen.X, sn.Y - cen.Y);
                        }
                        else ClearGuides();
                        MoveFollowers();                                    // the rest of a multi-selection follows
                        RebuildAnnotations();
                    }
                    break;

                case DragMode.ArrowEnd:
                case DragMode.DrawArrow:
                    {
                        ArrowAnnotation a = _selected as ArrowAnnotation;
                        if (a == null) break;
                        Point q = ClampToSel(c);
                        int moving = (_mode == DragMode.DrawArrow) ? 1 : _endIndex;
                        Point fixedEnd = (moving == 1) ? a.A : a.B;
                        if (acts.Contains("axis")) q = ClampToSel(Lerp(q, SnapAxis(fixedEnd, q), _strAxis / 100));
                        else if (constrain) q = ClampToSel(Lerp(q, SnapAngle(fixedEnd, q), _strConstrain / 100));
                        if (center && _mode == DragMode.DrawArrow)
                        {
                            // from the center (as far as the strength says): the first point is the middle of the arrow at 100%
                            Point mid = _brushFirst;
                            double tc = _strCenter / 100;
                            a.A = ClampToSel(new Point(mid.X - (q.X - mid.X) * tc, mid.Y - (q.Y - mid.Y) * tc));
                            a.B = q;
                        }
                        else if (moving == 1) a.B = q; else a.A = q;
                        if (_mode == DragMode.DrawArrow && a.Head) a.Head2 = acts.Contains("double");
                        RebuildAnnotations();
                        break;
                    }
            }
        }

        private void HandleUp()
        {
            Log.Debug("up mode=" + _mode + " sel=" + _sel + " items=" + _items.Count);
            if (_mode == DragMode.None) return;
            DragMode was = _mode;
            _mode = DragMode.None;
            _canvas.ReleaseMouseCapture();
            UpdatePointerAids(_lastPt); // the loupe and the crosshair go away once a region exists
            ClearGuides();
            if (_modMax != ModifierKeys.None) _modOther = true; // a key held across the end of the drawing is not a tap either

            if (was == DragMode.Erase)
            {
                if (_erased > 0) Commit();
                _erased = 0;
                RefreshPanels();
                return;
            }
            if (was == DragMode.Marquee)
            {
                MarqueeEnd();
                return;
            }
            if (was == DragMode.EditMove)
            {
                _orig = null;
                if (_editBox != null) { _editBox.Focus(); Keyboard.Focus(_editBox); }
                return;
            }
            if (was == DragMode.AnnoMove || was == DragMode.ArrowEnd || was == DragMode.AnnoResize || was == DragMode.Rotate || was == DragMode.TextResize)
            {
                bool moved = _orig != null && _selected != null && Differs(_orig, _selected);
                Annotation pending = _pendingEdit;
                _pendingEdit = null;
                if (moved) Commit();
                _groupOrig = null;
                _orig = null;
                RefreshPanels();
                // a plain click on a text with the text tool opens it for editing; dragging moves it
                if (!moved && pending is TextAnnotation && _items.Contains(pending)) BeginEdit((TextAnnotation)pending, false);
                return;
            }
            if (was == DragMode.DrawRect)
            {
                RectAnnotation ra = _selected as RectAnnotation;
                if (ra != null && (ra.R.Width < 3 || ra.R.Height < 3))
                {
                    _items.RemoveAll(x => x.Id == ra.Id);
                    _selected = null;
                    RebuildAnnotations();
                }
                else Commit();
                RefreshPanels();
                return;
            }
            if (was == DragMode.DrawBrush)
            {
                Commit();
                RefreshPanels();
                return;
            }
            if (was == DragMode.DrawArrow)
            {
                ArrowAnnotation a = _selected as ArrowAnnotation;
                if (a != null && a.Length < 6)
                {
                    _items.RemoveAll(x => x.Id == a.Id);
                    _selected = null;
                    RebuildAnnotations();
                }
                else Commit();
                RefreshPanels();
                return;
            }

            if (was == DragMode.New && (_sel.Width < 4 || _sel.Height < 4))
            {
                // a fixed size (capture.fixed_size): a plain click makes a region of exactly that size
                int fw, fh;
                if (TryParseSize(_settings.FixedSize, out fw, out fh))
                {
                    double nw = Math.Min(fw, _canvas.Width), nh = Math.Min(fh, _canvas.Height);
                    _sel = new Rect(Math.Max(0, Math.Min(_start.X, _canvas.Width - nw)), Math.Max(0, Math.Min(_start.Y, _canvas.Height - nh)), nw, nh);
                    if (_autoCopy) { DoCopy(); return; }
                    UpdateVisuals();
                    ShowPanels();
                    return;
                }
                // a plain click on a window selects the whole window
                Rect win;
                if (_settings.WindowSnap && TryWindowAt(_start, out win))
                {
                    _sel = win;
                    if (_autoCopy) { DoCopy(); return; }
                    UpdateVisuals();
                    ShowPanels();
                    return;
                }
                _hasSel = false;
                UpdateVisuals();
                return;
            }
            if (_autoCopy && was == DragMode.New) { DoCopy(); return; }
            UpdateVisuals();
            ShowPanels();
        }

        private static bool Differs(Annotation a, Annotation b)
        {
            if (Math.Abs(a.Angle - b.Angle) > 0.01) return true;
            ArrowAnnotation x = a as ArrowAnnotation, y = b as ArrowAnnotation;
            if (x != null && y != null) return x.A != y.A || x.B != y.B;
            TextAnnotation s = a as TextAnnotation, t = b as TextAnnotation;
            if (s != null && t != null) return s.Pos != t.Pos || s.FontSize != t.FontSize || s.BoxWidth != t.BoxWidth;
            Rect ra = a.Bounds(), rb = b.Bounds();
            return Math.Abs(ra.X - rb.X) > 0.01 || Math.Abs(ra.Y - rb.Y) > 0.01 || Math.Abs(ra.Width - rb.Width) > 0.01 || Math.Abs(ra.Height - rb.Height) > 0.01;
        }

        private static Rect ResizeRect(Rect start, int handle, Point c)
        {
            double l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
            switch (handle)
            {
                case 0: l = c.X; t = c.Y; break;
                case 1: t = c.Y; break;
                case 2: r = c.X; t = c.Y; break;
                case 3: r = c.X; break;
                case 4: r = c.X; b = c.Y; break;
                case 5: b = c.Y; break;
                case 6: l = c.X; b = c.Y; break;
                case 7: l = c.X; break;
            }
            return new Rect(new Point(Math.Min(l, r), Math.Min(t, b)), new Point(Math.Max(l, r), Math.Max(t, b)));
        }

        private static int HitRectHandle(RectAnnotation a, Point p)
        {
            Point[] pts = a.HandlePoints();
            for (int i = 0; i < 8; i++)
                if (Math.Abs(p.X - pts[i].X) <= 7 && Math.Abs(p.Y - pts[i].Y) <= 7) return i;
            return -1;
        }

    }
}
