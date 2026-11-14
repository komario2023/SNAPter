using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>Settings window: everything that config\settings.json holds, with the program's own theme and language.
    /// Apply writes only the changed user file (defaults stay untouched), reloads the settings and reopens the window.</summary>
    internal sealed partial class SettingsWindow : Window
    {
        private sealed class Val<T> { public T V; }

        private readonly AppController _app;
        private readonly Theme _t;
        private readonly I18n _i;
        private readonly Settings _s;
        private readonly bool _preview;          // shows a change that is not saved yet
        private bool _replacing;                 // closed because a preview window takes its place
        private bool _trayPreviewed;             // the tray icon was changed for a preview: restore it when closed
        private ScrollViewer _scroll;
        private double _initialScroll;
        private readonly List<Action<JsonObject>> _savers = new List<Action<JsonObject>>();
        private readonly List<KeyValuePair<string, StackPanel>> _pages = new List<KeyValuePair<string, StackPanel>>();
        private readonly List<Border> _nav = new List<Border>();
        private readonly ContentControl _host = new ContentControl();
        private readonly TextBlock _status = new TextBlock();
        private int _current;
        private readonly HotkeyCaptureHook _hotkeyCapture = new HotkeyCaptureHook();

        public SettingsWindow(AppController app, int page, Settings pending = null)
        {
            _app = app;
            if (pending == null) ResetHistory();       // a window opened by the user: the history of the unsaved changes starts empty
            if (page == 0 && pending == null && app.Settings.RememberSettingsTab) page = app.Settings.SettingsLastPage;   // opens where it was closed
            _preview = pending != null;
            _s = pending ?? app.Settings;
            _t = pending == null ? app.Theme : Theme.Load(pending.AccentTheme, pending.ThemeMode);
            if (pending == null || pending.Language == app.Settings.Language) _i = app.I18n;
            else { I18n li = new I18n(); li.Load(pending.Language); _i = li; }
            Title = _i.T("app.name") + " - " + _i.T("settings.title");
            ShowInTaskbar = false;   // the program lives in the tray only: the settings window has no taskbar button either
            Width = 880; Height = 640; MinWidth = 720; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ApplySavedBounds(); // the size and place of the window are remembered for good (no Apply needed)
            Background = Br(_t.Bg);
            Foreground = Br(_t.Text);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            BuildPages();
            BuildMorePages();
            SourceInitialized += delegate
            {
                var helper = new WindowInteropHelper(this);
                _hotkeyCapture.AttachSettingsWindow(helper.Handle);
                try { int off = 1; DwmSetWindowAttribute(helper.Handle, 3, ref off, 4); } catch { }   // DWMWA_TRANSITIONS_FORCEDISABLED: no fade when the window is replaced by a rebuilt one
            };

            Grid root = new Grid();
            _navCol = new ColumnDefinition { Width = new GridLength(_s.SettingsNavWidth > 0 ? _s.SettingsNavWidth : 190), MinWidth = 110, MaxWidth = 500 };
            root.ColumnDefinitions.Add(_navCol);
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // the dividing line: drag it left or right, the width is remembered
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel nav = new StackPanel { Margin = new Thickness(10) };
            nav.Children.Add(BuildSearchBox());     // a search over all the settings (SettingsWindow.Dirty.cs)
            for (int k = 0; k < _pages.Count; k++)
            {
                int idx = k;
                Border b = Btn(_pages[k].Key, false, delegate { ShowPage(idx); });
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.Padding = new Thickness(12, 8, 12, 8);
                _nav.Add(b);
                nav.Children.Add(b);
            }
            ScrollViewer navScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav };
            Border navBox = new Border { Background = Br(_t.Surface), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(0, 0, 1, 0), Child = navScroll };
            Grid.SetColumn(navBox, 0); Grid.SetRow(navBox, 0); Grid.SetRowSpan(navBox, 2);
            root.Children.Add(navBox);

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _host, Padding = new Thickness(22, 16, 22, 10) };
            _scroll = sv;
            FrameworkElement body = BuildBody(sv);                  // the pages and, at their right, the preview dock (SettingsWindow.Dock.cs)
            Grid.SetColumn(body, 2); Grid.SetRow(body, 0);
            root.Children.Add(body);

            WrapPanel bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 6, 18, 14), HorizontalAlignment = HorizontalAlignment.Left };
            _applyBtn = Btn(_i.T("settings.apply"), true, Apply);      // gets an orange outline while there are unsaved changes
            bar.Children.Add(_applyBtn);
            bar.Children.Add(Btn(_i.T("actions.close"), false, delegate { Close(); }));
            bar.Children.Add(Btn(_i.T("settings.export"), false, Export));
            bar.Children.Add(Btn(_i.T("settings.import"), false, Import));
            bar.Children.Add(Btn(_i.T("settings.reset"), false, ResetToBaseline));          // back to the base settings of the owner
            bar.Children.Add(Btn(_i.T("settings.baseline_save"), false, SaveBaseline));     // the current setup becomes the base
            bar.Children.Add(Btn(_i.T("settings.factory"), false, FactoryReset));           // the settings of a new program
            // the status line (preview note, errors) sits above the buttons so that it is always visible
            _status.Margin = new Thickness(22, 6, 18, 0);
            _status.TextWrapping = TextWrapping.Wrap;
            _status.Foreground = Br(_t.TextSecondary);
            // at the right end of the row of buttons: back / forward through the changes that are not saved yet (SettingsWindow.Dirty.cs)
            _undoBtn = Btn("↶", false, delegate { HistoryStep(-1); });
            _redoBtn = Btn("↷", false, delegate { HistoryStep(1); });
            foreach (Border hb in new Border[] { _undoBtn, _redoBtn }) { TextBlock ht = hb.Child as TextBlock; if (ht != null) ht.FontSize = 18; hb.Padding = new Thickness(12, 4, 12, 4); hb.Margin = new Thickness(4, 0, 0, 0); }
            StackPanel histBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 8) };
            histBox.Children.Add(_undoBtn); histBox.Children.Add(_redoBtn);
            Grid barRow = new Grid();
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(bar, 0); Grid.SetColumn(histBox, 1);
            barRow.Children.Add(bar); barRow.Children.Add(histBox);
            UpdateHistoryButtons();
            StackPanel barCol = new StackPanel();
            barCol.Children.Add(_status);
            barCol.Children.Add(barRow);
            Grid.SetColumn(barCol, 2); Grid.SetRow(barCol, 1);
            root.Children.Add(barCol);

            GridSplitter split = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndCurrent, ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ShowsPreview = false };
            Grid.SetColumn(split, 1); Grid.SetRow(split, 0); Grid.SetRowSpan(split, 2);
            split.DragCompleted += delegate { SaveNavWidth(); };
            root.Children.Add(split);
            Content = BuildFrame(root);   // the title bar with the two flags and the settings below it
            HookPanelPreview();
            StartDirtyWatch();
            ShowPage(Math.Max(0, Math.Min(_pages.Count - 1, page)));
            if (_preview) _status.Text = _i.T("settings.preview_note");
            Loaded += delegate { if (_initialScroll > 0) _scroll.ScrollToVerticalOffset(_initialScroll); };
            // Esc closes the settings like the Close button (the key capture boxes handle their own Esc first)
            // Preview so that no inner control can swallow it; only a key capture box (it records Esc as a hotkey) and an open drop-down list keep it
            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                TextBox tb = Keyboard.FocusedElement as TextBox;
                if (tb != null && tb.IsReadOnly) return;
                ComboBox cb = Keyboard.FocusedElement as ComboBox;
                if (cb != null && cb.IsDropDownOpen) return;
                e.Handled = true; Close();
            };
            // the window opened from the tray must really get the keyboard, otherwise Esc goes to another program
            Loaded += delegate { Activate(); Focusable = true; if (Keyboard.FocusedElement == null) Focus(); };
            // a click on the empty background of the window moves the focus to it, so Esc works after any click
            MouseDown += delegate { if (!(Keyboard.FocusedElement is TextBox)) Focus(); };
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private ColumnDefinition _navCol;

        private void SaveNavWidth()
        {
            if (NoPersist || _replacing || _navCol == null) return;
            try
            {
                double w = Math.Round(_navCol.ActualWidth);
                Settings.PatchUser(root => { Settings.Section(Settings.Section(root, "state"), "settings_window")["nav_width"] = w; });
                _app.Settings.SettingsNavWidth = w;
            }
            catch (Exception ex) { Log.Write("settings nav width save error: " + ex.Message); }
        }

        public int CurrentPage { get { return _current; } }

        // hooks for the self test
        private Action<string> _testSetTheme;
        internal void TestSetTheme(string id) { if (_testSetTheme != null) _testSetTheme(id); }
        internal string TestThemeId { get { return _t.Id; } }
        internal string TestTrayIcon { get { return _s.TrayIcon; } }
        internal int TestPageCount { get { return _pages.Count; } }
        internal void TestShowPage(int i) { ShowPage(i); }
        internal JsonObject TestCollect()
        {
            JsonObject r = new JsonObject();
            foreach (Action<JsonObject> sv in _savers) sv(r);
            return r;
        }

        // ----------------------------------------------------------------- pages
        private void ShowPage(int idx)
        {
            _current = idx;
            if (IsLoaded && !NoPersist && !_replacing && _s.RememberSettingsTab && _app.Settings.SettingsLastPage != idx)
            {
                try { Settings.PatchUser(root => { Settings.Section(Settings.Section(root, "state"), "settings_window")["page"] = idx; }); _app.Settings.SettingsLastPage = idx; }
                catch (Exception ex) { Log.Write("settings page save error: " + ex.Message); }
            }
            UpdateDock();                                           // puts the page (and its picture, if it has one) into the host
            for (int k = 0; k < _nav.Count; k++)
            {
                _nav[k].Background = k == idx ? Br(_t.SurfaceMuted) : Brushes.Transparent;
                _nav[k].BorderBrush = k == idx ? Br(_t.Accent) : Brushes.Transparent;
                _nav[k].BorderThickness = new Thickness(1);
            }
            if (IsLoaded && (_pinPanels || _markPanels)) Dispatcher.BeginInvoke(new Action(delegate { RefreshPanelPreview(true); }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private StackPanel NewPage(string title)
        {
            StackPanel p = new StackPanel();
            TextBlock h = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 0, 0, 12) };
            p.Children.Add(h);
            _pages.Add(new KeyValuePair<string, StackPanel>(title, p));
            return p;
        }

        /// <summary>A menu of fonts for a font field: the usual ones first, then every installed font.</summary>
        private void ShowFontsFor(TextBox box)
        {
            ContextMenu menu = new ContextMenu { MaxHeight = 460, PlacementTarget = box, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            HashSet<string> installed = new HashSet<string>(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
            List<string> mine = FontListNow().Where(f => installed.Contains(f)).ToList();   // your own short list first
            Func<string, MenuItem> make = delegate (string name)
            {
                MenuItem mi = new MenuItem { Header = name, FontFamily = new FontFamily(name) };
                mi.Click += delegate { box.Text = name; };
                return mi;
            };
            MenuItem none = new MenuItem { Header = _i.T("tip.font_global") };
            none.Click += delegate { box.Text = ""; };
            menu.Items.Add(none);
            menu.Items.Add(new System.Windows.Controls.Separator());
            foreach (string c in mine) menu.Items.Add(make(c));
            menu.Items.Add(new System.Windows.Controls.Separator());
            MenuItem all = new MenuItem { Header = _i.T("settings.fonts_all") };
            foreach (string n in installed.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) if (!mine.Contains(n)) all.Items.Add(make(n));
            menu.Items.Add(all);
            menu.IsOpen = true;
        }

        private StackPanel PageByTitle(string title)
        {
            foreach (KeyValuePair<string, StackPanel> kv in _pages) if (kv.Key == title) return kv.Value;
            return null;
        }

        private void ReopenAt(int page)
        {
            Close();
            _app.OpenSettings(page);
        }

        /// <summary>The pages that were added later: after capture, editor keys, presets, looks of text and steps, profiles;
        /// and extra rows on the capture, saving and general pages.</summary>

        // ----------------------------------------------------------------- controls
        /// <summary>A tray icon picture on the color of the Windows taskbar.</summary>
        private static System.Windows.Media.Imaging.BitmapSource IconPreview(string kind, System.Drawing.Color ink, System.Drawing.Color accent)
        {
            using (System.Drawing.Bitmap b = IconFactory.Draw(kind, 32, ink, accent))
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                System.Windows.Media.Imaging.BitmapImage bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        private static readonly string[] ToolKeys = { "arrow", "line", "rectangle", "ellipse", "brush", "marker", "text", "steps", "blur", "eraser", "picker" };

        private string ThemeLabel(string id)
        {
            string key = "theme.name." + id;
            string v = _i.T(key);
            return v == key ? Theme.DisplayName(id) : v;
        }

        private static SolidColorBrush Br(WpfColor c) { return new SolidColorBrush(c); }

        /// <summary>A hint (tooltip) from the language file: it explains what the field is for and what can be typed.</summary>
        private void Tip(FrameworkElement fe, string key)
        {
            string text = _i.T(key);
            if (text == key || fe == null) return;
            fe.ToolTip = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            ToolTipService.SetInitialShowDelay(fe, 150);
            ToolTipService.SetShowDuration(fe, 60000);
        }

        private static bool TryColor(string text, out WpfColor c)
        {
            c = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { c = (WpfColor)ColorConverter.ConvertFromString(text.Trim()); return true; }
            catch { return false; }
        }

        /// <summary>A color code field with a square next to it that shows the color at once.</summary>
        private FrameworkElement ColorField(string hex, double width, string tipKey, out TextBox box)
        {
            TextBox tb = Input(hex, width);
            Border sw = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Action upd = delegate
            {
                WpfColor c;
                if (TryColor(tb.Text, out c)) { sw.Background = Br(c); sw.Child = null; }
                else sw.Child = new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.TextSecondary) };
            };
            tb.TextChanged += delegate { upd(); };
            upd();
            Tip(tb, tipKey); Tip(sw, tipKey);
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(tb); sp.Children.Add(sw);
            box = tb;
            return sp;
        }

        /// <summary>A number field with a slider beside it: they follow each other (the typed number may stay outside the slider range, e.g. 0 = automatic).</summary>
        private FrameworkElement SliderBox(TextBox box, int min, int max)
        {
            Slider sl = new Slider { Minimum = min, Maximum = max, Width = 220, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            bool busy = false;
            int v0;
            if (int.TryParse(box.Text.Trim(), out v0)) sl.Value = Math.Max(min, Math.Min(max, v0)); else sl.Value = min;
            sl.ValueChanged += delegate { if (busy) return; busy = true; box.Text = ((int)Math.Round(sl.Value)).ToString(); busy = false; };
            box.TextChanged += delegate
            {
                if (busy) return;
                int v; busy = true;
                if (int.TryParse(box.Text.Trim(), out v)) sl.Value = Math.Max(min, Math.Min(max, v));
                busy = false;
            };
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(box); sp.Children.Add(sl);
            return sp;
        }

        private static void PutInt(JsonObject o, string key, TextBox t, int min, int max)
        {
            int v;
            if (int.TryParse(t.Text.Trim(), out v)) o[key] = Math.Max(min, Math.Min(max, v));
        }

        private FrameworkElement Row(string label, FrameworkElement control, string tipKey = null)
        {
            if (tipKey != null) Tip(control, tipKey);
            StackPanel p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            TextBlock lab = new TextBlock { Text = label, Foreground = Br(_t.TextSecondary), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            if (tipKey != null) Tip(lab, tipKey);
            p.Children.Add(lab);
            control.HorizontalAlignment = HorizontalAlignment.Left;
            p.Children.Add(control);
            return p;
        }

        /// <summary>An information block (like a note in an article): at the left edge, with an accent bar, so that it is never taken for a setting.</summary>
        private FrameworkElement Hint(string text)
        {
            TextBlock tb = new TextBlock { Text = text, Foreground = Br(_t.TextSecondary), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left };
            return new Border
            {
                Child = tb, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 14), Padding = new Thickness(12, 8, 12, 8),
                Background = Br(_t.SurfaceMuted), BorderBrush = Br(_t.Accent), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4)
            };
        }

        private CheckBox Check(string text, bool on)
        {
            // the label wraps instead of running out of the window
            TextBlock label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 540, Foreground = Br(_t.Text) };
            CheckBox c = new CheckBox { Content = label, IsChecked = on, Foreground = Br(_t.Text), Margin = new Thickness(0, 3, 0, 9) };
            return c;
        }

        private TextBox Input(string text, double width)
        {
            TextBox t = new TextBox { Text = text, Width = width, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
            t.Background = Br(_t.Surface);
            t.Foreground = Br(_t.Text);
            t.BorderBrush = Br(_t.Border);
            t.CaretBrush = Br(_t.Text);
            return t;
        }

        private Border Btn(string text, bool primary, Action click)
        {
            Brush normal = primary ? Br(_t.Accent) : Brushes.Transparent;
            Brush hover = primary ? Br(_t.AccentHover) : Br(_t.SurfaceMuted);
            TextBlock tb = new TextBlock { Text = text, Foreground = Br(primary ? _t.AccentText : _t.Text), VerticalAlignment = VerticalAlignment.Center };
            Border b = new Border();
            b.Child = tb;
            b.Background = normal;
            b.BorderBrush = Brushes.Transparent;
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(Math.Max(4, _t.RadiusMd - 4));
            b.Padding = new Thickness(14, 7, 14, 7);
            b.Margin = new Thickness(3);
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { if (b.Tag == null) b.Background = hover; };
            b.MouseLeave += delegate { if (b.Tag == null) b.Background = normal; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; if (click != null) click(); };
            return b;
        }

        /// <summary>A row of buttons, one of which is chosen (a drop-down list that stays readable in dark themes).</summary>
        private FrameworkElement Segment(List<string> values, List<string> labels, Val<string> state, bool preview = false, Action changed = null)
        {
            WrapPanel wp = new WrapPanel { MaxWidth = 660 };   // wide: a row of choices stays on one line when it can
            List<Border> items = new List<Border>();
            Action paint = delegate
            {
                for (int k = 0; k < items.Count; k++)
                {
                    bool on = values[k] == state.V;
                    items[k].Background = on ? Br(_t.SurfaceMuted) : Brushes.Transparent;
                    items[k].BorderBrush = on ? Br(_t.Accent) : Br(_t.Border);
                }
            };
            for (int k = 0; k < values.Count; k++)
            {
                string val = values[k];
                Border b = Btn(labels[k], false, delegate { state.V = val; paint(); if (changed != null) changed(); if (preview) Preview(); });
                b.Tag = "seg";
                b.Margin = new Thickness(0, 0, 4, 6);
                b.Padding = new Thickness(8, 6, 8, 6);
                ((TextBlock)b.Child).FontSize = 12;
                items.Add(b);
                wp.Children.Add(b);
            }
            paint();
            return wp;
        }

        private const int VkSnapshot = 0x2C;

        private static string KeyName(KeyEventArgs e, bool single = false)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
            string name = null;
            if (KeyInterop.VirtualKeyFromKey(key) == VkSnapshot) name = "PrintScreen";
            else if (key >= Key.A && key <= Key.Z) name = key.ToString();
            else if (key >= Key.D0 && key <= Key.D9) name = ((int)key - (int)Key.D0).ToString();
            else if (key >= Key.F1 && key <= Key.F24) name = key.ToString();
            else
            {
                switch (key)
                {
                    case Key.PrintScreen: name = "PrintScreen"; break;
                    case Key.Space: name = "Space"; break;
                    case Key.Enter: name = "Enter"; break;
                    case Key.Escape: name = "Esc"; break;
                    case Key.Tab: name = "Tab"; break;
                    case Key.Insert: name = "Insert"; break;
                    case Key.Delete: name = "Delete"; break;
                    case Key.Home: name = "Home"; break;
                    case Key.End: name = "End"; break;
                    case Key.PageUp: name = "PageUp"; break;
                    case Key.Next: name = "PageDown"; break;
                    case Key.Left: name = "Left"; break;
                    case Key.Up: name = "Up"; break;
                    case Key.Right: name = "Right"; break;
                    case Key.Down: name = "Down"; break;
                    case Key.Pause: name = "Pause"; break;
                }
            }
            if (name == null) return null;
            if (single)
            {
                // one key without modifiers: a letter, a digit or F1-F12 (editor keys)
                bool okSingle = name.Length == 1 || (name[0] == 'F' && name.Length <= 3 && char.IsDigit(name[1]));
                return okSingle ? name : null;
            }
            ModifierKeys m = Keyboard.Modifiers;
            bool free = name.StartsWith("F") && name.Length > 1 && char.IsDigit(name[1]) || name == "PrintScreen";
            if (m == ModifierKeys.None && !free) return null;
            string s = "";
            if ((m & ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((m & ModifierKeys.Shift) != 0) s += "Shift+";
            if ((m & ModifierKeys.Alt) != 0) s += "Alt+";
            if ((m & ModifierKeys.Windows) != 0) s += "Win+";
            return s + name;
        }

        // ----------------------------------------------------------------- cloud
        private TextBlock _cloudStatus;

        /// <summary>Writes the page values and reloads them, without closing the window (the cloud buttons need the fresh values).</summary>
        private void SaveQuiet()
        {
            try
            {
                Settings.PatchUser(root => { foreach (Action<JsonObject> sv in _savers) sv(root); });
                _app.ReloadSettings();
            }
            catch (Exception ex) { _status.Text = ex.Message; Log.Write("settings save error: " + ex.Message); }
        }

        private async void SignInClick()
        {
            SaveQuiet();
            _cloudStatus.Text = _i.T("settings.gd_waiting");
            string err = await _app.GDriveSignIn();
            _cloudStatus.Text = err == null ? _i.T("settings.gd_signed") : _i.T("settings.gd_error") + " " + err;
        }

        // ----------------------------------------------------------------- live preview
        /// <summary>A change of theme, light/dark, language or tray icon shows at once: the tray icon changes, and the window is built again
        /// with the new look and with everything typed so far. Nothing is saved until Apply; closing the window returns the tray icon.</summary>
        private void Preview()
        {
            JsonObject pending = new JsonObject();
            foreach (Action<JsonObject> sv in _savers) sv(pending);
            RecordHistoryNow(pending.ToJsonString());      // the change that made the window rebuild (a language, a theme) is a step of the history too
            Rebuild(pending);
        }

        /// <summary>The window is built again with these values on the pages (a preview of the theme, or a step back / forward in the history of the changes).</summary>
        private void Rebuild(JsonObject pending)
        {
            try
            {
                Settings s2 = Settings.WithOverrides(pending);
                SettingsWindow w = new SettingsWindow(_app, _current, s2);
                w._initialScroll = _scroll == null ? 0 : _scroll.VerticalOffset;
                w._trayPreviewed = true;
                w._baselineJson = _baselineJson;                       // "unsaved changes" is counted from the first window, not from this copy
                if (_search != null) w._search.Text = _search.Text;
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                double keepL = Left, keepT = Top;
                bool maximized = WindowState == WindowState.Maximized;
                w.Width = Width; w.Height = Height;
                // no flicker: the new window is built and drawn completely FAR OFF the screen (nobody sees the black frame of a window that is not
                // drawn yet), then it jumps to the place of the old one and the old one goes away in the same moment
                w.Left = maximized ? keepL : -32000; w.Top = maximized ? keepT : -32000;
                if (maximized) w.WindowState = WindowState.Maximized;
                _app.AttachSettingsWindow(w);
                _replacing = true;
                w._panelPreview = _panelPreview; w._previewLook = _previewLook; _panelPreview = null;   // the preview panels stay where they are; the new window changes them in place
                w.ContentRendered += delegate
                {
                    w.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!maximized) { w.Left = keepL; w.Top = keepT; }
                        w.Activate();
                        w.Dispatcher.BeginInvoke(new Action(delegate { try { Close(); } catch { } }), System.Windows.Threading.DispatcherPriority.Render);
                    }), System.Windows.Threading.DispatcherPriority.Render);
                };
                w.Show();
                _app.PreviewTrayIcon(s2, w._t);
            }
            catch (Exception ex) { _replacing = false; _status.Text = ex.Message; Log.Write("settings preview error: " + ex.Message); }
        }

        // ----------------------------------------------------------------- size and place are remembered
        /// <summary>Set by the self test so that it does not write into the user file.</summary>
        internal static bool NoPersist;
        private System.Windows.Threading.DispatcherTimer _boundsTimer;

        private void ApplySavedBounds()
        {
            try
            {
                double w = _s.SettingsWidth > 0 ? _s.SettingsWidth : 880, h = _s.SettingsHeight > 0 ? _s.SettingsHeight : 640;
                Width = Math.Max(MinWidth, w); Height = Math.Max(MinHeight, h);
                if (!double.IsNaN(_s.SettingsLeft) && !double.IsNaN(_s.SettingsTop))
                {
                    // only when enough of the window is still on a screen (a monitor may have been unplugged since)
                    Rect me = new Rect(_s.SettingsLeft, _s.SettingsTop, Width, Height);
                    Rect screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                    Rect common = Rect.Intersect(me, screens);
                    if (!common.IsEmpty && common.Width >= 160 && common.Height >= 80)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = _s.SettingsLeft; Top = _s.SettingsTop;
                    }
                }
                if (_s.SettingsMaximized) WindowState = WindowState.Maximized;
            }
            catch (Exception ex) { Log.Write("settings window bounds error: " + ex.Message); }

            // while the window is moved or resized it is saved a moment later, so even a crash keeps the place
            _boundsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _boundsTimer.Tick += delegate { _boundsTimer.Stop(); SaveBounds(); };
            SizeChanged += delegate { if (IsLoaded) { _boundsTimer.Stop(); _boundsTimer.Start(); } };
            LocationChanged += delegate { if (IsLoaded) { _boundsTimer.Stop(); _boundsTimer.Start(); } };
        }

        private void SaveBounds()
        {
            if (NoPersist || _replacing) return;
            try
            {
                Rect r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                bool max = WindowState == WindowState.Maximized;
                Settings.PatchUser(root =>
                {
                    JsonObject o = Settings.Section(Settings.Section(root, "state"), "settings_window");
                    o["left"] = Math.Round(r.Left); o["top"] = Math.Round(r.Top);
                    o["width"] = Math.Round(r.Width); o["height"] = Math.Round(r.Height);
                    o["maximized"] = max;
                });
                // the program's loaded copy too, so that the next window (after Apply) opens exactly here
                Settings cur = _app.Settings;
                cur.SettingsLeft = r.Left; cur.SettingsTop = r.Top; cur.SettingsWidth = r.Width; cur.SettingsHeight = r.Height; cur.SettingsMaximized = max;
            }
            catch (Exception ex) { Log.Write("settings window bounds save error: " + ex.Message); }
        }

        protected override void OnClosed(EventArgs e)
        {
            _hotkeyCapture.Dispose();
            ClosePanelPreview();
            if (_boundsTimer != null) _boundsTimer.Stop();
            SaveBounds();
            base.OnClosed(e);
            // closed without Apply: the tray icon goes back to the saved one
            if (!_replacing && _trayPreviewed) _app.RestoreTray();
        }

        // ----------------------------------------------------------------- buttons
        private void Apply()
        {
            try
            {
                Settings.PatchUser(root => { foreach (Action<JsonObject> sv in _savers) sv(root); });
            }
            catch (Exception ex) { _status.Text = ex.Message; Log.Write("settings apply error: " + ex.Message); return; }
            int page = _current;
            _app.ReloadSettings();
            Close();
            _app.OpenSettings(page); // reopened with the new language and theme
        }

        private void Export()
        {
            try
            {
                using (System.Windows.Forms.SaveFileDialog d = new System.Windows.Forms.SaveFileDialog())
                {
                    d.Filter = "JSON|*.json|Config|*.conf"; d.FileName = "SNAPter_Settings_" + DateTime.Now.ToString("yyyy.MM.dd_HH-mm-ss") + ".json";
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    // EVERY setting of the program (defaults plus changes, presets, profiles); new settings added later appear here by themselves
                    File.WriteAllText(d.FileName, Settings.ExportJson(), new System.Text.UTF8Encoding(false));
                    _status.Text = d.FileName;
                }
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void Import()
        {
            try
            {
                using (System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog())
                {
                    d.Filter = "JSON / Config|*.json;*.conf";
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    BackupUserFile();
                    File.Copy(d.FileName, Paths.UserSettings, true);
                }
                int page = _current;
                _app.ReloadSettings();
                Close();
                _app.OpenSettings(page);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        /// <summary>Before a reset or an import the old user file is kept next to it (settings.backup.json).</summary>
        private static void BackupUserFile()
        {
            string p = Paths.UserSettings;
            if (!File.Exists(p)) return;
            File.Copy(p, Path.Combine(Path.GetDirectoryName(p), "settings.backup.json"), true);
        }
    }
}
