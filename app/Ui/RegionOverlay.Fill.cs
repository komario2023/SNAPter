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
        // ================= paint bucket =================
        /// <summary>A click with the paint bucket: the area of similar color around the point (inside the region) is painted.</summary>
        private void PlaceFill(Point p)
        {
            Point q = ClampToSel(p);
            ToolStyle st = _styles.Current(EditorTool.Fill);
            double tol;
            if (!_sessionSize.TryGetValue(EditorTool.Fill, out tol)) tol = st.Size;
            Int32Rect lim = new Int32Rect((int)Math.Floor(_sel.X), (int)Math.Floor(_sel.Y), (int)Math.Ceiling(_sel.Width), (int)Math.Ceiling(_sel.Height));
            Int32Rect box; byte[] mask;
            if (!FloodFiller.Fill(q, lim, (int)Math.Round(tol), out box, out mask)) return;
            FillAnnotation f = new FillAnnotation { R = new Rect(box.X, box.Y, box.Width, box.Height), Mask = mask };
            ApplyStyle(f, st);
            _items.Add(f);
            _selected = f;
            Commit();
            RebuildAnnotations();
        }
    }
}
