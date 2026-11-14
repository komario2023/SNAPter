using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
namespace Snapter
{
    internal sealed partial class RegionOverlay
    {
        // ================= profiles on a key (settings: profile_cycle) =================
        // Before, while and after the region is drawn the key (default: the one left of 1, Ё / `) switches the profiles in the order of the settings.
        // The first profile of the order is the one a region starts with ("current" = the setup as saved). Only this capture changes, nothing is saved.
        private List<string> _profiles;
        private int _profileIdx;
        private Func<string, Settings> _profileSettings;
        private Func<string, string> _profileLabel;
        private Border _profileToast;
        private TextBlock _profileToastText;
        private DispatcherTimer _profileTimer;

        /// <summary>Called before Show: the order of the profiles, how to get the settings of a profile and its name for the message.</summary>
        internal void SetProfiles(List<string> order, Func<string, Settings> settingsFor, Func<string, string> label, int start = 0, bool applyStart = true)
        {
            if (order == null || order.Count == 0 || settingsFor == null) return;
            _profiles = order; _profileSettings = settingsFor; _profileLabel = label;
            _profileIdx = Math.Max(0, Math.Min(order.Count - 1, start));
            if (applyStart && order[_profileIdx] != "current") ApplyProfileSettings(settingsFor(order[_profileIdx]));
        }

        /// <summary>Key names of the settings: a letter, a digit, F1-F12 or the name of any WPF key (Oem3, Tab, Space...).</summary>
        internal static bool ParseKeyName(string s, out Key k)
        {
            k = Key.None;
            if (string.IsNullOrWhiteSpace(s) || s.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
            if (TryKey(s, out k)) return true;
            return Enum.TryParse(s.Trim(), true, out k) && k != Key.None;
        }

        private bool IsProfileKey(Key k)
        {
            if (_profiles == null || _profiles.Count < 2) return false;
            Key want;
            if (!ParseKeyName(_settings.ProfileKey, out want) || want != k) return false;
            return Keyboard.Modifiers == ModifierKeys.None;
        }

        /// <summary>Not while something is being typed: the key then belongs to the text.</summary>
        private bool ProfileKeyAllowed()
        {
            return _editBox == null && _sizeEdit == null && !_typingSize && !(Keyboard.FocusedElement is TextBox) && !_listOpen;
        }

        private void CycleProfile()
        {
            if (_profiles == null || _profiles.Count < 2) return;
            _profileIdx = (_profileIdx + 1) % _profiles.Count;
            string name = _profiles[_profileIdx];
            ApplyProfileSettings(_profileSettings(name));
            ShowProfileToast(name);
        }

        /// <summary>Puts the settings of a profile in place of the present ones: the tools, the look of the panels, the minimal mode. The drawing stays.</summary>
        private void ApplyProfileSettings(Settings s)
        {
            if (s == null) return;
            if (_editBox != null) EndEdit(true);
            _settings = s;
            _editor = s.EditorEnabled && !s.MinimalUi && !_autoCopy;
            if (!_editor || (_tool != EditorTool.Select && !s.ToolEnabled(StyleStore.Key(_tool)))) _tool = EditorTool.Select;   // the tool is not in this profile
            foreach (Border p in new Border[] { _panelA, _panelB, _panelC, _panelD }) if (p != null) _canvas.Children.Remove(p);
            _panelA = null; _panelB = null; _panelC = null; _panelD = null;
            _panelsBuilt = false;
            _toolButtons.Clear(); _swatches.Clear(); _presetButtons.Clear(); _swatchParts.Clear(); _pctBtns.Clear(); _toolLabelBlocks.Clear(); _propHosts.Clear();
            _recentHost = null; _recentShown = -1; _beautifyBtn = null;
            _userPosA = null; _userPosB = null; _userPosC = null;
            if (_hasSel) { ShowPanels(); if (!_preview) UpdateVisuals(); }   // the settings preview has no frame and no handles
        }

        private void ShowProfileToast(string name)
        {
            if (!_settings.NotifyEnabled) return;     // notifications are off: no pop-up on the profile key
            if (_profileToast == null)
            {
                _profileToastText = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(_theme.Text), TextAlignment = TextAlignment.Center };
                _profileToast = new Border
                {
                    Child = _profileToastText, Padding = new Thickness(14, 7, 14, 7), CornerRadius = new CornerRadius(Math.Max(4, _theme.RadiusMd - 2)), IsHitTestVisible = false,
                    Background = new SolidColorBrush(WpfColor.FromArgb(240, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B)),
                    BorderBrush = new SolidColorBrush(_theme.Accent), BorderThickness = new Thickness(1.5)
                };
                _canvas.Children.Add(_profileToast);
                _profileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                _profileTimer.Tick += delegate { _profileTimer.Stop(); _profileToast.Visibility = Visibility.Collapsed; };
            }
            string label = _profileLabel != null ? _profileLabel(name) : name;
            _profileToastText.Text = _i18n.T("profile.toast", label, _profileIdx + 1, _profiles.Count);
            _profileToast.Visibility = Visibility.Visible;
            _profileToast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            try
            {
                System.Drawing.Point cp = System.Windows.Forms.Cursor.Position;
                System.Drawing.Rectangle mon = System.Windows.Forms.Screen.FromPoint(cp).Bounds;
                Canvas.SetLeft(_profileToast, mon.X - _bounds.X + (mon.Width - _profileToast.DesiredSize.Width) / 2);
                Canvas.SetTop(_profileToast, mon.Y - _bounds.Y + 28);
            }
            catch { Canvas.SetLeft(_profileToast, 20); Canvas.SetTop(_profileToast, 28); }
            _profileTimer.Stop(); _profileTimer.Start();
        }

        // ---- self test hooks ----
        internal void TestSetProfiles(List<string> order, Func<string, Settings> settingsFor) { SetProfiles(order, settingsFor, null); }
        internal void TestCycleProfile() { CycleProfile(); }
        internal void TestClearProfiles() { _profiles = null; }
        internal int TestProfileIndex { get { return _profileIdx; } }
        internal bool TestEditor { get { return _editor; } }
        internal bool TestToolButton(EditorTool t) { return _toolButtons.ContainsKey(t); }
    }
}
