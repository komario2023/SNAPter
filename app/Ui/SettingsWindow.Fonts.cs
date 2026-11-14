using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapter
{
    /// <summary>The fonts of the text: a drop-down list of YOUR fonts (a short set, ten or so, is enough for a lifetime), and a quick filter over
    /// every installed font to add one to the list or to remove it. The font menus of the editor show this list first.</summary>
    internal sealed partial class SettingsWindow
    {
        private List<string> _fontList;

        private static List<string> ParseFonts(string s)
        {
            List<string> r = new List<string>();
            foreach (string part in (s ?? "").Split(','))
            {
                string f = part.Trim();
                if (f.Length > 0 && !r.Contains(f, StringComparer.OrdinalIgnoreCase)) r.Add(f);
            }
            return r;
        }

        private List<string> FontListNow()
        {
            if (_fontList == null) _fontList = ParseFonts(_s.FontList);
            return _fontList;
        }

        /// <summary>"font" holds the chosen font (empty - the global one); the returned control shows it as a drop-down and edits the list of fonts under it.</summary>
        private FrameworkElement BuildFontPicker(TextBox font)
        {
            List<string> list = FontListNow();
            HashSet<string> installed = new HashSet<string>(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
            StackPanel root = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };

            // 1. the drop-down: the current font, a click opens the list
            TextBlock curName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text), Margin = new Thickness(8, 0, 0, 0) };
            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.Children.Add(curName);
            TextBlock arrow = new TextBlock { Text = "▾", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.TextSecondary) };
            Grid.SetColumn(arrow, 1); inner.Children.Add(arrow);
            Border pick = new Border { Width = 260, Height = 32, Child = inner, Background = Br(_t.Surface), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand };
            Action paintCurrent = delegate
            {
                string f = font.Text.Trim();
                curName.Text = f.Length == 0 ? _i.T("tip.font_global") : f;
                try { curName.FontFamily = f.Length == 0 ? new FontFamily("Segoe UI") : new FontFamily(f); } catch { }
            };
            paintCurrent();
            font.TextChanged += delegate { paintCurrent(); };
            pick.MouseLeftButtonUp += delegate
            {
                ContextMenu menu = new ContextMenu { MaxHeight = 420, PlacementTarget = pick, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 260 };
                MenuItem none = new MenuItem { Header = _i.T("tip.font_global") };
                none.Click += delegate { font.Text = ""; };
                menu.Items.Add(none);
                menu.Items.Add(new System.Windows.Controls.Separator());
                foreach (string f in list.Where(x => installed.Contains(x)))
                {
                    string ff = f;
                    MenuItem mi = new MenuItem { Header = ff, FontFamily = new FontFamily(ff), IsChecked = string.Equals(font.Text.Trim(), ff, StringComparison.OrdinalIgnoreCase) };
                    mi.Click += delegate { font.Text = ff; };
                    menu.Items.Add(mi);
                }
                menu.IsOpen = true;
            };
            root.Children.Add(pick);

            // 2. "my fonts": chips with a cross
            root.Children.Add(new TextBlock { Text = _i.T("settings.fonts_mine"), Foreground = Br(_t.TextSecondary), Margin = new Thickness(0, 12, 0, 4) });
            WrapPanel chips = new WrapPanel { MaxWidth = 560 };
            root.Children.Add(chips);

            // 3. the filter over all installed fonts
            root.Children.Add(new TextBlock { Text = _i.T("settings.fonts_find"), Foreground = Br(_t.TextSecondary), Margin = new Thickness(0, 12, 0, 4) });
            TextBox filter = Input("", 260);
            root.Children.Add(filter);
            StackPanel results = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(results);

            Action rebuild = null;
            rebuild = delegate
            {
                chips.Children.Clear();
                foreach (string f in list.ToList())
                {
                    string ff = f;
                    StackPanel chip = new StackPanel { Orientation = Orientation.Horizontal };
                    chip.Children.Add(new TextBlock { Text = ff, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text), FontFamily = installed.Contains(ff) ? new FontFamily(ff) : new FontFamily("Segoe UI"), Margin = new Thickness(8, 0, 4, 0) });
                    Border x = new Border { Child = new TextBlock { Text = "×", Foreground = Br(_t.TextSecondary), FontSize = 15 }, Padding = new Thickness(4, 0, 8, 1), Cursor = Cursors.Hand, ToolTip = _i.T("actions.delete"), Background = Brushes.Transparent };
                    x.MouseLeftButtonUp += delegate { list.Remove(ff); rebuild(); };
                    chip.Children.Add(x);
                    chips.Children.Add(new Border { Child = chip, Background = Br(_t.SurfaceMuted), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0, 3, 0, 3) });
                }
                results.Children.Clear();
                string q = filter.Text.Trim();
                if (q.Length == 0) return;
                int shown = 0;
                foreach (string f in installed.Where(x => x.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (shown++ >= 10) break;
                    string ff = f;
                    bool has = list.Contains(ff, StringComparer.OrdinalIgnoreCase);
                    StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    line.Children.Add(new TextBlock { Text = ff, Width = 240, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text), FontFamily = new FontFamily(ff) });
                    line.Children.Add(Btn(has ? "✓ " + _i.T("settings.fonts_in_list") : "+ " + _i.T("settings.fonts_add"), false, delegate
                    {
                        if (list.Contains(ff, StringComparer.OrdinalIgnoreCase)) list.RemoveAll(x => string.Equals(x, ff, StringComparison.OrdinalIgnoreCase)); else list.Add(ff);
                        rebuild();
                    }));
                    results.Children.Add(line);
                }
                if (shown == 0) results.Children.Add(new TextBlock { Text = _i.T("settings.fonts_none"), Foreground = Br(_t.TextSecondary) });
            };
            filter.TextChanged += delegate { rebuild(); };
            rebuild();

            _savers.Add(r => { Settings.Section(r, "text")["font_list"] = string.Join(",", list); });
            return root;
        }
    }
}
