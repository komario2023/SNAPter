using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Snapter
{
    /// <summary>The settings window as a region: while a setting changes, the panels of the editor appear to its right and below it for a few
    /// seconds (or all the time with the left flag); the right flag outlines the parts of the panels that the open page changes.</summary>
    internal sealed partial class SettingsWindow
    {
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out NativeRect r);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        // the flags live while the program runs: a rebuilt window (theme preview) keeps them
        private static bool _pinPanels, _markPanels;
        private RegionOverlay _panelPreview;
        private DispatcherTimer _previewHide, _previewDebounce;
        private CheckBox _pinBox, _markBox;
        private static int _openMenus;                 // drop-down lists (context menus) open right now
        private static bool _menusHooked;
        private string _previewSig = "", _previewLook = "";
        private string _previewProfile = "";     // the profile whose bottom panel the preview shows (the mode "own for each profile": the one chosen on the page Panels for a big region)               // the values the preview was last built from

        /// <summary>All the values on the pages (not yet saved) as one text: two equal texts mean nothing was changed.</summary>
        private string PendingSignature()
        {
            try
            {
                JsonObject pending = new JsonObject();
                foreach (Action<JsonObject> sv in _savers) { try { sv(pending); } catch { } }
                return pending.ToJsonString() + "|" + _previewProfile;
            }
            catch { return Guid.NewGuid().ToString(); }
        }

        // page (the key of settings.tab.*) -> parts of the panels it changes
        private static readonly Dictionary<string, string[]> PageParts = new Dictionary<string, string[]>
        {
            { "layout", new[] { "tools", "props" } }, { "appearance", new[] { "tools", "props" } }, { "tools", new[] { "tools" } },
            { "presets", new[] { "presets", "colors", "sliders" } }, { "looks", new[] { "extra", "shape" } }, { "editor_keys", new[] { "tools", "presets" } },
            { "fit", new[] { "tools", "props" } }, { "beautify", new[] { "actions2" } }, { "frame", new[] { "frame" } },
            { "sizes", new[] { "frame", "actions" } }, { "mods", new[] { "frame" } }, { "after", new[] { "actions" } }, { "capture", new[] { "frame" } },
            { "output", new[] { "actions" } }, { "cloud", new[] { "actions" } }, { "record", new[] { "actions2" } }, { "scroll", new[] { "actions2" } },
            { "text", new[] { "extra" } }, { "profiles", new[] { "tools" } }, { "ratio", new[] { "frame" } }
        };
        private static readonly string[] PageKeys = { "general", "appearance", "frame", "hotkeys", "tools", "layout", "output", "capture", "cloud", "text", "mods", "after", "sizes", "record", "scroll", "editor_keys", "presets", "looks", "profiles", "beautify", "fit", "ratio" };

        private string CurrentPageKey()
        {
            if (_current < 0 || _current >= _pages.Count) return "";
            foreach (string k in PageKeys) if (_i.T("settings.tab." + k) == _pages[_current].Key) return k;
            return "";
        }

        /// <summary>The window with its own title bar (the two flags sit in it, beside the title) above the settings.</summary>
        private FrameworkElement BuildFrame(FrameworkElement body)
        {
            const double barH = 34;
            // the own title bar is switched on when the real window exists
            SourceInitialized += delegate
            {
                System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
                { CaptionHeight = barH, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            };
            Grid bar = new Grid { Height = barH, Background = Br(_t.Surface) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(new TextBlock { Text = Title, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text) });
            StackPanel flags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _pinBox = new CheckBox { IsChecked = _pinPanels, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = _i.T("settings.pin_panels_tip") };
            _markBox = new CheckBox { IsChecked = _markPanels, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = _i.T("settings.mark_panels_tip") };
            foreach (CheckBox cb in new[] { _pinBox, _markBox })
            {
                System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(cb, true);
                ToolTipService.SetInitialShowDelay(cb, 100); ToolTipService.SetShowDuration(cb, 60000);
                flags.Children.Add(cb);
            }
            _pinBox.Click += delegate { _pinPanels = _pinBox.IsChecked == true; RefreshPanelPreview(true); };
            _markBox.Click += delegate { _markPanels = _markBox.IsChecked == true; RefreshPanelPreview(true); };
            Grid.SetColumn(flags, 1); bar.Children.Add(flags);
            // the gap between the flags and the window buttons: the profile that is on now (it changes the whole look of the panels)
            TextBlock profileNow = new TextBlock { Text = CurrentProfileText(), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.TextSecondary), TextTrimming = TextTrimming.CharacterEllipsis };
            // right after the profile: the version of the running program (MAJOR.MINOR.BUILD), so it is clear at once whether the new build is running
            TextBlock versionNow = new TextBlock { Text = "v" + AppVersion.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text), ToolTip = _i.T("settings.version_tip") };
            StackPanel titleInfo = new StackPanel { Orientation = Orientation.Horizontal };
            titleInfo.Children.Add(profileNow); titleInfo.Children.Add(versionNow);
            Grid.SetColumn(titleInfo, 2); bar.Children.Add(titleInfo);
            StackPanel caps = new StackPanel { Orientation = Orientation.Horizontal };
            Border maxBtn = null;
            Action paintMax = delegate { if (maxBtn != null) ((TextBlock)maxBtn.Child).Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; };
            caps.Children.Add(CaptionButton("\uE921", false, delegate { WindowState = WindowState.Minimized; }));
            maxBtn = CaptionButton("\uE922", false, delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
            caps.Children.Add(maxBtn);
            caps.Children.Add(CaptionButton("\uE8BB", true, delegate { Close(); }));
            Grid.SetColumn(caps, 3); bar.Children.Add(caps);
            Grid outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.Children.Add(bar);
            Grid.SetRow(body, 1); outer.Children.Add(body);
            Border frame = new Border { Child = outer, BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1) };
            StateChanged += delegate
            {
                frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);   // a maximized window with its own frame sticks out of the screen by this much
                paintMax();
            };
            return frame;
        }

        private Border CaptionButton(string glyph, bool close, Action click)
        {
            Border b = new Border { Width = 46, Height = 34, Background = Brushes.Transparent, Cursor = Cursors.Arrow };
            b.Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons"), FontSize = 10, Foreground = Br(_t.Text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.MouseEnter += delegate { b.Background = close ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 17, 35)) : Br(_t.SurfaceMuted); if (close) ((TextBlock)b.Child).Foreground = Brushes.White; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; ((TextBlock)b.Child).Foreground = Br(_t.Text); };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        // Like the real region: while the window is dragged or stretched the panels are away, as soon as it is dropped they jump to the new place.
        private bool _inSizeMove, _resumePreview;
        private DispatcherTimer _followTimer;

        private IntPtr PreviewWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x100 && wParam.ToInt32() == 0x1B)   // WM_KEYDOWN Esc: closes the settings at once, whatever has the WPF focus (after the panel preview too)
            {
                TextBox tb = Keyboard.FocusedElement as TextBox;
                ComboBox cb = Keyboard.FocusedElement as ComboBox;
                bool keep = (tb != null && tb.IsReadOnly) || (cb != null && cb.IsDropDownOpen);
                if (!keep) { handled = true; Dispatcher.BeginInvoke(new Action(delegate { try { Close(); } catch { } })); }
                return IntPtr.Zero;
            }
            if (msg == 0x231)        // WM_ENTERSIZEMOVE: the user starts to drag or stretch the window
            {
                _inSizeMove = true;
                if (_panelPreview != null) { _resumePreview = true; ClosePanelPreview(); }
            }
            else if (msg == 0x232)   // WM_EXITSIZEMOVE: dropped
            {
                _inSizeMove = false;
                if (_resumePreview || _pinPanels || _markPanels) { _resumePreview = false; Dispatcher.BeginInvoke(new Action(delegate { RefreshPanelPreview(false); }), DispatcherPriority.Background); }
            }
            return IntPtr.Zero;
        }

        /// <summary>The window changed its place by itself (maximize, restore): the panels follow after a moment.</summary>
        private void FollowWindow()
        {
            if (_inSizeMove || (_panelPreview == null && !_pinPanels && !_markPanels)) return;
            if (_followTimer == null)
            {
                _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _followTimer.Tick += delegate { _followTimer.Stop(); if (!_inSizeMove && _panelPreview != null) RefreshPanelPreview(false); };
            }
            _followTimer.Stop(); _followTimer.Start();
        }

        /// <summary>Starts watching the controls of the pages: any change of a value shows the panels for a few seconds.</summary>
        private void HookPanelPreview()
        {
            _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _previewDebounce.Tick += delegate
            {
                _previewDebounce.Stop();
                // a drop-down list is open: rebuilding the preview would activate its window and close the list - wait until the list is closed
                if (_openMenus > 0) { _previewDebounce.Start(); return; }
                // nothing was changed (a click only opened a list, or chose the same value): leave the picture as it is
                if (_panelPreview != null && PendingSignature() == _previewSig) return;
                RefreshPanelPreview(false);
            };
            if (!_menusHooked)
            {
                _menusHooked = true;
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(delegate { _openMenus++; }));
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(delegate { if (_openMenus > 0) _openMenus--; }));
            }
            _previewHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _previewHide.Tick += delegate { _previewHide.Stop(); if (!_pinPanels && !_markPanels) ClosePanelPreview(); };

            Action changed = delegate { if (CurrentPageKey() != "hotkeys") { _previewDebounce.Stop(); _previewDebounce.Start(); } };
            AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(delegate (object s, TextChangedEventArgs e)
            {
                TextBox tb = e.OriginalSource as TextBox;
                if (tb != null && tb.IsKeyboardFocusWithin && InPageHost(tb)) changed();
            }));
            AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>(delegate (object s, RoutedPropertyChangedEventArgs<double> e)
            {
                Slider sl = e.OriginalSource as Slider;
                if (sl != null && (sl.IsMouseCaptureWithin || sl.IsKeyboardFocusWithin) && InPageHost(sl)) changed();
            }));
            AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(delegate (object s, RoutedEventArgs e)
            {
                DependencyObject d = e.OriginalSource as DependencyObject;
                if (d != null && InPageHost(d)) changed();
            }));
            // buttons made of borders (choices, tiles, colors): the click is over, the values are changed - a moment later the panels follow
            AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(delegate (object s, MouseButtonEventArgs e)
            {
                DependencyObject d = e.OriginalSource as DependencyObject;
                if (d != null && InPageHost(d) && !(d is TextBox)) changed();
            }), true);
            AddHandler(DragDrop.DropEvent, new DragEventHandler(delegate { changed(); }), true);
            SourceInitialized += delegate { HwndSource src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); if (src != null) src.AddHook(PreviewWndProc); };
            Loaded += delegate { if (_pinPanels || _markPanels) RefreshPanelPreview(true); };
            LocationChanged += delegate { FollowWindow(); };
            SizeChanged += delegate { FollowWindow(); };

            // another program is in front (the desktop, a folder, a browser) or the settings are minimized: the panels beside the window go away with it,
            // and come back when the settings come back to the front
            bool hiddenByFocus = false;
            DispatcherTimer watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            watch.Tick += delegate
            {
                try
                {
                    if (!IsLoaded || (!_pinPanels && !_markPanels)) { hiddenByFocus = false; return; }
                    IntPtr fg = GetForegroundWindow(), me = new WindowInteropHelper(this).Handle;
                    IntPtr pv = IntPtr.Zero;
                    if (_panelPreview != null) pv = new WindowInteropHelper(_panelPreview).Handle;
                    bool mine = WindowState != WindowState.Minimized && (fg == me || (pv != IntPtr.Zero && fg == pv) || fg == IntPtr.Zero || (fg != me && IsOwnWindow(fg)));
                    if (!mine && _panelPreview != null) { hiddenByFocus = true; ClosePanelPreview(); }
                    else if (mine && hiddenByFocus && !_inSizeMove) { hiddenByFocus = false; RefreshPanelPreview(false); }
                }
                catch { }
            };
            Loaded += delegate { watch.Start(); };
            Closed += delegate { watch.Stop(); };
        }

        /// <summary>A window of this program (a drop-down list, a dialog of the settings): the panels stay while it is in front.</summary>
        private static bool IsOwnWindow(IntPtr h)
        {
            try
            {
                foreach (Window w in System.Windows.Application.Current.Windows)
                    if (new WindowInteropHelper(w).Handle == h) return true;
            }
            catch { }
            return false;
        }

        private bool InPageHost(DependencyObject d)
        {
            while (d != null)
            {
                if (ReferenceEquals(d, _host)) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void ClosePanelPreview()
        {
            if (_previewHide != null) _previewHide.Stop();
            if (_panelPreview != null) { try { _panelPreview.Close(); } catch { } _panelPreview = null; }
        }

        /// <summary>Builds the panels from the values on the pages (not yet saved) and shows them beside the window.</summary>
        private void RefreshPanelPreview(bool forceShow)
        {
            try
            {
                if (!IsLoaded) return;
                if (Left < -20000) return;                         // still being built far off the screen (a rebuild of the window): the panels wait for it to arrive
                string page = CurrentPageKey();
                if (!forceShow && !_pinPanels && !_markPanels && page == "hotkeys") return;
                if (!_pinPanels && !_markPanels && forceShow) { ClosePanelPreview(); return; }
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                NativeRect nr; if (!GetWindowRect(hwnd, out nr)) { ClosePanelPreview(); return; }
                System.Drawing.Rectangle mon = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                Rect sel = Rect.Intersect(new Rect(nr.Left - mon.X, nr.Top - mon.Y, nr.Right - nr.Left, nr.Bottom - nr.Top), new Rect(0, 0, mon.Width, mon.Height));
                if (sel.IsEmpty) { ClosePanelPreview(); return; }
                JsonObject pending = new JsonObject();
                foreach (Action<JsonObject> sv in _savers) { try { sv(pending); } catch { } }
                Settings s2 = Settings.WithOverrides(pending);
                if (s2.PropsMode == "profile" && !string.IsNullOrEmpty(_previewProfile)) s2.PropsLayout = s2.ProfileLayoutOf(_previewProfile);   // the panels show the layout of the profile that is being edited
                try { _previewSig = pending.ToJsonString() + "|" + _previewProfile; } catch { _previewSig = ""; }

                // the same window, the same screen, the same theme and language: the panels are changed IN PLACE, at once, without a new window
                // (a new window each time flickered and was late: the panels now follow every change like the editor follows the region)
                string look = s2.AccentTheme + "|" + s2.ThemeMode + "|" + s2.Language + "|" + mon + "|" + sel;
                if (_panelPreview != null && look == _previewLook)
                {
                    RegionOverlay cur = _panelPreview;
                    cur.UpdatePreview(s2);
                    if (_markPanels)
                    {
                        string pg = CurrentPageKey();
                        string[] pts; if (!PageParts.TryGetValue(pg, out pts)) pts = new string[0];
                        cur.Dispatcher.BeginInvoke(new Action(delegate { try { cur.Highlight(pts); } catch (Exception ex) { Log.Write("highlight error: " + ex.Message); } }), DispatcherPriority.Loaded);
                    }
                    if (!_pinPanels && !_markPanels) { _previewHide.Stop(); _previewHide.Start(); }
                    return;
                }
                RegionOverlay old = _panelPreview; _panelPreview = null;          // the new panels appear first, then the old ones go away (no blink)
                _previewLook = look;
                RegionOverlay ov = RegionOverlay.CreatePreview(mon, sel, Theme.Load(s2.AccentTheme, s2.ThemeMode), _i, s2);
                ov.Show();
                _panelPreview = ov;
                if (old != null) { try { old.Close(); } catch { } }
                if (_markPanels)
                {
                    string[] parts; if (!PageParts.TryGetValue(page, out parts)) parts = new string[0];
                    ov.Dispatcher.BeginInvoke(new Action(delegate { try { ov.Highlight(parts); } catch (Exception ex) { Log.Write("highlight error: " + ex.Message); } }), DispatcherPriority.Loaded);
                }
                if (!_pinPanels && !_markPanels) { _previewHide.Stop(); _previewHide.Start(); }
                Activate();   // the settings window keeps the keyboard
            }
            catch (Exception ex) { Log.Write("panel preview error: " + ex.Message); }
        }
    }
}
