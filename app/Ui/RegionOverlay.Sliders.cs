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
        // ---- size and opacity sliders, typed size, quick percent sizes ----
        private void BuildSliderRow()
        {
            // compact: it sits at the right end of the first row of the panel; the names are in the hints (no text labels)
            _sliderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            _sizeSlider = new Slider { Minimum = _settings.SizeMin, Maximum = _settings.SizeMax, Width = 96, SmallChange = 1, LargeChange = 4, Focusable = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            _sizeSlider.ToolTip = _i18n.T("tip.size_slider");
            _sizeSlider.ValueChanged += delegate (object s, RoutedPropertyChangedEventArgs<double> e) { if (!_updatingUi) ApplySizeValue(Math.Round(e.NewValue), false); };
            _sizeSlider.PreviewMouseLeftButtonUp += delegate { CommitPendingSize(); };
            _sliderRow.Children.Add(_sizeSlider);

            // the number: click it and type any value (even above the slider): it works for this object and for new ones until the region is closed
            _sizeValue = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = Brush(_theme.Text), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MinWidth = 28, Cursor = Cursors.IBeam };
            _sizeValue.ToolTip = _i18n.T("tip.size_type");
            _sizeValue.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; BeginSizeValueEdit(); };
            _sizeValueBox = new TextBox { Width = 56, FontSize = 13, Padding = new Thickness(2), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            _sizeValueBox.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { EndSizeValueEdit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndSizeValueEdit(false); e.Handled = true; }
            };
            _sizeValueBox.LostKeyboardFocus += delegate { EndSizeValueEdit(false); };
            StackPanel sv = new StackPanel { Orientation = Orientation.Horizontal };
            sv.Children.Add(_sizeValue); sv.Children.Add(_sizeValueBox);
            _sizeValueHost = new Border { Child = sv, Margin = new Thickness(0, 0, 8, 0) };
            _sliderRow.Children.Add(_sizeValueHost);

            _opacityValueHost = new Border { Margin = new Thickness(0) };
            StackPanel op = new StackPanel { Orientation = Orientation.Horizontal };
            _opacitySlider = new Slider { Minimum = _settings.OpacityMin, Maximum = 100, Width = 70, SmallChange = 1, LargeChange = 10, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
            _opacitySlider.ToolTip = _i18n.T("tip.opacity_slider");
            _opacitySlider.ValueChanged += delegate (object s, RoutedPropertyChangedEventArgs<double> e) { if (!_updatingUi) ApplyOpacityValue((int)Math.Round(e.NewValue)); };
            _opacitySlider.PreviewMouseLeftButtonUp += delegate { CommitPendingSize(); };
            op.Children.Add(_opacitySlider);
            _opacityValue = new TextBlock { FontSize = 13, Foreground = Brush(_theme.Text), VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Center, ToolTip = _i18n.T("tip.opacity_slider") };
            op.Children.Add(_opacityValue);
            _opacityValueHost.Child = op;
            _sliderRow.Children.Add(_opacityValueHost);
        }

        private void BeginSizeValueEdit()
        {
            if (_typingSize) return;
            _typingSize = true;
            _sizeValue.Visibility = Visibility.Collapsed;
            _sizeValueBox.Text = ((int)Math.Round(CurrentSize())).ToString();
            _sizeValueBox.Visibility = Visibility.Visible;
            _sizeValueBox.Focus(); Keyboard.Focus(_sizeValueBox); _sizeValueBox.SelectAll();
        }

        private void EndSizeValueEdit(bool apply)
        {
            if (!_typingSize) return;
            _typingSize = false;
            double v;
            bool ok = apply && double.TryParse(_sizeValueBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 1;
            v = 0;
            if (ok) double.TryParse(_sizeValueBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            _sizeValueBox.Visibility = Visibility.Collapsed;
            _sizeValue.Visibility = Visibility.Visible;
            if (ok) ApplySizeValue(Math.Min(v, 5000), true);
            Focus();
        }

        private void CommitPendingSize()
        {
            if (_sizePending || _opacityPending) { _sizePending = false; _opacityPending = false; Commit(); }
        }

        /// <summary>A new size. Inside the range of the slider it goes into the preset (remembered); a typed value beyond the range
        /// applies only to the selected object and to new objects of this tool until the region is closed.</summary>
        private void ApplySizeValue(double v, bool commit)
        {
            if (_tool == EditorTool.Eraser) { _eraserSize = Math.Max(2, v); RefreshPanels(); return; }
            EditorTool tool = StyleTool();
            ToolStyle s = _styles.Current(tool);
            if (v >= _settings.SizeMin && v <= _settings.SizeMax) { s.Size = v; _sessionSize.Remove(tool); _dirtyStyles = true; }
            else _sessionSize[tool] = v;
            if (_editing != null)
            {
                _editing.FontSize = v;
                if (_editBox != null) _editBox.FontSize = v;
            }
            else if (_selected != null)
            {
                foreach (Annotation sa in SelectedAll()) SetSize(sa, v);
                RebuildAnnotations();
                _sizePending = true;
            }
            if (commit) CommitPendingSize();
            RefreshPanels();
        }

        private void ApplyOpacityValue(int v)
        {
            EditorTool tool = StyleTool();
            _styles.Current(tool).Opacity = v;
            _dirtyStyles = true;
            if (_editing != null) _editing.Opacity = v / 100.0;
            else if (_selected != null)
            {
                foreach (Annotation sa in SelectedAll()) sa.Opacity = v / 100.0;
                RebuildAnnotations();
                _opacityPending = true;
            }
            RefreshPanels();
        }

        private static double[] ParsePcts(string text, double[] fallback)
        {
            List<double> r = new List<double>();
            foreach (string p in (text ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double d;
                if (double.TryParse(p.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) && d > 0) r.Add(Math.Min(200, d));
            }
            return r.Count >= 3 ? new double[] { r[0], r[1], r[2] } : fallback;
        }

        private double[] PctsFor(EditorTool tool)
        {
            switch (tool)
            {
                case EditorTool.Marker: return ParsePcts(_settings.PctMarker, new double[] { 100, 50, 25 });
                case EditorTool.Line: return ParsePcts(_settings.PctLine, new double[] { 100, 50, 25 });
                case EditorTool.Eraser: return ParsePcts(_settings.PctEraser, new double[] { 100, 50, 25 });
                default: return ParsePcts(_settings.PctBrush, new double[] { 100, 50, 30 });
            }
        }

        /// <summary>The big / medium / small button: the thickness becomes that percent of the region height (for this region only).</summary>
        private void ApplyPercentSize(int which)
        {
            EditorTool tool = _tool == EditorTool.Eraser ? EditorTool.Eraser : StyleTool();
            double[] p = PctsFor(tool);
            double v = Math.Max(1, p[Math.Max(0, Math.Min(2, which))] / 100.0 * Math.Max(1, _sel.Height));
            if (tool == EditorTool.Eraser) _eraserSize = v;
            else
            {
                _sessionSize[tool] = v;
                if (_selected != null && ToolOf(_selected) == tool) { SetSize(_selected, v); Commit(); RebuildAnnotations(); }
            }
            RefreshPanels();
        }

        private void UpdateSliders(EditorTool tool, ToolStyle st, bool pctOn)
        {
            if (_sizeSlider == null) return;
            bool eraser = _tool == EditorTool.Eraser;
            _sliderRow.Visibility = _tool == EditorTool.Picker ? Visibility.Collapsed : Visibility.Visible;
            _sizeSlider.Visibility = _sizeValueHost.Visibility = tool == EditorTool.Spotlight ? Visibility.Collapsed : Visibility.Visible;   // the spotlight has no size
            _updatingUi = true;
            double cur = CurrentSize();
            _sizeSlider.Minimum = _settings.SizeMin; _sizeSlider.Maximum = _settings.SizeMax;
            _sizeSlider.Value = Math.Max(_settings.SizeMin, Math.Min(_settings.SizeMax, cur));
            if (!_typingSize) _sizeValue.Text = ((int)Math.Round(cur)).ToString();
            double opv = _selected != null ? _selected.Opacity * 100 : st.Opacity;
            _opacitySlider.Minimum = _settings.OpacityMin;
            _opacitySlider.Value = Math.Max(_settings.OpacityMin, Math.Min(100, opv));
            _opacityValue.Text = ((int)Math.Round(opv)) + "%";
            _opacityValueHost.Visibility = (eraser || tool == EditorTool.Blur) ? Visibility.Collapsed : Visibility.Visible;
            _updatingUi = false;
            double[] pcts = PctsFor(eraser ? EditorTool.Eraser : tool);
            for (int i = 0; i < _pctBtns.Count; i++)
            {
                _pctBtns[i].Visibility = pctOn ? Visibility.Visible : Visibility.Collapsed;
                _pctBtns[i].ToolTip = _i18n.T("tip.pct_size") + " " + pcts[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }
        }

        /// <summary>The mouse wheel: one step of the size (a click on the slider arrows does the same).</summary>
        private void StepSize(int dir)
        {
            EditorTool tool = StyleTool();
            double min, max, step;
            SizeRange(tool, out min, out max, out step);
            ApplySizeValue(Math.Max(1, CurrentSize() + step * dir), true);
        }
        // ================= eraser, pipette, any color, window under the cursor =================
        private void StartErase(Point p)
        {
            _mode = DragMode.Erase;
            _erased = 0;
            _canvas.CaptureMouse();
            EraseAt(p);
        }

        /// <summary>Every object that the round eraser (diameter = the eraser size) touches at this point.</summary>
        private List<Annotation> HitDisc(Point p, double radius)
        {
            List<Point> pts = new List<Point> { p };
            foreach (double f in new double[] { 0.25, 0.5, 0.75, 1.0 })
                for (int k = 0; k < 12; k++)
                {
                    double ang = k * Math.PI / 6;
                    pts.Add(new Point(p.X + Math.Cos(ang) * radius * f, p.Y + Math.Sin(ang) * radius * f));
                }
            List<Annotation> res = new List<Annotation>();
            foreach (Annotation a in _items)
            {
                if (a == _editing) continue;
                foreach (Point q in pts) if (a.Hit(q)) { res.Add(a); break; }
            }
            return res;
        }

        private void EraseAt(Point p)
        {
            List<Annotation> hit = HitDisc(p, _eraserSize / 2);
            if (hit.Count == 0) return;
            foreach (Annotation a in hit)
            {
                int id = a.Id;
                _items.RemoveAll(x => x.Id == id);
                if (_selected != null && _selected.Id == id) _selected = null;
                _erased++;
            }
            RebuildAnnotations();
        }

    }
}
