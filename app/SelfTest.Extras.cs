using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
namespace Snapter
{
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ rotation, text block handles, translucent colors
        private static void RotateTest(RegionOverlay ov)
        {
            Point P(double x, double y) { return new Point(x, y); }
            Say("rotate and text block test:");
            ov.TestSetSelection(new Rect(100, 100, 400, 300));
            ov.TestClearAll();
            ov.TestMods = ModifierKeys.None;
            ov.TestSetTool(EditorTool.Rect);
            ov.TestDown(P(200, 150), 1); ov.TestMove(P(250, 170), ModifierKeys.None); ov.TestMove(P(300, 200), ModifierKeys.None); ov.TestUp();
            ov.TestSetTool(EditorTool.Select);
            ov.TestDown(P(200, 175), 1); ov.TestUp();
            ShapeAnnotation sh = ov.TestSelected as ShapeAnnotation;
            Check(sh != null && sh.CanRotate && Math.Abs(sh.Angle) < 0.01, "a rectangle can be rotated and starts straight");

            Point hnd = ov.TestRotateHandle();
            Check(ov.TestHitRotate(hnd) && !ov.TestHitRotate(P(hnd.X + 40, hnd.Y)), "the rotate handle sits above the selected shape");
            ov.TestDown(hnd, 1); ov.TestMove(P(300, 190), ModifierKeys.None); ov.TestMove(P(350, 175), ModifierKeys.None); ov.TestUp();
            Check(Math.Abs(sh.Angle - 90) < 1, "dragging the handle to the right of the center turns it 90 degrees (" + sh.Angle.ToString("0.#") + ")");
            Check(sh.Hit(P(250, 125)) && !sh.Hit(P(200, 175)), "after the turn the hit test follows the shape (its top edge is now at the side)");
            Rect rb = sh.Bounds();
            Check(rb.Width < rb.Height, "the bounds of a turned wide rectangle are tall (" + (int)rb.Width + "x" + (int)rb.Height + ")");
            ov.TestKey(Key.Z, ModifierKeys.Control);
            Check(Math.Abs(((ShapeAnnotation)ov.TestItem(0)).Angle) < 0.01, "Ctrl+Z straightens it again");
            ov.TestKey(Key.Y, ModifierKeys.Control);
            sh = (ShapeAnnotation)ov.TestItem(0);
            Check(Math.Abs(sh.Angle - 90) < 1, "Ctrl+Y turns it back");

            // resizing a turned shape: the handle on the opposite side stays where it is on the screen
            ov.TestDown(sh.Hit(P(250, 125)) ? P(250, 125) : P(250, 125), 1); ov.TestUp();
            sh = (ShapeAnnotation)ov.TestSelected;
            Point[] hp = sh.HandlePoints();
            Point opposite = hp[7], grab = hp[3];
            double w0 = sh.R.Width;
            ov.TestDown(grab, 1); ov.TestMove(P(grab.X, grab.Y + 10), ModifierKeys.None); ov.TestMove(P(grab.X, grab.Y + 20), ModifierKeys.None); ov.TestUp();
            sh = (ShapeAnnotation)ov.TestItem(0);
            Point op2 = sh.HandlePoints()[7];
            Check(Math.Abs(sh.R.Width - (w0 + 20)) < 1.5 && (op2 - opposite).Length < 1.5, "resizing a turned rectangle: 20 px longer, the far side stays (" + (int)sh.R.Width + ", moved " + (op2 - opposite).Length.ToString("0.#") + ")");

            // a double click on the handle makes it straight
            ov.TestDown(ov.TestRotateHandle(), 2); ov.TestUp();
            Check(Math.Abs(((ShapeAnnotation)ov.TestItem(0)).Angle) < 0.01, "a double click on the rotate handle straightens the shape");

            // text: rotate, block width by the side handles, font size by the corners
            ov.TestClearAll();
            ov.TestSetTool(EditorTool.Text);
            ov.TestDown(P(150, 150), 1); ov.TestUp();
            ov.TestSetEditText("Hello big wide world"); ov.TestEndEdit();
            ov.TestSetTool(EditorTool.Select);
            TextAnnotation tx = (TextAnnotation)ov.TestItem(0);
            Rect r0 = tx.LocalRect();
            ov.TestDown(P(r0.X + 4, r0.Y + r0.Height / 2), 1); ov.TestUp();
            Check(ov.TestSelected == ov.TestItem(0) && ov.TestTextHandle(ov.TestTextHandlePoint(3)) == 3, "a selected text shows the handles of its block");
            Point right = ov.TestTextHandlePoint(3);
            ov.TestDown(right, 1); ov.TestMove(P(r0.X + 80, right.Y), ModifierKeys.None); ov.TestMove(P(r0.X + 90, right.Y), ModifierKeys.None); ov.TestUp();
            tx = (TextAnnotation)ov.TestItem(0);
            Check(Math.Abs(tx.BoxWidth - 90) < 1.5 && tx.LocalRect().Height > r0.Height * 1.5, "the side handle sets the width of the block: the text wraps (" + (int)tx.BoxWidth + " px, " + (int)tx.LocalRect().Height + " high)");
            ov.TestKey(Key.Z, ModifierKeys.Control);
            Check(((TextAnnotation)ov.TestItem(0)).BoxWidth == 0, "Ctrl+Z returns the automatic width");

            ov.TestClearAll();
            ov.TestSetTool(EditorTool.Text);
            ov.TestDown(P(150, 150), 1); ov.TestUp();
            ov.TestSetEditText("Hi"); ov.TestEndEdit();
            ov.TestSetTool(EditorTool.Select);
            tx = (TextAnnotation)ov.TestItem(0);
            r0 = tx.LocalRect(); double f0 = tx.FontSize;
            ov.TestDown(P(r0.X + 3, r0.Y + 3), 1); ov.TestUp();
            Point br = ov.TestTextHandlePoint(4);
            ov.TestDown(br, 1); ov.TestMove(P(br.X + r0.Width / 2, br.Y + 10), ModifierKeys.None); ov.TestMove(P(br.X + r0.Width, br.Y + 20), ModifierKeys.None); ov.TestUp();
            tx = (TextAnnotation)ov.TestItem(0);
            Check(tx.FontSize > f0 * 1.8 && tx.FontSize < f0 * 2.2 && Math.Abs(tx.Pos.X - r0.X) < 1 && Math.Abs(tx.Pos.Y - r0.Y) < 1, "a corner handle scales the font (" + f0 + " -> " + tx.FontSize.ToString("0.#") + ") and keeps the opposite corner");

            ov.TestMods = ModifierKeys.None;
            ov.TestDown(P(r0.X + 3, r0.Y + 3), 1); ov.TestUp();
            ov.TestDown(ov.TestRotateHandle(), 1); ov.TestMove(P(r0.X + 300, r0.Y + 10), ModifierKeys.None); ov.TestMove(P(r0.X + 400, r0.Y + 12), ModifierKeys.None); ov.TestUp();
            tx = (TextAnnotation)ov.TestItem(0);
            Check(Math.Abs(tx.Angle) > 45 && ov.TestTextHandle(P(0, 0)) == -1, "a text can be rotated too (" + tx.Angle.ToString("0") + " degrees); a turned text has no block handles");
            using (System.Drawing.Bitmap crop = ov.TestCrop()) Check(crop.Width == 400, "export with turned objects works");

            // translucent colors
            ov.TestClearAll();
            ov.TestSetTool(EditorTool.Rect);
            ov.TestDown(P(200, 150), 1); ov.TestMove(P(250, 170), ModifierKeys.None); ov.TestMove(P(300, 200), ModifierKeys.None); ov.TestUp();
            ov.TestSetColor(System.Windows.Media.Color.FromArgb(128, 10, 20, 30));
            Check(ov.TestSelected.Color.A == 128 && ov.TestStyle(EditorTool.Rect).Color.A == 128, "a translucent color is kept by the object and by the preset");
            ov.TestSetColor(System.Windows.Media.Color.FromRgb(229, 50, 45));
            ov.TestMods = null;
            ov.TestClearAll();
        }

        // ------------------------------------------------------------------ paint bucket, spotlight, magnifier
        private static void NewToolsTest(RegionOverlay ov, Settings st)
        {
            Point P(double x, double y) { return new Point(x, y); }
            Say("fill, spotlight and magnifier test:");
            ov.TestSetSelection(new Rect(100, 100, 400, 300));
            ov.TestClearAll();
            ov.TestMods = ModifierKeys.None;
            Check(st.ToolEnabled("fill") && st.ToolEnabled("spotlight") && st.ToolEnabled("magnifier"), "the three new tools are switched on by default");
            Check(ov.TestKey(Key.F, ModifierKeys.None) && ov.TestTool == EditorTool.Fill, "key F: the paint bucket");
            Check(ov.TestKey(Key.S, ModifierKeys.None) && ov.TestTool == EditorTool.Spotlight, "key S: the spotlight");
            Check(ov.TestKey(Key.Z, ModifierKeys.None) && ov.TestTool == EditorTool.Magnifier, "key Z: the magnifier");

            System.Drawing.Bitmap before = ov.TestCrop();      // the picture without any object

            // paint bucket: with the largest tolerance the whole region is one area
            ov.TestSetTool(EditorTool.Fill);
            ov.TestApplySize(100);
            ov.TestDown(P(300, 250), 1); ov.TestUp();
            FillAnnotation fl = ov.TestSelected as FillAnnotation;
            Check(fl != null && Math.Abs(fl.R.Width - 400) < 1.5 && Math.Abs(fl.R.Height - 300) < 1.5, "the bucket with a huge tolerance paints the whole region");
            using (System.Drawing.Bitmap c = ov.TestCrop())
            {
                System.Drawing.Color a = before.GetPixel(50, 50), b = c.GetPixel(50, 50);
                double want = 255 * 0.6 + a.R * 0.4;
                Check(fl != null && Math.Abs(b.R - want) < 10 && fl.Hit(P(150, 150)), "the area is painted over the picture, translucent (red " + a.R + " -> " + b.R + ")");
            }
            ov.TestKey(Key.Z, ModifierKeys.Control);
            Check(ov.TestItemCount == 0, "Ctrl+Z takes the paint away");

            // a small tolerance paints a small area only (at least the point itself)
            ov.TestApplySize(1);
            ov.TestDown(P(300, 250), 1); ov.TestUp();
            fl = ov.TestSelected as FillAnnotation;
            Check(fl != null && fl.R.Width >= 1 && fl.R.Width <= 400, "a tiny tolerance paints a smaller (or equal) area (" + (fl == null ? 0 : (int)fl.R.Width) + "x" + (fl == null ? 0 : (int)fl.R.Height) + ")");
            ov.TestClearAll();

            // spotlight: dark around the frame, bright inside
            ov.TestSetTool(EditorTool.Spotlight);
            ov.TestDown(P(200, 150), 1); ov.TestMove(P(250, 180), ModifierKeys.None); ov.TestMove(P(320, 220), ModifierKeys.None); ov.TestUp();
            Check(ov.TestSelected is SpotlightAnnotation && Math.Abs(ov.TestSelected.Opacity - 0.6) < 0.01, "the spotlight is drawn with a frame (strength 60% from its preset)");
            using (System.Drawing.Bitmap c = ov.TestCrop())
            {
                System.Drawing.Color outA = before.GetPixel(20, 20), outB = c.GetPixel(20, 20), inA = before.GetPixel(170, 90), inB = c.GetPixel(170, 90);
                Check(outB.R + outB.G + outB.B <= (outA.R + outA.G + outA.B) * 0.5 + 6, "the picture outside the frame is darker (" + (outA.R + outA.G + outA.B) + " -> " + (outB.R + outB.G + outB.B) + ")");
                Check(inA.ToArgb() == inB.ToArgb(), "the picture inside the frame is not changed");
            }
            ov.TestClearAll();

            // magnifier
            ov.TestSetTool(EditorTool.Magnifier);
            ov.TestDown(P(200, 150), 1); ov.TestMove(P(250, 200), ModifierKeys.None); ov.TestMove(P(300, 250), ModifierKeys.None); ov.TestUp();
            MagnifierAnnotation mg = ov.TestSelected as MagnifierAnnotation;
            Check(mg != null && Math.Abs(mg.Zoom - 2) < 0.01 && mg.R.Width > 90, "the magnifier is drawn with a frame and enlarges 2 times by its preset");
            Check(mg != null && mg.CreateVisual() != null && mg.Hit(P(250, 200)) && !mg.Hit(P(150, 120)), "the magnifier is clickable inside its ring only");
            ov.TestSetTool(EditorTool.Select);
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(c.Width == 400, "export with the new tools works");
            before.Dispose();
            ov.TestMods = null;
            ov.TestClearAll();
        }

        // ------------------------------------------------------------------ beautiful screenshot
        private static void BeautifyTest(RegionOverlay ov, Settings st)
        {
            Say("beautiful screenshot test:");
            Check(!st.BeautifyEnabled && st.BeautifyPadding == 48 && st.BeautifyRadius == 14 && st.BeautifyBackground == "gradient", "the defaults: switched off, 48 px of space, gradient");
            using (System.Drawing.Bitmap src = new System.Drawing.Bitmap(100, 60))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(src)) g.Clear(System.Drawing.Color.FromArgb(200, 40, 40));
                BeautifyOptions o = new BeautifyOptions { Background = "gradient", Preset = "ocean", Padding = 20, Radius = 10, Shadow = false };
                using (System.Drawing.Bitmap r = Beautifier.Apply(src, o))
                {
                    System.Drawing.Color corner = r.GetPixel(0, 0), mid = r.GetPixel(70, 50), tr = r.GetPixel(r.Width - 1, r.Height - 1);
                    Check(r.Width == 140 && r.Height == 100, "the picture grows by the space on every side (" + r.Width + "x" + r.Height + ")");
                    Check(mid.R == 200 && mid.G == 40 && mid.B == 40, "the screenshot itself is untouched in the middle");
                    Check(Math.Abs(corner.R - 0x21) < 12 && Math.Abs(corner.G - 0x93) < 12 && corner.A == 255, "the gradient starts with the first color of the preset in the corner");
                    Check(Math.Abs(tr.R - corner.R) > 30, "and ends with another color in the opposite corner");
                    Check(r.GetPixel(20, 20).R > 150 && r.GetPixel(21, 21).R == 200 || r.GetPixel(24, 24).R == 200, "the corners of the screenshot are rounded");
                }
                o.Background = "transparent";
                using (System.Drawing.Bitmap r = Beautifier.Apply(src, o)) Check(r.GetPixel(0, 0).A == 0 && r.GetPixel(70, 50).A == 255, "a transparent background stays transparent");
                o.Background = "solid"; o.Shadow = true; o.ShadowBlur = 20; o.ShadowOpacity = 60;
                using (System.Drawing.Bitmap r = Beautifier.Apply(src, o))
                {
                    System.Drawing.Color below = r.GetPixel(70, 84), far = r.GetPixel(2, 2);
                    Check(below.R + below.G + below.B < far.R + far.G + far.B, "the shadow darkens the background under the screenshot");
                }
                // the ten looks: every one gives a bigger picture that still holds the screenshot
                Check(BeautifyStyles.Ids.Length == 11 && BeautifyStyles.Ids[0] == "classic", "there are eleven built-in looks and the first is the classic one");
                foreach (string sid in BeautifyStyles.Ids)
                {
                    BeautifyOptions so = new BeautifyOptions { Style = sid, Background = "gradient", Preset = "ocean", Padding = 20, Radius = 10, Shadow = true, ShadowBlur = 20, ShadowOpacity = 40 };
                    using (System.Drawing.Bitmap r = Beautifier.Apply(src, so))
                    {
                        int red = 0;
                        for (int y = 0; y < r.Height; y += 2) for (int x = 0; x < r.Width; x += 2) { System.Drawing.Color c = r.GetPixel(x, y); if (c.R > 190 && c.G < 60 && c.B < 60) red++; }
                        Check(r.Width >= 140 && r.Height >= 100 && red > 300, "look '" + sid + "': " + r.Width + "x" + r.Height + ", the screenshot is inside (" + red + " red samples)");
                    }
                }

                // the looks are parameters: stored, changed, replaced, generated at random
                BeautifyLook wl = new BeautifyLook { Id = "my1", Type = "window", Bar = 50, Text = "My title", BarColor = "#223344", Dots = false, Radius = 6, Angle = 3, Shadow = 0, Layers = 4 };
                BeautifyLook back = BeautifyLook.FromJson("my1", wl.ToJson());
                Check(back.Type == "window" && back.Bar == 50 && back.Text == "My title" && back.BarColor == "#223344" && !back.Dots && back.Radius == 6 && back.Angle == 3 && back.Shadow == 0 && back.Layers == 4, "a look is stored in config and read back unchanged");
                BeautifyOptions co = new BeautifyOptions { Style = "my1", Background = "transparent", Padding = 0, Radius = 10, Shadow = false, Looks = new System.Collections.Generic.Dictionary<string, BeautifyLook> { { "my1", new BeautifyLook { Id = "my1", Type = "window", Bar = 50 } } } };
                using (System.Drawing.Bitmap r = Beautifier.Apply(src, co)) Check(r.Width == 100 && r.Height == 60 + 50, "the owner's own look is drawn from its parameters (the title bar of 50 px: " + r.Width + "x" + r.Height + ")");
                co.Looks["window"] = new BeautifyLook { Id = "window", Type = "cinema", BarsPct = 20, Touched = true }; co.Style = "window";
                using (System.Drawing.Bitmap r = Beautifier.Apply(src, co)) Check(r.Height == 60 + 2 * 12, "a built-in look can be replaced by the owner's version (a window id drawn as the cinema: " + r.Height + " px high)");
                Check(BeautifyStyles.IsKnown("my1", new BeautifyLook[] { wl }) && !BeautifyStyles.IsKnown("my2", new BeautifyLook[] { wl }) && BeautifyStyles.AllIds(new BeautifyLook[] { wl }).Count == 12, "the list of looks holds the built-in ones and the owner's");
                Random rr = new Random(7);
                bool lightOk = true, typesOk = true;
                for (int k = 0; k < 300; k++)
                {
                    BeautifyLook rl = BeautifyLook.Random(rr, "x", new BeautifyRandomOptions { AllowDark = false, Types = "classic,window,neon,cinema,bezel", Wild = 80 });
                    if (rl.Type == "neon" || rl.Type == "cinema") lightOk = false;
                    if (rl.BarColor == "#2B2D31" || rl.BarColor == "#1F2937" || rl.BarColor == "#2A2438" || rl.BarColor == "#1E293B" || rl.FrameColor == "#16181C" || rl.FrameColor == "#2B3A67" || rl.FrameColor == "#3A1F2B") lightOk = false;
                    if (rl.Type != "classic" && rl.Type != "window" && rl.Type != "bezel") typesOk = false;
                }
                Check(lightOk, "with dark looks switched off the random look never gives a dark one (neon, cinema, dark bars or frames)");
                Check(typesOk, "the random look gives only the bases that are allowed");
                bool allDraw = true; string failed = "";
                for (int k = 0; k < 120; k++)
                {
                    BeautifyLook rl = BeautifyLook.Random(rr, "r", new BeautifyRandomOptions { Wild = 100 });
                    BeautifyOptions ro = new BeautifyOptions { Style = "r", Background = "gradient", Padding = 16, Radius = 10, Shadow = true, ShadowBlur = 12, ShadowOpacity = 40, Looks = new System.Collections.Generic.Dictionary<string, BeautifyLook> { { "r", rl } } };
                    try
                    {
                        using (System.Drawing.Bitmap r = Beautifier.Apply(src, ro))
                        {
                            int red = 0;
                            for (int y = 0; y < r.Height; y += 3) for (int x = 0; x < r.Width; x += 3) { System.Drawing.Color c = r.GetPixel(x, y); if (c.R > 190 && c.G < 60 && c.B < 60) red++; }
                            if (r.Width < 100 || r.Height < 60 || red < 80) { allDraw = false; failed += rl.Type + " "; }
                        }
                    }
                    catch (Exception ex) { allDraw = false; failed += rl.Type + ":" + ex.Message + " "; }
                }
                Check(allDraw, "120 random looks are drawn without errors and keep the screenshot inside" + (failed.Length > 0 ? " (failed: " + failed + ")" : ""));
            }

            ov.TestSetSelection(new Rect(100, 100, 400, 300));
            ov.TestClearAll();
            ov.TestBeautify = false;
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(c.Width == 400 && c.Height == 300, "by default the export is the plain region");
            ov.TestBeautify = true;
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(c.Width == 400 + 96 && c.Height == 300 + 96, "with the switch on the export has the space around (" + c.Width + "x" + c.Height + ")");
            ov.TestBeautify = false;

            // the button "Beautiful": a click switches the look (no menu); a long press opens a short list; a choice switches the look on and shows its picture
            ov.TestToggleBeautify();
            Check(ov.TestBeautify && !ov.TestBeautifyMenuOpen, "a plain click on the button switches the look on and opens no list");
            ov.TestToggleBeautify();
            Check(!ov.TestBeautify, "and the next click switches it off");
            ov.TestOpenBeautifyMenu();
            Check(ov.TestBeautifyMenuOpen, "holding the button opens the list of the looks while the beautiful look is off");
            string shots = Environment.GetEnvironmentVariable("SNAPTER_SELFTEST_SHOTS");
            if (!string.IsNullOrEmpty(shots)) ov.TestSaveElement(false, System.IO.Path.Combine(shots, "beautify_list.png"));
            ov.TestHoverBeautify("browser");
            Check(ov.TestBeautifyMenuOpen && ov.TestBeautifyTemplateShown && !ov.TestBeautifyPreviewShown && ov.TestBeautifyTemplateRect.Width > 250, "moving over an item of the list shows a SAMPLE of that look beside the list, not your screenshot; the list stays open");
            Check(!ov.TestBeautifyTemplateRect.IntersectsWith(ov.TestBeautifyListRect), "the sample does not cover the list");
            if (!string.IsNullOrEmpty(shots)) ov.TestSaveElement(false, System.IO.Path.Combine(shots, "beautify_list.png"));
            ov.TestHoverBeautify("polaroid");
            Check(ov.TestBeautifyTemplateShown && !ov.TestBeautify, "the next item shows its sample and nothing is switched on yet");
            ov.TestOpenBeautifyMenu();
            Check(!ov.TestBeautifyMenuOpen && !ov.TestBeautifyTemplateShown && !ov.TestBeautifyPreviewShown, "the button closes the list again and the sample goes with it");
            ov.TestOpenBeautifyMenu();
            ov.TestPickBeautify("window");
            Check(!ov.TestBeautifyMenuOpen && ov.TestBeautify && ov.TestBeautifyStyle == "window", "a choice closes the list, switches the look on and remembers which one");
            Check(ov.TestBeautifyPreviewShown && Math.Abs(ov.TestBeautifyPreviewSize.Width - 496) < 1 && Math.Abs(ov.TestBeautifyPreviewSize.Height - 426) < 1,
                "the chosen look is shown as ONE picture at 100% of its size (" + (int)ov.TestBeautifyPreviewSize.Width + "x" + (int)ov.TestBeautifyPreviewSize.Height + ")");
            Check(!ov.TestBeautifyPreviewRect.IntersectsWith(ov.TestSelRect), "the live picture stands beside the region, not over it (" + (int)ov.TestBeautifyPreviewRect.X + "," + (int)ov.TestBeautifyPreviewRect.Y + ")");
            ov.TestClearAll();
            Check(ov.TestBeautify && ov.TestBeautifyPreviewShown, "the picture does not vanish by itself: it stays while the look is on (no two-second timer)");
            if (!string.IsNullOrEmpty(shots)) ov.TestSaveElement(true, System.IO.Path.Combine(shots, "beautify_preview_window.png"));
            ov.TestPickBeautify("browser");
            if (!string.IsNullOrEmpty(shots)) ov.TestSaveElement(true, System.IO.Path.Combine(shots, "beautify_preview_browser.png"));
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(c.Width == 400 + 96 && c.Height == 300 + 96 + 44, "the export now has the chosen look (the browser bar adds 44 px: " + c.Width + "x" + c.Height + ")");
            ov.TestPickBeautify("cinema");
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(c.Height == 300 + 96 + 84, "another choice changes the look at once (cinema bars: " + c.Width + "x" + c.Height + ")");
            ov.TestPickBeautify("random");
            Check(ov.TestBeautify && ov.TestBeautifyStyle == "random" && ov.TestRandomLook != null && ov.TestBeautifyPreviewShown, "the item Random of the list gives a new random look and shows its picture");
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(c.Width >= 400 && c.Height >= 300, "and the export uses it (" + c.Width + "x" + c.Height + ")");
            ov.TestToggleBeautify();
            Check(!ov.TestBeautify && !ov.TestBeautifyPreviewShown, "a click switches the look off and the picture goes");
            ov.TestToggleBeautify();
            Check(ov.TestBeautify && ov.TestBeautifyPreviewShown, "a click that switches the look on shows the picture of the look for a moment");
            ov.TestPickBeautify("off");
            using (System.Drawing.Bitmap c = ov.TestCrop()) Check(!ov.TestBeautify && c.Width == 400 && c.Height == 300 && !ov.TestBeautifyPreviewShown, "'No look' gives the plain region again and the picture is gone");
            ov.TestPickBeautify("classic");
            ov.TestBeautify = false;
        }

        // ------------------------------------------------------------------ base settings (texts and file name only: the buttons ask the owner, so the real files are not touched here)
        // ------------------------------------------------------------------ the panels beside the settings window
        private static void PreviewTest(Settings st)
        {
            Say("panel preview test:");
            System.Drawing.Rectangle mon = new System.Drawing.Rectangle(0, 0, 1920, 1080);
            Theme th = Theme.Load(st.AccentTheme, st.ThemeMode);
            I18n i18n = new I18n(); i18n.Load(st.Language);
            RegionOverlay pv = RegionOverlay.CreatePreview(mon, new System.Windows.Rect(400, 200, 880, 640), th, i18n, st);
            Check(pv.TestPanelRectA.Width > 10 && pv.TestPanelRectA.Height > 10, "the preview builds the tool panel (" + (int)pv.TestPanelRectA.Width + "x" + (int)pv.TestPanelRectA.Height + ")");
            pv.Highlight(new string[] { "tools", "props", "frame" });
            pv.Close();
            Check(true, "the preview highlights and closes without error");
        }

        private static void BaselineTest(Settings st)
        {
            Say("base settings test:");
            foreach (string lang in new string[] { "ru", "en" })
            {
                I18n t = new I18n(); t.Load(lang);
                string[] keys = { "settings.reset", "settings.baseline_save", "settings.factory", "settings.reset_confirm", "settings.baseline_none", "settings.baseline_saved", "settings.factory_confirm", "settings.baseline_save_confirm" };
                Check(keys.All(k => t.T(k) != k), "the texts of the base settings buttons exist (" + lang + ")");
            }
            Check(System.IO.Path.GetFileName(SettingsWindow.TestBaselinePath) == "settings.baseline.json" && System.IO.Path.GetDirectoryName(SettingsWindow.TestBaselinePath) == System.IO.Path.GetDirectoryName(Paths.UserSettings), "the base settings live next to the user settings");
        }
    }
}
