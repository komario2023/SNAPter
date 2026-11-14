using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Snapter
{
    /// <summary>The program builds itself. When the exe starts and the source files (app\**\*.cs, the project, the manifest) are not the ones
    /// it was built from - whoever edited them - a script rebuilds it and starts the new one.
    /// The fingerprint of the sources is "files|newest write time" (tools\BuildStamp.ps1 writes the same text after every build).
    /// Settings: startup.auto_rebuild (default on). Without the sources next to the exe (an installed copy) nothing happens.</summary>
    internal static class AutoUpdate
    {
        private static readonly string[] Exts = { ".cs", ".csproj", ".manifest", ".ico", ".resx" };

        internal static string StampOf(string root)
        {
            string app = Path.Combine(root, "app");
            long max = 0; int count = 0;
            foreach (string f in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(Exts, ext) < 0) continue;
                string low = f.ToLowerInvariant();
                if (low.Contains("\\obj\\") || low.Contains("\\bin\\") || low.Contains("\\publish\\")) continue;
                count++;
                long t = File.GetLastWriteTimeUtc(f).Ticks;
                if (t > max) max = t;
            }
            return count + "|" + max;
        }

        /// <summary>True when a rebuild was started (the caller must exit at once: the script waits for this process to end).</summary>
        internal static bool TryRebuildAndRestart(string[] args)
        {
            try
            {
                if (args.Any(a => string.Equals(a, "--after-build", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--no-autobuild", StringComparison.OrdinalIgnoreCase))) return false;
                if (Environment.GetEnvironmentVariable("SNAPTER_NO_AUTOBUILD") == "1") return false;
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                string dir = Path.GetDirectoryName(exe);                     // ...\app\publish
                string root = Path.GetFullPath(Path.Combine(dir, "..", ".."));
                string proj = Path.Combine(root, "app", "Snapter.csproj");
                string script = Path.Combine(root, "tools", "AutoBuild.ps1");
                if (!Path.GetFileName(dir).Equals("publish", StringComparison.OrdinalIgnoreCase) || !File.Exists(proj) || !File.Exists(script)) return false;
                if (!Settings.Load().AutoRebuild) return false;

                string now = StampOf(root);
                string stampFile = Path.Combine(dir, "build.stamp");
                bool stale;
                if (File.Exists(stampFile)) stale = File.ReadAllText(stampFile).Trim() != now;
                else
                {
                    // no fingerprint yet (the program was built by hand): stale only when some source is newer than the exe
                    DateTime built = File.GetLastWriteTimeUtc(exe);
                    stale = Directory.EnumerateFiles(Path.Combine(root, "app"), "*.cs", SearchOption.AllDirectories)
                        .Any(f => !f.ToLowerInvariant().Contains("\\obj\\") && !f.ToLowerInvariant().Contains("\\bin\\") && !f.ToLowerInvariant().Contains("\\publish\\") && File.GetLastWriteTimeUtc(f) > built);
                    if (!stale) { try { File.WriteAllText(stampFile, now); } catch { } return false; }
                }
                if (!stale) return false;

                Log.Write("sources changed: rebuilding (" + now + ")");
                string argLine = string.Join("|", args);                    // forwarded to the new exe (for example --settings 3)
                string lang = "en";
                try { lang = I18n.ResolveCode(Settings.Load().Language); } catch { }   // the words of the small window follow the language of the program
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -Root \"" + root.TrimEnd('\\') + "\" -WaitPid " + Process.GetCurrentProcess().Id + " -Lang " + lang
                        + (args.Length > 0 ? " -ArgLine \"" + argLine.Replace("\"", "") + "\"" : ""),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = root
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("auto rebuild check failed: " + ex.Message);   // never block the start
                return false;
            }
        }
    }
}
