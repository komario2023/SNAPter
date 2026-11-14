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
        // ================= the bottom panel as groups on lines, and the adaptive fit for a big region =================
        // The groups ("presets", "colors", "recent", "shape", "sliders", "extra", "actions", "actions2", "close") are put on lines as
        // layout.props_layout says. When the panels do not fit outside the region the program tries, in this order: fewer lines,
        // smaller panels, hiding the groups of layout.fit_hide_items - and at last puts them inside the region (corner_key moves them
        // from corner to corner).
        private readonly Dictionary<string, FrameworkElement> _propHosts = new Dictionary<string, FrameworkElement>();
        private StackPanel _propsCol;
        private int _panelCorner = -1;                        // 0 bottom right, 1 bottom left, 2 top left, 3 top right
        private bool _capsAtOpen;
        private long _capsFixTick;
        internal const string DefaultPropsLayout = "presets,colors,recent,shape,sliders,extra|copy,save,actions2,close";

        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        private static StackPanel Hst(params FrameworkElement[] kids)
        {
            StackPanel p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (FrameworkElement k in kids) p.Children.Add(k);
            return p;
        }

        private static int CornerIndex(string name)
        {
            switch (name) { case "bottom_left": return 1; case "top_left": return 2; case "top_right": return 3; default: return 0; }
        }

        private int PanelCorner { get { if (_panelCorner < 0) _panelCorner = CornerIndex(_settings.StartCorner); return _panelCorner; } }

        private void BuildPropsPanel()
        {
            _capsAtOpen = Keyboard.IsKeyToggled(Key.CapsLock);
            StackPanel presets = new StackPanel { Orientation = Orientation.Horizontal };
            StackPanel colors = new StackPanel { Orientation = Orientation.Horizontal };
            StackPanel shape = new StackPanel { Orientation = Orientation.Horizontal };
            if (_editor)
            {
                // presets 1 2 3 (keys 1, 2, 3): the remembered styles of the current tool
                for (int pi = 0; pi < _styles.PresetCount; pi++)
                {
                    int idx = pi;
                    Border pb = MakeButton((pi + 1).ToString(), false, delegate { SetPreset(idx); });
                    pb.Padding = new Thickness(9, 4, 9, 4);
                    pb.BorderThickness = new Thickness(1);
                    pb.BorderBrush = Brushes.Transparent;
                    pb.ToolTip = _i18n.T("tip.preset") + " " + (pi + 1) + " (" + (pi + 1) + ")";
                    _presetButtons.Add(pb);
                    presets.Children.Add(pb);
                }
                _swatchParts.Add(Separator(false));
                colors.Children.Add(_swatchParts[_swatchParts.Count - 1]);
                foreach (string hex in PaletteHex)
                {
                    WpfColor c = ParseColor(hex, Colors.Red);
                    Border sw = new Border();
                    sw.Width = 20; sw.Height = 20;
                    sw.CornerRadius = new CornerRadius(10);
                    sw.Background = Brush(c);
                    sw.BorderThickness = new Thickness(1);
                    sw.Margin = new Thickness(3, 6, 3, 6);
                    sw.Cursor = Cursors.Hand;
                    sw.ToolTip = _i18n.T("props.color");
                    WpfColor cc = c;
                    sw.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; SetColor(cc); };
                    _swatches.Add(new KeyValuePair<WpfColor, Border>(c, sw));
                    _swatchParts.Add(sw);
                    colors.Children.Add(sw);
                }
                // any color: the system color dialog; recently used custom colors stay next to it
                Border custom = MakeButton("+", false, PickCustomColor);
                custom.ToolTip = _i18n.T("props.custom_color");
                custom.Padding = new Thickness(8, 2, 8, 2);
                _swatchParts.Add(custom);
                colors.Children.Add(custom);
                _recentHost = new StackPanel { Orientation = Orientation.Horizontal };
                _swatchParts.Add(_recentHost);
                BuildSliderRow();

                _fillBtn = MakeButton("", false, CycleFill);
                _fillBtn.ToolTip = _i18n.T("props.fill");
                _roundBtn = MakeButton(_i18n.T("props.rounded"), false, ToggleRounded);
                _roundBtn.ToolTip = _i18n.T("props.rounded") + " (Ctrl)";
                _textStyleBtn = MakeButton("", false, CycleTextStyle);
                _textStyleBtn.ToolTip = _i18n.T("props.text_style");
                shape.Children.Add(_fillBtn);
                shape.Children.Add(_roundBtn);
                shape.Children.Add(_textStyleBtn);
            }

            // extra row: font, bold, italic, alignment for text; head style and size for arrows
            _extraRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            if (_editor)
            {
                _fontBtn = MakeButton("Aa", false, ShowFontMenu);
                _fontBtn.ToolTip = _i18n.T("tip.font");
                _boldBtn = MakeButton("B", false, ToggleBold); ((TextBlock)_boldBtn.Child).FontWeight = FontWeights.Bold;
                _boldBtn.ToolTip = _i18n.T("settings.text_bold");
                _italicBtn = MakeButton("I", false, ToggleItalic); ((TextBlock)_italicBtn.Child).FontStyle = FontStyles.Italic;
                _italicBtn.ToolTip = _i18n.T("settings.text_italic");
                _alignBtn = MakeButton("", false, CycleAlign);
                _alignBtn.ToolTip = _i18n.T("settings.text_align");
                _headBtn = MakeButton("", false, CycleHead);
                _headBtn.ToolTip = _i18n.T("settings.arrow_head");
                _headSizeBtn = MakeButton("", false, CycleHeadSize);
                _headSizeBtn.ToolTip = _i18n.T("tip.head_size");
                _headFillBtn = MakeButton("", false, ToggleHeadHollow);
                _headFillBtn.ToolTip = _i18n.T("tip.head_fill");
                foreach (Border eb in new Border[] { _fontBtn, _boldBtn, _italicBtn, _alignBtn, _headBtn, _headFillBtn, _headSizeBtn }) _extraRow.Children.Add(eb);
                // quick sizes of the brush, marker, line and eraser: big / medium / small as a percent of the region height
                for (int pi = 0; pi < 3; pi++)
                {
                    int which = pi;
                    Border pb = MakeButton("●", false, delegate { ApplyPercentSize(which); });
                    ((TextBlock)pb.Child).FontSize = pi == 0 ? 22 : pi == 1 ? 15 : 9;
                    pb.MinWidth = 34; pb.Padding = new Thickness(8, 0, 8, 0);
                    _pctBtns.Add(pb);
                    _extraRow.Children.Add(pb);
                }
            }

            // actions: "copy", "save", "actions2" (the rest), "close" - each can be left out of the layout (Ctrl+C, Ctrl+S and Esc do the same)
            Border copyBtn = MakeButton(_i18n.T("actions.copy"), true, DoCopy);
            copyBtn.ToolTip = WithKey(_i18n.T("actions.copy"), "copy");
            Border saveBtn = MakeButton(_i18n.T("actions.save"), false, DoSave);
            saveBtn.ToolTip = WithKey(_i18n.T("actions.save"), "save");
            StackPanel actCopy = Hst(copyBtn);
            StackPanel actSave = Hst(saveBtn);
            StackPanel act2 = new StackPanel { Orientation = Orientation.Horizontal };
            Border saveAs = MakeButton(_i18n.T("actions.save_as"), false, DoSaveAs);
            saveAs.ToolTip = WithKey(_i18n.T("actions.save_as"), "save_as");
            act2.Children.Add(saveAs);
            Border pin = MakeButton(_i18n.T("actions.pin"), false, DoPin);
            pin.ToolTip = WithKey(_i18n.T("actions.pin"), "pin");
            act2.Children.Add(pin);
            if (_editor) { _beautifyBtn = MakeButton(_i18n.T("actions.beautify"), false, ToggleBeautify); HookBeautifyLongPress(_beautifyBtn); _beautifyBtn.ToolTip = _i18n.T("tip.beautify"); act2.Children.Add(_beautifyBtn); UpdateBeautifyButton(); }
            if (UploadService.Enabled(_settings))
            {
                Border up = MakeButton(_i18n.T("actions.upload"), false, DoUpload);
                up.ToolTip = WithKey(_i18n.T("actions.upload"), "upload");
                act2.Children.Add(up);
            }
            if (_settings.RecordEditorButtons && _editor)
            {
                Border b1 = MakeButton("GIF", false, delegate { DoRecord("gif"); }); b1.ToolTip = _i18n.T("tip.rec_gif");
                Border b2 = MakeButton(_i18n.T("actions.video"), false, delegate { DoRecord("video"); }); b2.ToolTip = _i18n.T("tip.rec_video");
                Border b3 = MakeButton(_i18n.T("actions.scroll"), false, delegate { DoRecord("scroll"); }); b3.ToolTip = _i18n.T("tip.rec_scroll");
                act2.Children.Add(b1); act2.Children.Add(b2); act2.Children.Add(b3);
            }
            StackPanel close = Hst(MakeButton(_i18n.T("actions.close"), false, delegate { Close(); }));

            _propHosts.Clear();
            if (_editor)
            {
                _propHosts["presets"] = presets; _propHosts["colors"] = colors; _propHosts["recent"] = Hst(_recentHost);
                _propHosts["shape"] = shape; _propHosts["sliders"] = Hst(_sliderRow); _propHosts["extra"] = Hst(_extraRow);
            }
            _propHosts["copy"] = actCopy; _propHosts["save"] = actSave; _propHosts["actions2"] = act2; _propHosts["close"] = close;

            StackPanel col = new StackPanel { Orientation = Orientation.Vertical };
            _propsCol = new StackPanel { Orientation = Orientation.Vertical };
            _panelB = MakePanel(col, "props");
            if (_settings.PanelsDraggable) col.Children.Add(MakeGrip(_panelB, false, true));
            col.Children.Add(_propsCol);
            _canvas.Children.Add(_panelB);
            ArrangeProps(MergeLines(PropLines(), 0), double.PositiveInfinity);
        }

        // ---- lines ----
        internal static List<List<string>> ParsePropsLayout(string spec, ICollection<string> known)
        {
            HashSet<string> used = new HashSet<string>();
            List<List<string>> lines = new List<List<string>>();
            foreach (string ln in (string.IsNullOrWhiteSpace(spec) ? DefaultPropsLayout : spec).Split('|'))
            {
                List<string> l = new List<string>();
                foreach (string t in ln.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string k = t.Trim().ToLowerInvariant();
                    if (k == "actions" && !known.Contains("actions"))      // the old name of Copy + Save
                    {
                        foreach (string kk in new string[] { "copy", "save" }) if (known.Contains(kk) && used.Add(kk)) l.Add(kk);
                        continue;
                    }
                    if (known.Contains(k) && used.Add(k)) l.Add(k);
                }
                if (l.Count > 0) lines.Add(l);
            }
            return lines;          // any group can be left out, even all of them (the panel is then not shown): the keys do the same
        }

        private List<List<string>> PropLines() { return ParsePropsLayout(_settings.PropsLayout, _propHosts.Keys); }

        /// <summary>Fewer lines: the last (merge + 1) lines become one.</summary>
        private static List<List<string>> MergeLines(List<List<string>> lines, int merge)
        {
            if (merge <= 0 || lines.Count < 2) return lines;
            merge = Math.Min(merge, lines.Count - 1);
            List<List<string>> r = lines.Take(lines.Count - merge - 1).ToList();
            List<string> joined = new List<string>();
            foreach (List<string> l in lines.Skip(lines.Count - merge - 1)) joined.AddRange(l);
            r.Add(joined);
            return r;
        }

        private void ArrangeProps(List<List<string>> lines, double lineMaxW)
        {
            foreach (FrameworkElement h in _propHosts.Values) { Panel p = h.Parent as Panel; if (p != null) p.Children.Remove(h); }
            _propsCol.Children.Clear();
            foreach (List<string> l in lines)
            {
                WrapPanel wp = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = lineMaxW };
                foreach (string t in l) wp.Children.Add(_propHosts[t]);
                _propsCol.Children.Add(wp);
            }
        }

        private static HashSet<string> SplitList(string s)
        {
            HashSet<string> r = new HashSet<string>();
            foreach (string t in (s ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) r.Add(t.Trim().ToLowerInvariant());
            return r;
        }

        private List<double> FitScales()
        {
            List<double> r = new List<double> { 1.0 };
            if (_settings.FitShrink)
            {
                double min = Math.Max(0.5, Math.Min(1.0, _settings.FitMinScale / 100.0));
                for (double s = 0.92; s > min + 0.001; s -= 0.08) r.Add(s);
                if (min < 0.999) r.Add(min);
            }
            return r;
        }

        // What drops out of the bottom panel first when the room is short (only the groups allowed in layout.fit_hide_items): the rarely used
        // buttons first, the drawing aids (colors, presets) last. "actions" (copy, save) and "close" never drop out.
        private static readonly string[] HidePriority = { "recent", "actions2", "extra", "shape", "sliders", "presets", "colors" };

        private List<string> HideOrder()
        {
            List<string> r = new List<string>();
            if (!_settings.FitHide) return r;
            HashSet<string> allowed = SplitList(_settings.FitHideItems);
            foreach (string g in HidePriority) if (allowed.Contains(g)) r.Add(g);
            foreach (string g in allowed) if (!r.Contains(g) && g != "copy" && g != "save" && g != "actions" && g != "close") r.Add(g);
            return r;
        }

        private static HashSet<string> FirstGroups(List<string> order, int k) { return new HashSet<string>(order.Take(k)); }

        /// <summary>Sets the lines, the visibility of the groups (those in "hidden" are collapsed) and the scale of the bottom panel and measures it.</summary>
        private void ApplyProps(List<List<string>> lines, int merge, double scale, HashSet<string> hidden, double availW)
        {
            foreach (KeyValuePair<string, FrameworkElement> kv in _propHosts)
                kv.Value.Visibility = (hidden.Contains(kv.Key) && kv.Key != "copy" && kv.Key != "save" && kv.Key != "close") ? Visibility.Collapsed : Visibility.Visible;
            double over = _panelB.Padding.Left + _panelB.Padding.Right + _panelB.BorderThickness.Left + _panelB.BorderThickness.Right + 6;
            double lineW = double.IsInfinity(availW) ? double.PositiveInfinity : Math.Max(80, availW / scale - over);
            ArrangeProps(MergeLines(lines, merge), lineW);
            _panelB.LayoutTransform = scale >= 0.999 ? Transform.Identity : new ScaleTransform(scale, scale);
            // WPF keeps the old measure of the panel while only its children changed (hidden groups, other lines): without this reset every
            // variant was measured as the first one, and the panel on the screen was bigger than what was fitted (it ran over the screen edge)
            for (UIElement e = _propsCol; e != null; e = VisualTreeHelper.GetParent(e) as UIElement) { e.InvalidateMeasure(); if (e == _panelB) break; }   // the whole chain up to the panel
            _panelB.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        // ---- where is room ----
        /// <summary>The monitor of the region, in canvas coordinates: the panels stay on it.</summary>
        private Rect WorkArea()
        {
            try
            {
                System.Drawing.Point c = new System.Drawing.Point(_bounds.X + (int)(_sel.X + _sel.Width / 2), _bounds.Y + (int)(_sel.Y + _sel.Height / 2));
                System.Drawing.Rectangle b = System.Windows.Forms.Screen.FromPoint(c).Bounds;
                return new Rect(b.X - _bounds.X, b.Y - _bounds.Y, b.Width, b.Height);
            }
            catch { return new Rect(0, 0, _canvas.Width, _canvas.Height); }
        }

        private static bool InsideWork(Rect r, Rect work)
        {
            return r.Left >= work.Left - 0.5 && r.Right <= work.Right + 0.5 && r.Top >= work.Top - 0.5 && r.Bottom <= work.Bottom + 0.5;
        }

        // ---- the tool panel ----
        private readonly List<TextBlock> _toolLabelBlocks = new List<TextBlock>();

        /// <summary>The text labels beside the tool icons are shown only while the whole panel with them stands outside the region, on the screen;
        /// when there is no room (the panel would shrink or go inside the picture) the labels are hidden and the narrow panel is placed instead.</summary>
        private Rect LayoutToolsPanel()
        {
            if (_toolLabelBlocks.Count == 0 || _userPosA.HasValue || _preview) {   // the settings preview always shows the panels in full, with their labels
 foreach (TextBlock tbk in _toolLabelBlocks) tbk.Visibility = Visibility.Visible; return LayoutToolsPanelCore(); }
            foreach (TextBlock tbk in _toolLabelBlocks) tbk.Visibility = Visibility.Visible;
            _panelA.InvalidateMeasure(); _panelA.UpdateLayout();      // never measure the labelled panel from a stale layout: that cut the words
            Rect r = LayoutToolsPanelCore();
            Rect whole = new Rect(0, 0, _canvas.Width, _canvas.Height);
            // the labels stay only when the whole labelled panel is at scale 1, outside the region, inside the screen and inside the canvas
            bool roomy = _panelA.LayoutTransform == Transform.Identity && !r.IntersectsWith(_sel) && InsideWork(r, WorkArea()) && InsideWork(r, whole);
            if (!roomy)
            {
                foreach (TextBlock tbk in _toolLabelBlocks) tbk.Visibility = Visibility.Collapsed;
                _panelA.InvalidateMeasure(); _panelA.UpdateLayout();
                r = LayoutToolsPanelCore();
            }
            return r;
        }

        private Rect LayoutToolsPanelCore()
        {
            string side = _settings.ToolbarPosition;
            if (_userPosA.HasValue) { _panelA.LayoutTransform = Transform.Identity; return PlaceAt(_panelA, _userPosA.Value); }
            if (!_settings.FitEnabled) { _panelA.LayoutTransform = Transform.Identity; return PlacePanel(_panelA, side, Rect.Empty); }
            Rect work = WorkArea();
            foreach (double sc in FitScales())
            {
                _panelA.LayoutTransform = sc >= 0.999 ? Transform.Identity : new ScaleTransform(sc, sc);
                Rect r = PlacePanel(_panelA, side, Rect.Empty);
                if (!r.IntersectsWith(_sel) && InsideWork(r, work)) return r;
            }
            if (_settings.FitFallback != "inside") { _panelA.LayoutTransform = Transform.Identity; return PlacePanel(_panelA, side, Rect.Empty); }
            // inside the region: as large as fits, at the side of the toolbar, in the half opposite to the bottom panel
            foreach (double sc in FitScales())
            {
                _panelA.LayoutTransform = sc >= 0.999 ? Transform.Identity : new ScaleTransform(sc, sc);
                _panelA.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (_panelA.DesiredSize.Height <= _sel.Height - 20 && _panelA.DesiredSize.Width <= _sel.Width - 20) break;
            }
            double m = 10, w = _panelA.DesiredSize.Width, h = _panelA.DesiredSize.Height;
            double x = side == "right" ? _sel.Right - w - m : _sel.Left + m;
            double y = PanelCorner <= 1 ? _sel.Top + m : _sel.Bottom - h - m;
            Canvas.SetLeft(_panelA, x); Canvas.SetTop(_panelA, y);
            return new Rect(x, y, w, h);
        }

        // ---- the bottom panel ----
        private void LayoutPropsPanel(Rect a)
        {
            string side = _settings.PropertiesPosition == "top" ? "top" : "bottom";
            Rect work = WorkArea();
            HashSet<string> none = new HashSet<string>();
            if (PropLines().Count == 0) { _panelB.Visibility = Visibility.Collapsed; return; }     // the layout has no groups at all: no bottom panel
            if (_userPosB.HasValue) { ApplyProps(PropLines(), 0, 1, none, work.Width - 8); PlaceAt(_panelB, _userPosB.Value); return; }
            List<List<string>> lines = PropLines();
            if (!_settings.FitEnabled) { ApplyProps(lines, 0, 1, none, double.PositiveInfinity); PlacePanel(_panelB, side, a); return; }
            double availW = work.Width - 8;
            // the order (to keep the panel OUTSIDE the region as long as possible, with as much on it as possible): the whole panel in fewer lines,
            // then smaller (down to fit_min_scale), and only then the groups drop out one by one (the rarely used buttons first, the colors last),
            // each time again with the smaller scales. Only when nothing fits the panel goes inside the region.
            List<string> order = HideOrder();
            List<double> scales = FitScales();
            for (int k = 0; k <= order.Count; k++)
            {
                HashSet<string> hid = FirstGroups(order, k);
                foreach (double sc in scales)
                    for (int m = 0; m < lines.Count; m++)
                    {
                        ApplyProps(lines, m, sc, hid, availW);
                        Rect r = PlacePanel(_panelB, side, a);
                        double nx = Math.Max(work.Left + 4, Math.Min(work.Right - r.Width - 4, r.X));
                        if (Math.Abs(nx - r.X) > 0.5) { Canvas.SetLeft(_panelB, nx); r = new Rect(nx, r.Y, r.Width, r.Height); }
                        if (!r.IntersectsWith(_sel) && (a.IsEmpty || !r.IntersectsWith(a)) && InsideWork(r, work)) return;   // it fits outside
                    }
            }
            if (_settings.FitFallback != "inside") { ApplyProps(lines, 0, 1, none, double.PositiveInfinity); PlacePanel(_panelB, side, a); return; }
            PlacePropsInside(lines, a);
        }

        /// <summary>No room outside: the panel goes into a corner of the region, as LOW as possible (the least of the picture is covered):
        /// the lowest of all variants (lines, scale, dropped groups) that fits the width; at the same height the one with more on it.</summary>
        private void PlacePropsInside(List<List<string>> lines, Rect a)
        {
            // the width is that of the screen, as for the outside variants (not of the region: a narrow region used to strip the panel to three buttons);
            // a panel wider than the region hangs over its sides, centered on it
            Rect work = WorkArea();
            double m = 10, availW = Math.Max(120, work.Width - 8);
            List<string> order = HideOrder();
            // the variants in the order of "the most on the panel first" (the fewest dropped groups, the largest scale); the panel takes the first one
            // that is not more than 8 px taller than the lowest possible - dropping groups to gain a few pixels of height is not worth it
            List<double[]> cand = new List<double[]>();        // k, scale, lines, height
            double minH = double.MaxValue;
            for (int k = 0; k <= order.Count; k++)
            {
                HashSet<string> hid = FirstGroups(order, k);
                foreach (double sc in FitScales())
                    for (int mm = 0; mm < lines.Count; mm++)
                    {
                        ApplyProps(lines, mm, sc, hid, availW);
                        Size s = _panelB.DesiredSize;
                        if (s.Width > availW + 0.5) continue;
                        cand.Add(new double[] { k, sc, mm, s.Height });
                        if (s.Height < minH) minH = s.Height;
                    }
            }
            int bestK = -1, bestM = -1; double bestSc = 1;
            foreach (double[] cd in cand)
                if (cd[3] <= minH + 8) { bestK = (int)cd[0]; bestSc = cd[1]; bestM = (int)cd[2]; break; }
            if (bestK >= 0) ApplyProps(lines, bestM, bestSc, FirstGroups(order, bestK), availW);
            else ApplyProps(lines, lines.Count - 1, FitScales().Last(), FirstGroups(order, order.Count), availW);
            double w = _panelB.DesiredSize.Width, h = _panelB.DesiredSize.Height;
            int c = PanelCorner;
            bool wide = w > _sel.Width - 2 * m;
            double x = wide ? _sel.Left + (_sel.Width - w) / 2 : ((c == 0 || c == 3) ? _sel.Right - w - m : _sel.Left + m);
            if (wide) x = Math.Max(work.Left + 4, Math.Min(work.Right - w - 4, x));
            double y = c <= 1 ? _sel.Bottom - h - m : _sel.Top + m;
            Rect r = new Rect(x, y, w, h);
            if (!wide && !a.IsEmpty && r.IntersectsWith(a)) { double nx = (c == 0 || c == 3) ? _sel.Left + m : _sel.Right - w - m; r = new Rect(nx, y, w, h); }
            Canvas.SetLeft(_panelB, r.X); Canvas.SetTop(_panelB, r.Y);
        }

        // ---- the corner key ----
        private bool IsCornerKey(Key key)
        {
            string n = _settings.CornerKey;
            if (string.IsNullOrWhiteSpace(n) || n.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
            n = n.Trim();
            bool wantShift = n.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase);       // "Shift+Tab": Tab with Shift held
            if (wantShift) n = n.Substring(6);
            Key k;
            if (!Enum.TryParse(n, true, out k) || k != key) return false;
            return !wantShift || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        }

        /// <summary>True when the panels move on Shift+Tab (then Shift+Tab does not walk back through the aspect ratios).</summary>
        private bool CornerIsShiftTab { get { return string.Equals((_settings.CornerKey ?? "").Trim(), "Shift+Tab", StringComparison.OrdinalIgnoreCase); } }

        /// <summary>The panels inside the region go to the next corner, clockwise from the bottom right: bottom left, top left, top right.</summary>
        private void CyclePanelCorner(bool fromKey)
        {
            _panelCorner = (PanelCorner + 1) % 4;
            _userPosA = null; _userPosB = null; _userPosC = null;
            if (_panelsBuilt && _panelB != null && _panelB.Visibility == Visibility.Visible) LayoutPanels();
            if (fromKey) FixCapsToggle();
        }

        /// <summary>CapsLock was pressed for the corner, not for capital letters: put the Caps state back as it was when the overlay opened.</summary>
        private void FixCapsToggle()
        {
            try
            {
                if (_settings.CornerKey == null || !_settings.CornerKey.Trim().Equals("CapsLock", StringComparison.OrdinalIgnoreCase)) return;
                if (Keyboard.IsKeyToggled(Key.CapsLock) == _capsAtOpen) return;
                _capsFixTick = Environment.TickCount64;
                keybd_event(0x14, 0x3A, 0, UIntPtr.Zero);
                keybd_event(0x14, 0x3A, 2, UIntPtr.Zero);
            }
            catch (Exception ex) { Log.Write("caps fix error: " + ex.Message); }
        }

        private bool HandleCornerKey(Key key)
        {
            if (!_editor || !_panelsBuilt || !IsCornerKey(key)) return false;
            if (Environment.TickCount64 - _capsFixTick > 400) CyclePanelCorner(true);   // the key press we made ourselves is ignored
            return true;
        }

        // ---- self test hooks ----
        internal Rect TestWorkArea { get { return WorkArea(); } }
        internal int TestCorner { get { return PanelCorner; } }
        internal void TestCycleCorner() { CyclePanelCorner(false); }
        internal Rect TestPanelRectA { get { return PanelRect(_panelA); } }
    }
}
