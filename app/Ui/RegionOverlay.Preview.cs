using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>The panels without a screenshot: the settings window shows them as if it were the selected region
    /// (transparent, the mouse passes through, it never takes the keyboard).</summary>
    internal sealed partial class RegionOverlay
    {
        private bool _preview;
        private readonly List<FrameworkElement> _marks = new List<FrameworkElement>();

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

        /// <summary>monitor - the screen of the settings window; sel - the window on it (pixels from the corner of the screen).</summary>
        internal static RegionOverlay CreatePreview(System.Drawing.Rectangle monitor, Rect sel, Theme theme, I18n i18n, Settings st)
        {
            System.Drawing.Bitmap dummy = new System.Drawing.Bitmap(1, 1);
            RegionOverlay o = new RegionOverlay(dummy, monitor, theme, i18n, st, false);
            o.MakePreview(sel);
            return o;
        }

        private void MakePreview(Rect sel)
        {
            _preview = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false; Focusable = false; Cursor = Cursors.Arrow;
            _image.Visibility = Visibility.Collapsed; _dim.Visibility = Visibility.Collapsed;
            if (_hint != null) _hint.Visibility = Visibility.Collapsed;
            _root.IsHitTestVisible = false;
            IsHitTestVisible = false;
            SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000);   // click-through, not in Alt+Tab, never active
            };
            _hasSel = true; _sel = sel;
            ShowPanels();
        }

        /// <summary>The settings window changed a value: the panels take the new settings at once, in this same window.</summary>
        internal void UpdatePreview(Settings s)
        {
            _styles = StyleStore.Create(s);
            TextAnnotation.FontName = string.IsNullOrWhiteSpace(s.TextFont) ? "Segoe UI" : s.TextFont;
            TextAnnotation.Bold = s.TextBold; TextAnnotation.Italic = s.TextItalic; TextAnnotation.Align = s.TextAlign;
            ArrowAnnotation.HeadStyle = s.ArrowHead;
            foreach (FrameworkElement m in _marks) _canvas.Children.Remove(m);
            _marks.Clear();
            ApplyProfileSettings(s);
        }

        /// <summary>Outlines the parts of the panels (ids: tools, props, a group of the bottom panel, frame) that the open settings page changes.</summary>
        internal void Highlight(IEnumerable<string> ids)
        {
            foreach (FrameworkElement m in _marks) _canvas.Children.Remove(m);
            _marks.Clear();
            Action<Rect> mark = delegate (Rect r)
            {
                if (r.IsEmpty || r.Width < 2) return;
                r.Inflate(3, 3);
                Border b = new Border { Width = r.Width, Height = r.Height, BorderBrush = new SolidColorBrush(WpfColor.FromRgb(255, 138, 0)), BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(WpfColor.FromArgb(40, 255, 138, 0)), IsHitTestVisible = false };
                Canvas.SetLeft(b, r.X); Canvas.SetTop(b, r.Y);
                _canvas.Children.Add(b); _marks.Add(b);
            };
            Func<FrameworkElement, Rect> boundsOf = delegate (FrameworkElement fe)
            {
                if (fe == null || fe.Visibility != Visibility.Visible || fe.ActualWidth < 1) return Rect.Empty;
                try { return fe.TransformToVisual(_canvas).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight)); } catch { return Rect.Empty; }
            };
            foreach (string id in ids)
            {
                if (id == "tools") mark(boundsOf(_panelA));
                else if (id == "props") mark(boundsOf(_panelB));
                else if (id == "frame") mark(_sel);
                else if (id == "actions") { FrameworkElement c, sv; if (_propHosts.TryGetValue("copy", out c)) mark(boundsOf(c)); if (_propHosts.TryGetValue("save", out sv)) mark(boundsOf(sv)); }
                else { FrameworkElement h; if (_propHosts.TryGetValue(id, out h)) mark(boundsOf(h)); }
            }
        }
    }
}
