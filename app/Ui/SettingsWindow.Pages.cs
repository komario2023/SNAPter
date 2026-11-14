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
        private void BuildPages()
        {
            // ---- general ----
            StackPanel g = NewPage(_i.T("settings.tab.general"));
            Val<string> lang = new Val<string> { V = _s.Language };
            List<string> lv = new List<string> { "auto" }, ll = new List<string> { _i.T("lang.auto") };
            foreach (LanguageInfo l in I18n.Languages()) { lv.Add(l.Code); ll.Add(l.Native); }
            g.Children.Add(Row(_i.T("menu.language"), Segment(lv, ll, lang, true)));
            Val<string> tray = new Val<string> { V = _s.TrayClick };
            g.Children.Add(Row(_i.T("settings.tray_click"), Segment(new List<string> { "region", "menu", "none" },
                new List<string> { _i.T("settings.tray.region"), _i.T("settings.tray.menu"), _i.T("settings.tray.none") }, tray)));
            CheckBox run = Check(_i.T("settings.run_with_windows"), _s.RunWithWindows);
            g.Children.Add(run);
            CheckBox min = Check(_i.T("settings.minimal_ui"), _s.MinimalUi);
            g.Children.Add(min);
            _savers.Add(r =>
            {
                Settings.Section(r, "ui")["language"] = lang.V;
                Settings.Section(r, "ui")["tray_click"] = tray.V;
                Settings.Section(r, "startup")["run_with_windows"] = run.IsChecked == true;
                Settings.Section(r, "startup")["minimal_ui_mode"] = min.IsChecked == true;
            });
            g.Children.Add(Hint(_i.T("settings.about") + "\n" + Paths.Root));

            // ---- appearance ----
            StackPanel a = NewPage(_i.T("settings.tab.appearance"));
            Val<string> mode = new Val<string> { V = _s.ThemeMode };
            a.Children.Add(Row(_i.T("menu.theme_mode"), Segment(new List<string> { "system", "light", "dark" },
                new List<string> { _i.T("theme.mode.system"), _i.T("theme.mode.light"), _i.T("theme.mode.dark") }, mode, true)));
            Val<string> acc = new Val<string> { V = _s.AccentTheme };
            _testSetTheme = delegate (string id) { acc.V = id; Preview(); };
            a.Children.Add(Row(_i.T("menu.accent_theme"), Segment(Theme.Ids.ToList(), Theme.Ids.Select(id => ThemeLabel(id)).ToList(), acc, true)));

            // your own theme from one accent color (the file goes to config\themes); the color is shown next to its code at once,
            // and the new theme is chosen and shown in the window right after "Create"
            TextBox newThemeName = Input("", 150);
            Tip(newThemeName, "settings.theme_name.tip");
            TextBox newThemeColor;
            FrameworkElement newThemeColorField = ColorField("#2D8CFF", 100, "settings.theme_color.tip", out newThemeColor);
            TextBlock newThemeStatus = new TextBlock { Foreground = Br(_t.TextSecondary), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            StackPanel newTheme = new StackPanel { Orientation = Orientation.Horizontal };
            newTheme.Children.Add(newThemeName); newTheme.Children.Add(new TextBlock { Width = 8 }); newTheme.Children.Add(newThemeColorField);
            Border createBtn = Btn(_i.T("settings.theme_create"), false, delegate
            {
                try
                {
                    string nm = newThemeName.Text.Trim();
                    string id = new string(nm.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
                    if (id.Length == 0) { newThemeStatus.Text = _i.T("settings.theme_need_name"); return; }
                    WpfColor accentColor;
                    if (!TryColor(newThemeColor.Text, out accentColor)) { newThemeStatus.Text = _i.T("settings.theme_bad_color"); return; }
                    Theme.Create(id, nm, accentColor);
                    acc.V = id;      // the new theme is chosen ...
                    Preview();       // ... and the window shows it at once
                }
                catch (Exception ex) { newThemeStatus.Text = ex.Message; }
            });
            Tip(createBtn, "settings.theme_create.tip");
            newTheme.Children.Add(createBtn);
            newTheme.Children.Add(newThemeStatus);
            a.Children.Add(Row(_i.T("settings.theme_new"), newTheme));

            // the tray icon: one of ten shapes (the first pen icon is "quill"), its colors independent of the theme
            Val<string> trayIcon = new Val<string> { V = IconFactory.Kinds.Contains(_s.TrayIcon) ? _s.TrayIcon : "viewfinder" };
            Val<string> trayColor = new Val<string> { V = IconFactory.AccentKeys.Contains(_s.TrayIconColor) ? _s.TrayIconColor : "blue" };
            Val<string> trayInk = new Val<string> { V = IconFactory.InkKeys.Contains(_s.TrayIconInk) ? _s.TrayIconInk : "black" };
            // exactly two rows of icons, whatever the width of the window
            System.Windows.Controls.Primitives.UniformGrid iconPanel = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = (IconFactory.Kinds.Length + 1) / 2, HorizontalAlignment = HorizontalAlignment.Left };
            List<Border> iconTiles = new List<Border>();
            bool lightBar = IconFactory.TaskbarIsLight();
            WpfColor tileBg = lightBar ? WpfColor.FromRgb(243, 243, 243) : WpfColor.FromRgb(32, 32, 32);
            System.Drawing.Color previewInk = _s.TrayIconFollowTaskbar ? IconFactory.TaskbarInk() : IconFactory.InkColor(_s.TrayIconInk);
            System.Drawing.Color previewAccent = _s.TrayIconFollowTheme ? System.Drawing.Color.FromArgb(_t.Accent.R, _t.Accent.G, _t.Accent.B) : IconFactory.AccentColor(_s.TrayIconColor);
            Action paintIcons = delegate
            {
                for (int k = 0; k < iconTiles.Count; k++)
                {
                    bool on = IconFactory.Kinds[k] == trayIcon.V;
                    iconTiles[k].BorderBrush = on ? Br(_t.Accent) : Br(_t.Border);
                    iconTiles[k].BorderThickness = new Thickness(on ? 3 : 1);
                }
            };
            for (int k = 0; k < IconFactory.Kinds.Length; k++)
            {
                string kind = IconFactory.Kinds[k];
                Image pic = new Image { Width = 30, Height = 30, Source = IconPreview(kind, previewInk, previewAccent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Border tile = new Border { Child = pic, Width = 46, Height = 46, Background = Br(tileBg), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = _i.T("tray.icon." + kind) };
                tile.MouseLeftButtonUp += delegate { trayIcon.V = kind; paintIcons(); Preview(); };
                iconTiles.Add(tile);
                iconPanel.Children.Add(tile);
            }
            paintIcons();
            a.Children.Add(Row(_i.T("settings.tray_icon"), iconPanel));

            // ten colors for the arrow and the inside of the icon, five for the outline
            Func<string[], Val<string>, Func<string, WpfColor>, string, FrameworkElement> colorTiles = delegate (string[] keys, Val<string> val, Func<string, WpfColor> toColor, string tipPrefix)
            {
                WrapPanel wp = new WrapPanel { MaxWidth = 560 };
                foreach (string key in keys)
                {
                    string kk = key;
                    bool on = val.V == kk;
                    Border sw = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Br(toColor(kk)), BorderBrush = on ? Br(_t.Accent) : Br(_t.Border), BorderThickness = new Thickness(on ? 3 : 1), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand };
                    sw.ToolTip = _i.T(tipPrefix + kk);
                    sw.MouseLeftButtonUp += delegate { val.V = kk; Preview(); };
                    wp.Children.Add(sw);
                }
                return wp;
            };
            Func<string, WpfColor> accentToWpf = delegate (string key) { System.Drawing.Color c = IconFactory.AccentColor(key); return WpfColor.FromRgb(c.R, c.G, c.B); };
            Func<string, WpfColor> inkToWpf = delegate (string key) { System.Drawing.Color c = IconFactory.InkColor(key); return WpfColor.FromRgb(c.R, c.G, c.B); };
            FrameworkElement accentRow = Row(_i.T("settings.tray_color"), colorTiles(IconFactory.AccentKeys, trayColor, accentToWpf, "tray.color."), "settings.tray_color.tip");
            FrameworkElement inkRow = Row(_i.T("settings.tray_ink"), colorTiles(IconFactory.InkKeys, trayInk, inkToWpf, "tray.ink."), "settings.tray_ink.tip");
            a.Children.Add(accentRow);
            a.Children.Add(inkRow);
            CheckBox followTheme = Check(_i.T("settings.tray_follow_theme"), _s.TrayIconFollowTheme);
            Tip(followTheme, "settings.tray_follow_theme.tip");
            a.Children.Add(followTheme);
            CheckBox followBar = Check(_i.T("settings.tray_follow_taskbar"), _s.TrayIconFollowTaskbar);
            Tip(followBar, "settings.tray_follow_taskbar.tip");
            a.Children.Add(followBar);
            accentRow.Opacity = _s.TrayIconFollowTheme ? 0.4 : 1;
            inkRow.Opacity = _s.TrayIconFollowTaskbar ? 0.4 : 1;
            followBar.Checked += delegate { Preview(); };
            followBar.Unchecked += delegate { Preview(); };
            followTheme.Checked += delegate { Preview(); };
            followTheme.Unchecked += delegate { Preview(); };

            _savers.Add(r =>
            {
                Settings.Section(r, "ui")["theme_mode"] = mode.V;
                Settings.Section(r, "ui")["accent_theme"] = acc.V;
                Settings.Section(r, "ui")["tray_icon"] = trayIcon.V;
                Settings.Section(r, "ui")["tray_icon_color"] = trayColor.V;
                Settings.Section(r, "ui")["tray_icon_ink"] = trayInk.V;
                Settings.Section(r, "ui")["tray_icon_follow_taskbar"] = followBar.IsChecked == true;
                Settings.Section(r, "ui")["tray_icon_follow_theme"] = followTheme.IsChecked == true;
            });

            // ---- selection frame and dimming ----
            StackPanel fr = NewPage(_i.T("settings.tab.frame"));
            TextBox dim = Input(((int)Math.Round(_s.DimOpacity * 100)).ToString(), 60);
            fr.Children.Add(Row(_i.T("settings.dim"), dim));
            TextBox dimColor; FrameworkElement dimColorField = ColorField(_s.DimColor, 100, "settings.dim_color.tip", out dimColor);
            fr.Children.Add(Row(_i.T("settings.dim_color"), dimColorField, "settings.dim_color.tip"));
            TextBox frameColor; FrameworkElement frameColorField = ColorField(_s.FrameColor, 100, "settings.frame_color.tip", out frameColor);
            fr.Children.Add(Row(_i.T("settings.frame_color"), frameColorField, "settings.frame_color.tip"));
            TextBox frameThick = Input(_s.FrameThickness.ToString(System.Globalization.CultureInfo.InvariantCulture), 60);
            fr.Children.Add(Row(_i.T("settings.frame_thickness"), frameThick));
            TextBox handleSize = Input(_s.HandleSize.ToString(System.Globalization.CultureInfo.InvariantCulture), 60);
            fr.Children.Add(Row(_i.T("settings.handle_size"), handleSize));
            CheckBox cross = Check(_i.T("settings.crosshair"), _s.ShowCrosshair);
            fr.Children.Add(cross);
            CheckBox loupe = Check(_i.T("settings.magnifier"), _s.ShowMagnifier);
            fr.Children.Add(loupe);
            CheckBox sizeShow = Check(_i.T("settings.size_label"), _s.ShowSizeLabel);
            fr.Children.Add(sizeShow);
            Val<string> sizePos = new Val<string> { V = _s.ShowSizeLabelAbove ? "above" : "below" };
            fr.Children.Add(Row(_i.T("settings.size_label_pos"), Segment(new List<string> { "above", "below" }, new List<string> { _i.T("settings.above"), _i.T("settings.below") }, sizePos)));
            TextBox sizeFmt = Input(_s.SizeLabelFormat, 200);
            fr.Children.Add(Row(_i.T("settings.size_label_format"), sizeFmt));
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "overlay");
                double pct;
                if (double.TryParse(dim.Text.Trim(), out pct)) o["dim_opacity"] = Math.Max(0, Math.Min(90, pct)) / 100.0;
                o["dim_color"] = dimColor.Text.Trim();
                o["frame_color"] = frameColor.Text.Trim().Length == 0 ? "accent" : frameColor.Text.Trim();
                double th; if (double.TryParse(frameThick.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out th)) o["frame_thickness"] = Math.Max(1, Math.Min(8, th));
                double hs; if (double.TryParse(handleSize.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hs)) o["handle_size"] = Math.Max(5, Math.Min(20, hs));
                o["show_crosshair"] = cross.IsChecked == true;
                o["show_magnifier"] = loupe.IsChecked == true;
                o["show_size_label"] = sizeShow.IsChecked == true;
                o["size_label_above"] = sizePos.V == "above";
                o["size_label_format"] = sizeFmt.Text.Trim().Length == 0 ? "{w} x {h}" : sizeFmt.Text.Trim();
            });

            // ---- hotkeys ----
            StackPanel h = NewPage(_i.T("settings.tab.hotkeys"));
            h.Children.Add(Hint(_i.T("settings.hotkey_hint")));
            foreach (HotkeyDef d in _s.Hotkeys)
            {
                HotkeyDef def = d;
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                CheckBox on = Check("", def.Enabled);
                on.VerticalAlignment = VerticalAlignment.Center;
                on.Margin = new Thickness(0, 0, 10, 0);
                TextBox box = Input(def.Keys, 230);
                box.IsReadOnly = true;
                box.GotFocus += delegate
                {
                    _hotkeyCapture.SetTarget(box, delegate (string kn)
                    {
                        box.Text = kn;
                        box.BorderBrush = Br(_t.Border);
                    });
                };
                Border prtBtn = Btn("PrtSc", false, delegate
                {
                    box.Text = HotkeyCaptureHook.FormatPrintScreenCombo();
                    box.BorderBrush = Br(_t.Border);
                    box.Focus();
                });
                prtBtn.Margin = new Thickness(6, 0, 0, 0);
                prtBtn.Padding = new Thickness(8, 4, 8, 4);
                prtBtn.VerticalAlignment = VerticalAlignment.Center;
                prtBtn.ToolTip = "Держи Ctrl+Alt+Shift и нажми PrtSc, или Ctrl+Alt+Shift и клик по PrtSc";
                box.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(delegate (object s2, KeyEventArgs e2)
                {
                    e2.Handled = true;
                    string kn = KeyName(e2);
                    if (kn != null) { box.Text = kn; box.BorderBrush = Br(_t.Border); }
                    else if (e2.Key == Key.Back || e2.Key == Key.Delete) box.Text = "";
                }), true);
                TextBlock lab = new TextBlock { Text = _i.T(def.Action), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text) };
                row.Children.Add(on); row.Children.Add(box); row.Children.Add(prtBtn); row.Children.Add(lab);
                h.Children.Add(row);
                _savers.Add(r =>
                {
                    uint m, vk;
                    string keys = box.Text.Trim();
                    JsonObject o = new JsonObject();
                    bool ok = HotkeyManager.TryParse(keys, out m, out vk);
                    o["keys"] = ok ? keys : def.Keys;
                    o["enabled"] = on.IsChecked == true && ok;
                    o["action"] = def.Action;
                    Settings.Section(r, "hotkeys")[def.Id] = o;
                });
            }

            BuildActionKeys(h);   // the shortcuts after a capture: save, save as, copy, undo ...

            // ---- tools ----
            StackPanel tl = NewPage(_i.T("settings.tab.tools"));
            tl.Children.Add(Hint(_i.T("settings.tools_hint")));
            string[] toolKeys = { "arrow", "line", "rectangle", "ellipse", "brush", "marker", "text", "steps", "blur", "fill", "spotlight", "magnifier", "eraser", "picker" };
            List<KeyValuePair<string, CheckBox>> tcs = new List<KeyValuePair<string, CheckBox>>();
            foreach (string k in toolKeys)
            {
                CheckBox c = Check(_i.T("tools." + k), _s.ToolEnabled(k));
                tcs.Add(new KeyValuePair<string, CheckBox>(k, c));
                tl.Children.Add(c);
            }
            tl.Children.Add(Hint(_i.T("settings.tools_new_hint")));
            _savers.Add(r => { foreach (KeyValuePair<string, CheckBox> kv in tcs) Settings.Section(r, "tools")[kv.Key] = kv.Value.IsChecked == true; });

            // ---- panels ----
            StackPanel pn = NewPage(_i.T("settings.tab.layout"));
            Val<string> tb = new Val<string> { V = _s.ToolbarPosition };
            pn.Children.Add(Row(_i.T("layout.toolbar_position"), Segment(new List<string> { "left", "right", "top", "bottom" },
                new List<string> { _i.T("layout.left"), _i.T("layout.right"), _i.T("layout.top"), _i.T("layout.bottom") }, tb)));
            Val<string> pp = new Val<string> { V = _s.PropertiesPosition };
            pn.Children.Add(Row(_i.T("layout.properties_position"), Segment(new List<string> { "top", "bottom" },
                new List<string> { _i.T("layout.top"), _i.T("layout.bottom") }, pp)));
            Val<string> bsz = new Val<string> { V = _s.ButtonSize };
            pn.Children.Add(Row(_i.T("settings.button_size"), Segment(new List<string> { "small", "medium", "large" },
                new List<string> { _i.T("settings.size.small"), _i.T("settings.size.medium"), _i.T("settings.size.large") }, bsz)));
            CheckBox labels = Check(_i.T("settings.tool_labels"), _s.ToolLabels);
            pn.Children.Add(labels);
            CheckBox drag = Check(_i.T("settings.panels_draggable"), _s.PanelsDraggable);
            pn.Children.Add(drag);

            // exact size of the buttons and the look of the two panels (apart from the theme)
            TextBox btnPx = Input(_s.ButtonPx.ToString(), 60);
            pn.Children.Add(Row(_i.T("settings.button_px"), SliderBox(btnPx, 22, 64), "settings.button_px.tip"));
            // a thin panel with big icons: the icon takes a larger part of its button, the gaps and the padding shrink
            TextBox iconFill = Input(_s.IconFill.ToString(), 60), btnGap = Input(_s.ButtonGap.ToString(), 60), panelPad = Input(_s.PanelPad.ToString(), 60);
            pn.Children.Add(Row(_i.T("settings.icon_fill"), SliderBox(iconFill, 0, 100), "settings.icon_fill.tip"));
            pn.Children.Add(Row(_i.T("settings.button_gap"), SliderBox(btnGap, 0, 8)));
            pn.Children.Add(Row(_i.T("settings.panel_pad"), SliderBox(panelPad, 0, 12)));
            RegisterDock(_i.T("settings.tab.layout"), BuildThinPanelDock(iconFill, btnPx, btnGap, panelPad, bsz), RefreshThinDock);
            List<Action<JsonObject>> styleSavers = new List<Action<JsonObject>>();
            Action<string, string, bool, int, bool, int, bool> addPanelStyle = delegate (string key, string title, bool fillOn, int opacity, bool borderOn, int radius, bool shadowOn)
            {
                pn.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 14, 0, 4) });
                CheckBox cFill = Check(_i.T("settings.panel_fill"), fillOn); Tip(cFill, "settings.panel_fill.tip");
                CheckBox cBorder = Check(_i.T("settings.panel_border"), borderOn);
                CheckBox cShadow = Check(_i.T("settings.panel_shadow"), shadowOn);
                TextBox tOpacity = Input(opacity.ToString(), 60);
                TextBox tRadius = Input(radius.ToString(), 60);
                StackPanel line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(cFill); line.Children.Add(new TextBlock { Width = 18 }); line.Children.Add(cBorder); line.Children.Add(new TextBlock { Width = 18 }); line.Children.Add(cShadow);
                pn.Children.Add(line);
                pn.Children.Add(Row(_i.T("settings.panel_opacity"), tOpacity, "settings.panel_opacity.tip"));
                pn.Children.Add(Row(_i.T("settings.panel_radius"), tRadius, "settings.panel_radius.tip"));
                styleSavers.Add(r =>
                {
                    JsonObject o = Settings.Section(Settings.Section(r, "layout"), key);
                    o["fill"] = cFill.IsChecked == true; o["border"] = cBorder.IsChecked == true; o["shadow"] = cShadow.IsChecked == true;
                    int op; if (int.TryParse(tOpacity.Text.Trim(), out op)) o["opacity"] = Math.Max(0, Math.Min(100, op));
                    int rd; if (int.TryParse(tRadius.Text.Trim(), out rd)) o["radius"] = Math.Max(-1, Math.Min(40, rd));
                });
            };
            addPanelStyle("tool_panel", _i.T("settings.tool_panel_title"), _s.ToolPanelFill, _s.ToolPanelOpacity, _s.ToolPanelBorder, _s.ToolPanelRadius, _s.ToolPanelShadow);
            addPanelStyle("props_panel", _i.T("settings.props_panel_title"), _s.PropsPanelFill, _s.PropsPanelOpacity, _s.PropsPanelBorder, _s.PropsPanelRadius, _s.PropsPanelShadow);
            _savers.Add(r =>
            {
                int px; if (int.TryParse(btnPx.Text.Trim(), out px)) Settings.Section(r, "layout")["button_px"] = px <= 0 ? 0 : Math.Max(22, Math.Min(64, px));
                PutInt(Settings.Section(r, "layout"), "icon_fill", iconFill, 0, 100);
                PutInt(Settings.Section(r, "layout"), "button_gap", btnGap, 0, 8);
                PutInt(Settings.Section(r, "layout"), "panel_pad", panelPad, 0, 12);
                foreach (Action<JsonObject> ss in styleSavers) ss(r);
            });
            // the order of the tool buttons: arrows move a tool up or down
            pn.Children.Add(Hint(_i.T("settings.tool_order_hint")));
            List<string> order = new List<string>();
            if (!string.IsNullOrWhiteSpace(_s.ToolOrder))
                foreach (string part in _s.ToolOrder.Split(',')) { string k = part.Trim().ToLowerInvariant(); if (Array.IndexOf(ToolKeys, k) >= 0 && !order.Contains(k)) order.Add(k); }
            foreach (string k in ToolKeys) if (!order.Contains(k)) order.Add(k);
            StackPanel orderHost = new StackPanel();
            Action rebuildOrder = null;
            rebuildOrder = delegate
            {
                orderHost.Children.Clear();
                for (int idx = 0; idx < order.Count; idx++)
                {
                    int at = idx;
                    StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                    TextBlock nameLabel = new TextBlock { Text = "☰  " + _i.T("tools." + order[idx]), Width = 220, VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text), Cursor = Cursors.SizeAll, ToolTip = _i.T("tip.drag_row") };
                    line.Tag = order[idx];
                    DragReorder(nameLabel, line, orderHost, order, rebuildOrder);   // drag the name with the mouse: it stands between the others where it is dropped
                    line.Children.Add(nameLabel);
                    line.Children.Add(Btn("↑", false, delegate { if (at > 0) { string x = order[at]; order[at] = order[at - 1]; order[at - 1] = x; rebuildOrder(); } }));
                    line.Children.Add(Btn("↓", false, delegate { if (at < order.Count - 1) { string x = order[at]; order[at] = order[at + 1]; order[at + 1] = x; rebuildOrder(); } }));
                    orderHost.Children.Add(line);
                }
            };
            rebuildOrder();
            pn.Children.Add(orderHost);
            _savers.Add(r =>
            {
                JsonObject lay = Settings.Section(r, "layout");
                lay["toolbar_position"] = tb.V; lay["properties_panel_position"] = pp.V;
                lay["button_size"] = bsz.V; lay["tool_labels"] = labels.IsChecked == true; lay["panels_draggable"] = drag.IsChecked == true;
                lay["tool_order"] = string.Join(",", order);
            });

            // ---- saving ----
            StackPanel sv = NewPage(_i.T("settings.tab.output"));
            TextBox folder = Input(_s.SaveFolder, 420);
            StackPanel folderRow = new StackPanel { Orientation = Orientation.Horizontal };
            folderRow.Children.Add(folder);
            folderRow.Children.Add(Btn(_i.T("settings.browse"), false, delegate
            {
                using (System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    d.SelectedPath = Environment.ExpandEnvironmentVariables(folder.Text);
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) folder.Text = d.SelectedPath;
                }
            }));
            sv.Children.Add(Row(_i.T("settings.save_folder"), folderRow));
            TextBox name = Input(_s.FileNameTemplate, 420);
            sv.Children.Add(Row(_i.T("settings.file_name"), name));
            Val<string> fmt = new Val<string> { V = ImageOutput.NormalizeFormat(_s.Format) };
            List<string> fmtVals = new List<string>(ImageOutput.Formats);
            List<string> fmtLabels = new List<string> { "PNG", "JPG", "WebP", "BMP" };
            sv.Children.Add(Row(_i.T("settings.format"), Segment(fmtVals, fmtLabels, fmt)));
            sv.Children.Add(Hint(_i.T("settings.format_hint")));
            TextBox q = Input(_s.JpgQuality.ToString(), 60);
            sv.Children.Add(Row(_i.T("settings.jpg_quality"), q));
            TextBox wq = Input(_s.WebpQuality.ToString(), 60);
            sv.Children.Add(Row(_i.T("settings.webp_quality"), wq));
            CheckBox wl = Check(_i.T("settings.webp_lossless"), _s.WebpLossless);
            sv.Children.Add(wl);
            Val<string> fsFmt = new Val<string> { V = string.Equals(_s.FullscreenFormat, "same", StringComparison.OrdinalIgnoreCase) ? "same" : ImageOutput.NormalizeFormat(_s.FullscreenFormat) };
            List<string> fsVals = new List<string> { "same" }; fsVals.AddRange(ImageOutput.Formats);
            List<string> fsLabels = new List<string> { _i.T("settings.format_same") }; fsLabels.AddRange(fmtLabels);
            sv.Children.Add(Row(_i.T("settings.fullscreen_format"), Segment(fsVals, fsLabels, fsFmt)));
            Val<string> scope = new Val<string> { V = _s.FullscreenScope };
            // four choices: the monitor with the mouse, monitor 1, monitor 2, all monitors (an older saved value stays visible too)
            List<string> scopeVals = new List<string> { "cursor_monitor", "monitor_1", "monitor_2", "all_monitors" };
            List<string> scopeLabels = new List<string> { _i.T("settings.scope.cursor_monitor"), _i.T("settings.scope.monitor_1"), _i.T("settings.scope.monitor_2"), _i.T("settings.scope.all_monitors") };
            if (!scopeVals.Contains(scope.V)) { scopeVals.Add(scope.V); scopeLabels.Add(_i.T("settings.scope." + scope.V)); }
            sv.Children.Add(Row(_i.T("settings.fullscreen_scope"), Segment(scopeVals, scopeLabels, scope), "settings.fullscreen_scope.tip"));
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "output");
                o["save_folder"] = folder.Text.Trim();
                o["file_name_template"] = name.Text.Trim();
                o["format"] = fmt.V;
                int qi;
                if (int.TryParse(q.Text.Trim(), out qi)) o["jpg_quality"] = Math.Max(10, Math.Min(100, qi));
                int wqi;
                if (int.TryParse(wq.Text.Trim(), out wqi)) o["webp_quality"] = Math.Max(1, Math.Min(100, wqi));
                o["webp_lossless"] = wl.IsChecked == true;
                o["fullscreen_format"] = fsFmt.V;
                o["fullscreen_scope"] = scope.V;
            });

            // ---- capture ----
            StackPanel cp = NewPage(_i.T("settings.tab.capture"));
            TextBox delay = Input(_s.CaptureDelay.ToString(), 60);
            cp.Children.Add(Row(_i.T("settings.delay"), delay));
            CheckBox snap = Check(_i.T("settings.window_snap"), _s.WindowSnap);
            cp.Children.Add(snap);
            _savers.Add(r =>
            {
                int di;
                if (int.TryParse(delay.Text.Trim(), out di)) Settings.Section(r, "capture")["delay_seconds"] = Math.Max(1, Math.Min(60, di));
                Settings.Section(r, "capture")["window_snap"] = snap.IsChecked == true;
            });

            // ---- cloud ----
            StackPanel cl = NewPage(_i.T("settings.tab.cloud"));
            Val<string> upTarget = new Val<string> { V = _s.UploadTarget };
            cl.Children.Add(Row(_i.T("settings.upload_target"), Segment(new List<string> { "none", "folder", "gdrive" },
                new List<string> { _i.T("settings.upload.none"), _i.T("settings.upload.folder"), _i.T("settings.upload.gdrive") }, upTarget)));
            CheckBox upCopy = Check(_i.T("settings.upload_copy_link"), _s.UploadCopyLink);
            cl.Children.Add(upCopy);
            CheckBox upAuto = Check(_i.T("settings.upload_auto"), _s.UploadAuto);
            cl.Children.Add(upAuto);
            TextBox upFolder = Input(_s.UploadFolderPath, 420);
            StackPanel upFolderRow = new StackPanel { Orientation = Orientation.Horizontal };
            upFolderRow.Children.Add(upFolder);
            upFolderRow.Children.Add(Btn(_i.T("settings.browse"), false, delegate
            {
                using (System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    d.SelectedPath = Environment.ExpandEnvironmentVariables(upFolder.Text);
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) upFolder.Text = d.SelectedPath;
                }
            }));
            cl.Children.Add(Row(_i.T("settings.upload_folder"), upFolderRow));
            cl.Children.Add(Hint(_i.T("settings.gdrive_hint")));
            TextBox gid = Input(_s.GdClientId, 460);
            cl.Children.Add(Row(_i.T("settings.gd_client_id"), gid));
            TextBox gsec = Input(_s.GdClientSecret, 460);
            cl.Children.Add(Row(_i.T("settings.gd_client_secret"), gsec));
            TextBox gname = Input(_s.GdFolderName, 260);
            cl.Children.Add(Row(_i.T("settings.gd_folder_name"), gname));
            TextBox gfid = Input(_s.GdFolderId, 460);
            cl.Children.Add(Row(_i.T("settings.gd_folder_id"), gfid));
            CheckBox gpub = Check(_i.T("settings.gd_public"), _s.GdPublic);
            cl.Children.Add(gpub);
            StackPanel cloudButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            cloudButtons.Children.Add(Btn(_i.T("settings.gd_sign_in"), true, delegate { SignInClick(); }));
            cloudButtons.Children.Add(Btn(_i.T("settings.gd_sign_out"), false, delegate { SaveQuiet(); _app.GDriveSignOut(); _cloudStatus.Text = _i.T("settings.gd_not_signed"); }));
            cloudButtons.Children.Add(Btn(_i.T("settings.upload_test"), false, delegate { SaveQuiet(); _app.UploadTestImage(); _cloudStatus.Text = _i.T("settings.upload_test_sent"); }));
            cl.Children.Add(cloudButtons);
            _cloudStatus = new TextBlock { Foreground = Br(_t.TextSecondary), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 6, 0, 0) };
            bool signed = false;
            try { signed = _app.GDriveSignedIn; } catch { }
            _cloudStatus.Text = signed ? _i.T("settings.gd_signed") : _i.T("settings.gd_not_signed");
            cl.Children.Add(_cloudStatus);
            _savers.Add(r =>
            {
                JsonObject u = Settings.Section(r, "upload");
                u["target"] = upTarget.V;
                u["copy_link"] = upCopy.IsChecked == true;
                u["auto_after_save"] = upAuto.IsChecked == true;
                u["folder_path"] = upFolder.Text.Trim();
                JsonObject gd = Settings.Section(u, "gdrive");
                gd["client_id"] = gid.Text.Trim();
                gd["client_secret"] = gsec.Text.Trim();
                gd["folder_name"] = gname.Text.Trim();
                gd["folder_id"] = gfid.Text.Trim();
                gd["public_link"] = gpub.IsChecked == true;
            });

            // ---- text ----
            StackPanel tx = NewPage(_i.T("settings.tab.text"));
            CheckBox neural = Check(_i.T("settings.neural"), _s.NeuralSafe);
            tx.Children.Add(neural);
            tx.Children.Add(Hint(_i.T("settings.neural_hint")));
            RegisterDock(_i.T("settings.tab.text"), BuildNeuralDock(neural), null);        // "off" and "on" side by side
            _savers.Add(r => { Settings.Section(r, "text")["neural_safe"] = neural.IsChecked == true; });

            // ---- modifier keys ----
            StackPanel md = NewPage(_i.T("settings.tab.mods"));
            md.Children.Add(Hint(_i.T("settings.mods_hint")));
            // master switches: everything, while drawing, a plain tap, and the two targets (the region itself, shapes and arrows)
            CheckBox mEnabled = Check(_i.T("settings.mod_enabled"), _s.ModEnabled); Tip(mEnabled, "settings.mod_enabled.tip");
            CheckBox mHold = Check(_i.T("settings.mod_hold"), _s.ModHold); Tip(mHold, "settings.mod_hold.tip");
            CheckBox mTap = Check(_i.T("settings.mod_tap"), _s.ModTap); Tip(mTap, "settings.mod_tap.tip");
            CheckBox mRegion = Check(_i.T("settings.mod_region"), _s.ModRegion); Tip(mRegion, "settings.mod_region.tip");
            CheckBox mShapes = Check(_i.T("settings.mod_shapes"), _s.ModShapes); Tip(mShapes, "settings.mod_shapes.tip");
            md.Children.Add(mEnabled); md.Children.Add(mHold); md.Children.Add(mTap); md.Children.Add(mRegion); md.Children.Add(mShapes);

            string[] combos = { "shift", "ctrl", "alt", "ctrl+shift", "ctrl+alt", "shift+alt", "ctrl+shift+alt" };
            string[] actions = { "none", "constrain", "center", "rounded", "axis", "double", "fill", "constrain,center" };

            // which choices the lists below show: the unused ones can be hidden to keep the page clean (a switch shows them all again)
            md.Children.Add(new TextBlock { Text = _i.T("settings.mod_shown"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 12, 0, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left });
            HashSet<string> shownNow = new HashSet<string>((_s.ModShownActions ?? "").Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0));
            List<KeyValuePair<string, CheckBox>> shownBoxes = new List<KeyValuePair<string, CheckBox>>();
            WrapPanel shownPanel = new WrapPanel { MaxWidth = 580 };
            foreach (string act in actions)
            {
                if (act == "none") continue;
                string shownKey = act.Replace(",", "+");
                CheckBox cb = Check(_i.T("act." + act), shownNow.Contains(shownKey));
                cb.Margin = new Thickness(0, 2, 16, 4);
                cb.Checked += delegate { Preview(); };
                cb.Unchecked += delegate { Preview(); };
                shownBoxes.Add(new KeyValuePair<string, CheckBox>(shownKey, cb));
                shownPanel.Children.Add(cb);
            }
            md.Children.Add(shownPanel);
            md.Children.Add(Btn(_i.T("settings.mod_show_all"), false, delegate
            {
                // every choice back in the lists
                Settings.PatchUser(r => { Settings.Section(r, "modifiers")["shown_actions"] = string.Join(",", shownBoxes.Select(kv => kv.Key)); });
                int pg = _current; _app.ReloadSettings(); ReopenAt(pg);
            }));

            List<KeyValuePair<string, Val<string>>> mv = new List<KeyValuePair<string, Val<string>>>();
            foreach (string combo in combos)
            {
                string cur; if (!_s.ModActionMap.TryGetValue(combo, out cur) || string.IsNullOrWhiteSpace(cur)) cur = "none";
                Val<string> v = new Val<string> { V = cur };
                // only the shown choices (and the one that is chosen now)
                List<string> vals = actions.Where(x => x == "none" || shownNow.Contains(x.Replace(",", "+"))).ToList();
                if (!vals.Contains(cur)) vals.Add(cur);
                List<string> labs = vals.Select(x => _i.T("act." + x)).ToList();
                md.Children.Add(Row(combo.Replace("ctrl", "Ctrl").Replace("shift", "Shift").Replace("alt", "Alt"), Segment(vals, labs, v)));
                mv.Add(new KeyValuePair<string, Val<string>>(combo, v));
            }
            _savers.Add(r =>
            {
                foreach (KeyValuePair<string, Val<string>> kv in mv) Settings.Section(r, "modifier_actions")[kv.Key] = kv.Value.V;
                JsonObject m = Settings.Section(r, "modifiers");
                m["enabled"] = mEnabled.IsChecked == true; m["while_drawing"] = mHold.IsChecked == true; m["tap"] = mTap.IsChecked == true;
                m["for_region"] = mRegion.IsChecked == true; m["for_shapes"] = mShapes.IsChecked == true;
                m["shown_actions"] = string.Join(",", shownBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
            });
        }

        private string AlignLabel(string a)
        {
            return a == "left" ? _i.T("layout.left") : a == "center" ? _i.T("settings.center") : a == "right" ? _i.T("layout.right") : a == "justify" ? _i.T("settings.justify") : _i.T("settings.align_global");
        }

        private string HeadLabel(string h)
        {
            return h == "" ? _i.T("settings.head_global") : _i.T("settings.head." + h);
        }

    }
}
