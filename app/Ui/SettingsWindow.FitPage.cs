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
    internal sealed partial class SettingsWindow
    {
        // ---- panels for a big region (layout.fit_*, props_layout, corner_key) ----
        private FrameworkElement SegmentOf(string[] ids, string prefix, Val<string> state)
        {
            return Segment(new List<string>(ids), ids.Select(x => _i.T(prefix + x)).ToList(), state);
        }

        private void BuildFitPage()
        {
            StackPanel fp = NewPage(_i.T("settings.tab.fit"));
            fp.Children.Add(Hint(_i.T("settings.fit_hint")));
            StackPanel top = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };   // the bottom panel layout (the mode and the editors) is at the top of the page
            fp.Children.Add(top);
            RegisterFitDock();
            CheckBox fitOn = Check(_i.T("settings.fit_enabled"), _s.FitEnabled); fp.Children.Add(fitOn);
            CheckBox shrink = Check(_i.T("settings.fit_shrink"), _s.FitShrink); fp.Children.Add(shrink);
            TextBox minScale = Input(_s.FitMinScale.ToString(), 60);
            fp.Children.Add(Row(_i.T("settings.fit_min_scale"), SliderBox(minScale, 50, 100)));
            CheckBox hide = Check(_i.T("settings.fit_hide"), _s.FitHide); fp.Children.Add(hide);

            string[] hideIds = { "recent", "presets", "colors", "shape", "sliders", "extra", "actions2" };
            HashSet<string> cur = new HashSet<string>((_s.FitHideItems ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()));
            List<KeyValuePair<string, CheckBox>> hideBoxes = new List<KeyValuePair<string, CheckBox>>();
            foreach (string id in hideIds)
            {
                CheckBox cb = Check(_i.T("settings.fit_item." + id), cur.Contains(id));
                cb.Margin = new Thickness(24, 0, 0, 5);
                hideBoxes.Add(new KeyValuePair<string, CheckBox>(id, cb));
                fp.Children.Add(cb);
            }

            Val<string> fallback = new Val<string> { V = _s.FitFallback };
            fp.Children.Add(Row(_i.T("settings.fit_fallback"), SegmentOf(new string[] { "inside", "clamp" }, "settings.fit_fallback.", fallback)));
            Val<string> corner = new Val<string> { V = _s.StartCorner };
            fp.Children.Add(Row(_i.T("settings.fit_corner"), SegmentOf(new string[] { "bottom_right", "bottom_left", "top_left", "top_right" }, "settings.fit_corner.", corner)));
            Val<string> cornerKey = new Val<string> { V = _s.CornerKey };
            fp.Children.Add(Row(_i.T("settings.fit_corner_key"), SegmentOf(new string[] { "Shift+Tab", "CapsLock", "Tab", "F1", "none" }, "settings.fit_key.", cornerKey)));

            // the mode of the bottom panel layout: one for all profiles (the editor below) or its own for every profile (an editor per profile)
            string sharedLbl = _i.T("settings.fit_props_mode.shared"), ownLbl = _i.T("settings.fit_props_mode.profile");
            Val<string> propsMode = new Val<string> { V = _s.PropsMode == "profile" ? ownLbl : "" };
            StackPanel sharedBox = new StackPanel(), profBox = new StackPanel();
            Action applyMode = delegate
            {
                bool own = propsMode.V == ownLbl;
                sharedBox.Visibility = own ? Visibility.Collapsed : Visibility.Visible;
                profBox.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
            };
            top.Children.Add(Row(_i.T("settings.fit_props_mode"), DropDown(propsMode, () => new List<string> { ownLbl }, 320, sharedLbl, applyMode)));
            top.Children.Add(Hint(_i.T("settings.fit_props_mode_hint")));
            top.Children.Add(sharedBox); top.Children.Add(profBox);

            TextBox layout = Input(_s.PropsLayout, 560);
            sharedBox.Children.Add(Row(_i.T("settings.fit_layout"), BuildLayoutEditor(layout)));    // drag the groups (SettingsWindow.FitLayout.cs)
            sharedBox.Children.Add(Hint(_i.T("settings.fit_drag_hint")));
            sharedBox.Children.Add(Row(_i.T("settings.fit_text_label"), layout));
            sharedBox.Children.Add(Hint(_i.T("settings.fit_layout_hint")));
            Border reset = Btn(_i.T("settings.fit_layout_reset"), false, delegate { layout.Text = RegionOverlay.DefaultPropsLayout; });
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            sharedBox.Children.Add(reset);

            // the mode "own for each profile": choose a profile, drag its groups
            List<string> profNames = _app.ProfileCycleOrder();
            Dictionary<string, TextBox> profText = new Dictionary<string, TextBox>();
            Dictionary<string, StackPanel> profPanel = new Dictionary<string, StackPanel>();
            Val<string> profPick = new Val<string> { V = profNames.Count > 0 ? profNames[0] : "full" };
            Action showProf = delegate
            {
                foreach (KeyValuePair<string, StackPanel> kv in profPanel) kv.Value.Visibility = kv.Key == profPick.V ? Visibility.Visible : Visibility.Collapsed;
                _previewProfile = profPick.V;                                    // the panels pinned beside the window show this profile
                if (_previewDebounce != null) { _previewDebounce.Stop(); _previewDebounce.Start(); }
            };
            profBox.Children.Add(Row(_i.T("settings.fit_props_profile"), Segment(new List<string>(profNames), profNames.Select(n => _app.ProfileLabel(n)).ToList(), profPick, false, showProf)));
            foreach (string pn in profNames)
            {
                string pname = pn;
                TextBox pt = Input(_s.ProfileLayoutOf(pname), 560);
                profText[pname] = pt;
                StackPanel one = new StackPanel();
                one.Children.Add(BuildLayoutEditor(pt));
                one.Children.Add(Hint(_i.T("settings.fit_drag_hint")));
                one.Children.Add(Row(_i.T("settings.fit_text_label"), pt));
                Border preset = Btn(_i.T("settings.fit_layout_reset"), false, delegate { pt.Text = Settings.BuiltInPropsLayout(pname); });
                preset.HorizontalAlignment = HorizontalAlignment.Left;
                one.Children.Add(preset);
                profPanel[pname] = one;
                profBox.Children.Add(one);
            }
            showProf();
            applyMode();

            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "layout");
                o["fit_enabled"] = fitOn.IsChecked == true;
                o["fit_shrink"] = shrink.IsChecked == true;
                PutInt(o, "fit_min_scale", minScale, 50, 100);
                o["fit_hide"] = hide.IsChecked == true;
                o["fit_hide_items"] = string.Join(",", hideBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
                o["fit_fallback"] = fallback.V;
                o["start_corner"] = corner.V;
                o["corner_key"] = cornerKey.V;
                o["props_layout"] = string.IsNullOrWhiteSpace(layout.Text) ? RegionOverlay.DefaultPropsLayout : layout.Text.Trim();
                o["props_mode"] = propsMode.V == ownLbl ? "profile" : "shared";
                JsonObject pl = new JsonObject();                    // only the layouts that differ from the built-in ones are kept
                foreach (KeyValuePair<string, TextBox> kv in profText)
                {
                    string t = kv.Value.Text.Trim();
                    if (t.Length > 0 && t != Settings.BuiltInPropsLayout(kv.Key)) pl[kv.Key] = t;
                }
                o["profile_layouts"] = pl;
            });
        }
    }
}
