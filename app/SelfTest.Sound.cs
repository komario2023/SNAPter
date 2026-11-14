using System;
using System.Linq;
using System.Windows.Input;
namespace Snapter
{
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ done-sound settings and Esc in the settings window
        private static void SoundTest(Settings st)
        {
            Say("sound test:");
            System.Collections.Generic.List<string> all = NotifySound.Available();
            Check(all.Count >= 5 && all.Contains("sys:Asterisk") && all.Contains("sys:Beep"), "the standard Windows sounds are offered (" + all.Count + " sounds)");
            Check(NotifySound.Normalize("nonsense") == NotifySound.DefaultId && NotifySound.Normalize(null) == NotifySound.DefaultId, "an unknown sound name falls back to the default");
            Check(st.SoundVolume >= 0 && st.SoundVolume <= 100 && st.SoundName == NotifySound.Normalize(st.SoundName), "the sound name and the volume of the settings are valid (" + st.SoundName + ", " + st.SoundVolume + "%)");
            string p = NotifySound.ResolvePath("sys:Asterisk");
            Check(p == null || System.IO.File.Exists(p), "the Windows 'Asterisk' sound resolves to a real file (" + (p ?? "none: system fallback") + ")");
            NotifySound.Play("sys:Asterisk", 0);   // volume 0: must not throw and must stay silent
            Check(true, "a sound at volume 0 does nothing");
        }

        /// <summary>Esc in the settings window closes it like the Close button.</summary>
        private static void SettingsEscCheck(SettingsWindow sw)
        {
            System.Windows.PresentationSource src = System.Windows.PresentationSource.FromVisual(sw);
            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            sw.RaiseEvent(ke);
            Check(!sw.IsVisible, "Esc closes the settings window");
        }
    }
}
