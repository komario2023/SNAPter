using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Snapter
{
    internal sealed class LanguageInfo
    {
        public string Code;
        public string Native;
        public string Path;
    }

    /// <summary>Texts live in config\i18n\*.json (flat "key": "text"). English is the fallback.</summary>
    internal sealed class I18n
    {
        private Dictionary<string, string> _strings = new Dictionary<string, string>();
        public string Code { get; private set; }

        public static List<LanguageInfo> Languages()
        {
            List<LanguageInfo> list = new List<LanguageInfo>();
            if (!Directory.Exists(Paths.I18n)) return list;
            string[] files = Directory.GetFiles(Paths.I18n, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                try
                {
                    Dictionary<string, string> d = ReadFlat(f);
                    string code; if (!d.TryGetValue("_meta.code", out code) || string.IsNullOrEmpty(code)) code = System.IO.Path.GetFileNameWithoutExtension(f);
                    string native; if (!d.TryGetValue("_meta.native_name", out native) || string.IsNullOrEmpty(native)) native = code;
                    list.Add(new LanguageInfo { Code = code, Native = native, Path = f });
                }
                catch (Exception ex) { Log.Write("language file error " + f + ": " + ex.Message); }
            }
            return list;
        }

        public static Dictionary<string, string> ReadFlat(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)))
            {
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
            }
            return d;
        }

        public static string ResolveCode(string setting)
        {
            List<LanguageInfo> langs = Languages();
            if (!string.IsNullOrEmpty(setting) && setting != "auto")
                foreach (LanguageInfo l in langs) if (l.Code == setting) return setting;
            string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            foreach (LanguageInfo l in langs) if (l.Code == sys) return sys;
            return "en";
        }

        public void Load(string setting)
        {
            Code = ResolveCode(setting);
            Dictionary<string, string> d = new Dictionary<string, string>();
            string en = System.IO.Path.Combine(Paths.I18n, "en.json");
            if (File.Exists(en)) d = ReadFlat(en);
            string sel = System.IO.Path.Combine(Paths.I18n, Code + ".json");
            if (Code != "en" && File.Exists(sel))
                foreach (KeyValuePair<string, string> kv in ReadFlat(sel)) d[kv.Key] = kv.Value;
            _strings = d;
        }

        public string T(string key)
        {
            string v;
            return _strings.TryGetValue(key, out v) ? v : key;
        }

        public string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); } catch { return T(key); }
        }
    }
}
