using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>More pictures for the preview dock: the looks of text, steps and arrows (live), the quick sizes and snapping, and how the panels
    /// give way when the region is big. All of them are drawn by the program from the values on the page.</summary>
    internal sealed partial class SettingsWindow
    {
        private static string RomanOf(int n)
        {
            if (n <= 0 || n > 3999) return n.ToString();
            string[] t = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string r = "";
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { r += t[i]; n -= v[i]; }
            return r;
        }

        private static string StepLabel(string style, int n)
        {
            if (style == "upper") return ((char)('A' + (n - 1) % 26)).ToString();
            if (style == "lower") return ((char)('a' + (n - 1) % 26)).ToString();
            if (style == "roman") return RomanOf(n);
            return n.ToString();
        }

        // ----------------------------------------------------------------- looks of text, steps, arrows
        private void RegisterLooksDock(TextBox font, CheckBox caps, CheckBox bold, CheckBox italic, Val<string> align, TextBox plateColor, TextBox plateOp,
            Val<string> stepStyle, TextBox stepStart, Val<string> head)
        {
            TextBlock sample = new TextBlock { Text = _i.T("settings.looks_sample"), TextWrapping = TextWrapping.Wrap, Width = 270, FontSize = 15, Foreground = Br(WpfColor.FromRgb(20, 20, 20)) };
            Border plate = new Border { Child = sample, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
            Border backdrop = new Border { Child = plate, Background = Br(WpfColor.FromRgb(236, 239, 246)), Padding = new Thickness(8), CornerRadius = new CornerRadius(6) };
            StackPanel steps = new StackPanel { Orientation = Orientation.Horizontal };
            Canvas arrow = new Canvas { Width = 270, Height = 44 };
            StackPanel sp = new StackPanel();
            sp.Children.Add(DockCaption(_i.T("settings.looks_text")));
            sp.Children.Add(backdrop);
            sp.Children.Add(DockCaption(_i.T("settings.looks_steps")));
            sp.Children.Add(steps);
            sp.Children.Add(DockCaption(_i.T("settings.looks_arrow")));
            sp.Children.Add(arrow);

            Action refresh = delegate
            {
                string f = font.Text.Trim();
                try { sample.FontFamily = new FontFamily(f.Length == 0 ? "Segoe UI" : f); } catch { }
                sample.FontWeight = bold.IsChecked == true ? FontWeights.SemiBold : FontWeights.Normal;
                sample.FontStyle = italic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
                string txt = _i.T("settings.looks_sample");
                sample.Text = caps.IsChecked == true ? txt.ToUpperInvariant() : txt;
                sample.TextAlignment = align.V == "center" ? TextAlignment.Center : align.V == "right" ? TextAlignment.Right : align.V == "justify" ? TextAlignment.Justify : TextAlignment.Left;
                WpfColor pc; string ps = plateColor.Text.Trim();
                if (ps.Length == 0 || ps.Equals("auto", StringComparison.OrdinalIgnoreCase) || !TryColor(ps, out pc)) pc = WpfColor.FromRgb(255, 226, 120);
                int op; if (!int.TryParse(plateOp.Text.Trim(), out op)) op = 100;
                plate.Background = Br(WpfColor.FromArgb((byte)(255 * Math.Max(10, Math.Min(100, op)) / 100), pc.R, pc.G, pc.B));

                steps.Children.Clear();
                int start; if (!int.TryParse(stepStart.Text.Trim(), out start)) start = 1;
                for (int k = 0; k < 3; k++)
                {
                    steps.Children.Add(new Border
                    {
                        Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 0), Background = Br(WpfColor.FromRgb(214, 40, 40)),
                        Child = new TextBlock { Text = StepLabel(stepStyle.V, Math.Max(1, start + k)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    });
                }

                arrow.Children.Clear();
                Brush red = Br(WpfColor.FromRgb(214, 40, 40));
                arrow.Children.Add(new Line { X1 = 10, Y1 = 22, X2 = 170, Y2 = 22, Stroke = red, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round });
                if (head.V == "dot") arrow.Children.Add(Placed(new Ellipse { Width = 14, Height = 14, Fill = red }, 163, 15));
                else if (head.V == "open")
                {
                    arrow.Children.Add(new Polyline { Points = new PointCollection { new Point(158, 10), new Point(174, 22), new Point(158, 34) }, Stroke = red, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round });
                }
                else arrow.Children.Add(new Polygon { Points = new PointCollection { new Point(156, 9), new Point(180, 22), new Point(156, 35) }, Fill = red });
            };
            RegisterDock(_i.T("settings.tab.looks"), sp, refresh);
        }

        private static UIElement Placed(FrameworkElement el, double x, double y) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }

        // ----------------------------------------------------------------- quick sizes and snapping
        private void RegisterSizesDock(TextBox pBrush, TextBox pMarker, TextBox pLine, TextBox pEraser)
        {
            StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, Height = 52 };
            Canvas snap = new Canvas { Width = 270, Height = 86 };
            StackPanel sp = new StackPanel();
            sp.Children.Add(DockCaption(_i.T("settings.sizes_dots")));
            sp.Children.Add(dots);
            sp.Children.Add(DockCaption(_i.T("settings.sizes_snap")));
            sp.Children.Add(snap);
            Action refresh = delegate
            {
                dots.Children.Clear();
                string[] nums = pBrush.Text.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int k = 0; k < Math.Min(3, nums.Length); k++)
                {
                    int v; if (!int.TryParse(nums[k], out v)) v = 30;
                    double d = Math.Max(6, Math.Min(46, 8 + v * 0.38));
                    dots.Children.Add(new Ellipse { Width = d, Height = d, Fill = Br(_t.Accent), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = v + "%" });
                }
                snap.Children.Clear();
                Brush ink = Br(_t.Text), guide = Br(WpfColor.FromRgb(255, 138, 0));
                snap.Children.Add(Placed(new Rectangle { Width = 90, Height = 50, Stroke = ink, StrokeThickness = 2, RadiusX = 3, RadiusY = 3 }, 10, 6));
                snap.Children.Add(Placed(new Rectangle { Width = 70, Height = 36, Stroke = ink, StrokeThickness = 2, RadiusX = 3, RadiusY = 3 }, 140, 6));
                snap.Children.Add(new Line { X1 = 5, Y1 = 56, X2 = 215, Y2 = 56, Stroke = guide, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } });
                snap.Children.Add(Placed(new TextBlock { Text = _i.T("settings.sizes_snap_note"), Foreground = Br(_t.TextSecondary), FontSize = 12, Width = 260, TextWrapping = TextWrapping.Wrap }, 5, 60));
            };
            RegisterDock(_i.T("settings.tab.sizes"), sp, refresh);
        }

        // ----------------------------------------------------------------- panels for a big region
        private FrameworkElement FitStage(string caption, int stage)
        {
            Canvas c = new Canvas { Width = 270, Height = 74 };
            Brush region = Br(_t.SurfaceMuted), panel = Br(_t.Accent), line = Br(_t.Border);
            double rw = stage == 0 ? 120 : stage == 1 ? 140 : 160;
            c.Children.Add(Placed(new Rectangle { Width = rw, Height = 46, Fill = region, Stroke = line, StrokeThickness = 1 }, 6, 4));
            if (stage == 0)
            {
                c.Children.Add(Placed(new Rectangle { Width = 14, Height = 46, Fill = panel, RadiusX = 3, RadiusY = 3 }, 132, 4));
                c.Children.Add(Placed(new Rectangle { Width = 120, Height = 14, Fill = panel, RadiusX = 3, RadiusY = 3 }, 6, 54));
            }
            else if (stage == 1)
            {
                c.Children.Add(Placed(new Rectangle { Width = 10, Height = 46, Fill = panel, RadiusX = 3, RadiusY = 3 }, 150, 4));
                c.Children.Add(Placed(new Rectangle { Width = 90, Height = 10, Fill = panel, RadiusX = 3, RadiusY = 3 }, 6, 54));
            }
            else
            {
                c.Children.Add(Placed(new Rectangle { Width = 10, Height = 30, Fill = panel, RadiusX = 3, RadiusY = 3, Opacity = 0.9 }, 12, 8));
                c.Children.Add(Placed(new Rectangle { Width = 90, Height = 10, Fill = panel, RadiusX = 3, RadiusY = 3, Opacity = 0.9 }, 66, 36));
            }
            c.Children.Add(Placed(new TextBlock { Text = caption, Foreground = Br(_t.TextSecondary), FontSize = 12, Width = 78, TextWrapping = TextWrapping.Wrap }, 190, 6));
            return c;
        }

        private void RegisterFitDock()
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(DockCaption(_i.T("settings.fit_pic_title")));
            sp.Children.Add(FitStage(_i.T("settings.fit_stage1"), 0));
            sp.Children.Add(FitStage(_i.T("settings.fit_stage2"), 1));
            sp.Children.Add(FitStage(_i.T("settings.fit_stage3"), 2));
            RegisterDock(_i.T("settings.tab.fit"), sp, null);
        }
    }
}
