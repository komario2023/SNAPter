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
        // ---- the round outline of the eraser and a dashed frame round what it would remove ----
        private Ellipse _eraserRing, _eraserRingUnder;
        private Canvas _hoverLayer;

        private void UpdateEraserAids(Point p)
        {
            bool on = _editor && _tool == EditorTool.Eraser && _hasSel && (SelContains(p) || _mode == DragMode.Erase) && _editBox == null;
            if (_eraserRing == null)
            {
                _eraserRingUnder = new Ellipse { Stroke = Brushes.White, StrokeThickness = 3.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _eraserRing = new Ellipse { Stroke = Brushes.Black, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _hoverLayer = new Canvas { IsHitTestVisible = false, Width = _canvas.Width, Height = _canvas.Height };
                _canvas.Children.Add(_hoverLayer);
                _canvas.Children.Add(_eraserRingUnder);
                _canvas.Children.Add(_eraserRing);
            }
            _hoverLayer.Children.Clear();
            if (!on) { _eraserRing.Visibility = Visibility.Collapsed; _eraserRingUnder.Visibility = Visibility.Collapsed; return; }
            double d = Math.Max(4, _eraserSize);
            foreach (Ellipse e in new Ellipse[] { _eraserRingUnder, _eraserRing })
            {
                e.Width = d; e.Height = d; e.Visibility = Visibility.Visible;
                Canvas.SetLeft(e, p.X - d / 2); Canvas.SetTop(e, p.Y - d / 2);
            }
            foreach (Annotation a in HitDisc(p, d / 2))
            {
                Rect b = a.Bounds(); b.Inflate(3, 3);
                Rectangle r = new Rectangle { Width = Math.Max(1, b.Width), Height = Math.Max(1, b.Height), Stroke = Brush(_theme.Accent), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }), IsHitTestVisible = false };
                Canvas.SetLeft(r, b.X); Canvas.SetTop(r, b.Y);
                _hoverLayer.Children.Add(r);
            }
        }

        /// <summary>Pipette: the color of the screen pixel becomes the color of the current tool (or of the selected object).</summary>
        private void PickAt(Point p)
        {
            WpfColor c;
            if (!Backdrop.Pixel(p, out c)) return;
            _styles.AddRecent(c); _recentShown = -1;
            bool hadSelection = _selected != null;
            SetColor(c);
            SetTool(hadSelection ? EditorTool.Select : _lastTool);
        }

        private void UpdatePickChip(Point p)
        {
            WpfColor c;
            if (!_editor || _tool != EditorTool.Picker || !Backdrop.Pixel(p, out c))
            {
                if (_pickChip != null) _pickChip.Visibility = Visibility.Collapsed;
                return;
            }
            if (_pickChip == null)
            {
                _pickSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brush(_theme.Border) };
                _pickText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Brush(_theme.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(_pickSwatch);
                sp.Children.Add(_pickText);
                _pickChip = new Border { Child = sp, Background = Brush(_theme.Surface), BorderBrush = Brush(_theme.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(5), IsHitTestVisible = false };
                _canvas.Children.Add(_pickChip);
            }
            _pickSwatch.Background = Brush(c);
            _pickText.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            _pickChip.Visibility = Visibility.Visible;
            double x = p.X + 20, y = p.Y + 20;
            if (x + 120 > _canvas.Width) x = p.X - 130;
            if (y + 40 > _canvas.Height) y = p.Y - 44;
            Canvas.SetLeft(_pickChip, Math.Max(2, x));
            Canvas.SetTop(_pickChip, Math.Max(2, y));
        }

        // ---- own color picker (hue bar, saturation/brightness square, hex field) ----
        private Border _colorPop;
        private Rectangle _svBase;
        private Border _svMark, _hueMark, _popPreview;
        private TextBox _popHex;
        private double _hue, _sat = 1, _val = 1;
        private int _alpha = 255;                              // 0..255: the transparency of the chosen color
        private Slider _alphaSlider;
        private TextBlock _alphaText;
        private bool _popBusy;

        private static WpfColor FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return WpfColor.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static void ToHsv(WpfColor c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = 0;
            if (d > 1e-9)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
            }
            if (h < 0) h += 360;
            s = max <= 0 ? 0 : d / max;
            v = max;
        }

        private const double PopW = 200, PopSvH = 130, PopHueH = 16;

        private WpfColor PopupColor() { WpfColor c = FromHsv(_hue, _sat, _val); return WpfColor.FromArgb((byte)_alpha, c.R, c.G, c.B); }

        private void BuildColorPopup()
        {
            Canvas sv = new Canvas { Width = PopW, Height = PopSvH, Cursor = Cursors.Cross, ClipToBounds = true, Background = Brushes.Transparent };
            _svBase = new Rectangle { Width = PopW, Height = PopSvH };
            Rectangle white = new Rectangle { Width = PopW, Height = PopSvH, IsHitTestVisible = false };
            white.Fill = new LinearGradientBrush(Colors.White, WpfColor.FromArgb(0, 255, 255, 255), 0);
            Rectangle black = new Rectangle { Width = PopW, Height = PopSvH, IsHitTestVisible = false };
            black.Fill = new LinearGradientBrush(WpfColor.FromArgb(0, 0, 0, 0), Colors.Black, 90);
            _svMark = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), IsHitTestVisible = false };
            _svMark.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 };
            sv.Children.Add(_svBase); sv.Children.Add(white); sv.Children.Add(black); sv.Children.Add(_svMark);
            Action<Point> setSv = delegate (Point q)
            {
                _sat = Math.Max(0, Math.Min(1, q.X / PopW));
                _val = 1 - Math.Max(0, Math.Min(1, q.Y / PopSvH));
                PopupUpdate(true);
            };
            sv.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { sv.CaptureMouse(); setSv(e.GetPosition(sv)); e.Handled = true; };
            sv.MouseMove += delegate (object s, MouseEventArgs e) { if (sv.IsMouseCaptured) setSv(e.GetPosition(sv)); };
            sv.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { sv.ReleaseMouseCapture(); e.Handled = true; };

            Canvas hue = new Canvas { Width = PopW, Height = PopHueH, Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 8, 0, 0) };
            LinearGradientBrush hb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i <= 6; i++) hb.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
            hue.Children.Add(new Rectangle { Width = PopW, Height = PopHueH, Fill = hb, RadiusX = 3, RadiusY = 3, IsHitTestVisible = false });
            _hueMark = new Border { Width = 6, Height = PopHueH + 4, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
            _hueMark.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8 };
            Canvas.SetTop(_hueMark, -2);
            hue.Children.Add(_hueMark);
            Action<Point> setHue = delegate (Point q) { _hue = Math.Max(0, Math.Min(359.9, q.X / PopW * 360)); PopupUpdate(true); };
            hue.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { hue.CaptureMouse(); setHue(e.GetPosition(hue)); e.Handled = true; };
            hue.MouseMove += delegate (object s, MouseEventArgs e) { if (hue.IsMouseCaptured) setHue(e.GetPosition(hue)); };
            hue.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { hue.ReleaseMouseCapture(); e.Handled = true; };

            _popPreview = new Border { Width = 30, Height = 26, CornerRadius = new CornerRadius(5), BorderBrush = Brush(_theme.Border), BorderThickness = new Thickness(1) };
            _popHex = new TextBox { Width = 84, FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _popHex.Background = Brush(_theme.Bg);
            _popHex.Foreground = Brush(_theme.Text);
            _popHex.BorderBrush = Brush(_theme.Border);
            _popHex.TextChanged += delegate
            {
                if (_popHex.IsKeyboardFocusWithin)
                {
                    try
                    {
                        WpfColor c = (WpfColor)ColorConverter.ConvertFromString(_popHex.Text.Trim());
                        ToHsv(c, out _hue, out _sat, out _val);
                        _alpha = c.A;
                        PopupUpdate(false);
                    }
                    catch { }
                }
            };
            _popHex.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyPopupColor(); e.Handled = true; } };
            Border ok = MakeButton(_i18n.T("actions.ok"), true, ApplyPopupColor);
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(_popPreview); row.Children.Add(_popHex); row.Children.Add(ok);

            // transparency of the color (the "A" of #AARRGGBB): 100% = solid
            _alphaText = new TextBlock { FontSize = 12, Foreground = Brush(_theme.Text), Margin = new Thickness(0, 8, 0, 0) };
            _alphaSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100, SmallChange = 1, LargeChange = 10, Focusable = false, Width = PopW };
            _alphaSlider.ToolTip = _i18n.T("props.alpha");
            _alphaSlider.ValueChanged += delegate { if (_popBusy) return; _alpha = (int)Math.Round(_alphaSlider.Value * 2.55); PopupUpdate(true); };

            StackPanel col = new StackPanel();
            col.Children.Add(sv); col.Children.Add(hue); col.Children.Add(_alphaText); col.Children.Add(_alphaSlider); col.Children.Add(row);
            _colorPop = MakePanel(col);
            _colorPop.Padding = new Thickness(10);
            _canvas.Children.Add(_colorPop);
        }

        private void PopupUpdate(bool updateHex)
        {
            WpfColor pure = FromHsv(_hue, 1, 1);
            _svBase.Fill = Brush(pure);
            Canvas.SetLeft(_svMark, _sat * PopW - 6);
            Canvas.SetTop(_svMark, (1 - _val) * PopSvH - 6);
            Canvas.SetLeft(_hueMark, _hue / 360 * PopW - 3);
            WpfColor c = PopupColor();
            _popPreview.Background = Brush(c);
            _popBusy = true;
            _alphaSlider.Value = Math.Round(_alpha / 2.55);
            _alphaText.Text = _i18n.T("props.alpha") + ": " + (int)Math.Round(_alpha / 2.55) + "%";
            _popBusy = false;
            if (updateHex) _popHex.Text = (c.A == 255 ? "#" : "#" + c.A.ToString("X2")) + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private void ShowColorPopup()
        {
            if (_colorPop == null) BuildColorPopup();
            if (_colorPop.Visibility == Visibility.Visible) { _colorPop.Visibility = Visibility.Collapsed; return; }
            WpfColor cur = ActiveColor();
            ToHsv(cur, out _hue, out _sat, out _val);
            _alpha = cur.A;
            _colorPop.Visibility = Visibility.Visible;
            PopupUpdate(true);
            _colorPop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _colorPop.DesiredSize.Width, h = _colorPop.DesiredSize.Height;
            Rect pb = _panelB != null ? new Rect(Canvas.GetLeft(_panelB), Canvas.GetTop(_panelB), _panelB.DesiredSize.Width, _panelB.DesiredSize.Height) : _sel;
            double x = pb.Left + 80, y = pb.Bottom + 6;
            if (y + h > _canvas.Height - 4) y = pb.Top - h - 6;
            x = Math.Max(4, Math.Min(_canvas.Width - w - 4, x));
            y = Math.Max(4, Math.Min(_canvas.Height - h - 4, y));
            Canvas.SetLeft(_colorPop, x);
            Canvas.SetTop(_colorPop, y);
        }

        private void ApplyPopupColor()
        {
            WpfColor c = PopupColor();
            _colorPop.Visibility = Visibility.Collapsed;
            _styles.AddRecent(c); _recentShown = -1;
            SetColor(c);
            Focus();
        }

        private void PickCustomColor() { ShowColorPopup(); }

        // ---- window under the cursor: a plain click selects the whole window ----
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int L, T, R, B; }
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out NativeRect r, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

        private bool TryWindowAt(Point canvasPt, out Rect result)
        {
            result = Rect.Empty;
            int sx = (int)(canvasPt.X + _bounds.X), sy = (int)(canvasPt.Y + _bounds.Y);
            Rect found = Rect.Empty;
            try
            {
                EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    if (h == _hwnd || !IsWindowVisible(h) || IsIconic(h)) return true;
                    int cloaked;
                    if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                    NativeRect rc;
                    if (DwmGetWindowAttribute(h, 9, out rc, Marshal.SizeOf(typeof(NativeRect))) != 0) return true;
                    if (sx < rc.L || sx >= rc.R || sy < rc.T || sy >= rc.B) return true;
                    System.Text.StringBuilder cn = new System.Text.StringBuilder(64);
                    GetClassName(h, cn, 64);
                    string cls = cn.ToString();
                    if (cls == "Progman" || cls == "WorkerW") return true;
                    found = new Rect(rc.L - _bounds.X, rc.T - _bounds.Y, rc.R - rc.L, rc.B - rc.T);
                    return false;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Log.Write("window snap error: " + ex.Message); return false; }
            if (found.IsEmpty) return false;
            found.Intersect(new Rect(0, 0, _canvas.Width, _canvas.Height));
            if (found.IsEmpty || found.Width < 8 || found.Height < 8) return false;
            result = found;
            return true;
        }

        // ---- select everything, apply an initial region ----
        private void SelectAll()
        {
            Rect r;
            try
            {
                System.Drawing.Rectangle m = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
                r = new Rect(m.X - _bounds.X, m.Y - _bounds.Y, m.Width, m.Height);
            }
            catch { r = new Rect(0, 0, _canvas.Width, _canvas.Height); }
            SetRegion(r);
        }

        private void SetRegion(Rect r)
        {
            if (_hint != null) { _canvas.Children.Remove(_hint); _hint = null; }
            ClearAnnotations();
            _sel = r; _hasSel = true;
            UpdateVisuals();
            if (_autoCopy) { DoCopy(); return; }
            ShowPanels();
        }

        // ================= results =================
        private Int32Rect CropRect()
        {
            int x = (int)Math.Round(_sel.X), y = (int)Math.Round(_sel.Y);
            int w = (int)Math.Round(_sel.Width), h = (int)Math.Round(_sel.Height);
            x = Math.Max(0, Math.Min(_shot.Width - 1, x));
            y = Math.Max(0, Math.Min(_shot.Height - 1, y));
            w = Math.Max(1, Math.Min(_shot.Width - x, w));
            h = Math.Max(1, Math.Min(_shot.Height - y, h));
            return new Int32Rect(x, y, w, h);
        }

        /// <summary>The selected area as a bitmap with all annotations; with rounded corners when the region is rounded.</summary>
        private System.Drawing.Bitmap ExportSelection()
        {
            System.Drawing.Bitmap raw = ExportPlain();
            if (!_beautify) return raw;
            BeautifyOptions bo = BeautifyOpts(_beautifyStyle);                   // the look chosen for this capture (hold the button "Beautiful"; may be the random one)
            System.Drawing.Bitmap dressed = Beautifier.Apply(raw, bo);           // background, space, shadow (RegionOverlay.Beautify.cs)
            raw.Dispose();
            return dressed;
        }

        /// <summary>The selected area with the drawings (and rounded corners), without the beautiful look.</summary>
        private System.Drawing.Bitmap ExportPlain()
        {
            System.Drawing.Bitmap raw = ExportRaw();
            if (_regionRounded) raw = RoundCorners(raw);
            return raw;
        }

        private System.Drawing.Bitmap ExportRaw()
        {
            if (_editBox != null) EndEdit(true);
            Int32Rect r = CropRect();
            if (_items.Count == 0)
                return _shot.Clone(new System.Drawing.Rectangle(r.X, r.Y, r.Width, r.Height), _shot.PixelFormat);

            Canvas root = new Canvas();
            root.Width = r.Width; root.Height = r.Height;
            root.ClipToBounds = true;
            Image img = new Image();
            img.Source = new CroppedBitmap(_source, r);
            img.Width = r.Width; img.Height = r.Height;
            img.Stretch = Stretch.None;
            root.Children.Add(img);

            Canvas layer = new Canvas();
            layer.Width = _bounds.Width; layer.Height = _bounds.Height;
            layer.RenderTransform = new TranslateTransform(-r.X, -r.Y);
            foreach (Annotation a in _items) layer.Children.Add(a.BuildVisual());
            root.Children.Add(layer);

            root.Measure(new Size(r.Width, r.Height));
            root.Arrange(new Rect(0, 0, r.Width, r.Height));
            root.UpdateLayout();

            RenderTargetBitmap rtb = new RenderTargetBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream())
            {
                enc.Save(ms);
                ms.Position = 0;
                using (System.Drawing.Bitmap tmp = new System.Drawing.Bitmap(ms))
                    return new System.Drawing.Bitmap(tmp);
            }
        }

        private void DoCopy()
        {
            if (!_hasSel) return;
            if (PickMode != null) { RaisePicked(); return; }   // only an area is wanted (recording, scrolling screenshot)
            System.Drawing.Bitmap bmp = ExportSelection();
            try { Action<System.Drawing.Bitmap> h = CopyRequested; if (h != null) h(bmp); }
            catch (Exception ex) { Log.Write("copy error: " + ex.Message); }
            finally { bmp.Dispose(); }
            Close();
        }

        private void DoSave()
        {
            Log.Debug("DoSave hasSel=" + _hasSel);
            if (!_hasSel) return;
            System.Drawing.Bitmap bmp = ExportSelection();
            try { Action<System.Drawing.Bitmap> h = SaveRequested; if (h != null) h(bmp); }
            catch (Exception ex) { Log.Write("save error: " + ex.Message); }
            finally { bmp.Dispose(); }
            Close();
        }

        /// <summary>"Save as...": the dialog is shown after the overlay is gone.</summary>
        private void DoSaveAs()
        {
            if (!_hasSel) return;
            Log.Write("save as requested");
            System.Drawing.Bitmap bmp, copy;
            try { bmp = ExportSelection(); copy = new System.Drawing.Bitmap(bmp); bmp.Dispose(); }
            catch (Exception ex) { Log.Write("save as export error: " + ex.Message); return; }
            Action<System.Drawing.Bitmap> h = SaveAsRequested;
            Close();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try { if (h != null) h(copy); }
                catch (Exception ex) { Log.Write("save as error: " + ex.Message); }
                finally { copy.Dispose(); }
            }));
        }

        /// <summary>"Upload": the chosen target (folder or Google Drive) gets the picture; the link goes into the clipboard.</summary>
        private void DoUpload()
        {
            if (!_hasSel) return;
            System.Drawing.Bitmap bmp = ExportSelection();
            try { Action<System.Drawing.Bitmap> h = UploadRequested; if (h != null) h(bmp); }
            catch (Exception ex) { Log.Write("upload error: " + ex.Message); }
            finally { bmp.Dispose(); }
            Close();
        }

        /// <summary>"Pin": the region stays on the screen in its own window, above all other windows.</summary>
        private void DoPin()
        {
            if (!_hasSel) return;
            System.Drawing.Bitmap bmp = ExportSelection();
            System.Drawing.Rectangle where = new System.Drawing.Rectangle(_bounds.X + (int)Math.Round(_sel.X), _bounds.Y + (int)Math.Round(_sel.Y), bmp.Width, bmp.Height);
            try { Action<System.Drawing.Bitmap, System.Drawing.Rectangle> h = PinRequested; if (h != null) h(bmp, where); }
            catch (Exception ex) { Log.Write("pin error: " + ex.Message); }
            finally { bmp.Dispose(); }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // the region is remembered BEFORE the Closed event is raised: the program reads it there ("repeat the last region")
            if (_hasSel && _sel.Width >= 4 && _sel.Height >= 4)
                LastRegion = new System.Drawing.Rectangle(_bounds.X + (int)Math.Round(_sel.X), _bounds.Y + (int)Math.Round(_sel.Y), (int)Math.Round(_sel.Width), (int)Math.Round(_sel.Height));
            base.OnClosed(e);
            if (_dirtyStyles && !NoPersist && !_preview)
            {
                try { _settings.SavePresets(_styles.ToJson()); Log.Write("presets saved"); }
                catch (Exception ex) { Log.Write("presets save error: " + ex.Message); }
            }
            if (_editor && _settings.RememberTool && !NoPersist && !_preview)
            {
                try
                {
                    string last = StyleStore.Key(_tool == EditorTool.Picker || _tool == EditorTool.Eraser ? _lastTool : _tool);
                    if (last != _settings.LastTool)
                    {
                        Settings.PatchUser(r => { Settings.Section(r, "state")["last_tool"] = last; });
                        _settings.LastTool = last;
                    }
                }
                catch (Exception ex) { Log.Write("last tool save error: " + ex.Message); }
            }
        }

        // ================= hooks for the self test (same code paths as the mouse and keyboard) =================
        internal bool TestDimmed { get { return _dim.Data != null; } }
        internal void TestEnter(ModifierKeys m) { RunEnterAction(m); }
        internal EditorTool TestTool { get { return _tool; } }
        internal void TestModifierTap(ModifierKeys m) { ModifierTap(m); }
        internal bool TestRegionRounded { get { return _regionRounded; } }
        internal bool TestArrowPanelVisible { get { return _panelC != null && _panelC.Visibility == Visibility.Visible; } }
        internal Rect TestPanelC { get { return PanelRect(_panelC); } }
        internal void TestArrowDirection(int dx, int dy) { SetArrowDirection(dx, dy); }
        internal void TestRotateArrow(double deg) { RotateArrow(deg); }
        internal void TestFlipArrow() { FlipArrow(); }
        internal Rect TestSel { get { return _sel; } }
        internal void TestDeselectAll() { _selected = null; UpdateChrome(); }
        internal void TestRebuildKeys() { _toolKeys.Clear(); _presetKeys.Clear(); BuildKeyMaps(); }
        internal void TestTypeSize(string s) { BeginSizeEdit(); if (_sizeEdit != null) { _sizeEdit.Text = s; EndSizeEdit(true); } }
        internal void TestCycleOpacity() { CycleOpacity(); }
        internal int TestPresetButtons { get { return _presetButtons.Count; } }
        internal void TestDeselect() { Select(null); }
        internal Annotation TestLastItem { get { return _items.Count > 0 ? _items[_items.Count - 1] : null; } }
        internal void TestSetPreset(int i) { SetPreset(i); }
        internal ToolStyle TestStyle(EditorTool t) { return _styles.Current(t); }
        internal int TestPresetIndex(EditorTool t) { return _styles.Active(t); }
        internal void TestCycleTextStyle() { CycleTextStyle(); }
        internal void TestCycleFill() { CycleFill(); }
        internal void TestDown(Point p, int clicks) { HandleDown(p, clicks, null); }
        internal void TestMove(Point p, ModifierKeys mods) { HandleMove(p, mods); }
        internal void TestUp() { HandleUp(); }
        internal bool TestKey(Key k, ModifierKeys m) { return HandleKey(k, m); }
        internal void TestSetTool(EditorTool t) { SetTool(t); }
        internal void TestSetSelection(Rect r) { _sel = r; _hasSel = true; UpdateVisuals(); ShowPanels(); }
        internal System.Drawing.Bitmap TestCrop() { return ExportSelection(); }
        internal int TestItemCount { get { return _items.Count; } }
        internal Annotation TestSelected { get { return _selected; } }
        internal Annotation TestItem(int i) { return _items[i]; }
        internal bool TestPanelsVisible { get { return _panelB != null && _panelB.Visibility == Visibility.Visible; } }
        internal Rect TestPanelA { get { return PanelRect(_panelA); } }
        internal Rect TestPanelB { get { return PanelRect(_panelB); } }
        internal bool TestEditing { get { return _editBox != null; } }
        internal void TestSetEditText(string s) { if (_editBox != null) _editBox.Text = s; }
        internal void TestEndEdit() { EndEdit(true); }
        internal void TestStepSize(int d) { StepSize(d); }
        internal void TestSetColor(WpfColor c) { SetColor(c); }
        internal void TestPercent(int which) { ApplyPercentSize(which); }
        internal double TestSessionSize(EditorTool t) { double v; return _sessionSize.TryGetValue(t, out v) ? v : -1; }
        internal double TestEraserSize { get { return _eraserSize; } }
        internal void TestApplySize(double v) { ApplySizeValue(v, true); }
        internal string TestResolveEnter(string combo) { return ResolveEnter(combo); }
        internal static Rect TestBlend(Point a, Point c, double constrainT, double centerT, Rect limit) { return DragRectBlend(a, c, constrainT, centerT, limit); }
        internal Point TestSnapCenter(Point p) { Point r = SnapCenter(p, null); ClearGuides(); return r; }
        internal static double[] TestParsePcts(string s) { return ParsePcts(s, new double[] { -1, -1, -1 }); }

        private Rect PanelRect(Border b)
        {
            if (b == null) return Rect.Empty;
            return new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.DesiredSize.Width, b.DesiredSize.Height);
        }
    }
}
