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
        // ---- strength of the Shift / Ctrl / Alt actions: the mouse wheel changes it while the key is held ----
        private double _strRound = 40, _strCenter = 100, _strConstrain = 100, _strAxis = 100;
        private bool _strengthDirty;
        private Border _strengthHint;
        private TextBlock _strengthText;
        private System.Windows.Threading.DispatcherTimer _strengthTimer;

        private void ResetStrengths()
        {
            _strRound = _settings.StrengthRound; _strCenter = _settings.StrengthCenter;
            _strConstrain = _settings.StrengthConstrain; _strAxis = _settings.StrengthAxis;
        }

        /// <summary>The wheel while a Shift / Ctrl / Alt key is held: how strong the action is (0 to 100 percent).
        /// While drawing the shape follows at once; with a shape or the region selected it changes that.</summary>
        private bool AdjustStrength(ModifierKeys mods, int dir)
        {
            if (!_settings.ModEnabled || mods == ModifierKeys.None) return false;
            HashSet<string> acts = _settings.ModActions(mods);
            if (acts.Count == 0) return false;
            bool dragging = _mode != DragMode.None && _mode != DragMode.Erase;
            ShapeAnnotation sh = _selected as ShapeAnnotation;
            bool onShape = !dragging && sh != null && _settings.ModShapes;
            bool onRegion = !dragging && _selected == null && _hasSel && _settings.ModRegion;
            if (!dragging && !onShape && !onRegion) return false;
            double step = _settings.StrengthStep * dir;
            string what = null; double value = 0;
            Func<double, double> clamp = delegate (double v) { return Math.Max(0, Math.Min(100, v)); };
            if (acts.Contains("rounded"))
            {
                _strRound = clamp(_strRound + step); what = _i18n.T("act.rounded"); value = _strRound;
                if (onShape && !sh.Ellipse) { sh.Rounded = true; sh.RoundPct = _strRound; RebuildAnnotations(); }
                if (onRegion) { _regionRoundPct = _strRound; UpdateVisuals(); }
            }
            else if (acts.Contains("center")) { _strCenter = clamp(_strCenter + step); what = _i18n.T("act.center"); value = _strCenter; }
            else if (acts.Contains("constrain")) { _strConstrain = clamp(_strConstrain + step); what = _i18n.T("act.constrain"); value = _strConstrain; }
            else if (acts.Contains("axis")) { _strAxis = clamp(_strAxis + step); what = _i18n.T("act.axis"); value = _strAxis; }
            if (what == null) return false;
            if (dragging) HandleMove(_lastPt, mods);   // the shape follows the new strength at once
            else _strengthDirty = true;
            _modOther = true;                           // the wheel was used: it is not a plain tap of the key
            ShowStrengthHint(what + ": " + (int)Math.Round(value) + "%");
            return true;
        }

        private void ShowStrengthHint(string text)
        {
            if (_strengthHint == null)
            {
                _strengthText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush(_theme.Text) };
                _strengthHint = new Border { Child = _strengthText, Background = Brush(_theme.Surface), BorderBrush = Brush(_theme.Accent), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _canvas.Children.Add(_strengthHint);
                _strengthTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
                _strengthTimer.Tick += delegate { _strengthTimer.Stop(); _strengthHint.Visibility = Visibility.Collapsed; };
            }
            _strengthText.Text = text;
            _strengthHint.Visibility = Visibility.Visible;
            Canvas.SetLeft(_strengthHint, Math.Max(2, Math.Min(_canvas.Width - 180, _lastPt.X + 20)));
            Canvas.SetTop(_strengthHint, Math.Max(2, Math.Min(_canvas.Height - 34, _lastPt.Y - 36)));
            _strengthTimer.Stop(); _strengthTimer.Start();
        }

        // ---- snapping to the axes of other objects (settings: snap) ----
        private Canvas _guideLayer;

        private bool SnapHeld()
        {
            if (!_settings.SnapEnabled) return false;
            Key k;
            switch (_settings.SnapKey)
            {
                case "right_ctrl": k = Key.RightCtrl; break;
                case "right_shift": k = Key.RightShift; break;
                case "left_alt": k = Key.LeftAlt; break;
                case "left_ctrl": k = Key.LeftCtrl; break;
                case "left_shift": k = Key.LeftShift; break;
                default: k = Key.RightAlt; break;
            }
            return Keyboard.IsKeyDown(k);
        }

        private static Point CenterOf(Annotation a)
        {
            StepAnnotation st = a as StepAnnotation;
            if (st != null) return st.Pos;
            Rect b = a.Bounds();
            return new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
        }

        /// <summary>The centre moved onto the vertical and / or horizontal axis of the nearest other object when it is closer
        /// than snap.threshold_px (further away it is free again).</summary>
        private Point SnapCenter(Point center, Annotation exclude)
        {
            double thr = _settings.SnapThreshold, bx = thr + 1, by = thr + 1;
            double sx = center.X, sy = center.Y;
            bool hx = false, hy = false;
            foreach (Annotation o in _items)
            {
                if (exclude != null && o.Id == exclude.Id) continue;
                if (IsSelected(o)) continue;                        // the rest of a multi-selection moves along: it is not a target
                Point oc = CenterOf(o);
                double dx = Math.Abs(oc.X - center.X), dy = Math.Abs(oc.Y - center.Y);
                if (dx <= thr && dx < bx) { bx = dx; sx = oc.X; hx = true; }
                if (dy <= thr && dy < by) { by = dy; sy = oc.Y; hy = true; }
            }
            ShowGuides(hx ? (double?)sx : null, hy ? (double?)sy : null);
            return new Point(sx, sy);
        }

        private void ShowGuides(double? x, double? y)
        {
            if (_guideLayer == null)
            {
                _guideLayer = new Canvas { IsHitTestVisible = false, Width = _canvas.Width, Height = _canvas.Height };
                _canvas.Children.Add(_guideLayer);
            }
            _guideLayer.Children.Clear();
            if (!_settings.SnapGuides) return;
            Brush ink = Brush(_theme.Accent);
            if (x.HasValue)
            {
                Line l = new Line { X1 = x.Value, X2 = x.Value, Y1 = _sel.Top, Y2 = _sel.Bottom, Stroke = ink, StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }), IsHitTestVisible = false };
                _guideLayer.Children.Add(l);
            }
            if (y.HasValue)
            {
                Line l = new Line { Y1 = y.Value, Y2 = y.Value, X1 = _sel.Left, X2 = _sel.Right, Stroke = ink, StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }), IsHitTestVisible = false };
                _guideLayer.Children.Add(l);
            }
        }

        private void ClearGuides()
        {
            if (_guideLayer != null) _guideLayer.Children.Clear();
        }

        // ---- modifier keys ----
        private ModifierKeys _modMax = ModifierKeys.None;   // the keys held since the last time all were released
        private bool _modOther;                              // something else was pressed meanwhile: it is not a plain tap

        private static bool IsModifierKey(Key k)
        {
            return k == Key.LeftShift || k == Key.RightShift || k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt;
        }

        /// <summary>A plain tap of Shift / Ctrl / Alt (or a combination): the actions of modifier_actions are applied to the
        /// selected object - a rectangle becomes square or rounded, an arrow gets a second head, a line snaps to 45 degrees...</summary>
        private void ModifierTap(ModifierKeys m)
        {
            if (!_editor || !_hasSel || _mode != DragMode.None || _editBox != null || _sizeEdit != null) return;
            if (!_settings.ModEnabled || !_settings.ModTap) return;
            HashSet<string> acts = _settings.ModActions(m);
            if (acts.Count == 0) return;

            if (_selected == null)
            {
                // nothing is selected: the tap acts on the region itself (square, rounded corners)
                if (!_settings.ModRegion) return;
                bool regionChanged = false;
                if (acts.Contains("constrain"))
                {
                    double side = Math.Max(_sel.Width, _sel.Height);
                    side = Math.Min(side, Math.Min(_canvas.Width - _sel.X, _canvas.Height - _sel.Y));
                    if (side >= 4) { _sel = new Rect(_sel.X, _sel.Y, side, side); regionChanged = true; }
                }
                if (acts.Contains("rounded")) { _regionRoundPct = _regionRoundPct > 0 ? 0 : _strRound; regionChanged = true; }
                if (regionChanged)
                {
                    UpdateVisuals();
                    if (_panelsBuilt && _panelB.Visibility == Visibility.Visible) LayoutPanels();
                }
                return;
            }
            if (!_settings.ModShapes) return;
            bool changed = false;
            ArrowAnnotation ar = _selected as ArrowAnnotation;
            if (ar != null)
            {
                if (acts.Contains("axis")) { ar.B = ClampToSel(SnapAxis(ar.A, ar.B)); changed = true; }
                else if (acts.Contains("constrain")) { ar.B = ClampToSel(SnapAngle(ar.A, ar.B)); changed = true; }
                if (acts.Contains("double") && ar.Head) { ar.Head2 = !ar.Head2; changed = true; }
            }
            ShapeAnnotation sh = _selected as ShapeAnnotation;
            if (sh != null)
            {
                if (acts.Contains("rounded") && !sh.Ellipse) { sh.Rounded = !sh.Rounded; if (sh.Rounded) sh.RoundPct = _strRound; changed = true; }
                if (acts.Contains("fill")) { sh.Fill = sh.Fill == 2 ? 0 : 2; changed = true; }
                if (acts.Contains("constrain"))
                {
                    double side = Math.Max(sh.R.Width, sh.R.Height);
                    side = Math.Min(side, Math.Min(_sel.Right - sh.R.X, _sel.Bottom - sh.R.Y));
                    if (side >= 3) { sh.R = new Rect(sh.R.X, sh.R.Y, side, side); changed = true; }
                }
            }
            if (changed) { Commit(); RebuildAnnotations(); }
        }

        // ---- crosshair, loupe, typed size ----
        private Rectangle _crossH, _crossV;
        private Border _mag;
        private Image _magImg;
        private TextBlock _magText;
        private const double MagSize = 126;
        private const int MagCells = 21;
        private TextBox _sizeEdit;

        private void UpdatePointerAids(Point p)
        {
            UpdateEraserAids(p);
            // the crosshair and the loupe help to place the corner: while a region is drawn and also while its edge or corner is dragged
            bool aim = !_hasSel || _mode == DragMode.New || _mode == DragMode.Resize || (_editor && _tool == EditorTool.Picker);
            if (_crossH != null)
            {
                _crossH.Visibility = aim ? Visibility.Visible : Visibility.Collapsed;
                _crossV.Visibility = aim ? Visibility.Visible : Visibility.Collapsed;
                Canvas.SetTop(_crossH, p.Y); Canvas.SetLeft(_crossH, 0);
                Canvas.SetLeft(_crossV, p.X); Canvas.SetTop(_crossV, 0);
            }
            if (_mag == null) return;
            if (!aim) { _mag.Visibility = Visibility.Collapsed; return; }
            int half = MagCells / 2;
            int x0 = Math.Max(0, Math.Min(_source.PixelWidth - MagCells, (int)p.X - half));
            int y0 = Math.Max(0, Math.Min(_source.PixelHeight - MagCells, (int)p.Y - half));
            _magImg.Source = new CroppedBitmap(_source, new Int32Rect(x0, y0, MagCells, MagCells));
            WpfColor c; string hex = "";
            if (Backdrop.Pixel(p, out c)) hex = "  #" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            _magText.Text = ((int)p.X + _bounds.X) + ", " + ((int)p.Y + _bounds.Y) + hex;
            _mag.Visibility = Visibility.Visible;
            _mag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _mag.DesiredSize.Width, h = _mag.DesiredSize.Height;
            double x = p.X + 26, y = p.Y + 26;
            if (x + w > _canvas.Width - 2) x = p.X - w - 20;
            if (y + h > _canvas.Height - 2) y = p.Y - h - 20;
            Canvas.SetLeft(_mag, Math.Max(2, x)); Canvas.SetTop(_mag, Math.Max(2, y));
        }

        /// <summary>"800x600", "800 x 600", "800,600": returns the two numbers.</summary>
        public static bool TryParseSize(string text, out int w, out int h)
        {
            w = h = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(\d{1,5})\s*[xX×*,;\s]\s*(\d{1,5})\s*$");
            if (!m.Success) return false;
            w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value);
            return w >= 4 && h >= 4;
        }

        private void BeginSizeEdit()
        {
            if (!_hasSel || _sizeEdit != null) return;
            _sizeEdit = new TextBox { Text = ((int)Math.Round(_sel.Width)) + "x" + ((int)Math.Round(_sel.Height)), Width = 110, FontSize = 12, Padding = new Thickness(4, 2, 4, 2) };
            _sizeEdit.Background = Brush(_theme.Surface); _sizeEdit.Foreground = Brush(_theme.Text); _sizeEdit.BorderBrush = Brush(_theme.Accent);
            Canvas.SetLeft(_sizeEdit, Canvas.GetLeft(_sizeLabel)); Canvas.SetTop(_sizeEdit, Canvas.GetTop(_sizeLabel));
            _canvas.Children.Add(_sizeEdit);
            _sizeEdit.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { EndSizeEdit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndSizeEdit(false); e.Handled = true; }
            };
            _sizeEdit.LostKeyboardFocus += delegate { EndSizeEdit(false); };
            _sizeEdit.Focus(); Keyboard.Focus(_sizeEdit); _sizeEdit.SelectAll();
        }

        private void EndSizeEdit(bool apply)
        {
            if (_sizeEdit == null) return;
            TextBox box = _sizeEdit;
            _sizeEdit = null;
            int w, h;
            if (apply && TryParseSize(box.Text, out w, out h))
            {
                double nw = Math.Min(w, _canvas.Width), nh = Math.Min(h, _canvas.Height);
                double x = Math.Min(_sel.X, _canvas.Width - nw), y = Math.Min(_sel.Y, _canvas.Height - nh);
                _sel = new Rect(Math.Max(0, x), Math.Max(0, y), nw, nh);
                UpdateVisuals();
                if (_panelsBuilt && _panelB.Visibility == Visibility.Visible) LayoutPanels();
            }
            _canvas.Children.Remove(box);
            Focus();
        }

        private bool IsOnEditFrame(Point p)
        {
            Rect outer = new Rect(Canvas.GetLeft(_editBorder), Canvas.GetTop(_editBorder), _editBorder.ActualWidth, _editBorder.ActualHeight);
            Rect inner = outer;
            inner.Inflate(-EditPad + 1, -EditPad + 1);
            return outer.Contains(p) && !inner.Contains(p);
        }

        // ================= keyboard =================
        /// <summary>What Enter does: the action of the held keys from enter_actions (Enter alone: save without a dialog,
        /// Shift+Enter: save as, Ctrl+Enter: pin to the screen).</summary>
        private void RunEnterAction(ModifierKeys mods)
        {
            List<string> parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("ctrl");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("shift");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("alt");
            string combo = parts.Count == 0 ? "none" : string.Join("+", parts);
            RunAction(ResolveEnter(combo));
        }

        private string ResolveEnter(string combo)
        {
            string action;
            if (!_settings.EnterActions.TryGetValue(combo, out action))
                action = combo == "none" ? "save" : combo == "shift" ? "save_as" : combo == "ctrl" ? "pin" : "none";
            return action;
        }

        private void RunAction(string action)
        {
            if (!_hasSel) return;
            switch (action)
            {
                case "save": DoSave(); break;
                case "save_as": DoSaveAs(); break;
                case "pin": DoPin(); break;
                case "copy": DoCopy(); break;
                case "upload": if (UploadService.Enabled(_settings)) DoUpload(); break;
                case "close": Close(); break;
            }
        }

        /// <summary>One configurable shortcut (settings - Hotkeys): false when the action does not apply now (the key is then left alone).</summary>
        private bool RunShortcut(string action)
        {
            switch (action)
            {
                case "save": case "save_as": case "copy": case "pin": case "upload": RunAction(action); return true;
                case "select_all": SelectAll(); return true;
            }
            if (!_editor) return false;   // the rest works in the editor only
            switch (action)
            {
                case "beautify": ToggleBeautify(); return true;
                case "undo": Undo(); return true;
                case "redo": Redo(); return true;
                case "delete": DeleteSelected(); return true;
                case "duplicate": DuplicateSelected(); return true;
                case "forward": ReorderSelected(true); return true;
                case "backward": ReorderSelected(false); return true;
            }
            return false;
        }

        private bool HandleKey(Key key, ModifierKeys mods)
        {
            Log.Debug("key " + key + " mods=" + mods + " editing=" + (_editBox != null) + " tool=" + _tool + " items=" + _items.Count);
            if (_editBox != null || _sizeEdit != null || _typingSize) return false; // the text boxes own the keys while typing
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            bool shift = (mods & ModifierKeys.Shift) != 0;

            if (_colorPop != null && _colorPop.Visibility == Visibility.Visible)
            {
                if (key == Key.Escape) { _colorPop.Visibility = Visibility.Collapsed; Focus(); return true; }
                return false; // the hex field owns the keys while the color picker is open
            }
            if (BeautifyMenuOpen && key == Key.Escape) { CloseBeautifyMenu(); Focus(); return true; }   // Esc closes the menu of the looks first
            if (key == Key.Escape)
            {
                if (_selected != null) Select(null);
                else if (_tool == EditorTool.Picker || _tool == EditorTool.Eraser) SetTool(EditorTool.Select);
                else Close();
                return true;
            }
            if (HandleCornerKey(key)) return true;
            if (key == Key.Enter) { RunEnterAction(mods); return true; }
            string actionByKey = ActionForKey(key, mods);   // save, save as, copy, pin, upload: the shortcuts come from the settings
            if (actionByKey != null && RunShortcut(actionByKey)) { Log.Debug("shortcut -> " + actionByKey); return true; }
            if (_editor)
            {
                if (ctrl && shift && key == Key.Z) { Redo(); return true; }
                if (key == Key.Back) { DeleteSelected(); return true; }
                if (!ctrl && _hasSel && _mode == DragMode.None && (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down))
                {
                    // arrows: move the selected object (or the region while nothing is drawn) by 1 px, with Shift by 10 px
                    double step = shift ? 10 : 1;
                    double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
                    double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
                    if (_selected != null) { foreach (Annotation sa in SelectedAll()) sa.Translate(dx, dy); Commit(); RebuildAnnotations(); return true; }
                    if (_items.Count == 0)
                    {
                        _sel = new Rect(Math.Max(0, Math.Min(_canvas.Width - _sel.Width, _sel.X + dx)), Math.Max(0, Math.Min(_canvas.Height - _sel.Height, _sel.Y + dy)), _sel.Width, _sel.Height);
                        UpdateVisuals();
                        if (_panelsBuilt && _panelB.Visibility == Visibility.Visible) LayoutPanels();
                        return true;
                    }
                }
                if (!ctrl && _hasSel && _mode == DragMode.None)
                {
                    // tool keys and preset keys come from the settings (editor_keys), defaults V A L R O B M T N U E I and 1..9
                    EditorTool want;
                    if (_toolKeys.TryGetValue(key, out want))
                    {
                        if (want == EditorTool.Select) { SetTool(EditorTool.Select); return true; }
                        if (_settings.ToolEnabled(StyleStore.Key(want))) { SetTool(want); return true; }
                    }
                    int pi;
                    if (_presetKeys.TryGetValue(key, out pi) && pi < _styles.PresetCount) { SetPreset(pi); return true; }
                }
            }
            return false;
        }

        // ================= rounded region (Ctrl while drawing the region, or a tap of Ctrl on it) =================
        private double _regionRoundPct;                       // 0 = square corners; else the roundness in percent (100 = a pill)
        private bool _regionRounded { get { return _regionRoundPct > 0; } }

        /// <summary>The corner radius of a rounded region: a tenth of the smaller side, between 8 and 48 pixels.</summary>
        private double RegionRadius()
        {
            return Math.Max(0, _regionRoundPct / 100.0 * Math.Min(_sel.Width, _sel.Height) / 2);
        }

        /// <summary>The picture with transparent rounded corners.</summary>
        private System.Drawing.Bitmap RoundCorners(System.Drawing.Bitmap src)
        {
            System.Drawing.Bitmap dst = new System.Drawing.Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            double radius = Math.Max(0, _regionRoundPct / 100.0 * Math.Min(src.Width, src.Height) / 2);
            if (radius < 0.5) return src;
            float d = (float)(radius * 2);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(dst))
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(src.Width - d, 0, d, d, 270, 90);
                path.AddArc(src.Width - d, src.Height - d, d, d, 0, 90);
                path.AddArc(0, src.Height - d, d, d, 90, 90);
                path.CloseFigure();
                g.SetClip(path);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            src.Dispose();
            return dst;
        }

        // ================= drawing of the frame =================
        /// <summary>True while an arrow, a shape or a brush stroke is being drawn: it is shown WHOLE, also the part outside the region.</summary>
        private bool DrawingOutside { get { return _mode == DragMode.DrawArrow || _mode == DragMode.DrawRect || _mode == DragMode.DrawBrush; } }

        /// <summary>The drawings are cut by the region. The object being drawn lives in its own layer (_annoLive) and is not cut until the mouse is released.</summary>
        private void UpdateAnnoClip()
        {
            bool has = _hasSel && _sel.Width > 0 && _sel.Height > 0;
            double rr = has && _regionRounded ? RegionRadius() : 0;
            _annoLayer.Clip = has ? new RectangleGeometry(_sel, rr, rr) : new RectangleGeometry(new Rect(0, 0, 0, 0));
        }

        /// <summary>An object that lies completely outside the region shows nothing: it is dropped when the mouse is released.</summary>
        private bool OutsideRegion(Annotation a)
        {
            Rect b = a.Bounds();
            return !b.IsEmpty && !_sel.IntersectsWith(b);
        }

        private void UpdateVisuals()
        {
            GeometryGroup g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new RectangleGeometry(new Rect(0, 0, _canvas.Width, _canvas.Height)));
            bool has = _hasSel && _sel.Width > 0 && _sel.Height > 0;
            double rr = has && _regionRounded ? RegionRadius() : 0;
            if (has) g.Children.Add(new RectangleGeometry(_sel, rr, rr));
            _dim.Data = g;

            UpdateAnnoClip();

            Visibility vis = has ? Visibility.Visible : Visibility.Collapsed;
            _frame.Visibility = vis;
            foreach (Rectangle h in _handles) h.Visibility = vis;
            _sizeLabel.Visibility = (has && _settings.ShowSizeLabel) ? Visibility.Visible : Visibility.Collapsed;
            if (!has) return;

            Canvas.SetLeft(_frame, _sel.X); Canvas.SetTop(_frame, _sel.Y);
            _frame.Width = _sel.Width; _frame.Height = _sel.Height;
            _frame.RadiusX = rr; _frame.RadiusY = rr;

            Point[] pts = HandlePoints();
            for (int i = 0; i < 8; i++)
            {
                Canvas.SetLeft(_handles[i], pts[i].X - _handleSize / 2);
                Canvas.SetTop(_handles[i], pts[i].Y - _handleSize / 2);
            }

            _sizeText.Text = (string.IsNullOrEmpty(_settings.SizeLabelFormat) ? "{w} x {h}" : _settings.SizeLabelFormat)
                .Replace("{w}", ((int)Math.Round(_sel.Width)).ToString())
                .Replace("{h}", ((int)Math.Round(_sel.Height)).ToString())
                .Replace("{x}", ((int)Math.Round(_sel.X + _bounds.X)).ToString())
                .Replace("{y}", ((int)Math.Round(_sel.Y + _bounds.Y)).ToString());
            if (RatioActive) _sizeText.Text += "   " + RatioLabel();           // a locked ratio is shown next to the size
            _sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lh = _sizeLabel.DesiredSize.Height;
            double ly = _settings.ShowSizeLabelAbove ? _sel.Top - lh - 6 : _sel.Bottom + 6;
            if (_settings.ShowSizeLabelAbove && ly < 2) ly = _sel.Top + 6;
            if (!_settings.ShowSizeLabelAbove && ly + lh > _canvas.Height - 2) ly = _sel.Bottom - lh - 6;
            Canvas.SetLeft(_sizeLabel, Math.Max(2, _sel.Left));
            Canvas.SetTop(_sizeLabel, ly);
        }

        // ================= annotations =================
        private void RebuildAnnotations()
        {
            _annoLayer.Children.Clear();
            _annoLive.Children.Clear();
            foreach (Annotation a in _items)
            {
                if (a == _editing) continue;
                UIElement v = a.BuildVisual();
                if (DrawingOutside && ReferenceEquals(a, _selected)) _annoLive.Children.Add(v);     // whole, also the part outside the region
                else _annoLayer.Children.Add(v);
            }
            UpdateChrome();
        }

        private void UpdateChrome()
        {
            PruneGroup();
            _chrome.Children.Clear();
            if (_selected == null || _selected == _editing) return;
            if (_group.Count > 0) { DrawGroupChrome(); return; }      // several objects: a dashed frame around each, no handles
            Brush accent = Brush(_theme.Accent);

            ArrowAnnotation arrow = _selected as ArrowAnnotation;
            if (arrow != null)
            {
                foreach (Point p in new Point[] { arrow.A, arrow.B })
                {
                    Ellipse e = new Ellipse();
                    e.Width = 12; e.Height = 12;
                    e.Fill = Brush(_theme.Surface);
                    e.Stroke = accent;
                    e.StrokeThickness = 2;
                    Canvas.SetLeft(e, p.X - 6);
                    Canvas.SetTop(e, p.Y - 6);
                    _chrome.Children.Add(e);
                }
                return;
            }
            // every other object: a dashed frame around it (text, brush, step, shapes, blur)
            if (_mode == DragMode.DrawRect) return;                  // while a shape is being drawn: only the shape itself, no frame, no handles
            if (!(_selected is ShapeAnnotation))                     // a rectangle / ellipse has no extra dashed frame: only its handles
            {
                Rect b = _selected.LocalRect();                      // a turned object: its frame is turned too
                b.Inflate(3, 3);
                Rectangle r = new Rectangle();
                r.Width = Math.Max(1, b.Width); r.Height = Math.Max(1, b.Height);
                r.Stroke = accent;
                r.StrokeThickness = 1.5;
                r.StrokeDashArray = new DoubleCollection(new double[] { 4, 3 });
                if (Math.Abs(_selected.Angle) > 0.001) r.RenderTransform = new RotateTransform(_selected.Angle, r.Width / 2, r.Height / 2);
                Canvas.SetLeft(r, b.X);
                Canvas.SetTop(r, b.Y);
                _chrome.Children.Add(r);
            }

            RectAnnotation ra = _selected as RectAnnotation;
            if (ra != null)
            {
                foreach (Point p in ra.HandlePoints())
                {
                    Rectangle h = new Rectangle();
                    h.Width = 9; h.Height = 9;
                    h.Fill = Brush(_theme.Surface);
                    h.Stroke = accent;
                    h.StrokeThickness = 1.5;
                    Canvas.SetLeft(h, p.X - 4.5);
                    Canvas.SetTop(h, p.Y - 4.5);
                    _chrome.Children.Add(h);
                }
            }
            DrawEditHandles(accent);                                 // rotate handle, text block handles (RegionOverlay.Rotate.cs)
        }

        private void Select(Annotation a)
        {
            _selected = a;
            _group.Clear();
            UpdateChrome();
            RefreshPanels();
        }

        private void ClearAnnotations()
        {
            _sessionSize.Clear();                                   // a new region: the sizes come from the settings again
            _eraserSize = Math.Max(2, _settings.EraserRadius * 2.0);
            if (_editBox != null) EndEdit(false);
            _items.Clear();
            _selected = null;
            _group.Clear();
            _hist.Clear();
            _hist.Add(new List<Annotation>());
            _hi = 0;
            RebuildAnnotations();
            RefreshPanels();
        }

        private static List<Annotation> Snapshot(List<Annotation> src)
        {
            return src.Select(a => a.Clone()).ToList();
        }

        private void Commit()
        {
            while (_hist.Count > _hi + 1) _hist.RemoveAt(_hist.Count - 1);
            _hist.Add(Snapshot(_items));
            _hi = _hist.Count - 1;
            RefreshPanels();
            ScheduleBeautifyPreview();                                // the live picture of the beautiful look follows the drawings
        }

        private void Restore()
        {
            int selId = _selected != null ? _selected.Id : -1;
            List<int> groupIds = _group.Select(g => g.Id).ToList();
            _items.Clear();
            foreach (Annotation a in _hist[_hi]) _items.Add(a.Clone());
            _selected = _items.FirstOrDefault(x => x.Id == selId);
            _group.Clear();                                         // the objects are new copies: find the group again by id
            if (_selected != null) _group.AddRange(_items.Where(x => groupIds.Contains(x.Id) && x != _selected));
            RebuildAnnotations();
            RefreshPanels();
            ScheduleBeautifyPreview();                                // undo and redo too
        }

        private void Undo()
        {
            if (_editBox != null) return;
            if (_hi > 0) { _hi--; Restore(); }
        }

        private void Redo()
        {
            if (_editBox != null) return;
            if (_hi < _hist.Count - 1) { _hi++; Restore(); }
        }

        private void DeleteSelected()
        {
            if (_selected == null || _editBox != null) return;
            DeleteGroup();                                          // the main object and the rest of the selection
            Commit();
            RebuildAnnotations();
        }

        private void SetTool(EditorTool t)
        {
            if (_editBox != null) EndEdit(true);
            _tool = t;
            if (StyleStore.HasStyle(t)) _lastTool = t;
            if (t != EditorTool.Select && t != EditorTool.Picker) { _selected = null; UpdateChrome(); }
            if (t != EditorTool.Picker && _pickChip != null) _pickChip.Visibility = Visibility.Collapsed;
            UpdateEraserAids(_lastPt);
            RefreshPanels();
        }

        private void BeginAnnoDrag(DragMode mode, Point p)
        {
            _mode = mode;
            _start = p;
            _orig = _selected.Clone();
            _groupOrig = _group.Select(g => g.Clone()).ToList();
            _canvas.CaptureMouse();
        }

        private void StartArrow(Point p, bool head)
        {
            ArrowAnnotation a = new ArrowAnnotation();
            a.Head = head;
            ApplyStyle(a, _styles.Current(head ? EditorTool.Arrow : EditorTool.Line));
            a.A = Clamp(p);                              // the whole screen, not only the region: the start may be outside it
            a.B = a.A;
            _brushFirst = a.A;
            _items.Add(a);
            _selected = a;
            _mode = DragMode.DrawArrow;
            UpdateAnnoClip();
            _canvas.CaptureMouse();
            RebuildAnnotations();
        }

        private void StartRect(EditorTool tool, Point p)
        {
            RectAnnotation ra;
            if (tool == EditorTool.Blur) ra = new BlurAnnotation();
            else if (tool == EditorTool.Spotlight) ra = new SpotlightAnnotation();
            else if (tool == EditorTool.Magnifier) ra = new MagnifierAnnotation();
            else ra = new ShapeAnnotation { Ellipse = tool == EditorTool.Ellipse };
            ApplyStyle(ra, _styles.Current(tool));
            Point a = Clamp(p);
            _rectAnchor = new Rect(a, a);
            ra.R = new Rect(a, a);
            _items.Add(ra);
            _selected = ra;
            _mode = DragMode.DrawRect;
            UpdateAnnoClip();
            _canvas.CaptureMouse();
            RebuildAnnotations();
        }

        private void StartBrush(Point p, bool marker)
        {
            BrushAnnotation b = new BrushAnnotation { Marker = marker };
            ApplyStyle(b, _styles.Current(marker ? EditorTool.Marker : EditorTool.Brush));
            _brushFirst = Clamp(p);
            b.Pts.Add(_brushFirst);
            _items.Add(b);
            _selected = b;
            _mode = DragMode.DrawBrush;
            UpdateAnnoClip();
            _canvas.CaptureMouse();
            RebuildAnnotations();
        }

        private void PlaceStep(Point p)
        {
            int next = StepAnnotation.StartNumber;
            foreach (Annotation a in _items) { StepAnnotation s = a as StepAnnotation; if (s != null && s.Number >= next) next = s.Number + 1; }
            StepAnnotation st = new StepAnnotation();
            ApplyStyle(st, _styles.Current(EditorTool.Steps));
            st.Number = next;
            Point pp = ClampToSel(p);
            if (SnapHeld()) { pp = ClampToSel(SnapCenter(pp, null)); ClearGuides(); }   // the snap key: in line with the steps already there
            st.Pos = pp;
            _items.Add(st);
            _selected = st;
            Commit();
            RebuildAnnotations();
        }

    }
}
