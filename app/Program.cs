using System;
using System.Threading;

namespace Snapter
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase))
                    return SelfTest.Run();
                if (string.Equals(args[i], "--beautify-sheet", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    // a picture with the ten looks of the beautiful screenshot (for a look at them): --beautify-sheet file.png [preset]
                    string preset = i + 2 < args.Length ? args[i + 2] : "ocean";
                    using (System.Drawing.Bitmap sample = SettingsWindow.BeautifySample())
                    using (System.Drawing.Bitmap sheet = new System.Drawing.Bitmap(6 * 330, 3 * 250))
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(sheet))
                    {
                        g.Clear(System.Drawing.Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        for (int k = 0; k < 18; k++)
                        {
                            // the first eleven: the built-in looks; the rest: random looks (--beautify-sheet file.png preset [light])
                            BeautifyLook rl = k < BeautifyStyles.Ids.Length ? null : BeautifyLook.Random(new Random(100 + k), "r" + k, new BeautifyRandomOptions { AllowDark = !(args.Length > i + 3 && args[i + 3] == "light"), Wild = 60 });
                            string sid = rl == null ? BeautifyStyles.Ids[k] : "r" + k;
                            BeautifyOptions o = new BeautifyOptions { Enabled = true, Style = sid, Preset = preset };
                            if (rl != null) o.Looks = new System.Collections.Generic.Dictionary<string, BeautifyLook> { { sid, rl } };
                            using (System.Drawing.Bitmap d = Beautifier.Apply(sample, o))
                            {
                                double s = Math.Min(310.0 / d.Width, 210.0 / d.Height);
                                int w = (int)(d.Width * s), h = (int)(d.Height * s);
                                g.DrawImage(d, (k % 6) * 330 + (330 - w) / 2, (k / 6) * 250 + 4, w, h);
                            }
                            g.DrawString(sid + (rl != null ? " " + rl.Type : ""), new System.Drawing.Font("Segoe UI", 10), System.Drawing.Brushes.Black, (k % 6) * 330 + 8, (k / 6) * 250 + 224);
                        }
                        sheet.Save(args[i + 1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    return 0;
                }
                if (string.Equals(args[i], "--icon-sheet", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    IconFactory.WriteSheet(args[i + 1]);   // a picture with all tray icons (for a look at them)
                    return 0;
                }
                if (string.Equals(args[i], "--make-icon", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    // --make-icon file.ico         : the default icon (assets\Snapter.ico)
                    // --make-icon file.ico --tray  : the tray icon chosen in the settings (shape, colors) - for a shortcut
                    bool tray = args.Length > i + 2 && string.Equals(args[i + 2], "--tray", StringComparison.OrdinalIgnoreCase);
                    if (tray) IconFactory.WriteIcoFor(args[i + 1], Settings.Load());
                    else IconFactory.WriteIco(args[i + 1]);
                    return 0;
                }
            }

            bool created;
            string suffix = Environment.GetEnvironmentVariable("SNAPTER_INSTANCE") ?? "";
            using (Mutex mutex = new Mutex(true, "Local\\Snapter.App" + suffix, out created))
            {
                if (!created)
                {
                    // "--restart" (Start SNAPter.bat): ask the running copy to exit and take over; otherwise a second start is ignored
                    bool restart = Array.Exists(args, a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));
                    if (restart && AppController.AskRunningToExit(mutex, suffix)) Log.Write("restart: the old copy exited, starting the new one");
                    else
                    {
                        Log.Write("second start ignored (already running)");
                        return 0;
                    }
                }
                if (AutoUpdate.TryRebuildAndRestart(args)) return 0;    // the sources changed: a script rebuilds and starts the new exe (AutoUpdate.cs)

                System.Windows.Application app = new System.Windows.Application();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                AppController controller = new AppController(app);
                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("UNHANDLED: " + e.Exception);
                    e.Handled = true;
                };
                try
                {
                    controller.Start();
                    controller.ListenForRestart(suffix);
                    for (int i = 0; i < args.Length; i++)
                        if (string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase))
                        {
                            int page = 0; // "--settings 3" opens the window on the fourth page
                            if (i + 1 < args.Length) int.TryParse(args[i + 1], out page);
                            controller.OpenSettings(page);
                        }
                }
                catch (Exception ex)
                {
                    Log.Write("START FAILED: " + ex);
                    return 1;
                }
                app.Run();
                controller.Dispose();
                return 0;
            }
        }
    }
}
