using System;
using System.IO;

namespace Snapter
{
    /// <summary>Folders of the project. The program finds its root by walking up from the exe
    /// until it sees config\settings.default.json (so it works from app\publish and from a copy).</summary>
    internal static class Paths
    {
        public static readonly string Root = FindRoot();

        public static string Config { get { return Path.Combine(Root, "config"); } }
        public static string I18n { get { return Path.Combine(Config, "i18n"); } }
        public static string Themes { get { return Path.Combine(Config, "themes"); } }
        public static string DefaultSettings { get { return Path.Combine(Config, "settings.default.json"); } }
        /// <summary>config\settings.json; the environment variable SNAPTER_USER_SETTINGS can point elsewhere (used for tests).</summary>
        public static string UserSettings
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("SNAPTER_USER_SETTINGS");
                return string.IsNullOrEmpty(over) ? Path.Combine(Config, "settings.json") : over;
            }
        }
        public static string Logs { get { return Path.Combine(Root, "logs"); } }

        private static string FindRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "settings.default.json")))
                    return dir.FullName;
            }
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>The program version MAJOR.MINOR.BUILD. The only source is app\Snapter.csproj (Version); it is shown in the title bar of the settings window.</summary>
    internal static class AppVersion
    {
        public static string Text
        {
            get
            {
                Version v = typeof(AppVersion).Assembly.GetName().Version;
                return v == null ? "" : v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        private static readonly bool DebugOn = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAPTER_DEBUG"));

        /// <summary>Extra trace lines, written only when the environment variable SNAPTER_DEBUG is set.</summary>
        public static void Debug(string message)
        {
            if (DebugOn) Write("[debug] " + message);
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Paths.Logs);
                    File.AppendAllText(Path.Combine(Paths.Logs, "app.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
