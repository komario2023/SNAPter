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
        // ---- the bottom panel layout editor: drag the groups between lines (Settings - Panels for a big region) ----
        private const string DragFormat = "snapter.group";

        private FrameworkElement BuildLayoutEditor(TextBox text)
        {
            StackPanel root = new StackPanel();
            List<List<string>> lines; List<string> pool;
            FitLayoutModel.Parse(text.Text, out lines, out pool);
            bool syncing = false;
            Action render = null;
            Action<string, int, int> drop = delegate (string id, int line, int index)
            {
                if (!FitLayoutModel.Move(lines, pool, id, line, index)) return;
                syncing = true; text.Text = FitLayoutModel.ToText(lines); syncing = false;
                render();
            };
            render = delegate
            {
                root.Children.Clear();
                for (int li = 0; li < lines.Count; li++) root.Children.Add(LineBox(lines[li], li, drop));
                root.Children.Add(ZoneBox(_i.T("settings.fit_new_line"), delegate (string id) { drop(id, lines.Count, 0); }));
                Border poolBox = ZoneBox(_i.T("settings.fit_pool"), delegate (string id) { drop(id, -1, 0); });
                WrapPanel pw = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (string id in pool) pw.Children.Add(Chip(id));
                ((StackPanel)poolBox.Child).Children.Add(pw);
                root.Children.Add(poolBox);
            };
            text.TextChanged += delegate
            {
                if (syncing) return;
                FitLayoutModel.Parse(text.Text, out lines, out pool);
                render();
            };
            render();
            return root;
        }

        private Border Chip(string id)
        {
            Border chip = new Border
            {
                Background = Br(_t.SurfaceMuted), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Max(4, _t.RadiusMd - 4)), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(3),
                Cursor = Cursors.SizeAll, ToolTip = _i.T("settings.fit_chip_tip"),
                Child = new TextBlock { Text = _i.T("settings.fit_group." + id), Foreground = Br(_t.Text) }
            };
            Point down = new Point(); bool armed = false;
            chip.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { down = e.GetPosition(null); armed = true; };
            chip.MouseLeftButtonUp += delegate { armed = false; };
            chip.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!armed || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(null);
                if (Math.Abs(p.X - down.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - down.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                armed = false;
                DragDrop.DoDragDrop(chip, new DataObject(DragFormat, id), DragDropEffects.Move);
            };
            return chip;
        }

        private Border LineBox(List<string> line, int li, Action<string, int, int> drop)
        {
            WrapPanel wp = new WrapPanel();
            foreach (string id in line) wp.Children.Add(Chip(id));
            DockPanel dp = new DockPanel();
            TextBlock num = new TextBlock { Text = (li + 1).ToString(), Width = 20, Foreground = Br(_t.TextSecondary), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            DockPanel.SetDock(num, Dock.Left);
            dp.Children.Add(num); dp.Children.Add(wp);
            Border box = new Border
            {
                BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 6), Background = Brushes.Transparent, AllowDrop = true, Child = dp, MaxWidth = 600
            };
            box.DragOver += delegate (object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            box.Drop += delegate (object s, DragEventArgs e)
            {
                string id = e.Data.GetData(DragFormat) as string;
                if (id == null) return;
                drop(id, li, InsertIndex(wp, e.GetPosition(wp)));
                e.Handled = true;
            };
            return box;
        }

        /// <summary>A drop target that is not a line: "a new line" and "not shown".</summary>
        private Border ZoneBox(string label, Action<string> onDrop)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, Foreground = Br(_t.TextSecondary), TextWrapping = TextWrapping.Wrap });
            Border box = new Border
            {
                BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6), Background = Brushes.Transparent, AllowDrop = true, Child = sp, MaxWidth = 600, Opacity = 0.85
            };
            box.DragOver += delegate (object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            box.Drop += delegate (object s, DragEventArgs e)
            {
                string id = e.Data.GetData(DragFormat) as string;
                if (id != null) onDrop(id);
                e.Handled = true;
            };
            return box;
        }

        /// <summary>Where in the line a dropped group lands: before the first group whose middle is to the right of the pointer.</summary>
        private static int InsertIndex(WrapPanel wp, Point p)
        {
            for (int i = 0; i < wp.Children.Count; i++)
            {
                UIElement c = wp.Children[i];
                Rect r = c.TransformToAncestor(wp).TransformBounds(new Rect(c.RenderSize));
                if (p.Y < r.Top || (p.Y < r.Bottom && p.X < r.Left + r.Width / 2)) return i;
            }
            return wp.Children.Count;
        }
    }

    /// <summary>The lines of the bottom panel as the settings editor sees them: lines of groups and the pool of groups that are not shown.</summary>
    internal static class FitLayoutModel
    {
        public static readonly string[] All = { "presets", "colors", "recent", "shape", "sliders", "extra", "copy", "save", "actions2", "close" };

        public static void Parse(string text, out List<List<string>> lines, out List<string> pool)
        {
            List<List<string>> l = RegionOverlay.ParsePropsLayout(text, All);
            lines = l;
            pool = All.Where(t => !l.Any(x => x.Contains(t))).ToList();
        }

        public static string ToText(List<List<string>> lines)
        {
            string t = string.Join("|", lines.Where(l => l.Count > 0).Select(l => string.Join(",", l)));
            return t.Length == 0 ? "-" : t;           // "-" = no groups at all (a blank text would mean the default layout)
        }

        /// <summary>Takes a group out of where it is and puts it on a line at an index. line = lines.Count: a new line at the end; line = -1: the pool.
        /// Every group can go to the pool (the keys Ctrl+C, Ctrl+S and Esc do the same). Returns false when nothing changed.</summary>
        public static bool Move(List<List<string>> lines, List<string> pool, string id, int line, int index)
        {
            int oldLine = -1, oldIdx = -1;
            for (int i = 0; i < lines.Count; i++) { int k = lines[i].IndexOf(id); if (k >= 0) { oldLine = i; oldIdx = k; break; } }
            if (oldLine >= 0) lines[oldLine].RemoveAt(oldIdx);
            pool.Remove(id);
            if (line < 0) pool.Add(id);
            else
            {
                if (oldLine == line && oldIdx < index) index--;
                if (line >= lines.Count) { lines.Add(new List<string>()); index = 0; }
                List<string> target = lines[line];
                target.Insert(Math.Max(0, Math.Min(index, target.Count)), id);
            }
            lines.RemoveAll(x => x.Count == 0);
            return true;
        }
    }
}
