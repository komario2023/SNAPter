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
        // ================= aligning and spreading a multi-selection (a small panel next to the tool panel) =================
        private Border _panelD;

        // the icons: a line (the edge or the axis) and two bars
        private static readonly string[][] AlignKinds =
        {
            new[] { "left",    "M 2,2 L 2,16 M 4,5 L 14,5 M 4,11 L 10,11" },
            new[] { "hcenter", "M 9,2 L 9,16 M 3,5 L 15,5 M 5,11 L 13,11" },
            new[] { "right",   "M 16,2 L 16,16 M 4,5 L 14,5 M 8,11 L 14,11" },
            new[] { "top",     "M 2,2 L 16,2 M 5,4 L 5,14 M 11,4 L 11,10" },
            new[] { "vcenter", "M 2,9 L 16,9 M 5,3 L 5,15 M 11,5 L 11,13" },
            new[] { "bottom",  "M 2,16 L 16,16 M 5,4 L 5,14 M 11,8 L 11,14" },
            new[] { "disth",   "M 2,2 L 2,16 M 16,2 L 16,16 M 7,5 L 11,5 L 11,13 L 7,13 Z" },
            new[] { "distv",   "M 2,2 L 16,2 M 2,16 L 16,16 M 5,7 L 13,7 L 13,11 L 5,11 Z" },
        };

        private void BuildAlignPanel()
        {
            StackPanel col = new StackPanel();
            _panelD = MakePanel(col, "tool");
            for (int row = 0; row < 3; row++)
            {
                StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                int count = row < 2 ? 3 : 2;
                for (int k = 0; k < count; k++)
                {
                    string kind = AlignKinds[row * 3 + k][0], geo = AlignKinds[row * 3 + k][1];
                    line.Children.Add(IconButton(StrokeIcon(geo, 1.8, 1), _i18n.T("tip.align." + kind), delegate { AlignGroup(kind); }));
                }
                col.Children.Add(line);
                if (row < 2) col.Children.Add(new Border { Height = 1, Background = Brush(_theme.Border), Margin = new Thickness(4, 2, 4, 2) });
            }
            _canvas.Children.Add(_panelD);
        }

        /// <summary>Shown only while two or more objects are selected, beside the tool panel (where the arrow panel sits for one arrow).</summary>
        private void LayoutAlignPanel(Rect toolPanel)
        {
            bool show = _editor && GroupActive && _editBox == null && !toolPanel.IsEmpty;
            if (show && _panelD == null) BuildAlignPanel();
            if (_panelD == null) return;
            _panelD.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) PlaceBeside(_panelD, toolPanel, _settings.ToolbarPosition);
        }

        /// <summary>kind: left, hcenter, right, top, vcenter, bottom (to the edge or the middle of the whole group),
        /// disth, distv (equal gaps between the objects, the outer two stay where they are).</summary>
        private void AlignGroup(string kind)
        {
            List<Annotation> all = SelectedAll();
            if (all.Count < 2 || _editBox != null) return;
            Rect u = all[0].Bounds();
            foreach (Annotation a in all) u.Union(a.Bounds());
            bool moved = false;
            if (kind == "disth" || kind == "distv")
            {
                bool h = kind == "disth";
                List<Annotation> ord = all.OrderBy(a => h ? a.Bounds().Left + a.Bounds().Width / 2 : a.Bounds().Top + a.Bounds().Height / 2).ToList();
                if (ord.Count < 3) return;
                double total = 0;
                foreach (Annotation a in ord) total += h ? a.Bounds().Width : a.Bounds().Height;
                double gap = ((h ? u.Width : u.Height) - total) / (ord.Count - 1);
                double pos = h ? u.Left : u.Top;
                foreach (Annotation a in ord)
                {
                    Rect b = a.Bounds();
                    double d = h ? pos - b.Left : pos - b.Top;
                    if (Math.Abs(d) > 0.01) { if (h) a.Translate(d, 0); else a.Translate(0, d); moved = true; }
                    pos += (h ? b.Width : b.Height) + gap;
                }
            }
            else
            {
                foreach (Annotation a in all)
                {
                    Rect b = a.Bounds();
                    double dx = 0, dy = 0;
                    switch (kind)
                    {
                        case "left": dx = u.Left - b.Left; break;
                        case "right": dx = u.Right - b.Right; break;
                        case "hcenter": dx = (u.Left + u.Width / 2) - (b.Left + b.Width / 2); break;
                        case "top": dy = u.Top - b.Top; break;
                        case "bottom": dy = u.Bottom - b.Bottom; break;
                        case "vcenter": dy = (u.Top + u.Height / 2) - (b.Top + b.Height / 2); break;
                        default: return;
                    }
                    if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01) { a.Translate(dx, dy); moved = true; }
                }
            }
            if (!moved) return;
            Commit();
            RebuildAnnotations();
        }

        internal bool TestAlignPanelVisible { get { return _panelD != null && _panelD.Visibility == Visibility.Visible; } }
        internal void TestAlign(string kind) { AlignGroup(kind); }
    }
}
