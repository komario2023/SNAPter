using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>One entry of the aspect ratio list: "16:9", "4:3", "A4", "2.39:1" or "free".</summary>
    internal sealed class RatioItem
    {
        public string Token;      // as typed in the settings
        public double V;          // width / height as listed (A4 is 210/297: upright)
        public bool Free;         // no lock
        public bool Paper;        // A3, A4, A5...: the label tells upright or landscape instead of numbers
    }

    /// <summary>A region with a fixed aspect ratio. Tab (before the region and while it is drawn or stretched) walks through the list of the settings
    /// (layout "ratio.list": the first entry is the one used first, the order is yours). While the region is drawn the program follows the hand:
    /// a 16:9 region pulled downwards becomes 9:16 at once (and back), a 4:3 one becomes 3:4. Stretching keeps the ratio.</summary>
    internal sealed partial class RegionOverlay
    {
        internal const string DefaultRatioList = "free,16:9,4:3,3:2,1:1,21:9,4:5,A4";
        private List<RatioItem> _ratios;
        private int _ratioIdx;
        private bool _ratioPortrait;          // the current orientation of a ratio that is not square
        private double _ratioAspect = 1;      // the width / height that was applied last
        private Border _ratioToast;
        private TextBlock _ratioToastText;
        private DispatcherTimer _ratioTimer;

        internal static List<RatioItem> ParseRatios(string spec)
        {
            List<RatioItem> list = new List<RatioItem>();
            foreach (string raw in (spec ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim();
                string low = t.ToLowerInvariant();
                RatioItem it = new RatioItem { Token = t };
                if (low == "free" || low == "none" || low == "0") { it.Free = true; it.Token = "free"; list.Add(it); continue; }
                if (low.Length == 2 && low[0] == 'a' && char.IsDigit(low[1]) && low[1] >= '0' && low[1] <= '8') { it.Paper = true; it.V = 1.0 / Math.Sqrt(2); it.Token = t.ToUpperInvariant(); list.Add(it); continue; }
                string[] p = low.Split(new char[] { ':', '/', 'x', '×' }, StringSplitOptions.RemoveEmptyEntries);
                double w, h = 1;
                if (p.Length >= 1 && double.TryParse(p[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0
                    && (p.Length < 2 || (double.TryParse(p[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out h) && h > 0)))
                {
                    it.V = w / h;
                    if (it.V < 0.2 || it.V > 6) continue;
                    list.Add(it);
                }
            }
            return list;
        }

        private void InitRatios()
        {
            _ratios = ParseRatios(_settings.RatioList);
            if (_ratios.Count == 0) _ratios.Add(new RatioItem { Token = "free", Free = true });
            _ratioIdx = 0;
            _ratioPortrait = RatioActive && _ratios[0].V < 1;
        }

        private bool RatioActive { get { return _ratios != null && _ratioIdx >= 0 && _ratioIdx < _ratios.Count && !_ratios[_ratioIdx].Free; } }

        /// <summary>The width / height for the current entry in the orientation that is chosen now.</summary>
        private double RatioAspectFor(double v)
        {
            if (!_settings.RatioFlip) return v;
            double hi = Math.Max(v, 1 / v), lo = 1 / hi;
            if (hi - lo < 0.001) return v;
            return _ratioPortrait ? lo : hi;
        }

        /// <summary>The key of the aspect ratios (settings: ratio.key, Tab by default).</summary>
        private bool IsRatioKey(Key k)
        {
            Key want;
            return ParseKeyName(_settings.RatioKey, out want) && want == k;
        }

        private bool HandleRatioKey()
        {
            if (_preview || _ratios == null || _ratios.Count < 2) return false;
            if (_editBox != null || _sizeEdit != null || _typingSize || Keyboard.FocusedElement is TextBox) return false;
            Key rk; ParseKeyName(_settings.RatioKey, out rk);
            if (_editor && _panelsBuilt && IsCornerKey(rk)) return false;          // the corner key of the panels keeps this key if it is set to the same one
            if (_mode != DragMode.None && _mode != DragMode.New && _mode != DragMode.Resize) return false;
            CycleRatio((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            return true;
        }

        private void CycleRatio(bool back)
        {
            int n = _ratios.Count;
            _ratioIdx = (_ratioIdx + (back ? -1 : 1) + n) % n;
            _ratioPortrait = RatioActive && _ratios[_ratioIdx].V < 1;
            if (RatioActive) _ratioAspect = RatioAspectFor(_ratios[_ratioIdx].V);
            if (_mode == DragMode.New || _mode == DragMode.Resize) HandleMove(_lastPt, Keyboard.Modifiers);    // the frame follows at once
            else if (_hasSel && RatioActive && _items.Count == 0) RefitRegion();                           // a finished region without drawings takes the new shape
            ShowRatioToast();
            UpdateVisuals();
        }

        /// <summary>The region keeps its center and about its area, and takes the ratio (upright or landscape as it is now).</summary>
        private void RefitRegion()
        {
            double v = _ratios[_ratioIdx].V;
            if (_settings.RatioFlip) _ratioPortrait = _sel.Height > _sel.Width;
            double a = RatioAspectFor(v);
            double area = Math.Max(16, _sel.Width * _sel.Height);
            double w = Math.Sqrt(area * a), h = w / a;
            double k = Math.Min(1, Math.Min(_canvas.Width / w, _canvas.Height / h));
            w *= k; h *= k;
            Point c = new Point(_sel.X + _sel.Width / 2, _sel.Y + _sel.Height / 2);
            double x = Math.Max(0, Math.Min(_canvas.Width - w, c.X - w / 2)), y = Math.Max(0, Math.Min(_canvas.Height - h, c.Y - h / 2));
            _sel = new Rect(x, y, w, h);
            _ratioAspect = a;
            if (_panelsBuilt && _panelB != null && _panelB.Visibility == Visibility.Visible) LayoutPanels();
        }

        /// <summary>A drag from "a" to "c" with the ratio: the frame covers the pointer, never beyond "limit". The orientation follows the hand.</summary>
        private Rect RatioDrag(Point a, Point c, double centerT, Rect limit)
        {
            double adx = Math.Abs(c.X - a.X), ady = Math.Abs(c.Y - a.Y);
            double v = _ratios[_ratioIdx].V;
            if (_settings.RatioFlip && (adx > 6 || ady > 6))
            {
                double hi = Math.Max(v, 1 / v);
                if (hi - 1 / hi > 0.001)
                {
                    double dr = adx / Math.Max(1.0, ady);
                    if (dr > 1.08) _ratioPortrait = false; else if (dr < 0.93) _ratioPortrait = true;   // between the two: it stays as it is (no flicker)
                }
            }
            return RatioCore(a, c, RatioAspectFor(v), centerT, limit);
        }

        private Rect RatioCore(Point a, Point c, double asp, double centerT, Rect limit)
        {
            double dx = c.X - a.X, dy = c.Y - a.Y;
            double sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
            double w = Math.Max(Math.Abs(dx), Math.Abs(dy) * asp);
            double fwdX = sx < 0 ? a.X - limit.Left : limit.Right - a.X, backX = sx < 0 ? limit.Right - a.X : a.X - limit.Left;
            double fwdY = sy < 0 ? a.Y - limit.Top : limit.Bottom - a.Y, backY = sy < 0 ? limit.Bottom - a.Y : a.Y - limit.Top;
            double maxW = Math.Max(0, fwdX), maxH = Math.Max(0, fwdY);
            if (centerT > 0) { maxW = Math.Min(maxW, Math.Max(0, backX) / centerT); maxH = Math.Min(maxH, Math.Max(0, backY) / centerT); }
            w = Math.Min(w, Math.Min(maxW, maxH * asp));
            double h = w / asp;
            _ratioAspect = asp;
            return RectAnnotation.Norm(new Point(a.X - sx * w * centerT, a.Y - sy * h * centerT), new Point(a.X + sx * w, a.Y + sy * h));
        }

        /// <summary>Stretching by a handle keeps the ratio (the orientation of the region as it was).</summary>
        private Rect RatioResize(Rect start, int handle, Point c)
        {
            double v = _ratios[_ratioIdx].V;
            if (_settings.RatioFlip) _ratioPortrait = start.Height > start.Width;
            double a = RatioAspectFor(v);
            Rect all = new Rect(0, 0, _canvas.Width, _canvas.Height);
            if (handle % 2 == 0)
            {
                Point anchor = handle == 0 ? start.BottomRight : handle == 2 ? start.BottomLeft : handle == 4 ? start.TopLeft : start.TopRight;
                return RatioCore(anchor, c, a, 0, all);
            }
            double cx = start.X + start.Width / 2, cy = start.Y + start.Height / 2, w, h;
            if (handle == 1 || handle == 5)
            {
                double fixedY = handle == 1 ? start.Bottom : start.Top;
                h = Math.Abs(c.Y - fixedY);
                h = Math.Min(h, handle == 1 ? fixedY : _canvas.Height - fixedY);
                w = h * a;
                double maxW = 2 * Math.Min(cx, _canvas.Width - cx);
                if (w > maxW) { w = maxW; h = w / a; }
                double top = handle == 1 ? fixedY - h : fixedY;
                _ratioAspect = a;
                return new Rect(cx - w / 2, top, w, h);
            }
            double fixedX = handle == 3 ? start.Left : start.Right;
            w = Math.Abs(c.X - fixedX);
            w = Math.Min(w, handle == 3 ? _canvas.Width - fixedX : fixedX);
            h = w / a;
            double maxH = 2 * Math.Min(cy, _canvas.Height - cy);
            if (h > maxH) { h = maxH; w = h * a; }
            double left = handle == 3 ? fixedX : fixedX - w;
            _ratioAspect = a;
            return new Rect(left, cy - h / 2, w, h);
        }

        // ---- the label and the toast ----
        /// <summary>"16:9", "9:16" (swapped when the region is upright), "A4 (upright)", "free".</summary>
        private string RatioLabel()
        {
            RatioItem it = _ratios[_ratioIdx];
            if (it.Free) return _i18n.T("ratio.free");
            double a = RatioActive ? _ratioAspect : it.V;
            if (it.Paper) return it.Token + " (" + _i18n.T(a < 1 ? "ratio.port" : "ratio.land") + ")";
            string[] parts = it.Token.Split(new char[] { ':', '/', 'x', '×' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && Math.Abs(it.V - 1) > 0.001 && (it.V >= 1) != (a >= 1)) return parts[1] + ":" + parts[0];
            return parts.Length == 2 ? parts[0] + ":" + parts[1] : it.Token;
        }

        private void ShowRatioToast()
        {
            if (_ratios == null || _ratios.Count < 2 && !RatioActive) return;
            if (_ratioToast == null)
            {
                _ratioToastText = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(_theme.Text), TextAlignment = TextAlignment.Center };
                _ratioToast = new Border
                {
                    Child = _ratioToastText, Padding = new Thickness(14, 7, 14, 7), CornerRadius = new CornerRadius(Math.Max(4, _theme.RadiusMd - 2)), IsHitTestVisible = false,
                    Background = new SolidColorBrush(WpfColor.FromArgb(240, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B)),
                    BorderBrush = new SolidColorBrush(_theme.Accent), BorderThickness = new Thickness(1.5)
                };
                _canvas.Children.Add(_ratioToast);
                _ratioTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                _ratioTimer.Tick += delegate { _ratioTimer.Stop(); if (_hasSel || !RatioActive) _ratioToast.Visibility = Visibility.Collapsed; };
            }
            _ratioToastText.Text = _i18n.T("ratio.toast", RatioLabel(), _ratioIdx + 1, _ratios.Count,
                _i18n.T(_editor && _panelsBuilt && CornerIsShiftTab ? "ratio.toast_panels" : "ratio.toast_back"));
            _ratioToast.Visibility = Visibility.Visible;
            _ratioToast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            try
            {
                System.Drawing.Point cp = System.Windows.Forms.Cursor.Position;
                System.Drawing.Rectangle mon = System.Windows.Forms.Screen.FromPoint(cp).Bounds;
                Canvas.SetLeft(_ratioToast, mon.X - _bounds.X + (mon.Width - _ratioToast.DesiredSize.Width) / 2);
                Canvas.SetTop(_ratioToast, mon.Y - _bounds.Y + 28);
            }
            catch { Canvas.SetLeft(_ratioToast, 20); Canvas.SetTop(_ratioToast, 28); }
            _ratioTimer.Stop(); _ratioTimer.Start();
        }

        // ---- self test hooks ----
        internal void TestSetRatios(string spec, int idx, bool flip) { _settings.RatioFlip = flip; _ratios = ParseRatios(spec); _ratioIdx = idx; _ratioPortrait = RatioActive && _ratios[idx].V < 1; }
        internal Rect TestRatioDrag(Point a, Point c) { return RatioDrag(a, c, 0, new Rect(0, 0, _canvas.Width, _canvas.Height)); }
        internal Rect TestRatioResize(Rect start, int handle, Point c) { return RatioResize(start, handle, c); }
        internal string TestRatioLabel() { return RatioLabel(); }
        internal void TestCycleRatio(bool back) { CycleRatio(back); }
        internal int TestRatioIndex { get { return _ratioIdx; } }

        private void HideRatioToast() { if (_ratioToast != null) _ratioToast.Visibility = Visibility.Collapsed; }
    }
}
