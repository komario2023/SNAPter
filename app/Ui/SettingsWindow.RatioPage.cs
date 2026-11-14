using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>The page "Aspect ratios": the list that Tab walks through while a region is drawn (the first one is used first), its order,
    /// your own entries, and the switch that turns 16:9 into 9:16 when the hand goes the other way.</summary>
    internal sealed partial class SettingsWindow
    {
        private static readonly string[] RatioStock = { "free", "16:9", "4:3", "3:2", "1:1", "21:9", "2.39:1", "4:5", "5:4", "16:10", "2:1", "A4", "A5" };

        private string RatioName(string token)
        {
            return token == "free" ? _i.T("ratio.free") : token;
        }

        private void BuildRatioPage()
        {
            StackPanel rp = NewPage(_i.T("settings.tab.ratio"));
            rp.Children.Add(Hint(_i.T("settings.ratio_hint")));
            CheckBox flip = Check(_i.T("settings.ratio_flip"), _s.RatioFlip);
            Tip(flip, "settings.ratio_flip.tip");
            rp.Children.Add(flip);
            TextBox ratioKey = KeyBox(string.IsNullOrWhiteSpace(_s.RatioKey) ? "Tab" : _s.RatioKey);
            StackPanel ratioKeyRow = new StackPanel { Orientation = Orientation.Horizontal };
            ratioKeyRow.Children.Add(ratioKey);
            ratioKeyRow.Children.Add(Btn(_i.T("settings.ratio_key_default"), false, delegate { ratioKey.Text = "Tab"; }));
            rp.Children.Add(Row(_i.T("settings.ratio_key"), ratioKeyRow));

            List<string> order = RegionOverlay.ParseRatios(_s.RatioList).Select(r => r.Token).ToList();
            if (order.Count == 0) order.Add("free");
            StackPanel listHost = new StackPanel();
            WrapPanel stockHost = new WrapPanel { MaxWidth = 580 };
            TextBlock status = new TextBlock { Foreground = Br(_t.TextSecondary), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            Action rebuild = null;
            Action<string> add = delegate (string tok)
            {
                List<RatioItem> parsed = RegionOverlay.ParseRatios(tok);
                if (parsed.Count != 1) { status.Text = _i.T("settings.ratio_bad"); return; }
                string key = parsed[0].Token;
                if (order.Contains(key, StringComparer.OrdinalIgnoreCase)) { status.Text = _i.T("settings.ratio_dup"); return; }
                order.Add(key); status.Text = ""; rebuild();
            };
            rebuild = delegate
            {
                listHost.Children.Clear();
                for (int idx = 0; idx < order.Count; idx++)
                {
                    int at = idx;
                    StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    line.Children.Add(new TextBlock { Text = (idx + 1).ToString(), Width = 26, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(idx == 0 ? _t.Accent : _t.TextSecondary), FontWeight = idx == 0 ? FontWeights.Bold : FontWeights.Normal });
                    line.Children.Add(new TextBlock { Text = RatioName(order[idx]) + (idx == 0 ? "   " + _i.T("settings.ratio_first") : ""), Width = 250, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text) });
                    line.Children.Add(Btn("↑", false, delegate { if (at > 0) { string x = order[at]; order[at] = order[at - 1]; order[at - 1] = x; rebuild(); } }));
                    line.Children.Add(Btn("↓", false, delegate { if (at < order.Count - 1) { string x = order[at]; order[at] = order[at + 1]; order[at + 1] = x; rebuild(); } }));
                    line.Children.Add(Btn("×", false, delegate { if (order.Count > 1) { order.RemoveAt(at); rebuild(); } }));
                    listHost.Children.Add(line);
                }
                stockHost.Children.Clear();
                foreach (string s in RatioStock)
                {
                    string tok = s;
                    if (order.Contains(tok, StringComparer.OrdinalIgnoreCase)) continue;
                    Border chip = Btn("+ " + RatioName(tok), false, delegate { add(tok); });
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    stockHost.Children.Add(chip);
                }
            };
            rebuild();

            rp.Children.Add(Row(_i.T("settings.ratio_list"), listHost));
            rp.Children.Add(Row(_i.T("settings.ratio_stock"), stockHost));
            TextBox own = Input("", 120);
            Tip(own, "settings.ratio_own.tip");
            StackPanel ownRow = new StackPanel { Orientation = Orientation.Horizontal };
            ownRow.Children.Add(own);
            ownRow.Children.Add(Btn(_i.T("settings.ratio_add"), false, delegate { if (own.Text.Trim().Length > 0) { add(own.Text.Trim()); if (status.Text.Length == 0) own.Text = ""; } }));
            rp.Children.Add(Row(_i.T("settings.ratio_own"), ownRow));
            rp.Children.Add(status);
            Border reset = Btn(_i.T("settings.ratio_reset"), false, delegate { order.Clear(); order.AddRange(RegionOverlay.ParseRatios(RegionOverlay.DefaultRatioList).Select(r => r.Token)); rebuild(); });
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            rp.Children.Add(reset);

            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "ratio");
                o["list"] = string.Join(",", order);
                o["flip"] = flip.IsChecked == true;
                o["key"] = string.IsNullOrWhiteSpace(ratioKey.Text) ? "none" : ratioKey.Text.Trim();
            });

            // the picture at the right: the entries in the order Tab visits them, drawn to scale
            WrapPanel pics = new WrapPanel { MaxWidth = 300 };
            string lastKey = null;
            Action refresh = delegate
            {
                string key = string.Join(",", order);
                if (key == lastKey) return;
                lastKey = key;
                pics.Children.Clear();
                int n = 0;
                foreach (RatioItem it in RegionOverlay.ParseRatios(key))
                {
                    n++;
                    double v = it.Free ? 1.3 : it.V;
                    double w = v >= 1 ? 64 : 64 * v, h = v >= 1 ? 64 / v : 64;
                    Rectangle rect = new Rectangle { Width = w, Height = h, Stroke = Br(n == 1 ? _t.Accent : _t.Text), StrokeThickness = n == 1 ? 2.5 : 1.5, RadiusX = 2, RadiusY = 2, StrokeDashArray = it.Free ? new DoubleCollection { 3, 2 } : null };
                    StackPanel cell = new StackPanel { Width = 92, Margin = new Thickness(0, 0, 0, 8) };
                    Grid g = new Grid { Height = 66 };
                    g.Children.Add(rect);
                    rect.HorizontalAlignment = HorizontalAlignment.Center; rect.VerticalAlignment = VerticalAlignment.Center;
                    cell.Children.Add(g);
                    cell.Children.Add(new TextBlock { Text = n + "  " + RatioName(it.Token), FontSize = 12, Foreground = Br(_t.TextSecondary), HorizontalAlignment = HorizontalAlignment.Center });
                    pics.Children.Add(cell);
                }
            };
            StackPanel dock = new StackPanel();
            dock.Children.Add(DockCaption(_i.T("settings.ratio_pic")));
            dock.Children.Add(pics);
            RegisterDock(_i.T("settings.tab.ratio"), dock, refresh);
        }
    }
}
