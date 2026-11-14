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
        // ---- beautiful screenshot (section "beautify") ----
        // The page: the looks as compact tiles over all the room between the settings column and the pictures, which are pinned to the right edge.
        // A small pencil at the top right of a tile opens the editor of that look (SettingsWindow.LookEditor.cs); "Random look" adds a new one.
        private void BuildBeautifyPage()
        {
            StackPanel bp = NewPage(_i.T("settings.tab.beautify"));
            bp.Children.Add(Hint(_i.T("settings.beautify_hint")));
            CheckBox on = Check(_i.T("settings.beautify_enabled"), _s.BeautifyEnabled); bp.Children.Add(on);

            _looks = _s.BeautifyLooks.Select(x => x.Clone()).ToList();
            _lookStyle = new Val<string> { V = BeautifyStyles.IsKnown(_s.BeautifyStyle, _looks) ? _s.BeautifyStyle : "classic" };
            WrapPanel tiles = new WrapPanel();
            Dictionary<string, Border> tileBox = new Dictionary<string, Border>();
            Dictionary<string, Image> tileImg = new Dictionary<string, Image>();
            Dictionary<string, string> tileSig = new Dictionary<string, string>();
            Action refresh = null;
            string tileKey = null;
            string lastKey = null;
            Action paintTiles = delegate { foreach (KeyValuePair<string, Border> kv in tileBox) { bool sel = kv.Key == _lookStyle.V; kv.Value.BorderBrush = sel ? Br(_t.Accent) : Br(_t.Border); kv.Value.BorderThickness = new Thickness(sel ? 3 : 1); } };
            _paintTilesAct = paintTiles;

            Action buildTiles = delegate
            {
                tiles.Children.Clear(); tileBox.Clear(); tileImg.Clear(); tileSig.Clear();
                tileKey = null; lastKey = null;
                foreach (string sid in BeautifyStyles.AllIds(_looks))
                {
                    string id = sid;
                    Image im = new Image { Width = 126, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
                    RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                    StackPanel cell = new StackPanel { Width = 126 };
                    cell.Children.Add(new Border { Height = 82, Child = im });
                    cell.Children.Add(new TextBlock { Text = LookName(id), FontSize = 12, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Br(_t.Text), TextTrimming = TextTrimming.CharacterEllipsis });
                    // the small pencil at the top right of the picture: edit this look
                    Border pencil = new Border
                    {
                        Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand,
                        Background = new SolidColorBrush(WpfColor.FromArgb(225, _t.Surface.R, _t.Surface.G, _t.Surface.B)), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), ToolTip = _i.T("settings.look_edit"),
                        Child = new TextBlock { Text = "✎", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 13, Foreground = Br(_t.Text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    };
                    pencil.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; OpenLookEditor(id); };
                    Grid holder = new Grid();
                    holder.Children.Add(cell); holder.Children.Add(pencil);
                    Border tile = new Border { Child = holder, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Br(_t.Surface) };
                    tile.MouseLeftButtonUp += delegate { _lookStyle.V = id; paintTiles(); if (refresh != null) refresh(); };
                    tileBox[id] = tile; tileImg[id] = im; tiles.Children.Add(tile);
                }
                paintTiles();
            };
            _rebuildTiles = delegate { buildTiles(); if (refresh != null) refresh(); };
            buildTiles();
            bp.Children.Add(Row(_i.T("settings.beautify_style"), tiles));

            // random look, the editor of the chosen look
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -4, 0, 10) };
            actions.Children.Add(Btn(_i.T("settings.look_new"), false, delegate { NewRandomLook(); }));
            actions.Children.Add(new TextBlock { Text = _i.T("settings.look_new_tip"), Foreground = Br(_t.TextSecondary), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
            bp.Children.Add(actions);
            _lookEditorHost = new StackPanel();                          // the editor of the chosen look opens right under the tiles
            bp.Children.Add(_lookEditorHost);
            // what the random look may be: dark or not, which bases, how wild (used by "Random look", the editor and the item "Random" of the list in the editor)
            _randomOpts = BeautifyRandomOptions.From(_s);
            bp.Children.Add(new TextBlock { Text = _i.T("settings.random_title"), FontWeight = FontWeights.SemiBold, Foreground = Br(_t.Text), Margin = new Thickness(0, 4, 0, 4) });
            CheckBox rDark = Check(_i.T("settings.random_dark"), _randomOpts.AllowDark);
            rDark.Checked += delegate { _randomOpts.AllowDark = true; }; rDark.Unchecked += delegate { _randomOpts.AllowDark = false; };
            bp.Children.Add(rDark);
            WrapPanel rTypes = new WrapPanel();
            List<KeyValuePair<string, CheckBox>> rBoxes = new List<KeyValuePair<string, CheckBox>>();
            HashSet<string> allowedNow = new HashSet<string>(_randomOpts.Types.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()));
            foreach (string bt in BeautifyLook.Types)
            {
                CheckBox cb = Check(_i.T("beautify.style." + bt), allowedNow.Contains(bt));
                cb.Margin = new Thickness(0, 2, 16, 4);
                rBoxes.Add(new KeyValuePair<string, CheckBox>(bt, cb));
                Action sync = delegate { _randomOpts.Types = string.Join(",", rBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key)); };
                cb.Checked += delegate { sync(); }; cb.Unchecked += delegate { sync(); };
                rTypes.Children.Add(cb);
            }
            bp.Children.Add(Row(_i.T("settings.random_types"), rTypes));
            TextBox wildBox = Input(_randomOpts.Wild.ToString(), 60);
            wildBox.TextChanged += delegate { int wv; if (int.TryParse(wildBox.Text.Trim(), out wv)) _randomOpts.Wild = Math.Max(0, Math.Min(100, wv)); };
            bp.Children.Add(Row(_i.T("settings.random_wild"), SliderBox(wildBox, 0, 100)));

            Val<string> bg = new Val<string> { V = _s.BeautifyBackground };
            bp.Children.Add(Row(_i.T("settings.beautify_background"), SegmentOf(new string[] { "gradient", "solid", "transparent" }, "settings.beautify_bg.", bg)));
            Val<string> preset = new Val<string> { V = _s.BeautifyPreset };
            bp.Children.Add(Row(_i.T("settings.beautify_preset"), SegmentOf(BeautifyOptions.Presets, "settings.beautify_preset.", preset)));
            TextBox c1 = Input(_s.BeautifyColor1, 100), c2 = Input(_s.BeautifyColor2, 100);
            StackPanel colors = new StackPanel { Orientation = Orientation.Horizontal };
            colors.Children.Add(c1); colors.Children.Add(new TextBlock { Width = 12 }); colors.Children.Add(c2);
            bp.Children.Add(Row(_i.T("settings.beautify_colors"), colors));
            TextBox pad = Input(_s.BeautifyPadding.ToString(), 60), radius = Input(_s.BeautifyRadius.ToString(), 60);
            bp.Children.Add(Row(_i.T("settings.beautify_padding"), SliderBox(pad, 0, 200)));
            bp.Children.Add(Row(_i.T("settings.beautify_radius"), SliderBox(radius, 0, 60)));
            CheckBox shadow = Check(_i.T("settings.beautify_shadow"), _s.BeautifyShadow); bp.Children.Add(shadow);
            TextBox blur = Input(_s.BeautifyShadowBlur.ToString(), 60), opac = Input(_s.BeautifyShadowOpacity.ToString(), 60);
            bp.Children.Add(Row(_i.T("settings.beautify_blur"), SliderBox(blur, 0, 80)));
            bp.Children.Add(Row(_i.T("settings.beautify_opacity"), SliderBox(opac, 0, 100)));

            Func<BeautifyOptions> read = delegate
            {
                int v;
                BeautifyOptions o = new BeautifyOptions { Enabled = on.IsChecked == true, Style = _lookStyle.V, Looks = BeautifyOptions.LookMap(_looks), Background = bg.V, Preset = preset.V, Color1 = c1.Text.Trim(), Color2 = c2.Text.Trim(), Shadow = shadow.IsChecked == true };
                o.Padding = int.TryParse(pad.Text.Trim(), out v) ? Math.Max(0, Math.Min(300, v)) : 48;
                o.Radius = int.TryParse(radius.Text.Trim(), out v) ? Math.Max(0, Math.Min(100, v)) : 14;
                o.ShadowBlur = int.TryParse(blur.Text.Trim(), out v) ? Math.Max(0, Math.Min(120, v)) : 28;
                o.ShadowOpacity = int.TryParse(opac.Text.Trim(), out v) ? Math.Max(0, Math.Min(100, v)) : 40;
                return o;
            };
            // the pictures are pinned to the right edge: how a screenshot looks with the chosen look (always on in the picture) and without it
            Image preview = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            Image plain = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 260 };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
            _beautyPreviewImg = preview;
            Func<string, string> sigOf = delegate (string id) { BeautifyLook l = _looks.FirstOrDefault(x => x.Id == id); return id + (l == null ? "-" : l.ToJson().ToJsonString()); };
            refresh = delegate
            {
                try
                {
                    BeautifyOptions o = read();
                    o.Enabled = true;
                    string common = o.Background + o.Preset + o.Color1 + o.Color2 + o.Padding + o.Radius + o.Shadow + o.ShadowBlur + o.ShadowOpacity;
                    string key = o.Style + common + string.Join("|", BeautifyStyles.AllIds(_looks).Select(sigOf));
                    if (key == lastKey) return;
                    lastKey = key;
                    using (System.Drawing.Bitmap sample = BeautifySample())
                    {
                        using (System.Drawing.Bitmap dressed = Beautifier.Apply(sample, o))
                            preview.Source = Beautifier.ToSource(dressed);
                        if (_editPreviewImg != null) _editPreviewImg.Source = preview.Source;       // the same picture beside the buttons of the editor
                        if (plain.Source == null) plain.Source = Beautifier.ToSource(sample);
                        bool all = common != tileKey;             // the tiles follow the colors, the space and the shadow; one tile follows its own look
                        tileKey = common;
                        foreach (string sid in BeautifyStyles.AllIds(_looks))
                        {
                            string sig = sigOf(sid);
                            string old; if (!all && tileSig.TryGetValue(sid, out old) && old == sig) continue;
                            tileSig[sid] = sig;
                            Image target; if (!tileImg.TryGetValue(sid, out target)) continue;
                            BeautifyOptions t = read(); t.Enabled = true; t.Style = sid;
                            using (System.Drawing.Bitmap d2 = Beautifier.Apply(sample, t)) target.Source = Beautifier.ToSource(d2);
                        }
                    }
                }
                catch (Exception ex) { Log.Write("beautify preview error: " + ex.Message); }
            };
            _lookChanged = delegate { if (refresh != null) refresh(); };
            StackPanel dock = new StackPanel();
            dock.Children.Add(DockCaption(_i.T("settings.beautify_with")));
            dock.Children.Add(preview);
            dock.Children.Add(DockCaption(_i.T("settings.beautify_without")));
            dock.Children.Add(plain);
            RegisterDock(_i.T("settings.tab.beautify"), dock, refresh, true);

            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "beautify");
                o["enabled"] = on.IsChecked == true;
                o["style"] = _lookStyle.V;
                o["looks"] = LooksToJson();
                JsonObject ro = Settings.Section(o, "random");
                ro["allow_dark"] = _randomOpts.AllowDark; ro["types"] = _randomOpts.Types; ro["wild"] = _randomOpts.Wild;
                o["background"] = bg.V;
                o["preset"] = preset.V;
                o["color1"] = c1.Text.Trim();
                o["color2"] = c2.Text.Trim();
                PutInt(o, "padding", pad, 0, 300);
                PutInt(o, "radius", radius, 0, 100);
                o["shadow"] = shadow.IsChecked == true;
                PutInt(o, "shadow_blur", blur, 0, 120);
                PutInt(o, "shadow_opacity", opac, 0, 100);
            });
        }

        /// <summary>A small picture that looks like a program window, for the preview.</summary>
        internal static System.Drawing.Bitmap BeautifySample()
        {
            System.Drawing.Bitmap b = new System.Drawing.Bitmap(260, 160);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(b))
            {
                // a neutral page (a heading, a picture, lines, a button): no window decorations of its own, so the frames of the looks stand out
                g.Clear(System.Drawing.Color.FromArgb(250, 250, 252));
                using (System.Drawing.Brush head = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(70, 78, 98))) g.FillRectangle(head, 16, 14, 120, 12);
                using (System.Drawing.Brush pic = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(206, 224, 244))) g.FillRectangle(pic, 16, 38, 96, 66);
                using (System.Drawing.Brush sun = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(246, 190, 70))) g.FillEllipse(sun, 78, 46, 20, 20);
                using (System.Drawing.Brush hill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(120, 170, 130))) g.FillPolygon(hill, new System.Drawing.Point[] { new System.Drawing.Point(16, 104), new System.Drawing.Point(52, 70), new System.Drawing.Point(84, 104) });
                using (System.Drawing.Brush line = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(170, 176, 190)))
                    for (int i = 0; i < 4; i++) g.FillRectangle(line, 128, 40 + i * 16, i % 2 == 0 ? 112 : 80, 7);
                using (System.Drawing.Brush btn = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(60, 120, 230))) g.FillRectangle(btn, 16, 124, 72, 22);
                using (System.Drawing.Brush line2 = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(190, 195, 208))) g.FillRectangle(line2, 128, 124, 96, 7);
            }
            return b;
        }
    }
}
