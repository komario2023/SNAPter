using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapter
{
    /// <summary>Small helpers of the settings window: a drop-down list, the highlight of the fields that a choice really uses,
    /// and the list of audio devices that ffmpeg can record.</summary>
    internal sealed partial class SettingsWindow
    {
        /// <summary>Puts a row into a frame: Mark(frame, true) outlines it with the accent color, Mark(frame, false) dims it
        /// (the choice made above does not use this field).</summary>
        private Border Relevant(FrameworkElement row)
        {
            return new Border { Child = row, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 0), Margin = new Thickness(-7, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        }

        private void Mark(Border frame, bool used)
        {
            frame.BorderBrush = used ? Br(_t.Accent) : Brushes.Transparent;
            frame.Opacity = used ? 1 : 0.45;
        }

        /// <summary>A field that opens a list of ready values (a drop-down list in the colors of the program's theme).</summary>
        private FrameworkElement DropDown(Val<string> state, Func<List<string>> items, double width, string emptyText, Action changed = null)
        {
            TextBlock shown = new TextBlock { Foreground = Br(_t.Text), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            TextBlock arrow = new TextBlock { Text = "▾", Foreground = Br(_t.TextSecondary), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel dp = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(arrow, Dock.Right);
            dp.Children.Add(arrow); dp.Children.Add(shown);
            Border box = new Border { Width = width, Child = dp, Padding = new Thickness(8, 5, 8, 5), Background = Br(_t.Surface), BorderBrush = Br(_t.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand };
            Action paint = delegate { shown.Text = string.IsNullOrEmpty(state.V) ? emptyText : state.V; shown.Foreground = Br(string.IsNullOrEmpty(state.V) ? _t.TextSecondary : _t.Text); };
            paint();
            box.MouseLeftButtonUp += delegate
            {
                ContextMenu menu = new ContextMenu { MaxHeight = 420, MinWidth = width, PlacementTarget = box, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                MenuItem none = new MenuItem { Header = emptyText, IsChecked = string.IsNullOrEmpty(state.V) };
                none.Click += delegate { state.V = ""; paint(); if (changed != null) changed(); };
                menu.Items.Add(none);
                foreach (string it in items())
                {
                    string v = it;
                    MenuItem mi = new MenuItem { Header = v, IsChecked = v == state.V };
                    mi.Click += delegate { state.V = v; paint(); if (changed != null) changed(); };
                    menu.Items.Add(mi);
                }
                menu.IsOpen = true;
            };
            return box;
        }

        /// <summary>Makes a row of a list movable with the mouse: while it is dragged it stands between the other rows, on release it stays there
        /// (the list "order" follows the Tag of the rows).</summary>
        private void DragReorder(UIElement handle, FrameworkElement row, StackPanel host, List<string> order, Action rebuild)
        {
            bool dragging = false;
            handle.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                dragging = true; handle.CaptureMouse();
                Panel p = row as Panel; if (p != null) p.Background = Br(_t.SurfaceMuted);
                e.Handled = true;
            };
            handle.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!dragging) return;
                double y = e.GetPosition(host).Y, acc = 0;
                int target = host.Children.Count - 1;
                for (int k = 0; k < host.Children.Count; k++)
                {
                    FrameworkElement c = (FrameworkElement)host.Children[k];
                    double h = c.ActualHeight + c.Margin.Top + c.Margin.Bottom;
                    if (y < acc + h / 2 || k == host.Children.Count - 1) { target = k; break; }
                    acc += h;
                }
                int cur = host.Children.IndexOf(row);
                if (target != cur && cur >= 0) { host.Children.Remove(row); host.Children.Insert(Math.Min(target, host.Children.Count), row); }
            };
            handle.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                dragging = false; handle.ReleaseMouseCapture();
                List<string> now = new List<string>();
                foreach (FrameworkElement c in host.Children) now.Add((string)c.Tag);
                order.Clear(); order.AddRange(now);
                rebuild();
                e.Handled = true;
            };
        }

        private static readonly Regex QuotedName = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>The names of the sound inputs that DirectShow offers (asked from ffmpeg, which needs about a second).</summary>
        private static List<string> ListAudioDevices(string ffmpegExe)
        {
            List<string> res = new List<string>();
            if (string.IsNullOrEmpty(ffmpegExe)) return res;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ffmpegExe, "-hide_banner -list_devices true -f dshow -i dummy")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    Task<string> err = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(6000)) { try { p.Kill(); } catch { } }
                    string text = err.Wait(1000) ? err.Result : "";
                    string[] lines = text.Replace("\r", "").Split('\n');
                    bool tagged = text.Contains("(audio)");
                    bool inAudio = false;
                    foreach (string line in lines)
                    {
                        if (line.Contains("DirectShow audio devices")) { inAudio = true; continue; }
                        if (line.Contains("DirectShow video devices")) { inAudio = false; continue; }
                        if (line.Contains("Alternative name")) continue;
                        Match m = QuotedName.Match(line);
                        if (!m.Success) continue;
                        bool take = tagged ? line.Contains("(audio)") : inAudio;
                        if (take && !res.Contains(m.Groups[1].Value)) res.Add(m.Groups[1].Value);
                    }
                }
            }
            catch (Exception ex) { Log.Write("audio devices: " + ex.Message); }
            return res;
        }
    }
}
