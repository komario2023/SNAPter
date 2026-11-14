using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>The editor of one look of the beautiful screenshot: what can be changed depends on its base (a window has a title bar, a polaroid a caption...).
    /// "Random" replaces the look with a freshly generated one (a built-in look too: the new look takes its place and can be reset), "+ Random look" adds one more.
    /// The changes live on the page until Apply (the picture and the tiles follow at once); Apply writes config beautify.looks.</summary>
    internal sealed partial class SettingsWindow
    {
        private List<BeautifyLook> _looks = new List<BeautifyLook>();
        private Val<string> _lookStyle;
        private StackPanel _lookEditorHost;
        private Action _rebuildTiles, _lookChanged;
        private string _editingLook;
        private Action _paintTilesAct;                  // outlines the chosen tile
        private Image _beautyPreviewImg, _editPreviewImg;   // the big picture at the right edge and the one beside the buttons of the editor
        private BeautifyLook _editBuffer;                 // a built-in look that is open in the editor but not changed yet
        private readonly Random _lookRng = new Random();
        private BeautifyRandomOptions _randomOpts = new BeautifyRandomOptions();   // what the random look may be (dark or not, which bases, how wild)

        private string LookName(string id)
        {
            BeautifyLook l = _looks.FirstOrDefault(x => x.Id == id);
            if (l != null && l.Name.Length > 0) return l.Name;
            return BeautifyLook.IsBuiltIn(id) ? _i.T("beautify.style." + id) : id;
        }

        private JsonObject LooksToJson()
        {
            JsonObject o = new JsonObject();
            foreach (BeautifyLook l in _looks) if (!BeautifyLook.IsBuiltIn(l.Id) || l.Touched) o[l.Id] = l.ToJson();
            return o;
        }

        private BeautifyLook EditableLook(string id)
        {
            BeautifyLook l = _looks.FirstOrDefault(x => x.Id == id);
            if (l != null) return l;
            if (_editBuffer == null || _editBuffer.Id != id) _editBuffer = BeautifyLook.Default(id);
            return _editBuffer;
        }

        private void TouchLook(BeautifyLook l)
        {
            if (!_looks.Contains(l)) _looks.Add(l);
            l.Touched = true;
            if (_lookChanged != null) _lookChanged();
        }

        // ---- what a base looks like when nothing is changed (shown in the fields, stored only when changed) ----
        private static double DefFrame(string t) { switch (t) { case "polaroid": return 18; case "glass": return 12; case "neon": return 3; case "sticker": return 10; case "bezel": return 16; default: return 0; } }
        private static double DefBar(string t) { switch (t) { case "window": return 30; case "windows": return 34; case "browser": return 44; default: return 0; } }
        private static string DefFrameColor(string t) { switch (t) { case "polaroid": return "#FCFCFA"; case "sticker": return "#FFFFFF"; case "bezel": return "#16181C"; default: return ""; } }
        private static string DefBarColor(string t) { switch (t) { case "window": return "#E9ECF1"; case "windows": return "#F3F3F3"; case "browser": return "#DEE2E9"; case "cinema": return "#000000"; default: return ""; } }
        private static string DefText(string t) { switch (t) { case "browser": return "example.com"; case "windows": return "Untitled"; default: return ""; } }
        private static double DefAngle(string t) { return t == "polaroid" ? -2.5 : 0; }
        private static string Fmt(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
        private static bool Dbl(string s, out double v) { return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v); }

        private void CloseLookEditor() { _editingLook = null; _editBuffer = null; if (_lookEditorHost != null) _lookEditorHost.Children.Clear(); }

        private void NewRandomLook()
        {
            int n = 1; string id;
            do { id = "my" + n++; } while (BeautifyStyles.IsKnown(id, _looks));
            BeautifyLook l = BeautifyLook.Random(_lookRng, id, _randomOpts);
            l.Name = _i.T("settings.look_my_name", n - 1);
            _looks.Add(l);
            _lookStyle.V = id;
            if (_rebuildTiles != null) _rebuildTiles();
            OpenLookEditor(id);
        }

        /// <summary>The editor of a look: fields by base, then Random / Reset / Delete / Done.</summary>
        private void OpenLookEditor(string id)
        {
            if (_lookEditorHost == null) return;
            _editingLook = id;
            _lookEditorHost.Children.Clear();
            BeautifyLook l = EditableLook(id);
            string type = l.Type;
            bool builtIn = BeautifyLook.IsBuiltIn(id);

            StackPanel p = new StackPanel();
            p.Children.Add(new TextBlock { Text = _i.T("settings.look_editing", LookName(id)), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Br(_t.Text), Margin = new Thickness(0, 0, 0, 10) });

            // the name
            TextBox nameBox = Input(l.Name.Length > 0 ? l.Name : (builtIn ? _i.T("beautify.style." + id) : id), 260);
            nameBox.TextChanged += delegate { string v = nameBox.Text.Trim(); l.Name = (builtIn && v == _i.T("beautify.style." + id)) ? "" : v; TouchLook(l); };
            nameBox.LostFocus += delegate { if (_rebuildTiles != null) _rebuildTiles(); };
            p.Children.Add(Row(_i.T("settings.look_name"), nameBox));

            // the base
            Val<string> typeVal = new Val<string> { V = type };
            p.Children.Add(Row(_i.T("settings.look_base"), Segment(BeautifyLook.Types.ToList(), BeautifyLook.Types.Select(x => _i.T("beautify.style." + x)).ToList(), typeVal, false,
                delegate { l.Type = typeVal.V; TouchLook(l); OpenLookEditor(id); })));

            // a number field with a slider
            Func<double, double, double, Action<double>, FrameworkElement> num = delegate (double value, double min, double max, Action<double> set)
            {
                TextBox tb = Input(Fmt(value), 60);
                FrameworkElement ctl = SliderBox(tb, (int)min, (int)max);
                tb.TextChanged += delegate { double d; if (Dbl(tb.Text, out d)) { set(d); TouchLook(l); } };
                return ctl;
            };
            Func<string, string, Action<string>, FrameworkElement> color = delegate (string value, string tip, Action<string> set)
            {
                TextBox cb;
                FrameworkElement f = ColorField(value, 110, tip, out cb);
                cb.TextChanged += delegate { string t = cb.Text.Trim(); WpfColor c; if (t.Length == 0) { set(""); TouchLook(l); } else if (TryColor(t, out c)) { set(t); TouchLook(l); } };
                return f;
            };

            p.Children.Add(Row(_i.T("settings.look_angle"), num(double.IsNaN(l.Angle) ? DefAngle(type) : l.Angle, -15, 15, v => l.Angle = v)));
            bool hasFrame = type == "polaroid" || type == "glass" || type == "neon" || type == "sticker" || type == "bezel";
            bool hasBar = type == "window" || type == "windows" || type == "browser";
            if (hasFrame) p.Children.Add(Row(_i.T("settings.look_frame"), num(l.Frame >= 0 ? l.Frame : DefFrame(type), 0, 40, v => l.Frame = v)));
            if (type == "polaroid" || type == "sticker" || type == "bezel")
                p.Children.Add(Row(_i.T("settings.look_frame_color"), color(l.FrameColor.Length > 0 ? l.FrameColor : DefFrameColor(type), "settings.theme_color.tip", v => l.FrameColor = v)));
            if (type == "polaroid") p.Children.Add(Row(_i.T("settings.look_bottom"), num(l.Bottom >= 0 ? l.Bottom : 64, 20, 120, v => l.Bottom = v)));
            if (hasBar)
            {
                p.Children.Add(Row(_i.T("settings.look_bar"), num(l.Bar >= 0 ? l.Bar : DefBar(type), 16, 70, v => l.Bar = v)));
            }
            if (hasBar || type == "cinema") p.Children.Add(Row(_i.T("settings.look_bar_color"), color(l.BarColor.Length > 0 ? l.BarColor : DefBarColor(type), "settings.theme_color.tip", v => l.BarColor = v)));
            if (type == "window" || type == "browser")
            {
                CheckBox dots = Check(_i.T("settings.look_dots"), l.Dots);
                dots.Checked += delegate { l.Dots = true; TouchLook(l); };
                dots.Unchecked += delegate { l.Dots = false; TouchLook(l); };
                p.Children.Add(dots);
            }
            if (type == "window" || type == "windows" || type == "browser" || type == "polaroid")
            {
                TextBox tx = Input(l.Text.Length > 0 ? l.Text : DefText(type), 320);
                tx.TextChanged += delegate { l.Text = tx.Text; TouchLook(l); };
                p.Children.Add(Row(_i.T("settings.look_text_" + type), tx));
            }
            if (type == "glass") p.Children.Add(Row(_i.T("settings.look_alpha"), num(l.Alpha, 0, 255, v => l.Alpha = (int)v)));
            if (type == "neon") p.Children.Add(Row(_i.T("settings.look_glow"), color(l.Glow, "settings.theme_color.tip", v => l.Glow = v)));
            if (type == "stack") p.Children.Add(Row(_i.T("settings.look_layers"), num(l.Layers, 1, 5, v => l.Layers = (int)v)));
            if (type == "cinema") p.Children.Add(Row(_i.T("settings.look_bars"), num(l.BarsPct, 2, 30, v => l.BarsPct = (int)v)));
            if (type != "polaroid") p.Children.Add(Row(_i.T("settings.look_radius"), num(l.Radius, -1, 60, v => l.Radius = (int)v)));

            Val<string> sh = new Val<string> { V = l.Shadow.ToString() };
            p.Children.Add(Row(_i.T("settings.look_shadow"), Segment(new List<string> { "-1", "1", "0" },
                new List<string> { _i.T("settings.look_shadow_auto"), _i.T("settings.look_shadow_on"), _i.T("settings.look_shadow_off") }, sh, false, delegate { l.Shadow = int.Parse(sh.V); TouchLook(l); })));

            // an own background: two colors (empty - the palette of the page)
            StackPanel bgRow = new StackPanel { Orientation = Orientation.Horizontal };
            bgRow.Children.Add(color(l.Bg1, "settings.theme_color.tip", v => l.Bg1 = v));
            bgRow.Children.Add(new TextBlock { Width = 14 });
            bgRow.Children.Add(color(l.Bg2, "settings.theme_color.tip", v => l.Bg2 = v));
            p.Children.Add(Row(_i.T("settings.look_bg"), bgRow));

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(Btn(_i.T("settings.look_random"), true, delegate { RandomizeLook(id); }));
            if (builtIn && _looks.Any(x => x.Id == id)) buttons.Children.Add(Btn(_i.T("settings.look_reset"), false, delegate { _looks.RemoveAll(x => x.Id == id); _editBuffer = null; if (_rebuildTiles != null) _rebuildTiles(); OpenLookEditor(id); }));
            if (!builtIn) buttons.Children.Add(Btn(_i.T("settings.look_delete"), false, delegate
            {
                _looks.RemoveAll(x => x.Id == id);
                if (_lookStyle.V == id) _lookStyle.V = "classic";
                CloseLookEditor();
                if (_rebuildTiles != null) _rebuildTiles();
            }));
            buttons.Children.Add(Btn(_i.T("settings.look_done"), false, delegate { CloseLookEditor(); }));
            p.Children.Add(buttons);

            // the picture of this look stands right beside the fields and the buttons (bottom right of the card), so "Generate again" shows its result at once
            _editPreviewImg = new Image { Stretch = Stretch.Uniform, MaxWidth = 300, MaxHeight = 230, HorizontalAlignment = HorizontalAlignment.Left, Source = _beautyPreviewImg == null ? null : _beautyPreviewImg.Source };
            RenderOptions.SetBitmapScalingMode(_editPreviewImg, BitmapScalingMode.HighQuality);
            Border previewBox = new Border { Child = _editPreviewImg, Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
            Grid cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(p, 0); cardGrid.Children.Add(p);
            Grid.SetColumn(previewBox, 1); cardGrid.Children.Add(previewBox);
            _lookEditorHost.Children.Add(new Border
            {
                Child = cardGrid, Background = Br(_t.Surface), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Left
            });
            if (_lookStyle != null && _lookStyle.V != id) { _lookStyle.V = id; if (_paintTilesAct != null) _paintTilesAct(); if (_lookChanged != null) _lookChanged(); }
            Border shown = _lookEditorHost.Children.Count > 0 ? _lookEditorHost.Children[0] as Border : null;
            if (shown != null) Dispatcher.BeginInvoke(new Action(delegate { shown.BringIntoView(); }), System.Windows.Threading.DispatcherPriority.Background);   // scroll to it
        }

        /// <summary>Replaces the look with a freshly generated one (a new base and new parameters). The id stays, a built-in look can be reset later.</summary>
        private void RandomizeLook(string id)
        {
            BeautifyLook old = _looks.FirstOrDefault(x => x.Id == id);
            BeautifyLook fresh = BeautifyLook.Random(_lookRng, id, _randomOpts);
            if (old != null && old.Name.Length > 0 && !BeautifyLook.IsBuiltIn(id)) fresh.Name = old.Name;        // an own look keeps its name
            else fresh.Name = _i.T("beautify.look_auto_name", _i.T("beautify.style." + fresh.Type), _lookRng.Next(2, 99));
            _looks.RemoveAll(x => x.Id == id);
            _looks.Add(fresh);
            _editBuffer = null;
            _lookStyle.V = id;
            if (_rebuildTiles != null) _rebuildTiles();
            OpenLookEditor(id);
        }
    }
}
