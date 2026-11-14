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
        // ================= panels =================
        /// <summary>kind: "tool" (the tool panel), "props" (the bottom panel) or null (the color picker): the look follows the settings
        /// (layout.tool_panel / layout.props_panel: fill, opacity, border, corner radius, shadow).</summary>
        private Border MakePanel(FrameworkElement child, string kind = null)
        {
            bool fill = true, border = true, shadow = true; int opacity = 100, radius = -1;
            if (kind == "tool") { fill = _settings.ToolPanelFill; border = _settings.ToolPanelBorder; shadow = _settings.ToolPanelShadow; opacity = _settings.ToolPanelOpacity; radius = _settings.ToolPanelRadius; }
            else if (kind == "props") { fill = _settings.PropsPanelFill; border = _settings.PropsPanelBorder; shadow = _settings.PropsPanelShadow; opacity = _settings.PropsPanelOpacity; radius = _settings.PropsPanelRadius; }
            Border b = new Border();
            b.Child = child;
            WpfColor sf = _theme.Surface;
            b.Background = fill ? new SolidColorBrush(WpfColor.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(100, opacity)) / 100.0), sf.R, sf.G, sf.B)) : (Brush)Brushes.Transparent;
            b.BorderBrush = new SolidColorBrush(_theme.Border);
            b.BorderThickness = new Thickness(border ? 1 : 0);
            b.CornerRadius = new CornerRadius(radius >= 0 ? radius : _theme.RadiusMd);
            b.Padding = new Thickness(_settings.PanelPad);          // layout.panel_pad: the inner padding of the panel
            b.Visibility = Visibility.Collapsed;
            if (shadow) b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.25, Color = Colors.Black };
            return b;
        }

        private void StyleIcon(Border b, bool hover)
        {
            bool active = b.Tag is bool && (bool)b.Tag;
            b.Background = (active || hover) ? Brush(_theme.SurfaceMuted) : Brushes.Transparent;
            b.BorderBrush = active ? Brush(_theme.Accent) : Brushes.Transparent;
        }

        /// <summary>Button size from the settings (layout.button_size).</summary>
        private double BtnSize { get { if (_settings.ButtonPx > 0) return Math.Max(22, Math.Min(64, _settings.ButtonPx)); return _settings.ButtonSize == "small" ? 28 : _settings.ButtonSize == "large" ? 42 : 34; } }

        private Border IconButton(FrameworkElement icon, string tip, Action click, string label = null)
        {
            Border b = new Border();
            b.Height = BtnSize;
            if (label == null) b.Width = BtnSize; else b.MinWidth = BtnSize;
            b.Margin = new Thickness(_settings.ButtonGap);           // layout.button_gap
            b.CornerRadius = new CornerRadius(Math.Max(4, _theme.RadiusMd - 4));
            b.BorderThickness = new Thickness(1);
            b.Cursor = Cursors.Hand;
            b.ToolTip = tip;
            b.Tag = false;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            double iconPx = 24;
            if (_settings.IconFill > 0)
            {
                // layout.icon_fill: a big icon in a small button - the panel stays thin
                iconPx = Math.Max(14, BtnSize * _settings.IconFill / 100.0);
                icon = new Viewbox { Width = iconPx, Height = iconPx, Stretch = Stretch.Uniform, Child = icon, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            if (label == null) b.Child = icon;
            else
            {
                // icon and a text label (layout.tool_labels)
                StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 10, 0) };
                sp.Children.Add(new Border { Width = Math.Max(24, iconPx), Child = icon, HorizontalAlignment = HorizontalAlignment.Left });   // one icon column: every label starts at the same x
                TextBlock labelBlock = new TextBlock { Text = label, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = Brush(_theme.Text) };
                _toolLabelBlocks.Add(labelBlock);   // the labels are hidden when there is no room for them (RegionOverlay.Fit.cs)
                sp.Children.Add(labelBlock);
                b.Child = sp;
            }
            StyleIcon(b, false);
            b.MouseEnter += delegate { StyleIcon(b, true); };
            b.MouseLeave += delegate { StyleIcon(b, false); };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        private Border MakeButton(string text, bool primary, Action onClick)
        {
            Brush normal = primary ? Brush(_theme.Accent) : Brushes.Transparent;
            Brush hover = primary ? Brush(_theme.AccentHover) : Brush(_theme.SurfaceMuted);
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.FontFamily = new FontFamily("Segoe UI");
            tb.FontSize = 13;
            tb.FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = Brush(primary ? _theme.AccentText : _theme.Text);
            tb.VerticalAlignment = VerticalAlignment.Center;
            Border b = new Border();
            b.Child = tb;
            b.Background = normal;
            b.CornerRadius = new CornerRadius(Math.Max(4, _theme.RadiusMd - 4));
            b.Padding = new Thickness(12, 6, 12, 6);
            b.Margin = new Thickness(2);
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { b.Background = hover; };
            b.MouseLeave += delegate { b.Background = normal; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; onClick(); };
            return b;
        }

        private FrameworkElement Separator(bool horizontalLine)
        {
            Rectangle r = new Rectangle();
            r.Fill = Brush(_theme.Border);
            if (horizontalLine) { r.Height = 1; r.Margin = new Thickness(4, 4, 4, 4); }
            else { r.Width = 1; r.Margin = new Thickness(6, 4, 6, 4); }
            return r;
        }

        private TextBlock GlyphText(string text, double size, FontWeight weight)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI");
            t.FontSize = size;
            t.FontWeight = weight;
            t.Foreground = Brush(_theme.Text);
            return t;
        }

        private FrameworkElement SelectIcon()
        {
            Path p = new Path();
            p.Data = Geometry.Parse("M 4,2 L 4,16 L 7.5,12.8 L 10,18 L 12.2,17 L 9.8,11.8 L 14.5,11.8 Z");
            p.Fill = Brush(_theme.Text);
            p.Width = 18; p.Height = 20;
            p.Stretch = Stretch.None;
            return p;
        }

        private FrameworkElement ArrowIcon()
        {
            Path p = new Path();
            p.Data = Geometry.Parse("M 3,16 L 15,4 M 7,4 L 15,4 L 15,12");
            p.Stroke = Brush(_theme.Text);
            p.StrokeThickness = 2;
            p.StrokeLineJoin = PenLineJoin.Round;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.Width = 20; p.Height = 20;
            p.Stretch = Stretch.None;
            return p;
        }

        /// <summary>The drawing tools in the order of the settings (layout.tool_order), missing ones at the end.</summary>
        private List<EditorTool> OrderedTools()
        {
            EditorTool[] all = { EditorTool.Arrow, EditorTool.Line, EditorTool.Rect, EditorTool.Ellipse, EditorTool.Brush, EditorTool.Marker,
                EditorTool.Text, EditorTool.Steps, EditorTool.Blur, EditorTool.Fill, EditorTool.Spotlight, EditorTool.Magnifier, EditorTool.Eraser, EditorTool.Picker };
            List<EditorTool> res = new List<EditorTool>();
            if (!string.IsNullOrWhiteSpace(_settings.ToolOrder))
                foreach (string part in _settings.ToolOrder.Split(','))
                    foreach (EditorTool t in all)
                        if (StyleStore.Key(t) == part.Trim().ToLowerInvariant() && !res.Contains(t)) res.Add(t);
            foreach (EditorTool t in all) if (!res.Contains(t)) res.Add(t);
            return res;
        }

        /// <summary>The key of a tool as shown in tooltips (from editor_keys).</summary>
        private string KeyOf(EditorTool t)
        {
            foreach (KeyValuePair<Key, EditorTool> kv in _toolKeys)
                if (kv.Value == t) { string kn = kv.Key.ToString(); if (kn.Length == 2 && kn[0] == 'D' && char.IsDigit(kn[1])) kn = kn.Substring(1); return kn; }
            return ToolLetter(t);
        }

        private Border _panelC;                 // directions and rotation of the selected arrow
        private Point? _userPosC;

        private void BuildArrowPanel()
        {
            Grid dirs = new Grid();
            for (int i = 0; i < 3; i++) { dirs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); dirs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            // row, column, glyph, dx, dy, tip key
            object[][] cells =
            {
                new object[] { 0, 0, "\u2196", -1, -1, "tip.dir.nw" }, new object[] { 0, 1, "\u2191", 0, -1, "tip.dir.n" }, new object[] { 0, 2, "\u2197", 1, -1, "tip.dir.ne" },
                new object[] { 1, 0, "\u2190", -1, 0, "tip.dir.w" },  new object[] { 1, 2, "\u2192", 1, 0, "tip.dir.e" },
                new object[] { 2, 0, "\u2199", -1, 1, "tip.dir.sw" }, new object[] { 2, 1, "\u2193", 0, 1, "tip.dir.s" },  new object[] { 2, 2, "\u2198", 1, 1, "tip.dir.se" }
            };
            foreach (object[] c in cells)
            {
                int dx = (int)c[3], dy = (int)c[4];
                Border b = IconButton(GlyphText((string)c[2], 18, FontWeights.Normal), _i18n.T((string)c[5]), delegate { SetArrowDirection(dx, dy); });
                Grid.SetRow(b, (int)c[0]); Grid.SetColumn(b, (int)c[1]);
                dirs.Children.Add(b);
            }
            Border flip = IconButton(GlyphText("\u21C4", 16, FontWeights.Normal), _i18n.T("tip.flip"), FlipArrow);
            Grid.SetRow(flip, 1); Grid.SetColumn(flip, 1);
            dirs.Children.Add(flip);

            StackPanel rot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            rot.Children.Add(IconButton(GlyphText("\u21BA", 18, FontWeights.Normal), _i18n.T("tip.rotate_ccw") + " " + _settings.RotateStep.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "\u00B0", delegate { RotateArrow(-_settings.RotateStep); }));
            rot.Children.Add(IconButton(GlyphText("\u21BB", 18, FontWeights.Normal), _i18n.T("tip.rotate_cw") + " " + _settings.RotateStep.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "\u00B0", delegate { RotateArrow(_settings.RotateStep); }));

            StackPanel col = new StackPanel();
            _panelC = MakePanel(col, "tool");
            if (_settings.PanelsDraggable) col.Children.Add(MakeGrip(_panelC, null, true));
            col.Children.Add(dirs);
            col.Children.Add(new Border { Height = 1, Background = Brush(_theme.Border), Margin = new Thickness(4, 4, 4, 4) });
            col.Children.Add(rot);
            _canvas.Children.Add(_panelC);
        }

        /// <summary>The point where the ray from origin towards target leaves the region (target itself when it is inside).</summary>
        private Point FitInSel(Point origin, Point target)
        {
            if (_sel.Contains(target)) return target;
            Vector d = target - origin;
            double t = 1;
            if (d.X > 1e-9) t = Math.Min(t, (_sel.Right - origin.X) / d.X);
            else if (d.X < -1e-9) t = Math.Min(t, (_sel.Left - origin.X) / d.X);
            if (d.Y > 1e-9) t = Math.Min(t, (_sel.Bottom - origin.Y) / d.Y);
            else if (d.Y < -1e-9) t = Math.Min(t, (_sel.Top - origin.Y) / d.Y);
            t = Math.Max(0, t);
            return origin + d * t;
        }

        /// <summary>The selected arrow points in one of the eight directions; its start and its length stay.</summary>
        private void SetArrowDirection(int dx, int dy)
        {
            ArrowAnnotation ar = _selected as ArrowAnnotation;
            if (ar == null || (dx == 0 && dy == 0)) return;
            Vector v = new Vector(dx, dy);
            v.Normalize();
            double len = Math.Max(ar.Length, 24);
            Point nb = FitInSel(ar.A, ar.A + v * len);
            if ((nb - ar.A).Length < 4) return;
            ar.B = nb;
            Commit();
            RebuildAnnotations();
        }

        /// <summary>The selected arrow turns around its middle by the given degrees (positive: clockwise).</summary>
        private void RotateArrow(double degrees)
        {
            ArrowAnnotation ar = _selected as ArrowAnnotation;
            if (ar == null) return;
            Point m = new Point((ar.A.X + ar.B.X) / 2, (ar.A.Y + ar.B.Y) / 2);
            double rad = degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            Func<Point, Point> turn = delegate (Point p)
            {
                double x = p.X - m.X, y = p.Y - m.Y;
                return ClampToSel(new Point(m.X + x * cos - y * sin, m.Y + x * sin + y * cos));
            };
            ar.A = turn(ar.A); ar.B = turn(ar.B);
            Commit();
            RebuildAnnotations();
        }

        private void FlipArrow()
        {
            ArrowAnnotation ar = _selected as ArrowAnnotation;
            if (ar == null) return;
            Point t = ar.A; ar.A = ar.B; ar.B = t;
            Commit();
            RebuildAnnotations();
        }

        private static string ToolLetter(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Arrow: return "A";
                case EditorTool.Line: return "L";
                case EditorTool.Rect: return "R";
                case EditorTool.Ellipse: return "O";
                case EditorTool.Brush: return "B";
                case EditorTool.Marker: return "M";
                case EditorTool.Text: return "T";
                case EditorTool.Steps: return "N";
                case EditorTool.Blur: return "U";
                case EditorTool.Eraser: return "E";
                case EditorTool.Picker: return "I";
                default: return "V";
            }
        }

        private Path StrokeIcon(string data, double thickness, double alpha)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(data);
            WpfColor tc = _theme.Text;
            p.Stroke = Brush(WpfColor.FromArgb((byte)(255 * alpha), tc.R, tc.G, tc.B));
            p.StrokeThickness = thickness;
            p.StrokeLineJoin = PenLineJoin.Round;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.Width = 20; p.Height = 20;
            p.Stretch = Stretch.None;
            return p;
        }

        private FrameworkElement ToolIcon(EditorTool t)
        {
            switch (t)
            {
                case EditorTool.Arrow: return ArrowIcon();
                case EditorTool.Line: return StrokeIcon("M 3,17 L 17,3", 2, 1);
                case EditorTool.Rect: return StrokeIcon("M 3,5 L 17,5 L 17,15 L 3,15 Z", 2, 1);
                case EditorTool.Ellipse: return StrokeIcon("M 3,10 A 7,5.5 0 1 1 17,10 A 7,5.5 0 1 1 3,10 Z", 2, 1);
                case EditorTool.Brush: return StrokeIcon("M 3,15 C 5,6 8,17 11,9 S 15,6 17,4", 2.2, 1);
                case EditorTool.Marker: return StrokeIcon("M 4,14 L 16,6", 7, 0.5);
                case EditorTool.Text: return GlyphText("T", 18, FontWeights.Bold);
                case EditorTool.Steps:
                    {
                        Border c = new Border();
                        c.Width = 20; c.Height = 20;
                        c.CornerRadius = new CornerRadius(10);
                        c.BorderBrush = Brush(_theme.Text);
                        c.BorderThickness = new Thickness(2);
                        TextBlock n = GlyphText("1", 12, FontWeights.Bold);
                        n.HorizontalAlignment = HorizontalAlignment.Center;
                        n.VerticalAlignment = VerticalAlignment.Center;
                        c.Child = n;
                        return c;
                    }
                case EditorTool.Blur:
                    {
                        Path p = new Path();
                        p.Data = Geometry.Parse("M 3,3 H 8 V 8 H 3 Z M 12,3 H 17 V 8 H 12 Z M 7.5,7.5 H 12.5 V 12.5 H 7.5 Z M 3,12 H 8 V 17 H 3 Z M 12,12 H 17 V 17 H 12 Z");
                        p.Fill = Brush(_theme.Text);
                        p.Width = 20; p.Height = 20;
                        p.Stretch = Stretch.None;
                        return p;
                    }
                case EditorTool.Fill: return StrokeIcon("M 4,10 L 10,4 L 16,10 L 10,16 Z M 16,12 C 17.5,14 17.5,16 16,16 C 14.5,16 14.5,14 16,12 Z", 1.8, 1);
                case EditorTool.Spotlight: return StrokeIcon("M 2,2 H 18 V 18 H 2 Z M 6,6 H 14 V 14 H 6 Z", 1.6, 1);
                case EditorTool.Magnifier: return StrokeIcon("M 8.5,3 A 5.5,5.5 0 1 1 8.5,14 A 5.5,5.5 0 1 1 8.5,3 Z M 12.6,12.6 L 17,17", 1.9, 1);
                case EditorTool.Eraser: return StrokeIcon("M 3,13 L 11,4 L 17,9 L 10,17 L 6,17 Z M 7,8 L 13,13", 1.8, 1);
                case EditorTool.Picker: return StrokeIcon("M 14,3 L 17,6 L 8,15 L 4,16 L 5,12 L 14,3 Z M 11,6 L 14,9", 1.8, 1);
                default: return SelectIcon();
            }
        }

        private void BuildPanels()
        {
            BuildPropsPanel();                                      // panel B: the groups on lines, see RegionOverlay.Fit.cs

            if (_editor)
            {
                bool vertical = _settings.ToolbarPosition == "left" || _settings.ToolbarPosition == "right";
                StackPanel a = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };

                Border sel = IconButton(SelectIcon(), _i18n.T("tools.select") + " (" + KeyOf(EditorTool.Select) + ")", delegate { SetTool(EditorTool.Select); },
                    (_settings.ToolLabels && vertical) ? _i18n.T("tools.select") : null);
                _toolButtons[EditorTool.Select] = sel;
                a.Children.Add(sel);
                foreach (EditorTool tl in OrderedTools())
                {
                    if (!_settings.ToolEnabled(StyleStore.Key(tl))) continue;
                    EditorTool ct = tl;
                    Border tb = IconButton(ToolIcon(tl), _i18n.T("tools." + StyleStore.Key(tl)) + " (" + KeyOf(tl) + ")", delegate { SetTool(ct); },
                        (_settings.ToolLabels && vertical) ? _i18n.T("tools." + StyleStore.Key(tl)) : null);
                    _toolButtons[tl] = tb;
                    a.Children.Add(tb);
                }
                a.Children.Add(Separator(vertical));
                _undoBtn = IconButton(GlyphText("↶", 18, FontWeights.Normal), _i18n.T("actions.undo") + " (Ctrl+Z)", Undo);
                _redoBtn = IconButton(GlyphText("↷", 18, FontWeights.Normal), _i18n.T("actions.redo") + " (Ctrl+Y)", Redo);
                a.Children.Add(_undoBtn);
                a.Children.Add(_redoBtn);
                _panelA = MakePanel(a, "tool");
                if (_settings.PanelsDraggable) a.Children.Insert(0, MakeGrip(_panelA, true, vertical));
                _canvas.Children.Add(_panelA);

                // the arrow panel: appears next to the tool panel while an arrow is selected - eight directions, flip, rotate
                if (_settings.ArrowPanel) BuildArrowPanel();
            }

            _panelsBuilt = true;
            RefreshPanels();
        }

        private void RefreshPanels()
        {
            if (!_panelsBuilt) return;
            foreach (KeyValuePair<EditorTool, Border> kv in _toolButtons)
            {
                kv.Value.Tag = (kv.Key == _tool);
                StyleIcon(kv.Value, false);
            }
            WpfColor active = ActiveColor();
            foreach (KeyValuePair<WpfColor, Border> sw in _swatches)
            {
                bool on = sw.Key == active;
                sw.Value.BorderBrush = on ? Brush(_theme.Accent) : Brush(_theme.Border);
                sw.Value.BorderThickness = new Thickness(on ? 3 : 1);
            }
            if (_undoBtn != null) _undoBtn.Opacity = _hi > 0 ? 1.0 : 0.35;
            if (_redoBtn != null) _redoBtn.Opacity = _hi < _hist.Count - 1 ? 1.0 : 0.35;

            // what the properties panel shows depends on the current tool or the selected object
            if (_editor)
            {
                EditorTool tool = StyleTool();
                ToolStyle st = _styles.Current(tool);
                int act = _styles.Active(tool);
                for (int i = 0; i < _presetButtons.Count; i++)
                {
                    bool on = i == act;
                    _presetButtons[i].Background = on ? Brush(_theme.SurfaceMuted) : Brushes.Transparent;
                    _presetButtons[i].BorderBrush = on ? Brush(_theme.Accent) : Brushes.Transparent;
                    // the presets belong to a drawing tool (with the arrow-cursor tool and nothing selected: the tool used last) - the hint says which
                    _presetButtons[i].ToolTip = _i18n.T("tip.preset") + " " + (i + 1) + " (" + (i + 1) + ") - " + _i18n.T("tip.preset_for") + ": " + _i18n.T("tools." + StyleStore.Key(tool));
                }
                bool colors = tool != EditorTool.Blur;
                foreach (FrameworkElement fe in _swatchParts) fe.Visibility = colors ? Visibility.Visible : Visibility.Collapsed;
                bool shape = tool == EditorTool.Rect || tool == EditorTool.Ellipse;
                _fillBtn.Visibility = shape ? Visibility.Visible : Visibility.Collapsed;
                _roundBtn.Visibility = tool == EditorTool.Rect ? Visibility.Visible : Visibility.Collapsed;
                _textStyleBtn.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
                ((TextBlock)_fillBtn.Child).Text = _i18n.T("fill." + st.Fill);
                ((TextBlock)_textStyleBtn.Child).Text = _i18n.T("textstyle." + st.TextStyle);
                // the extra row: text (font, bold, italic, alignment) or arrow (head and its size)
                bool isText = tool == EditorTool.Text, isArrow = tool == EditorTool.Arrow;
                bool pctOn = _tool == EditorTool.Eraser || tool == EditorTool.Brush || tool == EditorTool.Marker || tool == EditorTool.Line;
                _extraRow.Visibility = (isText || isArrow || pctOn) ? Visibility.Visible : Visibility.Collapsed;
                foreach (Border eb in new Border[] { _fontBtn, _boldBtn, _italicBtn, _alignBtn }) eb.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
                foreach (Border eb in new Border[] { _headBtn, _headSizeBtn }) eb.Visibility = isArrow ? Visibility.Visible : Visibility.Collapsed;
                // fill of the head matters for the triangle and the dot (the open head is only strokes)
                bool headHasFill = isArrow && (string.IsNullOrEmpty(st.HeadStyle) ? ArrowAnnotation.HeadStyle : st.HeadStyle) != "open";
                _headFillBtn.Visibility = headHasFill ? Visibility.Visible : Visibility.Collapsed;
                ((TextBlock)_headFillBtn.Child).Text = _i18n.T(st.HeadHollow ? "head.fill_off" : "head.fill_on");
                if (isText)
                {
                    string fn = string.IsNullOrWhiteSpace(st.FontName) ? TextAnnotation.FontName : st.FontName;
                    ((TextBlock)_fontBtn.Child).Text = fn.Length > 14 ? fn.Substring(0, 13) + "." : fn;
                    bool bold = st.Bold < 0 ? TextAnnotation.Bold : st.Bold == 1, ital = st.Italic < 0 ? TextAnnotation.Italic : st.Italic == 1;
                    _boldBtn.Background = bold ? Brush(_theme.SurfaceMuted) : Brushes.Transparent;
                    _italicBtn.Background = ital ? Brush(_theme.SurfaceMuted) : Brushes.Transparent;
                    string al = string.IsNullOrEmpty(st.Align) ? TextAnnotation.Align : st.Align;
                    ((TextBlock)_alignBtn.Child).Text = _i18n.T(al == "center" ? "settings.center" : al == "right" ? "layout.right" : al == "justify" ? "settings.justify" : "layout.left");
                }
                if (isArrow)
                {
                    string hk = string.IsNullOrEmpty(st.HeadStyle) ? ArrowAnnotation.HeadStyle : st.HeadStyle;
                    ((TextBlock)_headBtn.Child).Text = _i18n.T("settings.head." + hk);
                    ((TextBlock)_headSizeBtn.Child).Text = "×" + st.HeadScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                }
                UpdateSliders(tool, st, pctOn);
                bool rounded = st.Rounded;
                ShapeAnnotation selShape = _selected as ShapeAnnotation;
                if (selShape != null && !selShape.Ellipse) rounded = selShape.Rounded;
                _roundBtn.Background = rounded ? Brush(_theme.SurfaceMuted) : Brushes.Transparent;

                // recently used custom colors
                if (_recentHost != null && _recentShown != _styles.Recent.Count)
                {
                    _recentHost.Children.Clear();
                    foreach (WpfColor rc in _styles.Recent)
                    {
                        Border sw = new Border();
                        sw.Width = 20; sw.Height = 20;
                        sw.CornerRadius = new CornerRadius(4);
                        sw.Background = Brush(rc);
                        sw.BorderBrush = Brush(_theme.Border);
                        sw.BorderThickness = new Thickness(1);
                        sw.Margin = new Thickness(3, 6, 3, 6);
                        sw.Cursor = Cursors.Hand;
                        sw.ToolTip = "#" + rc.R.ToString("X2") + rc.G.ToString("X2") + rc.B.ToString("X2");
                        WpfColor cc = rc;
                        sw.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; SetColor(cc); };
                        _recentHost.Children.Add(sw);
                    }
                    _recentShown = _styles.Recent.Count;
                }
                if (_panelB != null && _panelB.Visibility == Visibility.Visible) LayoutPanels();
            }
        }

        private void HidePanels()
        {
            if (_panelA != null) _panelA.Visibility = Visibility.Collapsed;
            if (_panelB != null) _panelB.Visibility = Visibility.Collapsed;
            if (_panelC != null) _panelC.Visibility = Visibility.Collapsed;
            if (_panelD != null) _panelD.Visibility = Visibility.Collapsed;
        }

        private void ShowPanels()
        {
            if (!_hasSel) return;
            if (!_panelsBuilt) BuildPanels();
            LayoutPanels();
        }

        private void LayoutPanels()
        {
            if (_listOpen) { _layoutWaits = true; return; }            // a drop-down list is open: rebuilding the panels would close it
            Rect a = Rect.Empty;
            if (_panelA != null)
            {
                _panelA.Visibility = Visibility.Visible;
                a = LayoutToolsPanel();                              // outside the region if it fits, else smaller, else inside (RegionOverlay.Fit.cs)
            }
            _panelB.Visibility = Visibility.Visible;
            LayoutPropsPanel(a);
            ScheduleBeautifyPreview();                                // beside the region, away from the panels, again

            // the arrow panel: only while an arrow is selected, next to the tool panel on its outer side
            if (_panelC != null)
            {
                bool show = _settings.ArrowPanel && _selected is ArrowAnnotation && !GroupActive && _editBox == null && !a.IsEmpty;
                _panelC.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    if (_userPosC.HasValue) PlaceAt(_panelC, _userPosC.Value);
                    else PlaceBeside(_panelC, a, _settings.ToolbarPosition);
                }
            }
            LayoutAlignPanel(a);                                   // the align panel: while several objects are selected
        }

        /// <summary>Places a panel beside another one on the side the tool panel faces away from the region; the other side when there is no room.</summary>
        private void PlaceBeside(Border panel, Rect other, string toolbarSide)
        {
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = panel.DesiredSize.Width, h = panel.DesiredSize.Height, gap = 6;
            double x, y;
            switch (toolbarSide)
            {
                case "left":
                    x = other.Left - w - gap; y = other.Top;
                    if (x < 4) x = other.Right + gap;
                    break;
                case "top":
                    x = other.Left; y = other.Top - h - gap;
                    if (y < 4) y = other.Bottom + gap;
                    break;
                case "bottom":
                    x = other.Left; y = other.Bottom + gap;
                    if (y + h > _canvas.Height - 4) y = other.Top - h - gap;
                    break;
                default: // right
                    x = other.Right + gap; y = other.Top;
                    if (x + w > _canvas.Width - 4) x = other.Left - w - gap;
                    break;
            }
            PlaceAt(panel, new Point(x, y));
        }

        // panels the user has dragged (layout.panels_draggable): their own position is kept until the overlay closes
        private Point? _userPosA, _userPosB;

        private Rect PlaceAt(Border panel, Point p)
        {
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = panel.DesiredSize.Width, h = panel.DesiredSize.Height;
            double x = Math.Max(0, Math.Min(_canvas.Width - w, p.X)), y = Math.Max(0, Math.Min(_canvas.Height - h, p.Y));
            Canvas.SetLeft(panel, x); Canvas.SetTop(panel, y);
            return new Rect(x, y, w, h);
        }

        /// <summary>A thin bar at the edge of a panel: drag it to move the panel.</summary>
        private Border MakeGrip(Border panel, bool? isA, bool horizontalBar)
        {
            Border grip = new Border();
            if (horizontalBar) { grip.Height = 10; grip.Margin = new Thickness(0, 0, 0, 3); }
            else { grip.Width = 10; grip.Margin = new Thickness(0, 0, 3, 0); }
            grip.CornerRadius = new CornerRadius(3);
            grip.Background = Brush(_theme.SurfaceMuted);
            grip.Cursor = Cursors.SizeAll;
            grip.ToolTip = _i18n.T("tip.drag_panel");
            Point startMouse = new Point(); Point startPanel = new Point();
            grip.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                grip.CaptureMouse();
                if (_editBox == null && _sizeEdit == null) { Focus(); Keyboard.Focus(this); }   // the keys (Caps, Enter...) keep working after a drag
                startMouse = e.GetPosition(_canvas);
                startPanel = new Point(Canvas.GetLeft(panel), Canvas.GetTop(panel));
                e.Handled = true;
            };
            grip.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!grip.IsMouseCaptured) return;
                Point m = e.GetPosition(_canvas);
                Point np = new Point(startPanel.X + m.X - startMouse.X, startPanel.Y + m.Y - startMouse.Y);
                PlaceAt(panel, np);
                Point placed = new Point(Canvas.GetLeft(panel), Canvas.GetTop(panel));
                if (isA == null) _userPosC = placed;
                else if (isA == true) _userPosA = placed;
                else _userPosB = placed;
            };
            grip.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { grip.ReleaseMouseCapture(); e.Handled = true; };
            return grip;
        }

        /// <summary>Places a panel next to the selection on the given side, inside the screen; moves it away from "avoid".</summary>
        private Rect PlacePanel(Border panel, string side, Rect avoid)
        {
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = panel.DesiredSize.Width, h = panel.DesiredSize.Height;
            double cw = _canvas.Width, ch = _canvas.Height, gap = 8;
            double x, y;

            switch (side)
            {
                case "left":
                    x = _sel.Left - w - gap;
                    if (x < 4) x = _sel.Right + gap;
                    if (x + w > cw - 4) x = _sel.Left + gap;
                    y = _sel.Top;
                    break;
                case "right":
                    x = _sel.Right + gap;
                    if (x + w > cw - 4) x = _sel.Left - w - gap;
                    if (x < 4) x = _sel.Right - w - gap;
                    y = _sel.Top;
                    break;
                case "top":
                    y = _sel.Top - h - gap;
                    if (y < 4) y = _sel.Bottom + gap;
                    if (y + h > ch - 4) y = _sel.Top + gap;
                    x = _sel.Right - w;
                    break;
                default: // bottom
                    y = _sel.Bottom + gap;
                    if (y + h > ch - 4) y = _sel.Top - h - gap;
                    if (y < 4) y = _sel.Bottom - h - gap;
                    x = _sel.Right - w;
                    break;
            }
            x = Math.Max(4, Math.Min(cw - w - 4, x));
            y = Math.Max(4, Math.Min(ch - h - 4, y));

            Rect r = new Rect(x, y, w, h);
            if (!avoid.IsEmpty && r.IntersectsWith(avoid))
            {
                // move below/above the other panel, whichever fits
                double below = avoid.Bottom + gap, above = avoid.Top - h - gap;
                if (below + h <= ch - 4) y = below; else if (above >= 4) y = above;
                r = new Rect(x, y, w, h);
            }
            Canvas.SetLeft(panel, r.X);
            Canvas.SetTop(panel, r.Y);
            return r;
        }

    }
}
