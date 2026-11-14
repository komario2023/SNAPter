using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapter
{
    /// <summary>A pinned snapshot: a small window above all others. Drag to move, wheel to zoom, Ctrl+wheel for
    /// transparency, double click, right click or Esc to close.</summary>
    internal sealed class PinWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private readonly BitmapSource _src;
        private readonly Image _img = new Image();
        private readonly Grid _root = new Grid();
        private readonly System.Drawing.Point _at;
        private double _zoom = 1;

        public PinWindow(System.Drawing.Bitmap bmp, System.Drawing.Rectangle screenRect, Theme theme)
        {
            _src = ToSource(bmp);
            _at = new System.Drawing.Point(screenRect.X, screenRect.Y);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = new SolidColorBrush(theme.Accent);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "SNAPter pin";
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _img.Source = _src;
            _img.Stretch = Stretch.Fill;
            _img.Width = _src.PixelWidth;
            _img.Height = _src.PixelHeight;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            Border frame = new Border();
            frame.BorderBrush = new SolidColorBrush(theme.Accent);
            frame.BorderThickness = new Thickness(1);
            frame.Child = _img;
            _root.Children.Add(frame);
            Content = _root;
            Cursor = Cursors.SizeAll;
            ToolTip = "Esc / right click - close";

            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount >= 2) { Close(); return; }
                try { DragMove(); } catch { }
            };
            MouseRightButtonDown += delegate { Close(); };
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            MouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    Opacity = Math.Max(0.2, Math.Min(1, Opacity + (e.Delta > 0 ? 0.1 : -0.1)));
                else
                {
                    _zoom = Math.Max(0.1, Math.Min(5, _zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)));
                    _img.Width = _src.PixelWidth * _zoom;
                    _img.Height = _src.PixelHeight * _zoom;
                }
            };
        }

        /// <summary>A long picture (scrolling screenshot) is shown reduced so that it fits the screen; the wheel zooms.</summary>
        public void FitToScreen()
        {
            System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromPoint(_at).WorkingArea;
            double k = Math.Min((wa.Width - 40.0) / _src.PixelWidth, (wa.Height - 40.0) / _src.PixelHeight);
            if (k >= 1) return;
            _zoom = Math.Max(0.02, k);
            _img.Width = _src.PixelWidth * _zoom;
            _img.Height = _src.PixelHeight * _zoom;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            _root.LayoutTransform = new ScaleTransform(1.0 / dpi.DpiScaleX, 1.0 / dpi.DpiScaleY);
            IntPtr h = new WindowInteropHelper(this).Handle;
            // HWND_TOPMOST = -1; SWP_NOSIZE = 1, SWP_NOACTIVATE = 0x10
            SetWindowPos(h, new IntPtr(-1), _at.X - 1, _at.Y - 1, 0, 0, 0x1 | 0x10);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _root.LayoutTransform = new ScaleTransform(1.0 / newDpi.DpiScaleX, 1.0 / newDpi.DpiScaleY);
        }

        private static BitmapSource ToSource(System.Drawing.Bitmap bmp)
        {
            System.Drawing.Rectangle r = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData d = bmp.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                BitmapSource s = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null, d.Scan0, d.Stride * bmp.Height, d.Stride);
                s.Freeze();
                return s;
            }
            finally { bmp.UnlockBits(d); }
        }
    }
}
