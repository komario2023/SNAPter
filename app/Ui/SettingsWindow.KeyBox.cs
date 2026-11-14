using System.Windows.Controls;
using System.Windows.Input;

namespace Snapter
{
    internal sealed partial class SettingsWindow
    {
        /// <summary>A field for ONE key of the editor: click it and press the key (any key except Shift, Ctrl, Alt, Win and Esc);
        /// the name stays in the field (Oem3 is the key left of 1, Ё on a Russian keyboard). Backspace / Delete turn the key off ("none").</summary>
        private TextBox KeyBox(string current, double width = 110)
        {
            TextBox box = Input(string.IsNullOrWhiteSpace(current) ? "none" : current, width);
            box.IsReadOnly = true;
            box.ToolTip = _i.T("settings.key_box_tip");
            box.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                e.Handled = true;
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.Back || k == Key.Delete) { box.Text = "none"; return; }
                if (k == Key.LeftShift || k == Key.RightShift || k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt
                    || k == Key.LWin || k == Key.RWin || k == Key.Escape || k == Key.None || k == Key.ImeProcessed || k == Key.DeadCharProcessed) return;
                box.Text = k.ToString();
            };
            return box;
        }
    }
}
