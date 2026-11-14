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
    internal sealed partial class RegionOverlay
    {
        // ---- text ----
        private void TextClick(Point p)
        {
            TextAnnotation existing = HitAnnotation(p) as TextAnnotation;
            if (existing != null)
            {
                // click opens the text for editing, drag moves it (decided when the button is released)
                Select(existing);
                _pendingEdit = existing;
                BeginAnnoDrag(DragMode.AnnoMove, p);
                return;
            }

            TextAnnotation t = new TextAnnotation();
            ApplyStyle(t, _styles.Current(EditorTool.Text));
            t.Pos = ClampToSel(p);
            _items.Add(t);
            BeginEdit(t, true);
        }

        // ---- styles and presets ----
        private static EditorTool ToolOf(Annotation a)
        {
            ArrowAnnotation ar = a as ArrowAnnotation;
            if (ar != null) return ar.Head ? EditorTool.Arrow : EditorTool.Line;
            ShapeAnnotation sh = a as ShapeAnnotation;
            if (sh != null) return sh.Ellipse ? EditorTool.Ellipse : EditorTool.Rect;
            if (a is BlurAnnotation) return EditorTool.Blur;
            if (a is FillAnnotation) return EditorTool.Fill;
            if (a is SpotlightAnnotation) return EditorTool.Spotlight;
            if (a is MagnifierAnnotation) return EditorTool.Magnifier;
            BrushAnnotation br = a as BrushAnnotation;
            if (br != null) return br.Marker ? EditorTool.Marker : EditorTool.Brush;
            if (a is TextAnnotation) return EditorTool.Text;
            if (a is StepAnnotation) return EditorTool.Steps;
            return EditorTool.Arrow;
        }

        /// <summary>The tool whose style the properties panel shows and edits.</summary>
        private EditorTool StyleTool()
        {
            if (_editing != null) return EditorTool.Text;
            if (_selected != null) return ToolOf(_selected);
            return StyleStore.HasStyle(_tool) ? _tool : _lastTool;
        }

        private static void SizeRange(EditorTool t, out double min, out double max, out double step)
        {
            switch (t)
            {
                case EditorTool.Text: min = 8; max = 96; step = 2; break;
                case EditorTool.Steps: min = 16; max = 64; step = 4; break;
                case EditorTool.Blur: min = 4; max = 40; step = 2; break;
                case EditorTool.Fill: min = 1; max = 48; step = 2; break;
                case EditorTool.Magnifier: min = 2; max = 8; step = 1; break;
                case EditorTool.Marker: min = 4; max = 48; step = 2; break;
                default: min = 1; max = 24; step = 1; break;
            }
        }

        private static double GetSize(Annotation a)
        {
            ArrowAnnotation ar = a as ArrowAnnotation; if (ar != null) return ar.Thickness;
            ShapeAnnotation sh = a as ShapeAnnotation; if (sh != null) return sh.Thickness;
            BlurAnnotation bl = a as BlurAnnotation; if (bl != null) return bl.Block;
            FillAnnotation fl = a as FillAnnotation; if (fl != null) return fl.Tolerance;
            MagnifierAnnotation mg = a as MagnifierAnnotation; if (mg != null) return mg.Zoom;
            BrushAnnotation br = a as BrushAnnotation; if (br != null) return br.Thickness;
            TextAnnotation tx = a as TextAnnotation; if (tx != null) return tx.FontSize;
            StepAnnotation st = a as StepAnnotation; if (st != null) return st.Diameter;
            return 4;
        }

        private static void SetSize(Annotation a, double v)
        {
            ArrowAnnotation ar = a as ArrowAnnotation; if (ar != null) { ar.Thickness = v; return; }
            ShapeAnnotation sh = a as ShapeAnnotation; if (sh != null) { sh.Thickness = v; return; }
            BlurAnnotation bl = a as BlurAnnotation; if (bl != null) { bl.Block = v; return; }
            FillAnnotation fl = a as FillAnnotation; if (fl != null) { fl.Tolerance = (int)Math.Round(v); return; }
            MagnifierAnnotation mg = a as MagnifierAnnotation; if (mg != null) { mg.Zoom = Math.Max(1.2, v); return; }
            BrushAnnotation br = a as BrushAnnotation; if (br != null) { br.Thickness = v; return; }
            TextAnnotation tx = a as TextAnnotation; if (tx != null) { tx.FontSize = v; return; }
            StepAnnotation st = a as StepAnnotation; if (st != null) { st.Diameter = v; }
        }

        /// <summary>Gives an object the values of a preset.</summary>
        private void ApplyStyle(Annotation a, ToolStyle s)
        {
            double sessionSize;
            a.Color = s.Color;
            a.Opacity = Math.Max(0.1, Math.Min(1.0, s.Opacity / 100.0));
            SetSize(a, s.Size);
            if (_sessionSize.TryGetValue(ToolOf(a), out sessionSize)) SetSize(a, sessionSize);   // a size typed or taken from a quick button lasts until the region is closed
            ShapeAnnotation sh = a as ShapeAnnotation;
            if (sh != null) { sh.Fill = s.Fill; sh.Rounded = !sh.Ellipse && s.Rounded; }
            TextAnnotation tx = a as TextAnnotation;
            if (tx != null) { tx.Style = s.TextStyle; SetFontFields(tx, s); }
            ArrowAnnotation arr = a as ArrowAnnotation;
            if (arr != null) { arr.HeadKind = s.HeadStyle; arr.HeadScale = s.HeadScale; arr.HeadHollow = s.HeadHollow; }
        }

        private void DuplicateSelected()
        {
            if (_selected == null || _editBox != null) return;
            if (_group.Count > 0) { DuplicateGroup(); Commit(); RebuildAnnotations(); return; }
            Annotation d = _selected.Clone();
            d.Id = Annotation.NewId();
            d.Translate(14, 14);
            _items.Add(d);
            _selected = d;
            Commit();
            RebuildAnnotations();
        }

        /// <summary>] brings the selected object to the front, [ sends it to the back.</summary>
        private void ReorderSelected(bool toFront)
        {
            if (_selected == null || _editBox != null) return;
            if (_group.Count > 0) { ReorderGroup(toFront); Commit(); RebuildAnnotations(); return; }
            int id = _selected.Id;
            Annotation a = _items.FirstOrDefault(x => x.Id == id);
            if (a == null) return;
            _items.Remove(a);
            if (toFront) _items.Add(a); else _items.Insert(0, a);
            Commit();
            RebuildAnnotations();
        }

        private void SetPreset(int i)
        {
            if (_editBox != null && _editing == null) return;
            EditorTool tool = StyleTool();
            _styles.SetActive(tool, i);
            _dirtyStyles = true;
            ToolStyle s = _styles.Current(tool);
            if (_editing != null)
            {
                ApplyStyle(_editing, s);
                if (_editBox != null)
                {
                    _editBox.Foreground = Brush(_editing.Color); _editBox.CaretBrush = Brush(_editing.Color);
                    _editBox.FontSize = _editing.FontSize;
                }
            }
            else if (_selected != null)
            {
                ApplyStyle(_selected, s);
                Commit();
                RebuildAnnotations();
            }
            RefreshPanels();
        }

        // ---- font, bold, italic, alignment of the text preset; head of the arrow preset ----
        private static void SetFontFields(TextAnnotation t, ToolStyle s)
        {
            t.Font = s.FontName; t.BoldMode = s.Bold; t.ItalicMode = s.Italic; t.AlignMode = s.Align;
        }

        /// <summary>After a font change of the text preset: the text being typed or the selected text takes it at once.</summary>
        private void TextPresetChanged()
        {
            _dirtyStyles = true;
            ToolStyle s = _styles.Current(EditorTool.Text);
            if (_editing != null)
            {
                SetFontFields(_editing, s);
                if (_editBox != null)
                {
                    _editBox.FontFamily = new FontFamily(_editing.FontFamilyName);
                    _editBox.FontWeight = _editing.Weight;
                    _editBox.FontStyle = _editing.Slant;
                    _editBox.TextAlignment = _editing.Alignment();
                }
            }
            else if (_selected is TextAnnotation)
            {
                SetFontFields((TextAnnotation)_selected, s);
                Commit();
                RebuildAnnotations();
            }
            RefreshPanels();
        }

        private void ToggleBold()
        {
            ToolStyle s = _styles.Current(EditorTool.Text);
            bool cur = s.Bold < 0 ? TextAnnotation.Bold : s.Bold == 1;
            s.Bold = cur ? 0 : 1;
            TextPresetChanged();
        }

        private void ToggleItalic()
        {
            ToolStyle s = _styles.Current(EditorTool.Text);
            bool cur = s.Italic < 0 ? TextAnnotation.Italic : s.Italic == 1;
            s.Italic = cur ? 0 : 1;
            TextPresetChanged();
        }

        private void CycleAlign()
        {
            ToolStyle s = _styles.Current(EditorTool.Text);
            string cur = string.IsNullOrEmpty(s.Align) ? TextAnnotation.Align : s.Align;
            s.Align = cur == "left" ? "center" : cur == "center" ? "right" : cur == "right" ? "justify" : "left";
            TextPresetChanged();
        }

        /// <summary>The list of fonts: the usual ones first (Verdana, Segoe UI, Arial...), then every installed font.</summary>
        private void ShowFontMenu()
        {
            ContextMenu menu = new ContextMenu { MaxHeight = 460, PlacementTarget = _fontBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            HashSet<string> installed = new HashSet<string>(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
            // your own short list (settings, text.font_list) first; every other installed font waits in the submenu "All fonts"
            List<string> mine = new List<string>();
            foreach (string part in (_settings.FontList ?? "").Split(',')) { string f = part.Trim(); if (f.Length > 0 && installed.Contains(f) && !mine.Contains(f)) mine.Add(f); }
            if (mine.Count == 0) mine.Add("Segoe UI");
            Func<string, MenuItem> make = delegate (string name)
            {
                MenuItem mi = new MenuItem { Header = name, FontFamily = new FontFamily(name) };
                mi.Click += delegate { _styles.Current(EditorTool.Text).FontName = name; TextPresetChanged(); };
                return mi;
            };
            MenuItem globalItem = new MenuItem { Header = _i18n.T("tip.font_global") };
            globalItem.Click += delegate { _styles.Current(EditorTool.Text).FontName = ""; TextPresetChanged(); };
            menu.Items.Add(globalItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            foreach (string c in mine) menu.Items.Add(make(c));
            menu.Items.Add(new System.Windows.Controls.Separator());
            MenuItem all = new MenuItem { Header = _i18n.T("settings.fonts_all") };
            foreach (string n in installed.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) if (!mine.Contains(n)) all.Items.Add(make(n));
            menu.Items.Add(all);
            OpenList(menu);
        }

        private bool _listOpen, _layoutWaits;

        /// <summary>Opens a drop-down list of the panels so that nothing disturbs it: while it is open the panels are not rebuilt (that would close it)
        /// and a text being typed is not finished when the list takes the focus; afterwards all that was postponed is done.</summary>
        private void OpenList(ContextMenu menu)
        {
            menu.Closed += delegate
            {
                _listOpen = false;
                if (_editBox != null) Dispatcher.BeginInvoke(new Action(delegate { if (_editBox != null) { _editBox.Focus(); Keyboard.Focus(_editBox); } }), DispatcherPriority.Input);
                if (_layoutWaits) { _layoutWaits = false; if (_panelsBuilt && _panelB != null && _panelB.Visibility == Visibility.Visible) LayoutPanels(); }
            };
            _listOpen = true;
            menu.IsOpen = true;
        }

        private void CycleHead()
        {
            ToolStyle s = _styles.Current(EditorTool.Arrow);
            string cur = string.IsNullOrEmpty(s.HeadStyle) ? ArrowAnnotation.HeadStyle : s.HeadStyle;
            s.HeadStyle = cur == "triangle" ? "open" : cur == "open" ? "dot" : "triangle";
            ArrowPresetChanged(s);
        }

        /// <summary>The head of the arrow: filled or an outline (a triangle or a dot with the same line thickness, empty inside).</summary>
        private void ToggleHeadHollow()
        {
            ToolStyle s = _styles.Current(EditorTool.Arrow);
            s.HeadHollow = !s.HeadHollow;
            ArrowPresetChanged(s);
        }

        private void CycleHeadSize()
        {
            ToolStyle s = _styles.Current(EditorTool.Arrow);
            double[] steps = { 0.5, 0.75, 1.0, 1.5, 2.0, 3.0 };
            int idx = 0; double best = double.MaxValue;
            for (int i = 0; i < steps.Length; i++) { double dd = Math.Abs(steps[i] - s.HeadScale); if (dd < best) { best = dd; idx = i; } }
            s.HeadScale = steps[(idx + 1) % steps.Length];
            ArrowPresetChanged(s);
        }

        private void ArrowPresetChanged(ToolStyle s)
        {
            _dirtyStyles = true;
            ArrowAnnotation ar = _selected as ArrowAnnotation;
            if (ar != null && ar.Head)
            {
                ar.HeadKind = s.HeadStyle; ar.HeadScale = s.HeadScale; ar.HeadHollow = s.HeadHollow;
                Commit();
                RebuildAnnotations();
            }
            RefreshPanels();
        }

        /// <summary>Transparency of the current tool: 100, 80, 60, 40, 20 percent in turn.</summary>
        private void CycleOpacity()
        {
            EditorTool tool = StyleTool();
            if (tool == EditorTool.Blur) return;
            ToolStyle s = _styles.Current(tool);
            int[] steps = { 100, 80, 60, 40, 20 };
            int idx = Array.IndexOf(steps, s.Opacity);
            s.Opacity = steps[(idx + 1) % steps.Length];
            _dirtyStyles = true;
            if (_editing != null) _editing.Opacity = s.Opacity / 100.0;
            else if (_selected != null) { _selected.Opacity = s.Opacity / 100.0; Commit(); RebuildAnnotations(); }
            RefreshPanels();
        }

        private void CycleFill()
        {
            EditorTool tool = StyleTool();
            if (tool != EditorTool.Rect && tool != EditorTool.Ellipse) return;
            ToolStyle s = _styles.Current(tool);
            s.Fill = (s.Fill + 1) % 3;
            _dirtyStyles = true;
            ShapeAnnotation sh = _selected as ShapeAnnotation;
            if (sh != null) { sh.Fill = s.Fill; Commit(); RebuildAnnotations(); }
            RefreshPanels();
        }

        private void ToggleRounded()
        {
            ToolStyle s = _styles.Current(EditorTool.Rect);
            s.Rounded = !s.Rounded;
            _dirtyStyles = true;
            ShapeAnnotation sh = _selected as ShapeAnnotation;
            if (sh != null && !sh.Ellipse) { sh.Rounded = s.Rounded; Commit(); RebuildAnnotations(); }
            RefreshPanels();
        }

        private void CycleTextStyle()
        {
            ToolStyle s = _styles.Current(EditorTool.Text);
            s.TextStyle = (s.TextStyle + 1) % 4;
            _dirtyStyles = true;
            if (_editing != null) _editing.Style = s.TextStyle;
            else
            {
                TextAnnotation tx = _selected as TextAnnotation;
                if (tx != null) { tx.Style = s.TextStyle; Commit(); RebuildAnnotations(); }
            }
            RefreshPanels();
        }

        private void BeginEdit(TextAnnotation t, bool isNew)
        {
            Log.Debug("begin edit new=" + isNew);
            if (_editBox != null) EndEdit(true);
            _editing = t;
            _editingNew = isNew;
            _selected = t;

            TextBox tb = new TextBox();
            tb.Text = t.Text;
            tb.FontFamily = new FontFamily(t.FontFamilyName);
            tb.FontSize = t.FontSize;
            tb.FontWeight = t.Weight;
            tb.FontStyle = t.Slant;
            tb.TextAlignment = t.Alignment();
            tb.Foreground = Brush(t.Color);
            tb.CaretBrush = Brush(t.Color);
            tb.Background = Brushes.Transparent;
            tb.BorderThickness = new Thickness(0);
            tb.Padding = new Thickness(0);
            tb.AcceptsReturn = true;
            if (_settings.TextAllCaps) tb.CharacterCasing = CharacterCasing.Upper; // text.all_caps: always CAPITAL letters
            tb.AcceptsTab = false;
            tb.MinWidth = 40;
            tb.TextWrapping = TextWrapping.NoWrap;
            tb.SelectionBrush = Brush(_theme.Accent);
            tb.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                bool ctrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (e.Key == Key.Escape || (e.Key == Key.Enter && ctrlDown))
                {
                    EndEdit(true);
                    e.Handled = true;
                }
                else if (ctrlDown && e.Key == Key.C && tb.SelectionLength == 0)
                {
                    // Ctrl+C with no text selected: the text is finished and the picture is copied (like the Copy button);
                    // with some text selected it copies that text as usual
                    e.Handled = true;
                    EndEdit(true);
                    DoCopy();
                }
                else if (ctrlDown && e.Key == Key.S)
                {
                    e.Handled = true;
                    EndEdit(true);
                    DoSave();
                }
            };
            tb.LostKeyboardFocus += delegate { if (!_endingEdit && !_listOpen) EndEdit(true); };   // a drop-down list of the panel takes the focus for a moment: the text goes on

            Border box = new Border();
            box.Child = tb;
            box.BorderBrush = Brush(_theme.Accent);
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(EditPad - 1);   // the frame is a handle: drag it to move the text
            box.Background = new SolidColorBrush(WpfColor.FromArgb(70, 255, 255, 255));
            box.ToolTip = _i18n.T("tip.drag_text");
            Canvas.SetLeft(box, t.Pos.X - EditPad);
            Canvas.SetTop(box, t.Pos.Y - EditPad);
            _canvas.Children.Add(box);
            _editBox = tb;
            _editBorder = box;
            _editStartPos = t.Pos;

            RebuildAnnotations();
            RefreshPanels();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_editBox != tb) return;
                tb.Focus();
                Keyboard.Focus(tb);
                tb.CaretIndex = tb.Text.Length;
            }), DispatcherPriority.Input);
        }

        private void EndEdit(bool commit)
        {
            if (_editBox == null || _endingEdit) return;
            Log.Debug("end edit commit=" + commit + " text='" + _editBox.Text + "'");
            _endingEdit = true;
            TextAnnotation t = _editing;
            string txt = _editBox.Text;
            bool isNew = _editingNew;
            _canvas.Children.Remove(_editBorder);
            _editBox = null; _editBorder = null; _editing = null;
            _endingEdit = false;

            if (!commit || string.IsNullOrWhiteSpace(txt))
            {
                _items.RemoveAll(x => x.Id == t.Id);
                if (_selected != null && _selected.Id == t.Id) _selected = null;
                if (!isNew && commit) Commit();
                else if (!isNew) { /* cancelled edit of an existing text: keep the old one */ _items.Add(t); _selected = t; }
            }
            else
            {
                bool changed = isNew || t.Text != txt || t.Pos != _editStartPos;
                t.Text = txt;
                if (changed) Commit();
            }
            RebuildAnnotations();
            RefreshPanels();
            Focus();
        }

        // ================= colors and size =================
        private WpfColor ActiveColor()
        {
            if (_editing != null) return _editing.Color;
            if (_selected != null) return _selected.Color;
            return _styles.Current(StyleTool()).Color;
        }

        private void SetColor(WpfColor c)
        {
            // the color belongs to the preset of this kind of object: next time the tool draws with it
            _styles.Current(StyleTool()).Color = c;
            if (_settings.SharedColor) foreach (EditorTool st2 in StyleStore.Drawing) if (st2 != EditorTool.Blur) _styles.Current(st2).Color = c;
            _dirtyStyles = true;
            if (_editing != null)
            {
                _editing.Color = c;
                if (_editBox != null) { _editBox.Foreground = Brush(c); _editBox.CaretBrush = Brush(c); }
            }
            else if (_selected != null)
            {
                foreach (Annotation sa in SelectedAll()) sa.Color = c;     // the whole multi-selection
                Commit();
                RebuildAnnotations();
            }
            RefreshPanels();
        }

        private double CurrentSize()
        {
            if (_tool == EditorTool.Eraser) return _eraserSize;
            if (_editing != null) return _editing.FontSize;
            if (_selected != null) return GetSize(_selected);
            EditorTool tool = StyleTool();
            double sv;
            if (_sessionSize.TryGetValue(tool, out sv)) return sv;
            return _styles.Current(tool).Size;
        }

    }
}
