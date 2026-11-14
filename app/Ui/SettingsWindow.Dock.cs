using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>The preview picture of a page: it stands inside the scrolling page, right next to the settings it belongs to (no extra panel,
    /// no frame). When the window is too narrow for both, it drops below the settings. A page may put a picture here (how the looks change,
    /// with the feature on and off); it is redrawn a moment after any value changes. The pictures are drawn by the program itself,
    /// so they follow the language and the theme and weigh nothing.</summary>
    internal sealed partial class SettingsWindow
    {
        private readonly Dictionary<string, FrameworkElement> _dockByPage = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, Action> _dockRefresh = new Dictionary<string, Action>();
        private readonly Dictionary<int, FrameworkElement> _pageWraps = new Dictionary<int, FrameworkElement>();
        private readonly HashSet<string> _dockPinRight = new HashSet<string>();      // pages whose picture stands at the far right edge, the settings take all the middle

        private FrameworkElement BuildBody(ScrollViewer sv) { return sv; }

        /// <summary>The picture for a page (its title is the key); refresh redraws it from the values on the page.</summary>
        private void RegisterDock(string pageTitle, FrameworkElement content, Action refresh, bool pinRight = false)
        {
            _dockByPage[pageTitle] = content;
            if (pinRight) _dockPinRight.Add(pageTitle);
            if (refresh != null) _dockRefresh[pageTitle] = refresh;
        }

        /// <summary>Shows the page in the host: alone, or in a wrapping row together with its picture (settings at the left, picture right of them).</summary>
        private void UpdateDock()
        {
            if (_current < 0 || _current >= _pages.Count) return;
            FrameworkElement wrap;
            if (!_pageWraps.TryGetValue(_current, out wrap))
            {
                StackPanel page = _pages[_current].Value;
                FrameworkElement el;
                if (_dockByPage.TryGetValue(_pages[_current].Key, out el))
                {
                    if (_dockPinRight.Contains(_pages[_current].Key))
                    {
                        // the picture is pinned to the far right edge; the page takes all the room in between (tiles wrap over it)
                        Grid g = new Grid();
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        Grid.SetColumn(page, 0); g.Children.Add(page);
                        Border side = new Border { Child = el, Width = 310, Margin = new Thickness(24, 40, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
                        Grid.SetColumn(side, 1); g.Children.Add(side);
                        wrap = g;
                    }
                    else
                    {
                        page.MinWidth = 600;                                  // the picture always stands at the same place, right of the settings
                        WrapPanel row = new WrapPanel { Orientation = Orientation.Horizontal };
                        row.Children.Add(page);
                        row.Children.Add(new Border { Child = el, Width = 310, Margin = new Thickness(24, 40, 0, 0), VerticalAlignment = VerticalAlignment.Top });
                        wrap = row;
                    }
                }
                else wrap = page;
                _pageWraps[_current] = wrap;
            }
            _host.Content = wrap;
            Action r; if (_dockRefresh.TryGetValue(_pages[_current].Key, out r)) { try { r(); } catch (Exception ex) { Log.Write("dock refresh error: " + ex.Message); } }
        }

        private void RefreshDock()
        {
            if (_current < 0 || _current >= _pages.Count) return;
            Action r; if (_dockRefresh.TryGetValue(_pages[_current].Key, out r)) { try { r(); } catch (Exception ex) { Log.Write("dock refresh error: " + ex.Message); } }
        }
        private FrameworkElement DockCaption(string text)
        {
            return new TextBlock { Text = text, Foreground = Br(_t.TextSecondary), FontSize = 12, Margin = new Thickness(0, 6, 0, 4), TextWrapping = TextWrapping.Wrap };
        }

        /// <summary>A small card for the "text for the neural network" picture: a caption of the screenshot with the plate drawn filled or as an outline.</summary>
        private FrameworkElement NeuralSample(bool outline)
        {
            Grid g = new Grid { Height = 92 };
            g.Children.Add(new Border { Background = Br(WpfColor.FromRgb(246, 247, 250)), CornerRadius = new CornerRadius(6) });
            for (int i = 0; i < 3; i++)
                g.Children.Add(new Border { Height = 7, Width = i == 1 ? 120 : 200, Background = Br(WpfColor.FromRgb(200, 205, 216)), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 14 + i * 14, 0, 0) });
            WpfColor ink = WpfColor.FromRgb(214, 40, 40);
            Border plate = new Border
            {
                CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, 12),
                Background = outline ? Brushes.Transparent : Br(ink), BorderBrush = Br(ink), BorderThickness = new Thickness(outline ? 2 : 0),
                Child = new TextBlock { Text = _i.T("settings.neural_sample"), FontWeight = FontWeights.SemiBold, Foreground = outline ? Br(ink) : Brushes.White }
            };
            g.Children.Add(plate);
            return g;
        }

        // ----------------------------------------------------------------- the thin panel with big icons
        private StackPanel _thinHost;
        private TextBlock _thinWidth;
        private TextBox _tIconFill, _tBtnPx, _tGap, _tPad;
        private Val<string> _tSize;
        private string _thinKey;

        private FrameworkElement BuildThinPanelDock(TextBox iconFill, TextBox btnPx, TextBox gap, TextBox pad, Val<string> size)
        {
            _tIconFill = iconFill; _tBtnPx = btnPx; _tGap = gap; _tPad = pad; _tSize = size;
            _thinHost = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            _thinWidth = new TextBlock { Foreground = Br(_t.TextSecondary), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            StackPanel sp = new StackPanel();
            sp.Children.Add(DockCaption(_i.T("settings.thin_caption")));
            sp.Children.Add(_thinHost);
            sp.Children.Add(_thinWidth);
            return sp;
        }

        private static int IntOf(TextBox t, int fallback) { int v; return int.TryParse(t.Text.Trim(), out v) ? v : fallback; }

        private void RefreshThinDock()
        {
            if (_thinHost == null) return;
            int px = IntOf(_tBtnPx, 0), fill = IntOf(_tIconFill, 0), gap = Math.Max(0, Math.Min(8, IntOf(_tGap, 2))), pad = Math.Max(0, Math.Min(12, IntOf(_tPad, 4)));
            double btn = px > 0 ? Math.Max(22, Math.Min(64, px)) : (_tSize.V == "small" ? 28 : _tSize.V == "large" ? 42 : 34);
            fill = fill <= 0 ? 0 : Math.Max(40, Math.Min(100, fill));
            string key = btn + "/" + fill + "/" + gap + "/" + pad;
            if (key == _thinKey) return;
            _thinKey = key;
            string[] paths = { "M 3,17 L 17,3", "M 3,5 L 17,5 L 17,15 L 3,15 Z", "M 3,10 A 7,5.5 0 1 1 17,10 A 7,5.5 0 1 1 3,10 Z", "M 3,15 C 5,6 8,17 11,9 S 15,6 17,4", "M 4,14 L 16,6" };
            StackPanel col = new StackPanel();
            foreach (string d in paths)
            {
                System.Windows.Shapes.Path p = new System.Windows.Shapes.Path { Data = Geometry.Parse(d), Stroke = Br(_t.Text), StrokeThickness = 2, Width = 20, Height = 20, Stretch = Stretch.None, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                FrameworkElement icon = p;
                if (fill > 0) { double ip = Math.Max(14, btn * fill / 100.0); icon = new Viewbox { Width = ip, Height = ip, Child = p }; }
                col.Children.Add(new Border { Width = btn, Height = btn, Margin = new Thickness(gap), CornerRadius = new CornerRadius(5), Background = col.Children.Count == 1 ? Br(_t.SurfaceMuted) : Brushes.Transparent, Child = icon });
            }
            _thinHost.Children.Clear();
            _thinHost.Children.Add(new Border { Child = col, Padding = new Thickness(pad), Background = Br(_t.Bg), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
            _thinWidth.Text = _i.T("settings.thin_width") + ": " + Math.Round(btn + 2 * gap + 2 * pad + 2) + " px";
        }

        private FrameworkElement BuildNeuralDock(CheckBox neural)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(DockCaption(_i.T("settings.neural_off")));
            sp.Children.Add(NeuralSample(false));
            sp.Children.Add(DockCaption(_i.T("settings.neural_on")));
            sp.Children.Add(NeuralSample(true));
            return sp;
        }
    }
}
