using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Snapter
{
    /// <summary>The frame round the area that is being recorded, with a timer and a Stop button above it.
    /// It lies outside the recorded rectangle and is hidden from screen capture, so it is not in the GIF or the video.</summary>
    internal sealed class RecorderWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        private const int Frame = 4, Strip = 40;
        private readonly System.Drawing.Rectangle _region;
        private readonly bool _stripOnTop;
        private readonly Grid _root = new Grid();
        private readonly TextBlock _label = new TextBlock();
        private readonly TextBlock _dot = new TextBlock();
        private IntPtr _hwnd = IntPtr.Zero;

        public event Action StopClicked;

        public RecorderWindow(System.Drawing.Rectangle region, I18n i18n, Theme theme)
        {
            _region = region;
            _stripOnTop = region.Y >= Strip + Frame + 4;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = null;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "SNAPter recording";

            double w = region.Width + 2 * Frame, h = region.Height + 2 * Frame + Strip;
            Canvas host = new Canvas { Width = Math.Max(w, 270), Height = h };
            double frameTop = _stripOnTop ? Strip : 0;

            Border frame = new Border
            {
                Width = region.Width + 2 * Frame, Height = region.Height + 2 * Frame,
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 40, 40)), BorderThickness = new Thickness(Frame),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, 0); Canvas.SetTop(frame, frameTop);
            host.Children.Add(frame);

            // the pill: a red dot, the time, the Stop button
            _dot.Text = "●"; _dot.Foreground = new SolidColorBrush(Color.FromRgb(230, 40, 40)); _dot.FontSize = 16; _dot.VerticalAlignment = VerticalAlignment.Center;
            _label.Text = "REC"; _label.FontFamily = new FontFamily("Segoe UI"); _label.FontSize = 14; _label.FontWeight = FontWeights.SemiBold;
            _label.Foreground = new SolidColorBrush(theme.Text); _label.VerticalAlignment = VerticalAlignment.Center; _label.Margin = new Thickness(6, 0, 10, 0); _label.MinWidth = 64;
            Border stop = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(230, 40, 40)), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand, Child = new TextBlock { Text = "■ " + i18n.T("rec.stop"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold }
            };
            stop.MouseLeftButtonUp += delegate { Action h2 = StopClicked; if (h2 != null) h2(); };
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_dot); sp.Children.Add(_label); sp.Children.Add(stop);
            Border pill = new Border
            {
                Child = sp, Background = new SolidColorBrush(Color.FromArgb(240, theme.Surface.R, theme.Surface.G, theme.Surface.B)),
                BorderBrush = new SolidColorBrush(theme.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 6, 3)
            };
            Canvas.SetLeft(pill, 0);
            Canvas.SetTop(pill, _stripOnTop ? 2 : frameTop + region.Height + 2 * Frame + 4);
            host.Children.Add(pill);

            _root.Children.Add(host);
            Content = _root;
        }

        public void ShowCountdown(int seconds) { _label.Text = seconds.ToString(); _dot.Foreground = Brushes.Orange; }

        public void SetElapsed(TimeSpan t)
        {
            _dot.Foreground = new SolidColorBrush(Color.FromRgb(230, 40, 40));
            _label.Text = string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            _root.LayoutTransform = new ScaleTransform(1.0 / dpi.DpiScaleX, 1.0 / dpi.DpiScaleY);
            // Windows 10 2004 and newer: the window is shown on the screen but is not part of any capture (0x11 = exclude from capture)
            try { SetWindowDisplayAffinity(_hwnd, 0x11); } catch { }
            int x = _region.X - Frame, y = _region.Y - Frame - (_stripOnTop ? Strip : 0);
            // HWND_TOPMOST = -1; SWP_NOSIZE = 1, SWP_NOACTIVATE = 0x10
            SetWindowPos(_hwnd, new IntPtr(-1), x, y, 0, 0, 0x1 | 0x10);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _root.LayoutTransform = new ScaleTransform(1.0 / newDpi.DpiScaleX, 1.0 / newDpi.DpiScaleY);
        }
    }
}
