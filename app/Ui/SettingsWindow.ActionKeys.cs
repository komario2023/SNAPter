using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Snapter
{
    internal sealed partial class SettingsWindow
    {
        /// <summary>The shortcuts that work after a capture (save, save as, copy, pin, upload, beautify, select all, undo, redo, delete,
        /// duplicate, forward, backward). Click a field and press the combination; the values go to editor_keys as act_*.</summary>
        private void BuildActionKeys(StackPanel page)
        {
            page.Children.Add(new TextBlock { Text = _i.T("settings.action_keys_title"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2), Foreground = Br(_t.Text) });
            page.Children.Add(Hint(_i.T("settings.action_keys_hint")));
            List<KeyValuePair<string, TextBox>> boxes = new List<KeyValuePair<string, TextBox>>();
            for (int i = 0; i < RegionOverlay.ActionIds.Length; i++)
            {
                string aid = "act_" + RegionOverlay.ActionIds[i], adef = RegionOverlay.ActionDefaults[i];
                string cur; if (!_s.EditorKeys.TryGetValue(aid, out cur) || string.IsNullOrWhiteSpace(cur)) cur = adef;
                TextBox box = Input(cur, 170);
                box.IsReadOnly = true;
                box.PreviewKeyDown += delegate (object s2, KeyEventArgs e2)
                {
                    e2.Handled = true;
                    string kn = ActionKeyName(e2);
                    if (kn != null) { box.Text = kn; MarkDuplicateKeys(boxes); }
                    else if (e2.Key == Key.Back) { box.Text = "none"; MarkDuplicateKeys(boxes); }
                };
                Border reset = Btn(_i.T("settings.key_default"), false, delegate { box.Text = adef; MarkDuplicateKeys(boxes); });
                reset.Margin = new Thickness(6, 0, 0, 0);
                reset.Padding = new Thickness(8, 4, 8, 4);
                reset.VerticalAlignment = VerticalAlignment.Center;
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(box);
                row.Children.Add(reset);
                row.Children.Add(new TextBlock { Text = _i.T("keyact." + RegionOverlay.ActionIds[i]), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Br(_t.Text) });
                page.Children.Add(row);
                boxes.Add(new KeyValuePair<string, TextBox>(aid, box));
            }
            MarkDuplicateKeys(boxes);
            _savers.Add(r =>
            {
                JsonObject o = Settings.Section(r, "editor_keys");
                foreach (KeyValuePair<string, TextBox> kv in boxes) o[kv.Key] = kv.Value.Text.Trim();
            });
        }

        /// <summary>The same combination in two fields: both get a red frame.</summary>
        private void MarkDuplicateKeys(List<KeyValuePair<string, TextBox>> boxes)
        {
            foreach (KeyValuePair<string, TextBox> a in boxes)
            {
                bool dup = false;
                string t = a.Value.Text.Trim();
                if (t.Length > 0 && t != "none")
                    foreach (KeyValuePair<string, TextBox> b in boxes)
                        if (b.Value != a.Value && b.Value.Text.Trim().Equals(t, System.StringComparison.OrdinalIgnoreCase)) dup = true;
                a.Value.BorderBrush = dup ? System.Windows.Media.Brushes.Red : Br(_t.Border);
                a.Value.ToolTip = dup ? _i.T("settings.key_duplicate") : null;
            }
        }

        /// <summary>The pressed combination as text. Letters and digits need Ctrl, Shift or Alt (they are the tool keys otherwise);
        /// Delete, F-keys, brackets and the like work alone.</summary>
        private static string ActionKeyName(KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
            if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LWin || key == Key.RWin || key == Key.Escape || key == Key.Back || key == Key.None || key == Key.ImeProcessed || key == Key.DeadCharProcessed) return null;
            ModifierKeys m = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
            string name = null;
            bool plain = false;
            if (key >= Key.A && key <= Key.Z) name = key.ToString();
            else if (key >= Key.D0 && key <= Key.D9) name = ((int)key - (int)Key.D0).ToString();
            else
            {
                plain = true;
                if (key >= Key.F1 && key <= Key.F24) name = key.ToString();
                else switch (key)
                {
                    case Key.Delete: name = "Delete"; break;
                    case Key.Enter: name = "Enter"; break;
                    case Key.Space: name = "Space"; break;
                    case Key.Tab: name = "Tab"; break;
                    case Key.Insert: name = "Insert"; break;
                    case Key.Home: name = "Home"; break;
                    case Key.End: name = "End"; break;
                    case Key.PageUp: name = "PageUp"; break;
                    case Key.Next: name = "PageDown"; break;
                    case Key.OemOpenBrackets: name = "["; break;
                    case Key.OemCloseBrackets: name = "]"; break;
                    default: name = key.ToString(); break;
                }
            }
            if (!plain && m == ModifierKeys.None) return null;
            string s = "";
            if ((m & ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((m & ModifierKeys.Shift) != 0) s += "Shift+";
            if ((m & ModifierKeys.Alt) != 0) s += "Alt+";
            return s + name;
        }
    }
}
