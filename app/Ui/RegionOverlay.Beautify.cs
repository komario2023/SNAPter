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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
namespace Snapter
{
    internal sealed partial class RegionOverlay
    {
        // ================= beautiful screenshot: a switch and a choice for this capture =================
        // A click on the button "Beautiful" switches the look on or off (like Ctrl+B). Holding the button (or the right button) opens a short LIST of the
        // looks (names only, scrolls, so even hundreds of looks fit). A choice switches the look on and shows ONE picture of it, at 100% size, for a moment.
        private bool _beautify;                                    // starts as beautify.enabled; the button or Ctrl+B changes it for this capture
        private string _beautifyStyle = "classic";                 // the look of this capture (starts as beautify.style, the last chosen look is remembered)
        private Border _beautifyBtn;
        private Border _beautifyPop;                               // the list
        private Border _beautifyPreview;                           // the picture of the chosen look
        private DispatcherTimer _beautifyPreviewTimer;
        private Border _beautifyTemplate;                          // the SAMPLE (template) of the look under the pointer in the list: a neutral page in that look, not your screenshot
        private BeautifyLook _randomLook;                          // the last "Random" look of this capture (id "random"), never stored
        private readonly Random _rng = new Random();

        /// <summary>The options of the beautiful look for a style id: the settings plus the owner's looks plus the random look of this capture.</summary>
        private BeautifyOptions BeautifyOpts(string style)
        {
            BeautifyOptions o = BeautifyOptions.FromSettings(_settings);
            o.Enabled = true; o.Style = style;
            o.Looks = new Dictionary<string, BeautifyLook>(o.Looks ?? new Dictionary<string, BeautifyLook>());
            if (_randomLook != null) o.Looks["random"] = _randomLook;
            return o;
        }

        private string BeautifyLookName(string id)
        {
            if (id == "random") return "\u2726 " + _i18n.T("beautify.style.random");
            BeautifyLook l = _settings.BeautifyLooks.FirstOrDefault(x => x.Id == id);
            if (l != null && l.Name.Length > 0) return l.Name;
            string key = "beautify.style." + id;
            string v = _i18n.T(key);
            return v == key ? id : v;
        }

        private void ToggleBeautify()
        {
            _beautify = !_beautify;
            UpdateBeautifyButton();
            ShowStrengthHint(_i18n.T(_beautify ? "msg.beautify_on" : "msg.beautify_off"));
            if (_beautify) ShowBeautifyPreview(); else HideBeautifyPreview();   // switched on: your screenshot in the look stays on the screen beside the region
        }

        private void UpdateBeautifyButton()
        {
            if (_beautifyBtn == null) return;
            // while the look is on the button stays clearly lit (a filled background and an accent outline), not just slightly grey
            _beautifyBtn.Background = _beautify ? Brush(_theme.SurfaceMuted) : Brushes.Transparent;
            _beautifyBtn.BorderBrush = _beautify ? Brush(_theme.Accent) : Brushes.Transparent;
            _beautifyBtn.BorderThickness = new Thickness(_beautify ? 2 : 0);
        }

        /// <summary>A click switches, a long press (0.45 s) or the right button opens the list. The click that ends a long press does not switch.</summary>
        private void HookBeautifyLongPress(Border b)
        {
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            bool fired = false;
            t.Tick += delegate { t.Stop(); fired = true; OpenBeautifyMenu(); };
            b.PreviewMouseLeftButtonDown += delegate { fired = false; t.Stop(); t.Start(); };
            b.PreviewMouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { t.Stop(); if (fired) { e.Handled = true; fired = false; } };
            b.MouseLeave += delegate { t.Stop(); };
            b.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; OpenBeautifyMenu(); };
        }

        private bool BeautifyMenuOpen { get { return _beautifyPop != null && _beautifyPop.Visibility == Visibility.Visible; } }

        private void CloseBeautifyMenu(bool keepPreview = true)
        {
            if (_beautifyPop != null) { _canvas.Children.Remove(_beautifyPop); _beautifyPop = null; }
            _previewCache.Clear();
            HideBeautifyTemplate();
        }

        /// <summary>While the pointer moves over the list: a SAMPLE of the look - a neutral page (a picture, lines, a button) in that look with the background,
        /// the turn and the frame - next to the list, so it is clear which template it is. Your screenshot does not change until you click an item.</summary>
        private void ShowBeautifyTemplate(string id)
        {
            HideBeautifyTemplate();
            if (_beautifyPop == null) return;
            try
            {
                BitmapSource src;
                string key = "t:" + id;
                if (!_previewCache.TryGetValue(key, out src))
                {
                    using (System.Drawing.Bitmap small = SettingsWindow.BeautifySample())
                    using (System.Drawing.Bitmap sample = new System.Drawing.Bitmap(small.Width * 4 / 3, small.Height * 4 / 3))
                    {
                        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(sample))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(small, 0, 0, sample.Width, sample.Height);
                        }
                        BeautifyOptions o = BeautifyOpts(id);
                        using (System.Drawing.Bitmap dressed = Beautifier.Apply(sample, o)) src = Beautifier.ToSource(dressed);
                    }
                    _previewCache[key] = src;
                }
                Rect work = WorkArea();
                double pad = 10, k = Math.Min(1.0, Math.Min((work.Width / 2 - 60) / src.PixelWidth, (work.Height - 120) / src.PixelHeight));
                Image im = new Image { Source = src, Width = src.PixelWidth * k, Height = src.PixelHeight * k, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(im, k < 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                StackPanel col = new StackPanel();
                col.Children.Add(new TextBlock { Text = _i18n.T("beautify.template") + ": " + BeautifyLookName(id), FontSize = 12, Foreground = Brush(_theme.TextSecondary), Margin = new Thickness(2, 0, 0, 6) });
                col.Children.Add(im);
                _beautifyTemplate = new Border
                {
                    Child = col, Padding = new Thickness(pad), CornerRadius = new CornerRadius(Math.Max(6, _theme.RadiusMd)), IsHitTestVisible = false,
                    Background = new SolidColorBrush(WpfColor.FromArgb(248, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B)),
                    BorderBrush = new SolidColorBrush(_theme.Accent), BorderThickness = new Thickness(2),
                    Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black }
                };
                Panel.SetZIndex(_beautifyTemplate, 90);
                _canvas.Children.Add(_beautifyTemplate);
                _beautifyTemplate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = _beautifyTemplate.DesiredSize.Width, h = _beautifyTemplate.DesiredSize.Height;
                double lx = Canvas.GetLeft(_beautifyPop), ly = Canvas.GetTop(_beautifyPop), lw = _beautifyPop.DesiredSize.Width, lh = _beautifyPop.DesiredSize.Height;
                // beside the list: on the side where there is more room (the panel is at the right - the sample goes left, and the other way round)
                double x = (work.Right - (lx + lw) >= lx - work.Left) ? lx + lw + 10 : lx - w - 10;
                double y = ly + lh / 2 - h / 2;
                x = Math.Max(work.Left + 4, Math.Min(work.Right - w - 4, x));
                y = Math.Max(work.Top + 4, Math.Min(work.Bottom - h - 4, y));
                Canvas.SetLeft(_beautifyTemplate, x); Canvas.SetTop(_beautifyTemplate, y);
            }
            catch (Exception ex) { Log.Write("beautify template error: " + ex.Message); }
        }

        private void HideBeautifyTemplate()
        {
            if (_beautifyTemplate != null) { _canvas.Children.Remove(_beautifyTemplate); _beautifyTemplate = null; }
        }

        private readonly Dictionary<string, BitmapSource> _previewCache = new Dictionary<string, BitmapSource>();   // pictures of the looks while the list is open

        /// <summary>The list of the looks: "No look" and every look by name, the current one marked. Scrolls when it is long.</summary>
        private void OpenBeautifyMenu()
        {
            if (BeautifyMenuOpen) { CloseBeautifyMenu(); return; }
            if (_colorPop != null) _colorPop.Visibility = Visibility.Collapsed;
            if (_editBox != null) EndEdit(true);

            StackPanel items = new StackPanel();
            List<string> ids = new List<string> { "random" };                       // the first item: a surprise
            ids.AddRange(BeautifyStyles.AllIds(_settings.BeautifyLooks));            // the built-in looks and the owner's own ones
            ids.Add("off");
            foreach (string id in ids)
            {
                string pick = id;
                bool current = id == "off" ? !_beautify : (_beautify && id == _beautifyStyle);
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = current ? "✓" : "", Foreground = Brush(_theme.Accent), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                TextBlock name = new TextBlock { Text = BeautifyLookName(id), FontSize = 13, Foreground = Brush(_theme.Text), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1); row.Children.Add(name);
                Border item = new Border { Child = row, Padding = new Thickness(6, 7, 14, 7), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1) };
                item.MouseEnter += delegate
                {
                    item.Background = Brush(_theme.SurfaceMuted);
                    // moving over the list shows the SAMPLE of the look beside the list (a template, not your screenshot), so it is clear what "Window" or "Windows" is;
                    // the surprise and "No look" show nothing. Only a click changes the screenshot.
                    if (pick == "random" || pick == "off") HideBeautifyTemplate(); else ShowBeautifyTemplate(pick);
                };
                item.MouseLeave += delegate { item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; PickBeautify(pick); };
                items.Children.Add(item);
                if (id == "random" || id == ids[ids.Count - 2]) items.Children.Add(new Border { Height = 1, Background = Brush(_theme.Border), Margin = new Thickness(6, 4, 6, 4) });   // "No look" stands apart
            }
            ScrollViewer sv = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 340, Width = 232 };
            sv.PreviewMouseWheel += delegate (object s, MouseWheelEventArgs e) { sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 2.0); e.Handled = true; };   // the wheel scrolls the list, it never resizes the brush
            _beautifyPop = MakePanel(sv);
            _beautifyPop.Padding = new Thickness(6);
            _beautifyPop.Visibility = Visibility.Visible;
            Panel.SetZIndex(_beautifyPop, 100);                                 // the list is always above the picture
            _canvas.Children.Add(_beautifyPop);
            _beautifyPop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _beautifyPop.DesiredSize.Width, h = _beautifyPop.DesiredSize.Height;
            Rect work = WorkArea();
            Rect pb = _panelB != null ? new Rect(Canvas.GetLeft(_panelB), Canvas.GetTop(_panelB), _panelB.DesiredSize.Width, _panelB.DesiredSize.Height) : _sel;
            double bx = pb.Left + 80;
            try { if (_beautifyBtn != null) { Point bp = _beautifyBtn.TranslatePoint(new Point(_beautifyBtn.ActualWidth / 2, 0), _canvas); bx = bp.X; } } catch { }
            double x = bx - w / 2, y = pb.Bottom + 6;
            if (y + h > work.Bottom - 4) y = pb.Top - h - 6;                    // below the panel, or above it when there is no room
            x = Math.Max(work.Left + 4, Math.Min(work.Right - w - 4, x));
            y = Math.Max(work.Top + 4, Math.Min(work.Bottom - h - 4, y));
            Canvas.SetLeft(_beautifyPop, x); Canvas.SetTop(_beautifyPop, y);
        }

        private void PickBeautify(string id)
        {
            CloseBeautifyMenu(true);
            if (id == "off") { _beautify = false; HideBeautifyPreview(); }
            else if (id == "random")
            {
                // a surprise: a new random look every time, within what the settings allow (no dark ones, only some bases, how wild); it is not stored
                _randomLook = BeautifyLook.Random(_rng, "random", BeautifyRandomOptions.From(_settings));
                _beautify = true; _beautifyStyle = "random";
                UpdateBeautifyButton(); ShowBeautifyPreview();
            }
            else
            {
                _beautify = true; _beautifyStyle = id;
                RememberBeautifyStyle(id);
                UpdateBeautifyButton(); ShowBeautifyPreview();                   // your screenshot in the chosen look: it stays while the look is on
            }
            UpdateBeautifyButton();
            ShowStrengthHint(id == "off" ? _i18n.T("msg.beautify_off") : _i18n.T("msg.beautify_style", BeautifyLookName(id)));
            Focus();
        }

        /// <summary>The look that was chosen last becomes the look of Ctrl+B and of the next captures.</summary>
        private void RememberBeautifyStyle(string id)
        {
            _settings.BeautifyStyle = id;
            if (_preview || SettingsWindow.NoPersist) return;
            try { Settings.PatchUser(root => { Settings.Section(root, "beautify")["style"] = id; }); }
            catch (Exception ex) { Log.Write("beautify style save error: " + ex.Message); }
        }

        // ---- the live picture: your screenshot in the chosen look, on screen as long as the look is on ----
        /// <summary>Your real screenshot (with the drawings) in the chosen look at 100% of its size. It stays on the screen while the beautiful look is on,
        /// beside the region (on the side where there is room, never over the panels), so drawing goes on under it; it follows the drawings and the region.
        /// Only when no side has room it is shrunk, and only at last it lies over the region, letting the mouse through.</summary>
        private void ShowBeautifyPreview()
        {
            HideBeautifyPreview();
            if (!_beautify || !_hasSel) return;
            try
            {
                BitmapSource src;
                using (System.Drawing.Bitmap plain = ExportPlain())
                {
                    BeautifyOptions o = BeautifyOpts(_beautifyStyle);
                    using (System.Drawing.Bitmap dressed = Beautifier.Apply(plain, o)) src = Beautifier.ToSource(dressed);
                }
                Rect work = WorkArea();
                double pad = 8, capH = 22;
                double natW = src.PixelWidth + 2 * pad + 4, natH = src.PixelHeight + 2 * pad + 4 + capH;
                // free boxes beside the region; the panels are obstacles
                List<Rect> obstacles = new List<Rect>();
                foreach (Border pn in new Border[] { _panelA, _panelB, _panelC, _panelD })
                    if (pn != null && pn.Visibility == Visibility.Visible) { pn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); obstacles.Add(new Rect(Canvas.GetLeft(pn) - 4, Canvas.GetTop(pn) - 4, pn.DesiredSize.Width + 8, pn.DesiredSize.Height + 8)); }
                double gap = 10, bestK = 0, bx = 0, by = 0;
                Rect[] boxes =
                {
                    new Rect(_sel.Right + gap, work.Top + 4, work.Right - _sel.Right - gap - 4, work.Height - 8),           // right of the region
                    new Rect(work.Left + 4, work.Top + 4, _sel.Left - gap - work.Left - 4, work.Height - 8),               // left
                    new Rect(work.Left + 4, _sel.Bottom + gap, work.Width - 8, work.Bottom - _sel.Bottom - gap - 4),         // below
                    new Rect(work.Left + 4, work.Top + 4, work.Width - 8, _sel.Top - gap - work.Top - 4)                    // above
                };
                foreach (Rect box in boxes)
                {
                    if (box.Width < 120 || box.Height < 90) continue;
                    double k = Math.Min(1.0, Math.Min(box.Width / natW, box.Height / natH));
                    if (k < 0.4) continue;
                    double w = natW * k, h = natH * k;
                    // try the corners of the box, take the first that is free of the panels
                    Point[] spots = { new Point(box.Left, box.Top), new Point(box.Right - w, box.Top), new Point(box.Left, box.Bottom - h), new Point(box.Right - w, box.Bottom - h) };
                    foreach (Point sp in spots)
                    {
                        Rect r = new Rect(sp.X, sp.Y, w, h);
                        if (obstacles.Any(ob => ob.IntersectsWith(r))) continue;
                        if (k > bestK) { bestK = k; bx = sp.X; by = sp.Y; }
                        break;
                    }
                }
                double scale = bestK;
                bool over = bestK <= 0;
                if (over)
                {
                    scale = Math.Min(1.0, Math.Min((_sel.Width - 20) / natW, (_sel.Height - 20) / natH));
                    scale = Math.Max(0.25, scale);
                }
                double iw = src.PixelWidth * scale, ih = src.PixelHeight * scale;
                Image im = new Image { Source = src, Width = iw, Height = ih, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(im, scale < 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
                StackPanel col = new StackPanel();
                col.Children.Add(new TextBlock { Text = _i18n.T("beautify.live") + ": " + BeautifyLookName(_beautifyStyle), FontSize = 12, Foreground = Brush(_theme.TextSecondary), Margin = new Thickness(2, 0, 0, 4), IsHitTestVisible = false });
                col.Children.Add(im);
                _beautifyPreview = new Border
                {
                    Child = col, Padding = new Thickness(pad), CornerRadius = new CornerRadius(Math.Max(6, _theme.RadiusMd)), IsHitTestVisible = false,
                    Background = new SolidColorBrush(WpfColor.FromArgb(over ? (byte)225 : (byte)248, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B)),
                    BorderBrush = new SolidColorBrush(_theme.Accent), BorderThickness = new Thickness(2),
                    Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black }
                };
                Panel.SetZIndex(_beautifyPreview, 40);
                _canvas.Children.Add(_beautifyPreview);
                _beautifyPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double cw = _beautifyPreview.DesiredSize.Width, ch = _beautifyPreview.DesiredSize.Height;
                double x = over ? _sel.X + _sel.Width / 2 - cw / 2 : bx, y = over ? _sel.Y + _sel.Height / 2 - ch / 2 : by;
                x = Math.Max(work.Left + 4, Math.Min(work.Right - cw - 4, x));
                y = Math.Max(work.Top + 4, Math.Min(work.Bottom - ch - 4, y));
                Canvas.SetLeft(_beautifyPreview, x); Canvas.SetTop(_beautifyPreview, y);
            }
            catch (Exception ex) { Log.Write("beautify preview error: " + ex.Message); }
        }

        private void HideBeautifyPreview()
        {
            if (_beautifyPreviewTimer != null) _beautifyPreviewTimer.Stop();
            if (_beautifyPreview != null) { _canvas.Children.Remove(_beautifyPreview); _beautifyPreview = null; }
        }

        /// <summary>The live picture follows the drawings and the region: redrawn a moment after the last change.</summary>
        private void ScheduleBeautifyPreview()
        {
            if (!_beautify || !_hasSel || _preview) return;
            if (_beautifyPreviewTimer == null)
            {
                _beautifyPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
                _beautifyPreviewTimer.Tick += delegate { _beautifyPreviewTimer.Stop(); if (_mode == DragMode.None) ShowBeautifyPreview(); else ScheduleBeautifyPreview(); };
            }
            _beautifyPreviewTimer.Stop(); _beautifyPreviewTimer.Start();
        }
        // ---- self test hooks ----
        internal bool TestBeautify { get { return _beautify; } set { _beautify = value; UpdateBeautifyButton(); } }
        internal string TestBeautifyStyle { get { return _beautifyStyle; } }
        internal void TestPickBeautify(string id) { PickBeautify(id); }
        internal BeautifyLook TestRandomLook { get { return _randomLook; } }
        internal void TestHoverBeautify(string id) { ShowBeautifyTemplate(id); }
        internal bool TestBeautifyTemplateShown { get { return _beautifyTemplate != null; } }
        internal Rect TestBeautifyTemplateRect { get { return _beautifyTemplate == null ? Rect.Empty : new Rect(Canvas.GetLeft(_beautifyTemplate), Canvas.GetTop(_beautifyTemplate), _beautifyTemplate.DesiredSize.Width, _beautifyTemplate.DesiredSize.Height); } }
        internal Rect TestBeautifyListRect { get { return _beautifyPop == null ? Rect.Empty : new Rect(Canvas.GetLeft(_beautifyPop), Canvas.GetTop(_beautifyPop), _beautifyPop.DesiredSize.Width, _beautifyPop.DesiredSize.Height); } }
        internal bool TestBeautifyMenuOpen { get { return BeautifyMenuOpen; } }
        internal void TestOpenBeautifyMenu() { OpenBeautifyMenu(); }
        internal void TestToggleBeautify() { ToggleBeautify(); }
        internal bool TestBeautifyPreviewShown { get { return _beautifyPreview != null; } }
        internal Size TestBeautifyPreviewSize
        {
            get
            {
                StackPanel sp = _beautifyPreview == null ? null : _beautifyPreview.Child as StackPanel;
                Image im = sp == null ? null : sp.Children.OfType<Image>().FirstOrDefault();
                return im == null ? Size.Empty : new Size(im.Width, im.Height);
            }
        }
        internal Rect TestBeautifyPreviewRect { get { return _beautifyPreview == null ? Rect.Empty : new Rect(Canvas.GetLeft(_beautifyPreview), Canvas.GetTop(_beautifyPreview), _beautifyPreview.DesiredSize.Width, _beautifyPreview.DesiredSize.Height); } }
        internal Rect TestSelRect { get { return _sel; } }
        internal System.Drawing.Bitmap TestExport() { return ExportSelection(); }
        /// <summary>Draws the open list or the picture into a PNG file (for a look at it; used by the self test only when SNAPTER_SELFTEST_SHOTS is set).</summary>
        internal void TestSaveElement(bool preview, string file)
        {
            FrameworkElement el = preview ? (FrameworkElement)_beautifyPreview : _beautifyPop;
            if (el == null) return;
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size s = el.DesiredSize;
            el.Arrange(new Rect(0, 0, s.Width, s.Height));
            el.UpdateLayout();
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)Math.Ceiling(s.Width), (int)Math.Ceiling(s.Height), 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, s.Width, s.Height));
                dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, s.Width, s.Height));
            }
            rtb.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }
    }
}
