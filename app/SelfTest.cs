using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;

namespace Snapter
{
    /// <summary>Snapter.exe --selftest : checks config, themes, languages, hotkey parser, capture, saving,
    /// the clipboard and the editor on the region overlay (shown for about a second).
    /// Writes logs\selftest.txt. The editor is driven through the same handlers as the mouse and keyboard.</summary>
    internal static partial class SelfTest
    {
        private static List<string> _errors;
        private static StringBuilder _sb;

        private static void Say(string s) { _sb.AppendLine(s); }
        private static void Check(bool ok, string what)
        {
            Say("  [" + (ok ? "ok" : "FAIL") + "] " + what);
            if (!ok) _errors.Add(what);
        }

        public static int Run()
        {
            _sb = new StringBuilder();
            _errors = new List<string>();
            RegionOverlay.NoPersist = true;
            SettingsWindow.NoPersist = true; // the test must not overwrite the user's saved presets
            Environment.SetEnvironmentVariable("SNAPTER_USER_SETTINGS", Path.Combine(Path.GetTempPath(), "Snapter_selftest_no_user_settings.json"));
            try { if (File.Exists(Paths.UserSettings)) File.Delete(Paths.UserSettings); } catch { }

            try
            {
                Say("root: " + Paths.Root);

                // settings
                Settings st = Settings.Load();
                Say("settings: language=" + st.Language + " mode=" + st.ThemeMode + " theme=" + st.AccentTheme + " hotkeys=" + st.Hotkeys.Count + " scope=" + st.FullscreenScope);
                Say("editor: enabled=" + st.EditorEnabled + " minimal=" + st.MinimalUi + " toolbar=" + st.ToolbarPosition + " properties=" + st.PropertiesPosition + " color=" + st.DefaultColor + " thickness=" + st.DefaultThickness + " text=" + st.DefaultTextSize + " arrow=" + st.ToolEnabled("arrow") + " text=" + st.ToolEnabled("text"));
                Check(st.Hotkeys.Count > 0, "settings contain hotkeys");

                // hotkey parser
                foreach (HotkeyDef d in st.Hotkeys)
                {
                    uint m, vk;
                    bool ok = HotkeyManager.TryParse(d.Keys, out m, out vk);
                    Check(ok, "hotkey " + d.Keys + " parses (mods=" + m + " vk=0x" + vk.ToString("X") + ")");
                }
                uint pm, pv;
                Check(HotkeyManager.TryParse("Ctrl+PrintScreen", out pm, out pv) && pv == 0x2C, "PrintScreen parses");

                // languages
                List<LanguageInfo> langs = I18n.Languages();
                Dictionary<string, string> en = I18n.ReadFlat(Path.Combine(Paths.I18n, "en.json"));
                foreach (LanguageInfo l in langs)
                {
                    Dictionary<string, string> d = I18n.ReadFlat(l.Path);
                    int missing = 0;
                    foreach (string k in en.Keys) if (!d.ContainsKey(k)) missing++;
                    Check(missing == 0, "language " + l.Code + " (" + l.Native + "): keys=" + d.Count + " missing=" + missing);
                }

                // themes
                foreach (string id in Theme.Ids)
                    foreach (string mode in new string[] { "light", "dark" })
                    {
                        Theme t = Theme.Load(id, mode);
                        Say("theme " + id + "/" + mode + ": accent=" + t.Accent + " bg=" + t.Bg + " radius=" + t.RadiusMd);
                    }

                // capture
                Rectangle vs = ScreenCapture.VirtualScreen();
                Say("virtual screen: " + vs + " monitors=" + System.Windows.Forms.Screen.AllScreens.Length);
                foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
                    Say("  monitor " + s.DeviceName + " bounds=" + s.Bounds + (s.Primary ? " primary" : ""));

                using (Bitmap shot = ScreenCapture.Capture(vs))
                {
                    Say("capture: " + shot.Width + "x" + shot.Height + " format=" + shot.PixelFormat);
                    Check(shot.Width == vs.Width && shot.Height == vs.Height, "capture size equals the virtual screen");

                    long sum = 0; int n = 0;
                    for (int y = 0; y < shot.Height; y += Math.Max(1, shot.Height / 40))
                        for (int x = 0; x < shot.Width; x += Math.Max(1, shot.Width / 40)) { Color c = shot.GetPixel(x, y); sum += c.R + c.G + c.B; n++; }
                    Say("capture average brightness: " + (sum / (3.0 * n)).ToString("F1") + " of 255");
                    Check(sum > 0, "capture is not completely black");

                    // saving into a temp folder
                    Settings tmp = Settings.Load();
                    string dir = Path.Combine(Path.GetTempPath(), "Snapter_selftest_" + Guid.NewGuid().ToString("N"));
                    tmp.SaveFolder = dir;
                    string path = ImageOutput.Save(shot, tmp);
                    using (Bitmap back = new Bitmap(path))
                        Check(back.Width == shot.Width && back.Height == shot.Height, "saved PNG has the same size (" + Path.GetFileName(path) + ", " + new FileInfo(path).Length + " bytes)");
                    string path2 = ImageOutput.Save(shot, tmp);
                    Check(path != path2, "second save gets a new name (" + Path.GetFileName(path2) + ")");

                    // formats: png, jpg (quality), webp (lossy and lossless), bmp, separate format for the whole screen
                    using (Bitmap small = shot.Clone(new Rectangle(0, 0, Math.Min(600, shot.Width), Math.Min(400, shot.Height)), shot.PixelFormat))
                    {
                        foreach (string f in ImageOutput.Formats)
                        {
                            string fp = Path.Combine(dir, "fmt_test." + f);
                            ImageOutput.SaveTo(small, fp, tmp);
                            byte[] head = new byte[12];
                            using (FileStream fs = File.OpenRead(fp)) fs.Read(head, 0, 12);
                            bool ok = f == "png" ? head[0] == 0x89 && head[1] == 0x50
                                    : f == "jpg" ? head[0] == 0xFF && head[1] == 0xD8
                                    : f == "bmp" ? head[0] == 0x42 && head[1] == 0x4D
                                    : head[0] == 'R' && head[1] == 'I' && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P';
                            Check(ok, "format " + f + " writes a valid file (" + new FileInfo(fp).Length + " bytes)");
                        }
                        tmp.JpgQuality = 30; string jl = Path.Combine(dir, "q30.jpg"); ImageOutput.SaveTo(small, jl, tmp);
                        tmp.JpgQuality = 95; string jh = Path.Combine(dir, "q95.jpg"); ImageOutput.SaveTo(small, jh, tmp);
                        Check(new FileInfo(jl).Length < new FileInfo(jh).Length, "JPG quality 30 gives a smaller file than 95");
                        tmp.WebpLossless = false; tmp.WebpQuality = 40; string wl = Path.Combine(dir, "w40.webp"); ImageOutput.SaveTo(small, wl, tmp);
                        tmp.WebpLossless = true; string wlos = Path.Combine(dir, "wl.webp"); ImageOutput.SaveTo(small, wlos, tmp);
                        Check(new FileInfo(wl).Length != new FileInfo(wlos).Length, "WebP lossless differs from lossy (" + new FileInfo(wl).Length + " / " + new FileInfo(wlos).Length + " bytes)");
                        tmp.Format = "png"; tmp.FullscreenFormat = "webp";
                        Check(ImageOutput.FormatFor(tmp, true) == "webp" && ImageOutput.FormatFor(tmp, false) == "png", "whole-screen format can differ from the region format");
                        tmp.FullscreenFormat = "same";
                        Check(ImageOutput.FormatFor(tmp, true) == "png" && ImageOutput.NormalizeFormat("JPEG") == "jpg", "format names are normalized");
                    }
                    Directory.Delete(dir, true);

                    EditorTest(shot, vs, st);
                }

                ClipboardTest();
                try { ExtrasTest(st); } catch (Exception ex) { _errors.Add("extras test: " + ex); }
                try { RecordingTest(st); } catch (Exception ex) { _errors.Add("recording test: " + ex); }
                try { CloudTest(); } catch (Exception ex) { _errors.Add("cloud test: " + ex); }

                // hotkey registration (a harmless combination, released at once)
                using (HotkeyManager hm = new HotkeyManager())
                {
                    string err;
                    bool ok = hm.Register("selftest", "Ctrl+Shift+Alt+F12", out err);
                    Check(ok, "hotkey register test Ctrl+Shift+Alt+F12" + (ok ? "" : " (" + err + ")"));
                }
            }
            catch (Exception ex)
            {
                _errors.Add("exception: " + ex);
            }

            _sb.AppendLine(_errors.Count == 0 ? "SELFTEST: OK" : "SELFTEST: FAILED");
            foreach (string e in _errors) _sb.AppendLine("  - " + e);
            try
            {
                Directory.CreateDirectory(Paths.Logs);
                File.WriteAllText(Path.Combine(Paths.Logs, "selftest.txt"), _sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
            return _errors.Count == 0 ? 0 : 1;
        }


        // ------------------------------------------------------------------ GIF, scrolling screenshot, FFmpeg
        private static Bitmap Solid(int w, int h, Color c)
        {
            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(c);
            return b;
        }

        private static void RecordingTest(Settings st)
        {
            Say("recording test:");
            string dir = Path.Combine(Path.GetTempPath(), "snapter_rec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                // --- GIF encoder: frames, colours, merging of identical frames
                List<Bitmap> fr = new List<Bitmap> { Solid(120, 80, Color.FromArgb(220, 30, 30)), Solid(120, 80, Color.FromArgb(220, 30, 30)), Solid(120, 80, Color.FromArgb(30, 200, 60)) };
                using (Graphics g = Graphics.FromImage(fr[2])) g.FillRectangle(Brushes.Blue, 10, 10, 40, 30);
                string gif1 = Path.Combine(dir, "a.gif"), gif2 = Path.Combine(dir, "b.gif");
                GifEncoder.Save(gif1, fr, new List<int> { 100, 100, 100 }, 256, true, 0, false);
                GifEncoder.Save(gif2, fr, new List<int> { 100, 100, 100 }, 256, true, 0, true);
                byte[] head = new byte[6]; using (FileStream fs = File.OpenRead(gif1)) fs.Read(head, 0, 6);
                Check(Encoding.ASCII.GetString(head) == "GIF89a", "the GIF file starts with GIF89a");
                using (Image im = Image.FromFile(gif1))
                {
                    int n = im.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    Check(n == 3, "the GIF holds 3 frames (got " + n + ")");
                    im.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, 0);
                    Color c0 = ((Bitmap)im).GetPixel(100, 70);
                    Check(c0.R > 190 && c0.G < 70 && c0.B < 70, "GIF frame 1 keeps the red colour (" + c0.R + "," + c0.G + "," + c0.B + ")");
                    im.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, 2);
                    Color c2 = ((Bitmap)im).GetPixel(20, 20), c3 = ((Bitmap)im).GetPixel(100, 70);
                    Check(c2.B > 180 && c2.R < 70 && c3.G > 160 && c3.R < 70, "GIF frame 3 has the blue square on green (" + c2.R + "," + c2.G + "," + c2.B + " / " + c3.R + "," + c3.G + "," + c3.B + ")");
                }
                using (Image im2 = Image.FromFile(gif2))
                {
                    int n2 = im2.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    Check(n2 == 2, "identical frames are merged into one (got " + n2 + " frames)");
                }
                foreach (Bitmap b in fr) b.Dispose();

                // --- scrolling screenshot: rows, shift, stitching
                Random rnd = new Random(7);
                Bitmap page = new Bitmap(300, 1200, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < 1200; y++)
                {
                    int band = rnd.Next(256), band2 = rnd.Next(256);
                    for (int x = 0; x < 300; x++) page.SetPixel(x, y, Color.FromArgb((band + x * 3) & 255, (band2 + x) & 255, (y * 7) & 255));
                }
                int[] tops = { 0, 250, 500, 800 };
                List<Bitmap> pieces = new List<Bitmap>(); List<int> shifts = new List<int>();
                foreach (int t in tops) pieces.Add(page.Clone(new Rectangle(0, t, 300, 400), System.Drawing.Imaging.PixelFormat.Format32bppArgb));
                bool shiftsOk = true;
                for (int i = 0; i + 1 < pieces.Count; i++)
                {
                    int s = ScrollCapture.FindShift(ScrollCapture.RowHashes(pieces[i]), ScrollCapture.RowHashes(pieces[i + 1]), 0, 0, 3);
                    shifts.Add(s);
                    if (s != tops[i + 1] - tops[i]) shiftsOk = false;
                }
                Check(shiftsOk, "the scroll distance between pieces is found (" + string.Join(",", shifts) + ")");
                Check(ScrollCapture.FindShift(ScrollCapture.RowHashes(pieces[0]), ScrollCapture.RowHashes(pieces[0]), 0, 0, 3) == 0, "two equal pieces mean the page did not move (shift 0)");
                using (Bitmap whole = ScrollCapture.Stitch(pieces, shifts, 0, 0))
                {
                    bool same = whole.Width == 300 && whole.Height == 1200;
                    for (int y = 0; same && y < 1200; y += 37) for (int x = 0; x < 300; x += 41) if (whole.GetPixel(x, y).ToArgb() != page.GetPixel(x, y).ToArgb()) same = false;
                    Check(same, "the joined picture equals the original page (" + whole.Width + "x" + whole.Height + ")");
                }
                // a fixed header (skip_top) does not spoil the search
                List<Bitmap> withHead = new List<Bitmap>();
                foreach (int t in tops)
                {
                    Bitmap b = page.Clone(new Rectangle(0, t, 300, 400), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(b)) { g.FillRectangle(Brushes.Navy, 0, 0, 300, 50); g.DrawString("HEADER", SystemFonts.DefaultFont, Brushes.White, 5, 5); }
                    withHead.Add(b);
                }
                int sh = ScrollCapture.FindShift(ScrollCapture.RowHashes(withHead[0]), ScrollCapture.RowHashes(withHead[1]), 50, 0, 3);
                Check(sh == 250, "with a fixed 50 px header the shift is still found when skip_top is set (" + sh + ")");
                foreach (Bitmap b in pieces) b.Dispose();
                foreach (Bitmap b in withHead) b.Dispose();
                page.Dispose();

                // --- the volume of options: percent strings
                double[] pc = RegionOverlay.TestParsePcts("100, 50, 30");
                Check(pc[0] == 100 && pc[1] == 50 && pc[2] == 30, "quick sizes '100, 50, 30' are parsed");
                Check(RegionOverlay.TestParsePcts("bad")[0] == -1, "bad quick sizes fall back to the defaults");

                // --- the real recorders on a small piece of the screen
                Rectangle pr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                Rectangle area = new Rectangle(pr.X + 20, pr.Y + 20, 321, 241);
                Settings gs = Settings.WithOverrides(new System.Text.Json.Nodes.JsonObject
                {
                    ["record"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["gif"] = new System.Text.Json.Nodes.JsonObject { ["fps"] = 10, ["colors"] = 64, ["scale_pct"] = 50 },
                        ["video"] = new System.Text.Json.Nodes.JsonObject { ["fps"] = 15, ["rate_mode"] = "bitrate", ["bitrate_kbps"] = 2500, ["scale_pct"] = 100 }
                    }
                });
                Say("  overrides: gif scale=" + gs.GifScale + " colors=" + gs.GifColors + " video fps=" + gs.VideoFps + " mode=" + gs.VideoRateMode + " kbps=" + gs.VideoBitrateKbps);
                GifRecorder rec = new GifRecorder(area, gs);
                rec.Start();
                System.Threading.Thread.Sleep(1500);
                rec.Stop();
                string recGif = Path.Combine(dir, "screen.gif");
                int nframes = rec.Finish(recGif);
                Check(nframes >= 3 && File.Exists(recGif) && new FileInfo(recGif).Length > 500, "a real GIF recording of the screen works (" + nframes + " frames, " + (File.Exists(recGif) ? new FileInfo(recGif).Length : 0) + " bytes)");
                if (File.Exists(recGif))
                    using (Image im = Image.FromFile(recGif)) Check(im.Width == 160 && im.Height == 120, "GIF scale 50% gives half the size (" + im.Width + "x" + im.Height + ")");

                string args = FfmpegRecorder.BuildArgs(gs, area, "x.mp4");
                Say("  args: " + args);
                Check(args.Contains("-video_size 320x240") && args.Contains("-b:v 2500k") && args.Contains("-framerate 15") && args.Contains("-offset_x " + area.X), "the FFmpeg command line has the even size, the bitrate, the frame rate and the offset");
                Check(FfmpegRecorder.BuildArgs(st, area, "x.mp4").Contains("-crf " + st.VideoCrf), "the CRF mode puts -crf in the command line");
                string ffExe = FfmpegRecorder.Find(st);
                if (ffExe == null) Say("  (FFmpeg not found: the real video test is skipped)");
                else
                {
                    FfmpegRecorder ff = new FfmpegRecorder();
                    string mp4 = Path.Combine(dir, "screen.mp4"), err;
                    if (!ff.Start(gs, area, mp4, out err)) _errors.Add("ffmpeg did not start: " + err);
                    else
                    {
                        System.Threading.Thread.Sleep(2000);
                        bool ok = ff.Stop(out err);
                        bool box = false;
                        if (File.Exists(mp4)) { byte[] hb = new byte[12]; using (FileStream fs = File.OpenRead(mp4)) fs.Read(hb, 0, 12); box = Encoding.ASCII.GetString(hb, 4, 4) == "ftyp"; }
                        Check(ok && box, "a real MP4 recording through FFmpeg works (" + (File.Exists(mp4) ? new FileInfo(mp4).Length : 0) + " bytes) " + (ok ? "" : err));
                    }
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ------------------------------------------------------------------ icons, themes, formats of names, presets, step labels
        private static void ExtrasTest(Settings st)
        {
            Say("extras test:");
            // ten tray icons that really differ from each other
            Check(IconFactory.Kinds.Length == 20 && Array.IndexOf(IconFactory.Kinds, "quill") >= 0, "twenty tray icons, one of them is the quill");
            HashSet<string> sigs = new HashSet<string>();
            foreach (string k in IconFactory.Kinds)
                using (Bitmap b = IconFactory.Draw(k, 32, Color.Black, IconFactory.Accent))
                {
                    long h = 17;
                    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) h = h * 31 + b.GetPixel(x, y).ToArgb();
                    sigs.Add(h.ToString());
                    int opaque = 0; for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if (b.GetPixel(x, y).A > 0) opaque++;
                    if (opaque < 40) _errors.Add("icon " + k + " is almost empty");
                }
            Check(sigs.Count == 20, "all twenty icons are different pictures (" + sigs.Count + ")");
            Check(st.TrayIcon == "viewfinder" && st.TrayIconFollowTaskbar, "default tray icon is the viewfinder and it follows the taskbar");

            // defaults of the new settings
            Check(st.PresetCount == 3 && !st.SharedColor && st.EraserRadius == 6 && st.ButtonSize == "medium" && !st.PanelsDraggable && st.NotifyEnabled && st.NotifySeconds == 3 && st.RememberTool, "defaults of the new settings are loaded");
            Check(st.EditorKeys.ContainsKey("arrow") && st.EditorKeys["arrow"] == "A" && st.EditorKeys["preset9"] == "9", "editor keys are loaded from the defaults");
            Check(st.TextFont == "Segoe UI" && st.TextBold && st.PlateOpacity == 80 && st.StepStyle == "number" && st.ArrowHead == "triangle", "text and step defaults are loaded");

            // presets: more than three, opacity is remembered
            Settings five = Settings.Load(); five.PresetCount = 5;
            StyleStore store = StyleStore.Create(five);
            Check(store.PresetCount == 5, "five presets per tool can be made");
            store.Current(EditorTool.Arrow).Opacity = 40;
            five.SavePresets(store.ToJson());
            Settings back = Settings.Load(); back.PresetCount = 5;
            Check(StyleStore.Create(back).Current(EditorTool.Arrow).Opacity == 40, "preset opacity survives saving and loading");

            // labels of numbered steps
            StepAnnotation.LabelStyle = "upper"; StepAnnotation s27 = new StepAnnotation { Number = 27 };
            string up = s27.Label();
            StepAnnotation.LabelStyle = "roman"; string ro = new StepAnnotation { Number = 14 }.Label();
            StepAnnotation.LabelStyle = "lower"; string lo = new StepAnnotation { Number = 3 }.Label();
            StepAnnotation.LabelStyle = "number";
            Check(up == "AA" && ro == "XIV" && lo == "c", "step labels: 27 = AA, 14 = XIV, 3 = c (" + up + ", " + ro + ", " + lo + ")");

            // arrow heads
            foreach (string hs in new string[] { "triangle", "open", "dot" })
            {
                ArrowAnnotation.HeadStyle = hs;
                ArrowAnnotation a = new ArrowAnnotation { A = new System.Windows.Point(10, 10), B = new System.Windows.Point(110, 60), Head2 = true };
                Check(a.BuildVisual() != null, "arrow head '" + hs + "' builds");
            }
            ArrowAnnotation.HeadStyle = "triangle";

            // names: window title, program, sub-folders
            string dir = Path.Combine(Path.GetTempPath(), "Snapter_names_" + Guid.NewGuid().ToString("N"));
            ImageOutput.WindowTitle = "Notepad"; ImageOutput.ProcessName = "notepad";
            Check(ImageOutput.ExpandTemplate("a_{window}_{process}", DateTime.Now) == "a_Notepad_notepad", "name tokens {window} and {process}");
            Settings ns = Settings.Load();
            ns.SaveFolder = dir; ns.FileNameTemplate = "shot_{window}"; ns.SubfolderTemplate = "{process}\\{yyyy}";
            using (Bitmap tiny = new Bitmap(20, 20))
            {
                string p = ImageOutput.Save(tiny, ns);
                Check(File.Exists(p) && p.Contains("notepad") && Path.GetFileName(p).StartsWith("shot_Notepad"), "sub-folder and file name from the active window (" + p.Substring(dir.Length) + ")");
            }
            try { Directory.Delete(dir, true); } catch { }
            ImageOutput.WindowTitle = ""; ImageOutput.ProcessName = "";

            // the screen with the mouse pointer
            using (Bitmap withCursor = ScreenCapture.Capture(new Rectangle(0, 0, 200, 100), true)) Check(withCursor.Width == 200, "capture with the pointer works");

            // per-preset font, alignment and arrow head survive saving; the objects take them
            Settings fnt = Settings.Load();
            StyleStore fs = StyleStore.Create(fnt);
            ToolStyle tx0 = fs.Current(EditorTool.Text); tx0.FontName = "Verdana"; tx0.Bold = 0; tx0.Italic = 1; tx0.Align = "center";
            ToolStyle ar0 = fs.Current(EditorTool.Arrow); ar0.HeadStyle = "open"; ar0.HeadScale = 2;
            fnt.SavePresets(fs.ToJson());
            StyleStore fs2 = StyleStore.Create(Settings.Load());
            Check(fs2.Current(EditorTool.Text).FontName == "Verdana" && fs2.Current(EditorTool.Text).Bold == 0 && fs2.Current(EditorTool.Text).Italic == 1 && fs2.Current(EditorTool.Text).Align == "center", "preset font, bold, italic and alignment survive saving");
            Check(fs2.Current(EditorTool.Arrow).HeadStyle == "open" && Math.Abs(fs2.Current(EditorTool.Arrow).HeadScale - 2) < 0.001, "preset arrow head and its size survive saving");
            TextAnnotation vt = new TextAnnotation { Text = "Verdana", Font = "Verdana", BoldMode = 0, ItalicMode = 1, AlignMode = "center", Pos = new System.Windows.Point(5, 5) };
            Check(vt.BuildVisual() != null && vt.FontFamilyName == "Verdana" && vt.Alignment() == System.Windows.TextAlignment.Center, "a text takes the font and alignment of its preset");
            ArrowAnnotation dotA = new ArrowAnnotation { A = new System.Windows.Point(0, 0), B = new System.Windows.Point(80, 20), HeadKind = "dot", HeadScale = 2 };
            Check(dotA.BuildVisual() != null && dotA.HeadLength > 0, "an arrow takes the head of its preset");
            // a hollow head (an outline of the line's thickness) for the triangle and the dot
            foreach (string hk in new string[] { "triangle", "dot" })
            {
                ArrowAnnotation solid = new ArrowAnnotation { A = new System.Windows.Point(0, 0), B = new System.Windows.Point(120, 0), HeadKind = hk, Thickness = 4 };
                ArrowAnnotation hollow = new ArrowAnnotation { A = new System.Windows.Point(0, 0), B = new System.Windows.Point(120, 0), HeadKind = hk, Thickness = 4, HeadHollow = true };
                System.Windows.Shapes.Path ps = solid.BuildVisual() as System.Windows.Shapes.Path, ph = hollow.BuildVisual() as System.Windows.Shapes.Path;
                Check(ps != null && ph != null && ps.Fill != null && ph.Fill == null && Math.Abs(ph.StrokeThickness - 4) < 0.001, "a " + hk + " head can be an outline with the same thickness as the line");
                Check(hollow.Clone() is ArrowAnnotation && ((ArrowAnnotation)hollow.Clone()).HeadHollow, "the outline head is kept when the arrow is copied");
            }
            fs.Current(EditorTool.Arrow).HeadHollow = true;
            fnt.SavePresets(fs.ToJson());
            Check(StyleStore.Create(Settings.Load()).Current(EditorTool.Arrow).HeadHollow, "the outline head is saved with the preset");
            try { File.Delete(Paths.UserSettings); } catch { }

            // ten colors for the tray icon, five inks
            Check(IconFactory.AccentKeys.Length == 10 && IconFactory.InkKeys.Length >= 4, "ten accent colors and several inks for the tray icon");
            HashSet<int> accents = new HashSet<int>(); foreach (string k in IconFactory.AccentKeys) accents.Add(IconFactory.AccentColor(k).ToArgb());
            Check(accents.Count == 10, "the ten accent colors are different");
            using (Bitmap oi = IconFactory.Draw("camera", 32, IconFactory.InkColor("black"), IconFactory.AccentColor("orange"))) Check(oi.Width == 32, "an icon is drawn in the chosen colors");

            // monitors and all settings as one export
            Rectangle m1 = ScreenCapture.MonitorNumber(1), m2 = ScreenCapture.MonitorNumber(2), sec = ScreenCapture.SecondaryMonitor();
            Check(m1.Width > 0 && m2.Width > 0 && sec.Width > 0, "monitor 1, monitor 2 and the additional monitor have a size (" + m1 + " " + m2 + ")");
            string exported = Settings.ExportJson();
            System.Text.Json.Nodes.JsonObject ex = System.Text.Json.Nodes.JsonNode.Parse(exported) as System.Text.Json.Nodes.JsonObject;
            Check(ex != null && ex["modifiers"] != null && ex["editor_keys"] != null && ex["layout"] != null && ex["upload"] != null && ex["output"] != null && ex["hotkeys"] != null, "export holds every section of the settings");

            // the settings window remembers its size and place
            Settings wb = Settings.Load(); wb.SettingsLeft = 120; wb.SettingsTop = 90; wb.SettingsWidth = 777; wb.SettingsHeight = 555;
            AppController ac2 = new AppController(null); ac2.TestInit();
            SettingsWindow bw = new SettingsWindow(ac2, 0, wb);
            Check(Math.Abs(bw.Width - 777) < 0.5 && Math.Abs(bw.Height - 555) < 0.5 && Math.Abs(bw.Left - 120) < 0.5 && Math.Abs(bw.Top - 90) < 0.5, "the settings window opens with the remembered size and place");

            // your own theme
            string tid = "selftest-theme";
            string tfile = Path.Combine(Paths.Themes, tid + ".json");
            try
            {
                Theme.Create(tid, "Self test", System.Windows.Media.Color.FromRgb(200, 40, 120));
                Check(Array.IndexOf(Theme.Ids, tid) >= 0 && Theme.DisplayName(tid) == "Self test", "a created theme appears in the list with its name");
                Theme loaded = Theme.Load(tid, "light");
                Check(loaded.Accent.R == 200 && loaded.Accent.G == 40, "the accent of the created theme is used");
            }
            finally { try { File.Delete(tfile); } catch { } }
            Check(Array.IndexOf(Theme.Ids, tid) < 0, "the test theme is removed again");
            try { File.Delete(Paths.UserSettings); } catch { }
            Say("  ok");
        }

        // ------------------------------------------------------------------ cloud (a fake Google on the local machine)
        private sealed class FakeGoogle : IDisposable
        {
            private readonly System.Net.Sockets.TcpListener _l;
            public readonly int Port;
            public readonly List<string> Requests = new List<string>();
            public string LastUploadBody = "";
            private volatile bool _stop;

            public FakeGoogle()
            {
                _l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                _l.Start();
                Port = ((System.Net.IPEndPoint)_l.LocalEndpoint).Port;
                System.Threading.Tasks.Task.Run(Loop);
            }

            private async System.Threading.Tasks.Task Loop()
            {
                while (!_stop)
                {
                    System.Net.Sockets.TcpClient c;
                    try { c = await _l.AcceptTcpClientAsync(); } catch { break; }
                    System.Threading.Tasks.Task.Run(() => Handle(c));
                }
            }

            private async System.Threading.Tasks.Task Handle(System.Net.Sockets.TcpClient c)
            {
                using (c)
                {
                    try
                    {
                        System.Net.Sockets.NetworkStream ns = c.GetStream();
                        MemoryStream all = new MemoryStream();
                        byte[] buf = new byte[16384];
                        int headEnd = -1, need = -1;
                        while (true)
                        {
                            int n = await ns.ReadAsync(buf, 0, buf.Length);
                            if (n <= 0) break;
                            all.Write(buf, 0, n);
                            string txt = Encoding.Latin1.GetString(all.ToArray());
                            if (headEnd < 0) { headEnd = txt.IndexOf("\r\n\r\n", StringComparison.Ordinal); if (headEnd >= 0) { headEnd += 4; int ci = txt.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase); need = ci >= 0 ? int.Parse(txt.Substring(ci + 15, txt.IndexOf('\r', ci) - ci - 15).Trim()) : 0; } }
                            if (headEnd >= 0 && all.Length >= headEnd + need) break;
                        }
                        string full = Encoding.Latin1.GetString(all.ToArray());
                        string head = full.Substring(0, headEnd), body = full.Substring(headEnd);
                        string first = head.Split('\n')[0].Trim();
                        string[] fp = first.Split(' ');
                        string method = fp[0], path = fp[1];
                        bool auth = head.Contains("Authorization: Bearer AT");
                        lock (Requests) Requests.Add(method + " " + path.Split('?')[0] + (auth ? " [auth]" : ""));
                        string json = "{}"; int code = 200;
                        if (method == "POST" && path.StartsWith("/token"))
                        {
                            if (body.Contains("grant_type=authorization_code") && body.Contains("code=CODE123") && body.Contains("code_verifier="))
                                json = "{\"access_token\":\"AT1\",\"refresh_token\":\"RT1\",\"expires_in\":3600}";
                            else if (body.Contains("grant_type=refresh_token") && body.Contains("refresh_token=RT1"))
                                json = "{\"access_token\":\"AT2\",\"expires_in\":3600}";
                            else { code = 400; json = "{\"error\":\"invalid_grant\"}"; }
                        }
                        else if (!auth) { code = 401; json = "{\"error\":\"unauthorized\"}"; }
                        else if (method == "GET" && path.StartsWith("/drive/v3/files")) json = "{\"files\":[]}";
                        else if (method == "POST" && path.StartsWith("/drive/v3/files/") && path.Contains("/permissions")) json = body.Contains("\"anyone\"") ? "{\"id\":\"perm1\"}" : "{}";
                        else if (method == "POST" && path.StartsWith("/drive/v3/files")) json = "{\"id\":\"FOLDER1\"}";
                        else if (method == "POST" && path.StartsWith("/upload/drive/v3/files"))
                        {
                            LastUploadBody = body;
                            json = "{\"id\":\"FILE1\",\"webViewLink\":\"https://drive.google.com/file/d/FILE1/view\"}";
                        }
                        byte[] bb = Encoding.UTF8.GetBytes(json);
                        byte[] hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " X\r\nContent-Type: application/json\r\nContent-Length: " + bb.Length + "\r\nConnection: close\r\n\r\n");
                        await ns.WriteAsync(hb, 0, hb.Length);
                        await ns.WriteAsync(bb, 0, bb.Length);
                    }
                    catch { }
                }
            }

            public void Dispose() { _stop = true; try { _l.Stop(); } catch { } }
        }

        private static void CloudTest()
        {
            Say("cloud test (fake Google on this computer):");
            // PKCE: the example from RFC 7636, appendix B
            Check(Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "PKCE challenge matches the RFC 7636 example");

            string dir = Path.Combine(Path.GetTempPath(), "Snapter_cloud_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string oldAuth = GDriveClient.AuthUrl, oldTok = GDriveClient.TokenUrl, oldApi = GDriveClient.ApiUrl, oldUp = GDriveClient.UploadUrl, oldFile = GDriveClient.TokenFile;
            using (FakeGoogle g = new FakeGoogle())
            {
                string root = "http://127.0.0.1:" + g.Port;
                GDriveClient.AuthUrl = root + "/auth"; GDriveClient.TokenUrl = root + "/token";
                GDriveClient.ApiUrl = root + "/drive/v3"; GDriveClient.UploadUrl = root + "/upload/drive/v3";
                GDriveClient.TokenFile = Path.Combine(dir, "gdrive.token");
                try
                {
                    Settings s = Settings.Load();
                    s.UploadTarget = "gdrive"; s.GdClientId = "cid"; s.GdClientSecret = "sec"; s.GdFolderName = "Snapter"; s.GdFolderId = ""; s.GdPublic = true;
                    GDriveClient gd = new GDriveClient(s);
                    Check(!gd.SignedIn, "before sign-in the program is not signed in");

                    string seenUrl = null;
                    gd.SignInAsync(url =>
                    {
                        seenUrl = url;
                        // the "browser": Google would send the user back to the local address with the code
                        Uri u = new Uri(url);
                        System.Collections.Specialized.NameValueCollection q = System.Web.HttpUtility.ParseQueryString(u.Query);
                        string back = q["redirect_uri"] + "?code=CODE123&state=" + q["state"];
                        System.Threading.Tasks.Task.Run(async () => { await System.Threading.Tasks.Task.Delay(200); using (System.Net.Http.HttpClient h = new System.Net.Http.HttpClient()) await h.GetStringAsync(back); });
                    }, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                    Check(seenUrl != null && seenUrl.Contains("code_challenge_method=S256") && seenUrl.Contains("scope=") && seenUrl.Contains("drive.file") && seenUrl.Contains("client_id=cid"), "sign-in address has PKCE, scope drive.file and the client id");
                    Check(gd.SignedIn, "after sign-in the refresh token is stored");
                    byte[] raw = File.ReadAllBytes(GDriveClient.TokenFile);
                    Check(Encoding.UTF8.GetString(raw).IndexOf("RT1", StringComparison.Ordinal) < 0, "the stored token file is encrypted (no plain text)");

                    byte[] img = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
                    UploadResult r = UploadService.UploadAsync(s, img, "test.png", "image/png").GetAwaiter().GetResult();
                    Check(r.Ok && r.Link == "https://drive.google.com/file/d/FILE1/view", "upload returns the link (" + (r.Ok ? r.Link : r.Message) + ")");
                    Check(g.LastUploadBody.Contains("\"parents\":[\"FOLDER1\"]") && g.LastUploadBody.Contains("test.png") && g.LastUploadBody.Contains("image/png"), "upload goes into the program's folder with name and type");
                    string reqs; lock (g.Requests) reqs = string.Join(" | ", g.Requests);
                    Say("  requests: " + reqs);
                    Check(reqs.Contains("POST /upload/drive/v3/files") && reqs.Contains("permissions"), "the file gets a public 'anyone with the link' permission");
                    // second upload: the folder is remembered, no new search
                    int before; lock (g.Requests) before = g.Requests.FindAll(x => x.StartsWith("GET /drive/v3/files")).Count;
                    UploadResult r2 = UploadService.UploadAsync(s, img, "test2.png", "image/png").GetAwaiter().GetResult();
                    int after; lock (g.Requests) after = g.Requests.FindAll(x => x.StartsWith("GET /drive/v3/files")).Count;
                    Check(r2.Ok && before == after, "the folder id is remembered (no second folder search)");

                    gd.SignOut();
                    Check(!gd.SignedIn && !File.Exists(GDriveClient.TokenFile), "sign out removes the token");

                    // folder target
                    s.UploadTarget = "folder"; s.UploadFolderPath = Path.Combine(dir, "sync");
                    UploadResult fr = UploadService.UploadAsync(s, img, "a.png", "image/png").GetAwaiter().GetResult();
                    UploadResult fr2 = UploadService.UploadAsync(s, img, "a.png", "image/png").GetAwaiter().GetResult();
                    Check(fr.Ok && File.Exists(fr.Path) && fr2.Ok && fr.Path != fr2.Path, "folder target copies the file and keeps both when names match");
                    s.UploadTarget = "none";
                    Check(!UploadService.Enabled(s), "target 'none' means upload is off");
                }
                finally
                {
                    GDriveClient.AuthUrl = oldAuth; GDriveClient.TokenUrl = oldTok; GDriveClient.ApiUrl = oldApi; GDriveClient.UploadUrl = oldUp; GDriveClient.TokenFile = oldFile;
                }
            }
            try { Directory.Delete(dir, true); } catch { }
        }

        // ------------------------------------------------------------------ clipboard
        private static void ClipboardTest()
        {
            // only when the clipboard holds plain text or nothing; the text is restored afterwards
            try
            {
                bool plain = !System.Windows.Forms.Clipboard.ContainsImage() && !System.Windows.Forms.Clipboard.ContainsFileDropList();
                System.Windows.Forms.IDataObject cur = System.Windows.Forms.Clipboard.GetDataObject();
                string[] formats = cur == null ? new string[0] : cur.GetFormats();
                bool onlyText = true;
                foreach (string f in formats)
                    if (f != System.Windows.Forms.DataFormats.Text && f != System.Windows.Forms.DataFormats.UnicodeText && f != System.Windows.Forms.DataFormats.OemText && f != "Locale" && f != "System.String") onlyText = false;
                if (!plain || !onlyText)
                {
                    Say("clipboard test: skipped (clipboard holds non-text data, left untouched)");
                    return;
                }
                string savedText = System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null;
                using (Bitmap probe = new Bitmap(50, 40))
                {
                    using (Graphics g = Graphics.FromImage(probe)) g.Clear(Color.OrangeRed);
                    ImageOutput.CopyToClipboard(probe);
                }
                System.Windows.Forms.IDataObject got = System.Windows.Forms.Clipboard.GetDataObject();
                bool hasPng = got.GetDataPresent("PNG");
                bool hasBmp = System.Windows.Forms.Clipboard.ContainsImage();
                string size = "?";
                if (hasBmp) using (Image img = System.Windows.Forms.Clipboard.GetImage()) size = img.Width + "x" + img.Height;
                Check(hasPng && hasBmp && size == "50x40", "clipboard round trip: PNG=" + hasPng + " bitmap=" + hasBmp + " size=" + size);
                if (savedText != null) System.Windows.Forms.Clipboard.SetText(savedText); else System.Windows.Forms.Clipboard.Clear();
            }
            catch (Exception ex) { _errors.Add("clipboard test: " + ex.Message); }
        }
    }
}
