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
    /// <summary>
    /// Full-screen overlay over all monitors: frozen screenshot, dim, selection frame with 8 handles,
    /// size label and the editor (arrows and text, undo/redo) with two panels.
    /// Everything is drawn in real pixels: the root has a scale transform that cancels the window DPI.
    /// Annotations are objects (see Annotations.cs) rendered on top of the picture; the image is never changed.
    /// </summary>
    internal sealed partial class RegionOverlay : Window
    {
        private readonly double _handleSize;
        private const double Hit = 8;

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private enum DragMode { None, New, Move, Resize, AnnoMove, ArrowEnd, DrawArrow, DrawRect, AnnoResize, DrawBrush, EditMove, Erase, Marquee, Rotate, TextResize }

        /// <summary>Set by the self test so that it does not overwrite the user's saved presets.</summary>
        internal static bool NoPersist;

        private const double EditPad = 7; // border + padding around the text box while typing

        private static readonly string[] PaletteHex = { "#E5322D", "#FF8A00", "#FFD400", "#2FB24C", "#1E88E5", "#8E44AD", "#111111", "#FFFFFF" };

        private readonly System.Drawing.Bitmap _shot;
        private readonly BitmapSource _source;
        private readonly System.Drawing.Rectangle _bounds;
        private readonly Theme _theme;
        private readonly I18n _i18n;
        private Settings _settings;                         // replaced while the profile key walks through the profiles (RegionOverlay.Profiles.cs)
        private readonly bool _autoCopy;
        private bool _editor;

        private readonly Grid _root = new Grid();
        private readonly Canvas _canvas = new Canvas();
        private readonly Image _image = new Image();
        private readonly Path _dim = new Path();
        private readonly Canvas _annoLayer = new Canvas();
        private readonly Canvas _annoLive = new Canvas();     // the object that is being drawn right now: shown whole (also outside the region), above the clipped rest
        private readonly Canvas _chrome = new Canvas();
        private readonly Rectangle _frame = new Rectangle();
        private readonly Rectangle[] _handles = new Rectangle[8];
        private readonly Border _sizeLabel = new Border();
        private readonly TextBlock _sizeText = new TextBlock();
        private Border _hint;

        // selection of the region
        private Rect _sel = Rect.Empty;
        private bool _hasSel;
        private DragMode _mode = DragMode.None;
        private Point _start;
        private Rect _selStart;
        private int _handle = -1;

        // editor state
        private readonly List<Annotation> _items = new List<Annotation>();
        private readonly List<List<Annotation>> _hist = new List<List<Annotation>>();
        private int _hi;
        private Annotation _selected;
        private Annotation _orig;
        private int _endIndex;
        private EditorTool _tool = EditorTool.Select;
        private EditorTool _lastTool = EditorTool.Arrow;   // the last drawing tool: its style is shown while "Select" is active
        private StyleStore _styles;
        private bool _dirtyStyles;
        private Point _brushFirst;
        private Rect _rectAnchor;
        private int _annoHandle = -1;
        private Annotation _pendingEdit;                    // text clicked with the text tool: edit on release, move on drag
        private Point _editStartPos;

        // text editing
        private Border _editBorder;
        private TextBox _editBox;
        private TextAnnotation _editing;
        private bool _editingNew;
        private bool _endingEdit;

        // panels
        private bool _panelsBuilt;
        private Border _panelA;   // tools
        private Border _panelB;   // presets, colors, size, actions
        private readonly Dictionary<EditorTool, Border> _toolButtons = new Dictionary<EditorTool, Border>();
        private readonly List<KeyValuePair<WpfColor, Border>> _swatches = new List<KeyValuePair<WpfColor, Border>>();
        private readonly List<Border> _presetButtons = new List<Border>();
        private readonly List<FrameworkElement> _swatchParts = new List<FrameworkElement>();
        private Border _fillBtn, _roundBtn, _textStyleBtn;
        private StackPanel _sliderRow;                              // size slider and opacity slider
        private Slider _sizeSlider, _opacitySlider;
        private Border _sizeValueHost, _opacityValueHost;
        private TextBox _sizeValueBox;
        private TextBlock _opacityValue;
        private bool _updatingUi, _sizePending, _opacityPending, _typingSize;
        private readonly List<Border> _pctBtns = new List<Border>();
        private readonly Dictionary<EditorTool, double> _sessionSize = new Dictionary<EditorTool, double>();   // sizes typed or taken from the quick buttons: for this region only
        private double _eraserSize;
        private StackPanel _extraRow;                               // font, bold, italic, alignment (text) / head and its size (arrow)
        private Border _fontBtn, _boldBtn, _italicBtn, _alignBtn, _headBtn, _headSizeBtn, _headFillBtn;
        private StackPanel _recentHost;
        private int _recentShown = -1;
        private Border _pickChip;
        private TextBlock _pickText;
        private Border _pickSwatch;
        private Point _lastPt;
        private int _erased;

        public event Action<System.Drawing.Bitmap> SaveAsRequested;
        public event Action<System.Drawing.Bitmap> UploadRequested;
        /// <summary>The "GIF", "Video" and "Scroll" buttons: the chosen region (screen pixels) goes to the recording.</summary>
        public event Action<string, System.Drawing.Rectangle> RecordRequested;
        /// <summary>Set before Show: the overlay only picks an area ("gif", "video" or "scroll") and reports it, no editor.</summary>
        public string PickMode;
        public event Action<System.Drawing.Rectangle> RegionPicked;

        /// <summary>The text at the top of the screen before the first selection.</summary>
        public void SetHint(string text)
        {
            TextBlock tb = _hint == null ? null : _hint.Child as TextBlock;
            if (tb == null) return;
            tb.Text = text;
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double cx = Canvas.GetLeft(_hint) + 0;
            Canvas.SetLeft(_hint, Math.Max(4, (_canvas.Width / 2) - _hint.DesiredSize.Width / 2 - (_bounds.X < 0 ? 0 : 0)));
        }

        private void RaisePicked()
        {
            System.Drawing.Rectangle r = new System.Drawing.Rectangle(_bounds.X + (int)Math.Round(_sel.X), _bounds.Y + (int)Math.Round(_sel.Y), (int)Math.Round(_sel.Width), (int)Math.Round(_sel.Height));
            Action<System.Drawing.Rectangle> h = RegionPicked;
            Close();
            if (h != null) h(r);
        }

        private void DoRecord(string kind)
        {
            if (!_hasSel) return;
            System.Drawing.Rectangle r = new System.Drawing.Rectangle(_bounds.X + (int)Math.Round(_sel.X), _bounds.Y + (int)Math.Round(_sel.Y), (int)Math.Round(_sel.Width), (int)Math.Round(_sel.Height));
            Action<string, System.Drawing.Rectangle> h = RecordRequested;
            Close();
            if (h != null) h(kind, r);
        }
        public event Action<System.Drawing.Bitmap, System.Drawing.Rectangle> PinRequested;

        /// <summary>Region of the last use (screen pixels): "repeat last region".</summary>
        public System.Drawing.Rectangle? LastRegion { get; private set; }

        /// <summary>Start with this region already selected (canvas pixels).</summary>
        public Rect? InitialSelection;
        private TextBlock _sizeValue;
        private Border _undoBtn, _redoBtn;

        private IntPtr _hwnd = IntPtr.Zero;
        private bool _positioning;

        public event Action<System.Drawing.Bitmap> CopyRequested;
        public event Action<System.Drawing.Bitmap> SaveRequested;

        public RegionOverlay(System.Drawing.Bitmap shot, System.Drawing.Rectangle bounds, Theme theme, I18n i18n, Settings settings, bool autoCopy)
        {
            _shot = shot; _bounds = bounds; _theme = theme; _i18n = i18n; _settings = settings; _autoCopy = autoCopy;
            _editor = settings.EditorEnabled && !settings.MinimalUi && !autoCopy;

            _styles = StyleStore.Create(settings);
            _handleSize = Math.Max(5, Math.Min(20, settings.HandleSize));
            _eraserSize = Math.Max(2, settings.EraserRadius * 2.0);
            _strRound = settings.StrengthRound; _strCenter = settings.StrengthCenter; _strConstrain = settings.StrengthConstrain; _strAxis = settings.StrengthAxis;
            TextAnnotation.NeuralSafe = settings.NeuralSafe;
            TextAnnotation.FontName = string.IsNullOrWhiteSpace(settings.TextFont) ? "Segoe UI" : settings.TextFont;
            TextAnnotation.Bold = settings.TextBold;
            TextAnnotation.Italic = settings.TextItalic;
            TextAnnotation.Align = settings.TextAlign;
            TextAnnotation.PlateColor = settings.PlateColor;
            TextAnnotation.PlateOpacity = settings.PlateOpacity / 100.0;
            TextAnnotation.OutlineWidth = settings.OutlineWidth;
            StepAnnotation.LabelStyle = settings.StepStyle;
            StepAnnotation.StartNumber = settings.StepStart;
            ArrowAnnotation.HeadStyle = settings.ArrowHead;
            BuildKeyMaps();
            InitRatios();                                           // aspect ratios on Tab (RegionOverlay.Ratio.cs)
            if (_editor && settings.RememberTool && !string.IsNullOrEmpty(settings.LastTool))
            {
                // the tool of the last time (a drawing tool); "select" and unknown names mean the select tool
                foreach (EditorTool t in StyleStore.Drawing)
                    if (StyleStore.Key(t) == settings.LastTool && settings.ToolEnabled(settings.LastTool)) { _tool = t; _lastTool = t; }
            }
            _hist.Add(new List<Annotation>());
            _beautify = settings.BeautifyEnabled;
            _beautifyStyle = settings.BeautifyStyle;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Cursor = Cursors.Cross;
            Title = "SNAPter overlay";
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _canvas.Width = bounds.Width;
            _canvas.Height = bounds.Height;
            _canvas.Background = Brushes.Transparent;
            _canvas.HorizontalAlignment = HorizontalAlignment.Left;
            _canvas.VerticalAlignment = VerticalAlignment.Top;
            RenderOptions.SetEdgeMode(_canvas, EdgeMode.Unspecified);

            _source = ToSource(shot);
            Backdrop.Set(_source);
            _image.Source = _source;
            _image.Stretch = Stretch.None;
            _image.Width = bounds.Width;
            _image.Height = bounds.Height;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            _image.IsHitTestVisible = false;
            _canvas.Children.Add(_image);

            WpfColor dimBase = ParseColor(settings.DimColor, Colors.Black);
            _dim.Fill = new SolidColorBrush(WpfColor.FromArgb((byte)Math.Max(0, Math.Min(255, (int)(settings.DimOpacity * 255))), dimBase.R, dimBase.G, dimBase.B));
            _dim.IsHitTestVisible = false;
            _canvas.Children.Add(_dim);

            _annoLayer.Width = bounds.Width;
            _annoLayer.Height = bounds.Height;
            _annoLayer.IsHitTestVisible = false;
            _canvas.Children.Add(_annoLayer);
            _annoLive.Width = bounds.Width;
            _annoLive.Height = bounds.Height;
            _annoLive.IsHitTestVisible = false;
            _canvas.Children.Add(_annoLive);

            WpfColor frameColor = _theme.Accent;
            if (!string.Equals(settings.FrameColor, "accent", StringComparison.OrdinalIgnoreCase))
                frameColor = ParseColor(settings.FrameColor, frameColor);
            _frame.Stroke = new SolidColorBrush(frameColor);
            _frame.StrokeThickness = Math.Max(1, Math.Min(8, settings.FrameThickness));
            _frame.Fill = Brushes.Transparent;
            _frame.IsHitTestVisible = false;
            _frame.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(_frame);

            _chrome.Width = bounds.Width;
            _chrome.Height = bounds.Height;
            _chrome.IsHitTestVisible = false;
            _canvas.Children.Add(_chrome);

            for (int i = 0; i < 8; i++)
            {
                Rectangle h = new Rectangle();
                h.Width = _handleSize; h.Height = _handleSize;
                h.Fill = new SolidColorBrush(_theme.Surface);
                h.Stroke = new SolidColorBrush(frameColor);
                h.StrokeThickness = 1.5;
                h.IsHitTestVisible = false;
                h.Visibility = Visibility.Collapsed;
                _handles[i] = h;
                _canvas.Children.Add(h);
            }

            _sizeText.FontFamily = new FontFamily("Segoe UI");
            _sizeText.FontSize = 12;
            _sizeText.Foreground = new SolidColorBrush(_theme.Text);
            _sizeLabel.Child = _sizeText;
            _sizeLabel.Background = new SolidColorBrush(WpfColor.FromArgb(235, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B));
            _sizeLabel.CornerRadius = new CornerRadius(4);
            _sizeLabel.Padding = new Thickness(6, 2, 6, 2);
            _sizeLabel.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(_sizeLabel);
            // click the size label to type an exact size ("800x600")
            if (_editor)
            {
                _sizeLabel.Cursor = Cursors.IBeam;
                _sizeLabel.ToolTip = i18n.T("tip.type_size");
                _sizeLabel.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; BeginSizeEdit(); };
            }
            else _sizeLabel.IsHitTestVisible = false;

            // crosshair lines across the screen (overlay.show_crosshair)
            if (settings.ShowCrosshair)
            {
                SolidColorBrush cb = new SolidColorBrush(WpfColor.FromArgb(150, 255, 255, 255));
                _crossH = new Rectangle { Height = 1, Width = bounds.Width, Fill = cb, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _crossV = new Rectangle { Width = 1, Height = bounds.Height, Fill = cb, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _canvas.Children.Add(_crossH); _canvas.Children.Add(_crossV);
            }
            // loupe with pixel coordinates and color (overlay.show_magnifier)
            if (settings.ShowMagnifier)
            {
                _magImg = new Image { Width = MagSize, Height = MagSize, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(_magImg, BitmapScalingMode.NearestNeighbor);
                _magText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = new SolidColorBrush(_theme.Text), Margin = new Thickness(0, 3, 0, 0) };
                Rectangle mid = new Rectangle { Width = MagSize / MagCells, Height = MagSize / MagCells, Stroke = Brushes.Red, StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(mid, MagSize / 2 - MagSize / MagCells / 2); Canvas.SetTop(mid, MagSize / 2 - MagSize / MagCells / 2);
                Canvas inner = new Canvas { Width = MagSize, Height = MagSize, IsHitTestVisible = false };
                inner.Children.Add(_magImg); inner.Children.Add(mid);
                StackPanel sp = new StackPanel(); sp.Children.Add(inner); sp.Children.Add(_magText);
                _mag = new Border { Child = sp, Background = new SolidColorBrush(_theme.Surface), BorderBrush = new SolidColorBrush(_theme.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _canvas.Children.Add(_mag);
            }

            BuildHint();

            _root.Children.Add(_canvas);
            Content = _root;
            UpdateVisuals(); // the whole screen is dimmed at once, like in Lightshot

            _canvas.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { HandleDown(e.GetPosition(_canvas), e.ClickCount, e.OriginalSource as DependencyObject); };
            _canvas.MouseMove += delegate (object s, MouseEventArgs e) { HandleMove(e.GetPosition(_canvas), Keyboard.Modifiers); };
            _canvas.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { HandleUp(); };
            _canvas.MouseRightButtonDown += delegate (object s, MouseButtonEventArgs e) { HandleRight(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true; };   // Alt+letter arrives as Key.System
            // Shift / Ctrl / Alt: while a shape is being drawn the effect shows at once (no mouse move needed);
            // a plain tap of the key (nothing else pressed) applies its action to the selected object
            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (IsRatioKey(k) && HandleRatioKey()) { _modOther = true; e.Handled = true; return; }   // Tab: the next aspect ratio, Shift+Tab: the previous one
                // the corner key works wherever the keyboard focus is (a grip, a button, a slider) and has nothing to do with the dragging of panels
                if (_editBox == null && _sizeEdit == null && !_typingSize && HandleCornerKey(k)) { _modOther = true; e.Handled = true; return; }   // Shift+Tab is not a plain tap of Shift (no square region)
                if (!_preview && IsProfileKey(k) && ProfileKeyAllowed()) { CycleProfile(); _modOther = true; e.Handled = true; return; }   // the next profile (Ё / ` by default)
                if (IsModifierKey(k))
                {
                    if (_modMax == ModifierKeys.None) _modOther = false; // a new episode of holding keys
                    if (_mode != DragMode.None) _modOther = true;         // pressed while drawing: it belongs to the drawing, it is not a tap
                    _modMax |= Keyboard.Modifiers;
                    if (_mode != DragMode.None) HandleMove(_lastPt, Keyboard.Modifiers);
                }
                else _modOther = true;
            };
            PreviewKeyUp += delegate (object s, KeyEventArgs e)
            {
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (!IsModifierKey(k)) return;
                if (_mode != DragMode.None) HandleMove(_lastPt, Keyboard.Modifiers);
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    if (_strengthDirty) { _strengthDirty = false; Commit(); }
                    if (!_modOther && _modMax != ModifierKeys.None) ModifierTap(_modMax);
                    _modMax = ModifierKeys.None; _modOther = false;
                }
            };
            _canvas.MouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                // a key held (Shift / Ctrl / Alt): the wheel sets how strong its action is
                if (AdjustStrength(Keyboard.Modifiers, e.Delta > 0 ? 1 : -1)) { e.Handled = true; return; }
                // wheel: thickness / size, like in Flameshot
                if (_editor && _hasSel && _mode == DragMode.None && _editBox == null && _tool != EditorTool.Eraser && _tool != EditorTool.Picker)
                    StepSize(e.Delta > 0 ? 1 : -1);
            };
            ContentRendered += delegate
            {
                if (_preview) { PositionWindow(); return; }   // the settings preview never takes the keyboard
                PositionWindow(); Activate(); ForceForeground(); Focus();
                if (RatioActive && !_hasSel && !InitialSelection.HasValue) ShowRatioToast();   // the locked ratio is visible before the first drag
                if (InitialSelection.HasValue && !_hasSel) SetRegion(InitialSelection.Value);
            };
        }

        // ================= editor keys (settings: editor_keys) =================
        private readonly Dictionary<Key, EditorTool> _toolKeys = new Dictionary<Key, EditorTool>();
        private readonly Dictionary<Key, int> _presetKeys = new Dictionary<Key, int>();
        // shortcuts of the actions (settings: editor_keys act_*): action -> (modifiers, key)
        private readonly Dictionary<string, KeyValuePair<ModifierKeys, Key>> _actionKeys = new Dictionary<string, KeyValuePair<ModifierKeys, Key>>();
        private readonly Dictionary<string, string> _actionKeyText = new Dictionary<string, string>();
        internal static readonly string[] ActionIds = { "save", "save_as", "copy", "pin", "upload", "beautify", "select_all", "undo", "redo", "delete", "duplicate", "forward", "backward" };
        internal static readonly string[] ActionDefaults = { "Ctrl+S", "Ctrl+Shift+S", "Ctrl+C", "Ctrl+P", "Ctrl+U", "Ctrl+B", "Ctrl+A", "Ctrl+Z", "Ctrl+Y", "Delete", "Ctrl+D", "]", "[" };

        /// <summary>"Ctrl+Shift+S" -> modifiers and key; false when the text is empty, "none" or not understood.</summary>
        internal static bool ParseCombo(string text, out ModifierKeys mods, out Key key)
        {
            mods = ModifierKeys.None; key = Key.None;
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string p = parts[i].Trim().ToUpperInvariant();
                if (p == "CTRL" || p == "CONTROL") mods |= ModifierKeys.Control;
                else if (p == "SHIFT") mods |= ModifierKeys.Shift;
                else if (p == "ALT") mods |= ModifierKeys.Alt;
                else return false;
            }
            string last = parts[parts.Length - 1].Trim();
            if (last.Equals("Esc", StringComparison.OrdinalIgnoreCase)) last = "Escape";
            if (last == "]") { key = Key.OemCloseBrackets; return true; }
            if (last == "[") { key = Key.OemOpenBrackets; return true; }
            return ParseKeyName(last, out key);
        }

        /// <summary>The shortcut of an action as text for tooltips ("Ctrl+S"), empty when the action has no shortcut.</summary>
        private string ActionKeyText(string action)
        {
            string t; return _actionKeyText.TryGetValue(action, out t) ? t : "";
        }

        private string WithKey(string label, string action)
        {
            string t = ActionKeyText(action);
            return t.Length == 0 ? label : label + " (" + t + ")";
        }

        private string ActionForKey(Key key, ModifierKeys mods)
        {
            ModifierKeys m = mods & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
            foreach (KeyValuePair<string, KeyValuePair<ModifierKeys, Key>> kv in _actionKeys)
                if (kv.Value.Value == key && kv.Value.Key == m) return kv.Key;
            return null;
        }

        private static bool TryKey(string s, out Key k)
        {
            k = Key.None;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Length == 1 && char.IsLetter(s[0])) return Enum.TryParse(s.ToUpperInvariant(), out k);
            if (s.Length == 1 && char.IsDigit(s[0])) { k = (Key)((int)Key.D0 + (s[0] - '0')); return true; }
            if ((s[0] == 'F' || s[0] == 'f') && s.Length <= 3) return Enum.TryParse(s.ToUpperInvariant(), out k);
            return false;
        }

        private void BuildKeyMaps()
        {
            string[] ids = { "select", "arrow", "line", "rectangle", "ellipse", "brush", "marker", "text", "steps", "blur", "eraser", "picker", "fill", "spotlight", "magnifier" };
            string[] defs = { "V", "A", "L", "R", "O", "B", "M", "T", "N", "U", "E", "I", "F", "S", "Z" };
            EditorTool[] tools = { EditorTool.Select, EditorTool.Arrow, EditorTool.Line, EditorTool.Rect, EditorTool.Ellipse, EditorTool.Brush,
                EditorTool.Marker, EditorTool.Text, EditorTool.Steps, EditorTool.Blur, EditorTool.Eraser, EditorTool.Picker, EditorTool.Fill, EditorTool.Spotlight, EditorTool.Magnifier };
            for (int i = 0; i < ids.Length; i++)
            {
                string v; if (!_settings.EditorKeys.TryGetValue(ids[i], out v)) v = defs[i];
                Key k; if (!TryKey(v, out k)) TryKey(defs[i], out k);
                _toolKeys[k] = tools[i];
            }
            _actionKeys.Clear(); _actionKeyText.Clear();
            for (int i = 0; i < ActionIds.Length; i++)
            {
                string v; if (!_settings.EditorKeys.TryGetValue("act_" + ActionIds[i], out v) || string.IsNullOrWhiteSpace(v)) v = ActionDefaults[i];
                ModifierKeys am; Key ak;
                if (!ParseCombo(v, out am, out ak)) continue;   // "none": the action has no shortcut
                _actionKeys[ActionIds[i]] = new KeyValuePair<ModifierKeys, Key>(am, ak);
                _actionKeyText[ActionIds[i]] = v.Trim();
            }
            for (int i = 0; i < 9; i++)
            {
                string v; if (!_settings.EditorKeys.TryGetValue("preset" + (i + 1), out v)) v = (i + 1).ToString();
                Key k; if (!TryKey(v, out k)) continue;
                _presetKeys[k] = i;
                // the number pad works like the digit row
                if (k >= Key.D0 && k <= Key.D9) _presetKeys[(Key)((int)Key.NumPad0 + (k - Key.D0))] = i;
            }
        }

        // ================= window placement in real pixels =================
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyScale(VisualTreeHelper.GetDpi(this));
            PositionWindow();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ApplyScale(newDpi);
            PositionWindow();
        }

        private void ApplyScale(DpiScale dpi)
        {
            _root.LayoutTransform = new ScaleTransform(1.0 / dpi.DpiScaleX, 1.0 / dpi.DpiScaleY);
        }

        private void PositionWindow()
        {
            if (_hwnd == IntPtr.Zero || _positioning) return;
            _positioning = true;
            try
            {
                // HWND_TOPMOST = -1; SWP_NOOWNERZORDER = 0x200
                SetWindowPos(_hwnd, new IntPtr(-1), _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x200);
            }
            finally { _positioning = false; }
        }

        private void ForceForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint pid;
                uint fgThread = GetWindowThreadProcessId(fg, out pid);
                uint cur = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != cur) AttachThreadInput(cur, fgThread, true);
                SetForegroundWindow(_hwnd);
                BringWindowToTop(_hwnd);
                if (fgThread != 0 && fgThread != cur) AttachThreadInput(cur, fgThread, false);
            }
            catch (Exception ex) { Log.Write("foreground error: " + ex.Message); }
        }

        private static BitmapSource ToSource(System.Drawing.Bitmap bmp)
        {
            System.Drawing.Rectangle r = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData d = bmp.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                BitmapSource src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgr32, null, d.Scan0, d.Stride * bmp.Height, d.Stride);
                src.Freeze();
                return src;
            }
            finally { bmp.UnlockBits(d); }
        }

        private static WpfColor ParseColor(string text, WpfColor fallback)
        {
            try { return (WpfColor)ColorConverter.ConvertFromString(text); } catch { return fallback; }
        }

        private static SolidColorBrush Brush(WpfColor c)
        {
            return new SolidColorBrush(c);
        }

        // ================= hint before the first selection =================
        private void BuildHint()
        {
            if (!_settings.NotifyEnabled) return;     // notifications are off: no pop-up hint before the first selection
            TextBlock t = new TextBlock();
            t.Text = _i18n.T("overlay.hint_select");
            t.FontFamily = new FontFamily("Segoe UI");
            t.FontSize = 14;
            t.Foreground = Brush(_theme.Text);
            _hint = new Border();
            _hint.Child = t;
            _hint.Background = new SolidColorBrush(WpfColor.FromArgb(235, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B));
            _hint.CornerRadius = new CornerRadius(Math.Max(4, _theme.RadiusMd - 2));
            _hint.Padding = new Thickness(14, 8, 14, 8);
            _hint.IsHitTestVisible = false;
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            System.Drawing.Rectangle mon;
            try { mon = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds; }
            catch { mon = _bounds; }
            double cx = mon.X - _bounds.X + mon.Width / 2.0;
            Canvas.SetLeft(_hint, Math.Max(4, cx - _hint.DesiredSize.Width / 2));
            Canvas.SetTop(_hint, mon.Y - _bounds.Y + 24);
            _canvas.Children.Add(_hint);
        }

        // ================= helpers =================
        private static bool IsInside(DependencyObject node, DependencyObject ancestor)
        {
            if (ancestor == null) return false;
            while (node != null)
            {
                if (node == ancestor) return true;
                node = (node is Visual || node is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        private bool IsInsidePanel(DependencyObject source)
        {
            return IsInside(source, _panelA) || IsInside(source, _panelB) || IsInside(source, _panelC) || IsInside(source, _panelD) || IsInside(source, _colorPop) || IsInside(source, _beautifyPop) || IsInside(source, _beautifyPreview);
        }

        private Point Clamp(Point p)
        {
            return new Point(Math.Max(0, Math.Min(_canvas.Width, p.X)), Math.Max(0, Math.Min(_canvas.Height, p.Y)));
        }

        private Point ClampToSel(Point p)
        {
            return new Point(Math.Max(_sel.Left, Math.Min(_sel.Right, p.X)), Math.Max(_sel.Top, Math.Min(_sel.Bottom, p.Y)));
        }

        private bool SelContains(Point p)
        {
            if (!_hasSel) return false;
            Rect r = _sel;
            r.Inflate(2, 2);
            return r.Contains(p);
        }

        private int HitHandle(Point p)
        {
            if (!_hasSel) return -1;
            Point[] pts = HandlePoints();
            for (int i = 0; i < 8; i++)
                if (Math.Abs(p.X - pts[i].X) <= Hit && Math.Abs(p.Y - pts[i].Y) <= Hit) return i;
            return -1;
        }

        private Point[] HandlePoints()
        {
            double l = _sel.Left, t = _sel.Top, r = _sel.Right, b = _sel.Bottom, cx = (l + r) / 2, cy = (t + b) / 2;
            return new Point[] { new Point(l, t), new Point(cx, t), new Point(r, t), new Point(r, cy), new Point(r, b), new Point(cx, b), new Point(l, b), new Point(l, cy) };
        }

        private int HitArrowEnd(ArrowAnnotation a, Point p)
        {
            if ((p - a.A).Length <= 9) return 0;
            if ((p - a.B).Length <= 9) return 1;
            return -1;
        }

        private Annotation HitAnnotation(Point p)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i] != _editing && _items[i].Hit(p)) return _items[i];
            return null;
        }

        // ================= mouse =================
        private void HandleDown(Point p, int clickCount, DependencyObject source)
        {
            Log.Debug("down " + (int)p.X + "," + (int)p.Y + " clicks=" + clickCount + " tool=" + _tool + " hasSel=" + _hasSel + " items=" + _items.Count + " panel=" + IsInsidePanel(source));
            _modOther = true; // a click with a key held is not a plain tap of the key
            ResetStrengths();  // every new drawing starts with the strengths from the settings

            if (IsInsidePanel(source)) return;
            if (_sizeEdit != null && IsInside(source, _sizeEdit)) return;
            if (_colorPop != null && _colorPop.Visibility == Visibility.Visible)
            {
                _colorPop.Visibility = Visibility.Collapsed; // a click outside closes the color picker
                return;
            }
            if (BeautifyMenuOpen) { CloseBeautifyMenu(); return; }      // a click outside closes the menu of the looks
            if (_editBox != null && IsInside(source, _editBorder))
            {
                // the frame around the text box is a handle: drag it to move the text while typing
                if (ReferenceEquals(source, _editBorder))
                {
                    _mode = DragMode.EditMove; _start = p; _orig = _editing.Clone();
                    _canvas.CaptureMouse();
                }
                return;
            }
            if (_hint != null) { _canvas.Children.Remove(_hint); _hint = null; }
            if (_editBox != null) EndEdit(true);

            if (_hasSel)
            {
                // the pipette takes a color from anywhere on the screen
                if (_editor && _tool == EditorTool.Picker) { PickAt(p); return; }

                if (_editor && _selected is ArrowAnnotation && !GroupActive)
                {
                    int ep = HitArrowEnd((ArrowAnnotation)_selected, p);
                    if (ep >= 0) { _endIndex = ep; BeginAnnoDrag(DragMode.ArrowEnd, p); return; }
                }
                RectAnnotation selRect = _selected as RectAnnotation;
                if (_editor && selRect != null && !GroupActive)
                {
                    int rh = HitRectHandle(selRect, p);
                    if (rh >= 0) { _annoHandle = rh; BeginAnnoDrag(DragMode.AnnoResize, p); return; }
                }

                if (_editor && !GroupActive && _selected != null && _selected != _editing)
                {
                    // the round handle above a shape or text turns it; a double click makes it straight again
                    if (_selected.CanRotate && HitRotateHandle(_selected, p))
                    {
                        if (clickCount >= 2 && Math.Abs(_selected.Angle) > 0.001) { _selected.Angle = 0; Commit(); RebuildAnnotations(); return; }
                        BeginAnnoDrag(DragMode.Rotate, p);
                        return;
                    }
                    TextAnnotation selText = _selected as TextAnnotation;       // the handles of a text block: width and font size
                    if (selText != null && clickCount < 2)                      // a double click on a text edits it
                    {
                        int th = HitTextHandle(selText, p);
                        if (th >= 0) { _annoHandle = th; BeginAnnoDrag(DragMode.TextResize, p); return; }
                    }
                }

                int h = HitHandle(p);
                if (h >= 0)
                {
                    _mode = DragMode.Resize; _handle = h; _start = p; _selStart = _sel;
                    HidePanels(); _canvas.CaptureMouse(); return;
                }

                // a drawing tool keeps working when the press is OUTSIDE the region: the object is clipped by the region and the region itself never changes
                // (to change the region, pick the select tool - the top one - and drag the frame; with it a press outside starts a new region as before)
                bool pressInside = SelContains(p);
                if (pressInside || (_editor && _tool != EditorTool.Select))
                {
                    if (!pressInside && (_tool == EditorTool.Fill || _tool == EditorTool.Steps || _tool == EditorTool.Text)) return;   // a click outside has nothing to fill, number or write
                    if (_tool == EditorTool.Select)
                    {
                        Annotation hit = HitAnnotation(p);
                        if (hit != null)
                        {
                            if (SelectAddHeld()) { ToggleSelect(hit); return; }     // Shift / Ctrl + click: add or remove
                            if (!(GroupActive && IsSelected(hit))) Select(hit);     // a click inside the group keeps it (the whole group moves)
                            if (hit is TextAnnotation && clickCount >= 2) { Select(hit); BeginEdit((TextAnnotation)hit, false); return; }
                            BeginAnnoDrag(DragMode.AnnoMove, p);
                            return;
                        }
                        bool addMode = SelectAddHeld() && _items.Count > 0;
                        if (!addMode) Select(null);
                        if (_items.Count == 0)
                        {
                            _mode = DragMode.Move; _start = p; _selStart = _sel;
                            HidePanels(); _canvas.CaptureMouse();
                        }
                        else if (_editor) BeginMarquee(p, addMode);                 // a rubber band over empty space
                        return;
                    }
                    switch (_tool)
                    {
                        case EditorTool.Arrow: StartArrow(p, true); return;
                        case EditorTool.Line: StartArrow(p, false); return;
                        case EditorTool.Rect:
                        case EditorTool.Ellipse:
                        case EditorTool.Blur:
                        case EditorTool.Spotlight:
                        case EditorTool.Magnifier: StartRect(_tool, p); return;
                        case EditorTool.Fill: PlaceFill(p); return;
                        case EditorTool.Brush: StartBrush(p, false); return;
                        case EditorTool.Marker: StartBrush(p, true); return;
                        case EditorTool.Steps: PlaceStep(p); return;
                        case EditorTool.Eraser: StartErase(p); return;
                        case EditorTool.Text: TextClick(p); return;
                    }
                }
            }

            // a new region: everything drawn before belongs to the old one
            ClearAnnotations();
            _mode = DragMode.New;
            _ratioPortrait = RatioActive && _ratios[_ratioIdx].V < 1;   // a new frame starts in the orientation of the list, then follows the hand
            HideRatioToast();
            HideBeautifyPreview();                                    // a new region: the live picture of the look comes back when it is ready
            _start = Clamp(p);
            _sel = new Rect(_start, _start);
            _hasSel = true;
            HidePanels();
            _canvas.CaptureMouse();
            UpdateVisuals();
        }

        private void HandleRight()
        {
            if (_editBox != null) { EndEdit(true); return; }
            if (_selected != null) { Select(null); return; }
            if (_hasSel && _items.Count == 0) { _hasSel = false; HidePanels(); UpdateVisuals(); return; }
            Close();
        }

        private void UpdateCursor(Point p)
        {
            int h = HitHandle(p);
            if (h == 0 || h == 4) Cursor = Cursors.SizeNWSE;
            else if (h == 2 || h == 6) Cursor = Cursors.SizeNESW;
            else if (h == 1 || h == 5) Cursor = Cursors.SizeNS;
            else if (h == 3 || h == 7) Cursor = Cursors.SizeWE;
            else if (_hasSel && SelContains(p))
            {
                if (_editBox != null && _editBorder != null && IsOnEditFrame(p)) Cursor = Cursors.SizeAll;
                else if (_tool == EditorTool.Text) Cursor = (HitAnnotation(p) != null) ? Cursors.SizeAll : Cursors.IBeam;
                else if (_tool != EditorTool.Select) Cursor = Cursors.Cross;
                else Cursor = (HitAnnotation(p) != null || _items.Count == 0) ? Cursors.SizeAll : Cursors.Arrow;
            }
            else Cursor = Cursors.Cross;
        }

    }
}
