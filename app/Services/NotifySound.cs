using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using Microsoft.Win32;

namespace Snapter
{
    /// <summary>The "done" sound: one of the standard Windows sounds, played at the volume from the settings.
    /// A sound id is "sys:Asterisk" (the sound set in Windows for that event) or "file:Windows Ding.wav" (a file of C:\Windows\Media).</summary>
    internal static class NotifySound
    {
        public const string DefaultId = "sys:Asterisk";

        // the sound events of Windows: name -> registry key of the sound scheme
        private static readonly string[] SysNames = { "Asterisk", "Exclamation", "Hand", "Question", "Beep" };
        private static readonly string[] MediaFiles =
        {
            "Windows Notify System Generic.wav", "Windows Notify Messaging.wav", "Windows Notify Email.wav", "Windows Notify Calendar.wav",
            "Windows Notify.wav", "Windows Background.wav", "Windows Balloon.wav", "Windows Ding.wav", "Windows Default.wav",
            "Windows Information Bar.wav", "Windows Message Nudge.wav", "Windows Proximity Notification.wav", "chimes.wav", "chord.wav", "ding.wav", "tada.wav", "notify.wav"
        };

        private static System.Windows.Media.MediaPlayer _player;   // kept alive while the sound plays

        private static string MediaDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media"); } }

        /// <summary>All the sounds the settings offer: the five Windows events, then the notification files that exist on this computer.</summary>
        public static List<string> Available()
        {
            List<string> r = new List<string>();
            foreach (string n in SysNames) r.Add("sys:" + n);
            foreach (string f in MediaFiles) if (File.Exists(Path.Combine(MediaDir, f))) r.Add("file:" + f);
            return r;
        }

        /// <summary>A name for the button: the translated Windows event, or the file name without the extension.</summary>
        public static string Label(string id, I18n i18n)
        {
            if (id.StartsWith("sys:")) { string k = "sound.sys." + id.Substring(4); string v = i18n.T(k); return v == k ? id.Substring(4) : v; }
            if (id.StartsWith("file:")) return Path.GetFileNameWithoutExtension(id.Substring(5)).Replace("Windows ", "");
            return id;
        }

        public static string Normalize(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return DefaultId;
            return Available().Contains(id) ? id : DefaultId;
        }

        /// <summary>The wav file behind the id (the user's own Windows sound scheme is respected), or null.</summary>
        public static string ResolvePath(string id)
        {
            try
            {
                if (id.StartsWith("file:")) { string p = Path.Combine(MediaDir, id.Substring(5)); return File.Exists(p) ? p : null; }
                if (id.StartsWith("sys:"))
                {
                    string ev = id.Substring(4);
                    string key = ev == "Beep" ? ".Default" : "System" + ev;
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\" + key + @"\.Current"))
                    {
                        string v = k == null ? null : k.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(v))
                        {
                            v = Environment.ExpandEnvironmentVariables(v);
                            if (File.Exists(v)) return v;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Play the sound at volume 0..100 percent (of the Windows volume). Without a file it falls back to the Windows event (no volume then).</summary>
        public static void Play(string id, int volumePercent)
        {
            try
            {
                id = Normalize(id);
                double vol = Math.Max(0, Math.Min(100, volumePercent)) / 100.0;
                if (vol <= 0) return;
                string path = ResolvePath(id);
                if (path != null)
                {
                    if (_player == null) _player = new System.Windows.Media.MediaPlayer();
                    _player.Stop();
                    _player.Open(new Uri(path));
                    _player.Volume = vol;
                    _player.Play();
                    return;
                }
                switch (id)
                {
                    case "sys:Exclamation": SystemSounds.Exclamation.Play(); break;
                    case "sys:Hand": SystemSounds.Hand.Play(); break;
                    case "sys:Question": SystemSounds.Question.Play(); break;
                    case "sys:Beep": SystemSounds.Beep.Play(); break;
                    default: SystemSounds.Asterisk.Play(); break;
                }
            }
            catch (Exception ex) { Log.Write("sound error: " + ex.Message); }
        }
    }
}
