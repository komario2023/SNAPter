using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
namespace Snapter
{
    internal sealed partial class SettingsWindow
    {
        // ---- the base settings: a point to return to, between the factory settings and the zero ----
        // "Save as base" keeps a copy of the whole current setup (config\settings.baseline.json). "Return to base" puts it back: the program is
        // fully set up again, only the latest experiments are gone. "Factory settings" are the settings of a freshly installed program.
        private static string BaselinePath { get { return Path.Combine(Path.GetDirectoryName(Paths.UserSettings), "settings.baseline.json"); } }

        private void SaveBaseline()
        {
            try
            {
                if (File.Exists(BaselinePath) && MessageBox.Show(this, _i.T("settings.baseline_save_confirm"), _i.T("settings.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                SaveQuiet();                                                       // what is on the pages now is saved first
                if (!File.Exists(Paths.UserSettings)) { _status.Text = _i.T("settings.baseline_nothing"); return; }
                if (File.Exists(BaselinePath)) File.Copy(BaselinePath, Path.Combine(Path.GetDirectoryName(BaselinePath), "settings.baseline.backup.json"), true);
                File.Copy(Paths.UserSettings, BaselinePath, true);
                _status.Text = _i.T("settings.baseline_saved");
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        /// <summary>"Return to base": the saved base settings come back; without them the choice is the factory settings.</summary>
        private void ResetToBaseline()
        {
            try
            {
                if (!File.Exists(BaselinePath))
                {
                    if (MessageBox.Show(this, _i.T("settings.baseline_none"), _i.T("settings.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) DoReset(false);
                    return;
                }
                if (MessageBox.Show(this, _i.T("settings.reset_confirm"), _i.T("settings.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                DoReset(true);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void FactoryReset()
        {
            try
            {
                if (MessageBox.Show(this, _i.T("settings.factory_confirm"), _i.T("settings.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                DoReset(false);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void DoReset(bool toBase)
        {
            BackupUserFile();
            if (toBase) File.Copy(BaselinePath, Paths.UserSettings, true);
            else if (File.Exists(Paths.UserSettings)) File.Delete(Paths.UserSettings);
            int page = _current;
            _app.ReloadSettings();
            Close();
            _app.OpenSettings(page);
        }

        internal static string TestBaselinePath { get { return BaselinePath; } }
    }
}
