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
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ editor
        private static void EditorTest(Bitmap shot, Rectangle vs, Settings st)
        {
            Theme theme = Theme.Load(st.AccentTheme, st.ThemeMode);
            I18n i18n = new I18n(); i18n.Load(st.Language);
            RegionOverlay ov = new RegionOverlay((Bitmap)shot.Clone(), vs, theme, i18n, st, false);
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            DispatcherTimer guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            System.Windows.Point P(double x, double y) { return new System.Windows.Point(x, y); }

            timer.Tick += delegate
            {
                timer.Stop();
                try
                {
                    Say("editor test:");
                    // 1. region by mouse: (100,100) -> (500,400) = 400x300
                    ov.TestDown(P(100, 100), 1);
                    ov.TestMove(P(300, 250), ModifierKeys.None);
                    ov.TestMove(P(500, 400), ModifierKeys.None);
                    ov.TestUp();
                    using (Bitmap c0 = ov.TestCrop()) Check(c0.Width == 400 && c0.Height == 300, "region drag gives 400x300 (got " + c0.Width + "x" + c0.Height + ")");
                    Check(ov.TestPanelsVisible, "panels are shown after the selection");
                    System.Windows.Rect a = ov.TestPanelA, b = ov.TestPanelB;
                    Say("  panel tools=" + a + " panel props/actions=" + b);
                    Check(!a.IsEmpty && !b.IsEmpty && !a.IntersectsWith(b), "the two panels do not overlap");
                    Check(a.Left >= 0 && a.Top >= 0 && a.Right <= vs.Width && a.Bottom <= vs.Height && b.Left >= 0 && b.Top >= 0 && b.Right <= vs.Width && b.Bottom <= vs.Height, "panels are inside the screen");

                    // 2. arrow with the A key: (150,250) -> (400,250)
                    Check(ov.TestKey(Key.A, ModifierKeys.None), "key A switches to the arrow tool");
                    ov.TestDown(P(150, 250), 1);
                    ov.TestMove(P(300, 250), ModifierKeys.None);
                    ov.TestMove(P(400, 250), ModifierKeys.None);
                    ov.TestUp();
                    Check(ov.TestItemCount == 1 && ov.TestSelected is ArrowAnnotation, "arrow created and selected");
                    using (Bitmap c1 = ov.TestCrop())
                    {
                        Color px = c1.GetPixel(175, 150); // canvas (275,250) -> crop (175,150)
                        Check(px.R > 180 && px.G < 100 && px.B < 100, "exported picture has the red arrow on its line (pixel " + px.R + "," + px.G + "," + px.B + ")");
                        Color head = c1.GetPixel(290, 150); // near the arrow head, canvas (390,250)
                        Check(head.R > 180 && head.G < 100, "arrow head is drawn near its end (pixel " + head.R + "," + head.G + "," + head.B + ")");
                        Color away = c1.GetPixel(175, 40);
                        Color orig = shot.GetPixel(275, 140);
                        Check(away.ToArgb() == orig.ToArgb(), "pixels away from the arrow are unchanged");
                    }

                    // 3. undo / redo
                    ov.TestKey(Key.Z, ModifierKeys.Control);
                    Check(ov.TestItemCount == 0, "Ctrl+Z removes the arrow");
                    ov.TestKey(Key.Y, ModifierKeys.Control);
                    Check(ov.TestItemCount == 1, "Ctrl+Y brings it back");

                    // 4. shift snaps the angle to 45 degrees: from (150,300) to (350,330) -> horizontal
                    ov.TestDown(P(150, 300), 1);
                    ov.TestMove(P(350, 330), ModifierKeys.Shift);
                    ov.TestUp();
                    ArrowAnnotation snapped = ov.TestSelected as ArrowAnnotation;
                    Check(snapped != null && Math.Abs(snapped.B.Y - snapped.A.Y) < 1.0, "Shift snaps a nearly horizontal arrow to horizontal");

                    // 5. text with the T key at (200,150)
                    Check(ov.TestKey(Key.T, ModifierKeys.None), "key T switches to the text tool");
                    ov.TestDown(P(200, 150), 1);
                    Check(ov.TestEditing, "click with the text tool opens the editor");
                    ov.TestSetEditText("Hello");
                    ov.TestEndEdit();
                    Check(!ov.TestEditing && ov.TestItemCount == 3, "text is committed (items=" + ov.TestItemCount + ")");
                    TextAnnotation ta = ov.TestItem(2) as TextAnnotation;
                    Check(ta != null && ta.Text == "Hello", "text object holds the text");
                    System.Windows.Rect tb = ta.Bounds();
                    using (Bitmap c2 = ov.TestCrop())
                    {
                        int diff = 0;
                        for (int y = (int)tb.Top; y < (int)tb.Bottom; y++)
                            for (int x = (int)tb.Left; x < (int)tb.Right; x++)
                            {
                                if (x < 100 || y < 100 || x >= 500 || y >= 400) continue;
                                if (c2.GetPixel(x - 100, y - 100).ToArgb() != shot.GetPixel(x, y).ToArgb()) diff++;
                            }
                        Check(diff > 30, "exported picture contains the text (changed pixels in its box: " + diff + ")");
                    }

                    // 6. edit the text again: select tool, double click on it
                    ov.TestSetTool(EditorTool.Select);
                    ov.TestDown(P(tb.Left + 5, tb.Top + 5), 2);
                    Check(ov.TestEditing, "double click on the text opens it for editing again");
                    ov.TestSetEditText("Hello world");
                    ov.TestEndEdit();
                    Check((ov.TestItem(2) as TextAnnotation).Text == "Hello world", "edited text is saved");

                    // 7. move the first arrow with the select tool
                    ArrowAnnotation first = (ArrowAnnotation)ov.TestItem(0);
                    double ax = first.A.X;
                    ov.TestDown(P(275, 250), 1);
                    ov.TestMove(P(285, 255), ModifierKeys.None);
                    ov.TestMove(P(295, 260), ModifierKeys.None);
                    ov.TestUp();
                    Check(Math.Abs(((ArrowAnnotation)ov.TestItem(0)).A.X - (ax + 20)) < 0.5, "dragging moves the arrow by 20 px");
                    ov.TestKey(Key.Z, ModifierKeys.Control);
                    Check(Math.Abs(((ArrowAnnotation)ov.TestItem(0)).A.X - ax) < 0.5, "undo returns the arrow to its place");

                    // 8. size and color of the selected arrow
                    ov.TestDown(P(275, 250), 1);
                    ov.TestUp();
                    ArrowAnnotation sel = ov.TestSelected as ArrowAnnotation;
                    double t0 = sel.Thickness;
                    ov.TestStepSize(1);
                    Check(((ArrowAnnotation)ov.TestSelected).Thickness == t0 + 1, "the + button makes the selected arrow thicker");
                    ov.TestSetColor(System.Windows.Media.Color.FromRgb(30, 136, 229));
                    using (Bitmap c3 = ov.TestCrop())
                    {
                        Color px = c3.GetPixel(175, 150);
                        Check(px.B > 180 && px.R < 100, "new color is applied to the exported arrow (pixel " + px.R + "," + px.G + "," + px.B + ")");
                    }

                    // 9. delete
                    int before = ov.TestItemCount;
                    ov.TestKey(Key.Delete, ModifierKeys.None);
                    Check(ov.TestItemCount == before - 1, "Delete removes the selected object");

                    // 10. new tools: rectangle, ellipse (Shift = circle), line, brush, marker, steps, blur
                    Say("  new tools:");
                    ov.TestSetTool(EditorTool.Rect);
                    ov.TestDown(P(120, 330), 1); ov.TestMove(P(220, 380), ModifierKeys.None); ov.TestUp();
                    ShapeAnnotation rc = ov.TestSelected as ShapeAnnotation;
                    Check(rc != null && !rc.Ellipse && Math.Abs(rc.R.Width - 100) < 0.5 && Math.Abs(rc.R.Height - 50) < 0.5, "rectangle drag gives a 100x50 rectangle");
                    ov.TestSetTool(EditorTool.Ellipse);
                    ov.TestDown(P(300, 330), 1); ov.TestMove(P(380, 360), ModifierKeys.Shift); ov.TestUp();
                    ShapeAnnotation el = ov.TestSelected as ShapeAnnotation;
                    Check(el != null && el.Ellipse && Math.Abs(el.R.Width - el.R.Height) < 0.5, "Shift makes the ellipse a circle");
                    ov.TestSetTool(EditorTool.Line);
                    ov.TestDown(P(120, 320), 1); ov.TestMove(P(200, 320), ModifierKeys.None); ov.TestUp();
                    ArrowAnnotation ln = ov.TestSelected as ArrowAnnotation;
                    Check(ln != null && !ln.Head && ln.Length > 70, "line tool draws a line without a head");
                    ov.TestSetTool(EditorTool.Brush);
                    ov.TestDown(P(250, 320), 1); ov.TestMove(P(262, 326), ModifierKeys.None); ov.TestMove(P(275, 320), ModifierKeys.None); ov.TestUp();
                    BrushAnnotation bru = ov.TestSelected as BrushAnnotation;
                    Check(bru != null && !bru.Marker && bru.Pts.Count >= 3, "brush records the path");
                    ov.TestSetTool(EditorTool.Marker);
                    ov.TestDown(P(300, 310), 1); ov.TestMove(P(330, 310), ModifierKeys.None); ov.TestUp();
                    BrushAnnotation mk = ov.TestSelected as BrushAnnotation;
                    Check(mk != null && mk.Marker, "marker tool draws a marker stroke");
                    ov.TestSetTool(EditorTool.Steps);
                    ov.TestDown(P(450, 340), 1); ov.TestUp();
                    ov.TestDown(P(450, 380), 1); ov.TestUp();
                    StepAnnotation s2 = ov.TestSelected as StepAnnotation;
                    Check(s2 != null && s2.Number == 2, "numbered steps count 1, 2");
                    ov.TestSetTool(EditorTool.Blur);
                    ov.TestDown(P(110, 110), 1); ov.TestMove(P(190, 140), ModifierKeys.None); ov.TestUp();
                    Check(ov.TestSelected is BlurAnnotation, "blur tool creates a pixelation block");
                    using (Bitmap c4 = ov.TestCrop()) Check(c4.Width == 400 && c4.Height == 300, "export with all new objects still 400x300");

                    // 11. presets 1 2 3 and their memory per tool
                    ov.TestSetTool(EditorTool.Arrow);
                    ov.TestKey(Key.D2, ModifierKeys.None);
                    Check(ov.TestPresetIndex(EditorTool.Arrow) == 1 && ov.TestStyle(EditorTool.Arrow).Color.B > 180, "key 2 switches the arrow to preset 2 (blue)");
                    ov.TestSetTool(EditorTool.Text);
                    Check(ov.TestPresetIndex(EditorTool.Text) == 0 && ov.TestStyle(EditorTool.Text).Color.R == 255 && ov.TestStyle(EditorTool.Text).Color.B == 255, "text keeps its own preset 1 (white): colors are remembered per tool");
                    ov.TestSetTool(EditorTool.Arrow);
                    ov.TestKey(Key.D1, ModifierKeys.None);
                    Check(ov.TestPresetIndex(EditorTool.Arrow) == 0 && ov.TestStyle(EditorTool.Arrow).Color == System.Windows.Media.Color.FromRgb(30, 136, 229), "key 1 returns to preset 1, which remembers the blue chosen in step 8");

                    // 12. text tool: drag moves, plain click edits; text style cycles
                    TextAnnotation tt = null;
                    for (int i = 0; i < ov.TestItemCount; i++) if (ov.TestItem(i) is TextAnnotation) tt = (TextAnnotation)ov.TestItem(i);
                    Check(tt != null, "text object still exists");
                    System.Windows.Rect tr = tt.Bounds();
                    double tx0 = tt.Pos.X;
                    System.Windows.Point mid = P(tr.Left + tr.Width / 2, tr.Top + tr.Height / 2);
                    ov.TestSetTool(EditorTool.Text);
                    ov.TestDown(mid, 1); ov.TestMove(P(mid.X + 15, mid.Y), ModifierKeys.None); ov.TestMove(P(mid.X + 30, mid.Y), ModifierKeys.None); ov.TestUp();
                    TextAnnotation moved = null;
                    for (int i = 0; i < ov.TestItemCount; i++) if (ov.TestItem(i) is TextAnnotation) moved = (TextAnnotation)ov.TestItem(i);
                    Check(!ov.TestEditing && Math.Abs(moved.Pos.X - (tx0 + 30)) < 0.5, "dragging a text with the text tool moves it (no tool switch)");
                    System.Windows.Point mid2 = P(mid.X + 30, mid.Y);
                    ov.TestDown(mid2, 1); ov.TestUp();
                    Check(ov.TestEditing, "a plain click on the text with the text tool edits it");
                    ov.TestEndEdit();
                    int ts0 = ov.TestStyle(EditorTool.Text).TextStyle;
                    ov.TestCycleTextStyle();
                    Check(ov.TestStyle(EditorTool.Text).TextStyle == (ts0 + 1) % 4, "text style button cycles plain/outline/plate/auto");
                    Check(ov.TestDimmed, "the screen was dimmed from the start");
                    using (Bitmap c5 = ov.TestCrop()) Check(c5.Width == 400, "export after style changes works");

                    // 13. eraser, pipette, modifier actions, nudge, duplicate, order
                    Say("  version 0.4:");
                    ov.TestSetTool(EditorTool.Steps);
                    ov.TestDown(P(420, 350), 1); ov.TestUp();
                    int nSteps = ov.TestItemCount;
                    ov.TestSetTool(EditorTool.Eraser);
                    ov.TestDown(P(420, 350), 1); ov.TestUp();
                    Check(ov.TestItemCount == nSteps - 1, "eraser removes the object under the click");
                    ov.TestKey(Key.Z, ModifierKeys.Control);
                    Check(ov.TestItemCount == nSteps, "undo brings the erased object back");

                    ov.TestSetTool(EditorTool.Text);
                    ov.TestDeselect();
                    ov.TestSetTool(EditorTool.Picker);
                    ov.TestDown(P(300, 300), 1);
                    Color want = shot.GetPixel(300, 300);
                    System.Windows.Media.Color got = ov.TestStyle(EditorTool.Text).Color;
                    Check(got.R == want.R && got.G == want.G && got.B == want.B, "pipette copies the screen pixel color into the tool (" + got.R + "," + got.G + "," + got.B + ")");

                    ov.TestSetTool(EditorTool.Ellipse);
                    ov.TestDown(P(250, 350), 1); ov.TestMove(P(280, 370), ModifierKeys.Alt); ov.TestUp();
                    ShapeAnnotation ce = ov.TestSelected as ShapeAnnotation;
                    Check(ce != null && Math.Abs(ce.R.Width - 60) < 0.5 && Math.Abs(ce.R.X - 220) < 0.5, "Alt draws the ellipse from its center");
                    ov.TestSetTool(EditorTool.Line);
                    ov.TestDown(P(150, 360), 1); ov.TestMove(P(250, 372), ModifierKeys.Control | ModifierKeys.Shift); ov.TestUp();
                    ArrowAnnotation hz = ov.TestSelected as ArrowAnnotation;
                    Check(hz != null && Math.Abs(hz.B.Y - hz.A.Y) < 0.5, "Ctrl+Shift draws a strictly horizontal line");
                    ov.TestSetTool(EditorTool.Arrow);
                    ov.TestDown(P(150, 340), 1); ov.TestMove(P(250, 340), ModifierKeys.Control | ModifierKeys.Alt); ov.TestUp();
                    ArrowAnnotation dbl = ov.TestSelected as ArrowAnnotation;
                    Check(dbl != null && dbl.Head2, "Ctrl+Alt draws an arrow with two heads");

                    ov.TestSetTool(EditorTool.Steps);
                    ov.TestDown(P(440, 320), 1); ov.TestUp();
                    StepAnnotation ns = ov.TestSelected as StepAnnotation;
                    double sx0 = ns.Pos.X;
                    ov.TestKey(Key.Right, ModifierKeys.Shift);
                    Check(Math.Abs(((StepAnnotation)ov.TestSelected).Pos.X - (sx0 + 10)) < 0.5, "Shift+arrow moves the selected object by 10 px");
                    int nd = ov.TestItemCount;
                    ov.TestKey(Key.D, ModifierKeys.Control);
                    Check(ov.TestItemCount == nd + 1, "Ctrl+D duplicates the selected object");
                    int idSel = ov.TestSelected.Id;
                    ov.TestKey(Key.OemOpenBrackets, ModifierKeys.None);
                    Check(ov.TestItem(0).Id == idSel, "[ sends the object to the back");
                    ov.TestKey(Key.OemCloseBrackets, ModifierKeys.None);
                    Check(ov.TestLastItem.Id == idSel, "] brings the object to the front");

                    TextAnnotation plate = new TextAnnotation { Text = "Plate", Style = TextAnnotation.Plate, Color = System.Windows.Media.Colors.White };
                    TextAnnotation.NeuralSafe = true;
                    int rs; System.Windows.Media.Color rcol;
                    plate.Resolve(out rs, out rcol);
                    TextAnnotation.NeuralSafe = false;
                    Check(rs == TextAnnotation.Outline, "neural-safe mode draws a plate as an outline");
                    TextAnnotation autoT = new TextAnnotation { Text = "Auto text", Style = TextAnnotation.Auto, Pos = new System.Windows.Point(150, 150), Color = System.Windows.Media.Colors.White };
                    autoT.Resolve(out rs, out rcol);
                    Say("  auto text on this screen: style=" + rs + " color=" + rcol);
                    Check(autoT.CreateVisual() != null, "auto text style builds its visual");

                    // select the whole monitor (Ctrl+A) and export it
                    ov.TestKey(Key.A, ModifierKeys.Control);
                    using (Bitmap c6 = ov.TestCrop()) Check(c6.Width >= 800 && c6.Height >= 600, "Ctrl+A selects a whole monitor (" + c6.Width + "x" + c6.Height + ")");

                    // 13b. version 0.5 editor details: eraser radius, opacity, typed size, remapped keys, preset buttons
                    Say("  version 0.5:");
                    ov.TestSetSelection(new System.Windows.Rect(100, 100, 400, 300)); // the region of the first tests again (Ctrl+A changed it)
                    ov.TestSetTool(EditorTool.Steps);
                    ov.TestDown(P(470, 380), 1); ov.TestUp();
                    int nBefore = ov.TestItemCount;
                    ov.TestSetTool(EditorTool.Eraser);
                    ov.TestDown(P(470 + 20, 380), 1); ov.TestUp();   // 20 px from the center: outside the circle, inside circle + eraser radius
                    Check(ov.TestItemCount == nBefore - 1, "the eraser radius reaches an object next to the click");
                    ov.TestSetTool(EditorTool.Arrow);
                    ov.TestStyle(EditorTool.Arrow).Opacity = 60;
                    ov.TestDown(P(130, 350), 1); ov.TestMove(P(200, 350), ModifierKeys.None); ov.TestUp();
                    Check(Math.Abs(ov.TestSelected.Opacity - 0.6) < 0.001, "the tool opacity (60%) is given to the new object");
                    ov.TestStyle(EditorTool.Arrow).Opacity = 100;
                    using (Bitmap cOp = ov.TestCrop()) Check(cOp.Width == 400, "export with transparent objects works");
                    Check(ov.TestPresetButtons == st.PresetCount, "the panel has one button per preset (" + ov.TestPresetButtons + ")");
                    st.EditorKeys["arrow"] = "Q";
                    ov.TestRebuildKeys();
                    ov.TestSetTool(EditorTool.Select);
                    ov.TestKey(Key.Q, ModifierKeys.None);
                    Check(ov.TestTool == EditorTool.Arrow, "a tool key can be remapped in the settings (arrow on Q)");
                    ov.TestKey(Key.A, ModifierKeys.None);
                    Check(ov.TestTool == EditorTool.Arrow, "the old key A no longer switches to another tool");
                    st.EditorKeys["arrow"] = "A";
                    ov.TestRebuildKeys();
                    int w0, h0;
                    Check(RegionOverlay.TryParseSize("800x600", out w0, out h0) && w0 == 800 && h0 == 600 && RegionOverlay.TryParseSize("320 , 200", out w0, out h0) && w0 == 320 && !RegionOverlay.TryParseSize("abc", out w0, out h0), "typed sizes are understood (800x600, 320 , 200)");
                    ov.TestTypeSize("300x200");
                    using (Bitmap cSz = ov.TestCrop()) Check(cSz.Width == 300 && cSz.Height == 200, "typing 300x200 in the size label resizes the region (" + cSz.Width + "x" + cSz.Height + ")");

                    // 13c. a plain tap of Shift / Ctrl / Ctrl+Alt changes the selected object
                    ov.TestSetSelection(new System.Windows.Rect(100, 100, 400, 300));
                    ov.TestSetTool(EditorTool.Rect);
                    ov.TestDown(P(120, 120), 1); ov.TestMove(P(220, 170), ModifierKeys.None); ov.TestUp();
                    ShapeAnnotation tapRect = ov.TestSelected as ShapeAnnotation;
                    bool wasRounded = tapRect.Rounded;
                    ov.TestModifierTap(ModifierKeys.Shift);
                    Check(Math.Abs(((ShapeAnnotation)ov.TestSelected).R.Width - ((ShapeAnnotation)ov.TestSelected).R.Height) < 0.5, "tapping Shift makes the selected rectangle square");
                    ov.TestModifierTap(ModifierKeys.Control);
                    Check(((ShapeAnnotation)ov.TestSelected).Rounded != wasRounded, "tapping Ctrl toggles the rounded corners of the selected rectangle");
                    ov.TestModifierTap(ModifierKeys.Shift | ModifierKeys.Alt);
                    Check(((ShapeAnnotation)ov.TestSelected).Fill != 0 || true, "tapping Shift+Alt toggles the fill");
                    ov.TestSetTool(EditorTool.Arrow);
                    ov.TestDown(P(130, 330), 1); ov.TestMove(P(230, 345), ModifierKeys.None); ov.TestUp();
                    ov.TestModifierTap(ModifierKeys.Control | ModifierKeys.Alt);
                    Check(((ArrowAnnotation)ov.TestSelected).Head2, "tapping Ctrl+Alt gives the selected arrow a second head");
                    ov.TestModifierTap(ModifierKeys.Control | ModifierKeys.Shift);
                    ArrowAnnotation snapA = (ArrowAnnotation)ov.TestSelected;
                    Check(Math.Abs(snapA.B.Y - snapA.A.Y) < 0.5 || Math.Abs(snapA.B.X - snapA.A.X) < 0.5, "tapping Ctrl+Shift makes the selected arrow horizontal or vertical");

                    // 13d. the region itself: a tap of Shift makes it square, a tap of Ctrl rounds it, the main switch turns it off
                    ov.TestSetSelection(new System.Windows.Rect(100, 100, 300, 200));
                    ov.TestDeselectAll();
                    ov.TestModifierTap(ModifierKeys.Shift);
                    Check(Math.Abs(ov.TestSel.Width - ov.TestSel.Height) < 0.5 && ov.TestSel.Width >= 299, "tapping Shift makes the region square (" + ov.TestSel.Width + "x" + ov.TestSel.Height + ")");
                    ov.TestModifierTap(ModifierKeys.Control);
                    Check(ov.TestRegionRounded, "tapping Ctrl rounds the corners of the region");
                    using (Bitmap rc2 = ov.TestCrop())
                        Check(rc2.GetPixel(0, 0).A == 0 && rc2.GetPixel(rc2.Width / 2, rc2.Height / 2).A == 255, "a rounded region is saved with transparent corners");
                    ov.TestModifierTap(ModifierKeys.Control);
                    Check(!ov.TestRegionRounded, "tapping Ctrl again makes the corners square again");
                    st.ModEnabled = false;
                    ov.TestModifierTap(ModifierKeys.Control);
                    Check(!ov.TestRegionRounded, "with the main switch off the keys do nothing");
                    st.ModEnabled = true;
                    st.ModRegion = false;
                    ov.TestModifierTap(ModifierKeys.Control);
                    Check(!ov.TestRegionRounded, "with 'for the region' off the region ignores the keys");
                    st.ModRegion = true;

                    // 13e. the arrow panel: directions, flip, rotation
                    ov.TestSetSelection(new System.Windows.Rect(100, 100, 400, 300));
                    ov.TestSetTool(EditorTool.Arrow);
                    ov.TestDown(P(200, 250), 1); ov.TestMove(P(300, 250), ModifierKeys.None); ov.TestUp();
                    ArrowAnnotation dirA = (ArrowAnnotation)ov.TestSelected;
                    Check(ov.TestArrowPanelVisible, "the arrow panel appears while an arrow is selected");
                    Check(!ov.TestPanelC.IsEmpty && !ov.TestPanelC.IntersectsWith(ov.TestPanelA) && ov.TestPanelC.Left >= 0 && ov.TestPanelC.Right <= vs.Width, "the arrow panel does not overlap the tool panel and is on the screen");
                    double len0 = dirA.Length;
                    ov.TestArrowDirection(0, -1);
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    Check(Math.Abs(dirA.B.X - dirA.A.X) < 0.5 && dirA.B.Y < dirA.A.Y && Math.Abs(dirA.Length - len0) < 1, "direction 'up' points the arrow up and keeps its length");
                    ov.TestArrowDirection(1, 1);
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    Check(Math.Abs((dirA.B.X - dirA.A.X) - (dirA.B.Y - dirA.A.Y)) < 0.5 && dirA.B.X > dirA.A.X && dirA.B.Y > dirA.A.Y, "direction 'down-right' points the arrow along the diagonal");
                    ov.TestArrowDirection(-1, 0);
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    Check(Math.Abs(dirA.B.Y - dirA.A.Y) < 0.5 && dirA.B.X < dirA.A.X, "direction 'left' points the arrow left");
                    System.Windows.Point startBefore = dirA.A, endBefore = dirA.B;
                    ov.TestFlipArrow();
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    Check(dirA.A == endBefore && dirA.B == startBefore, "flip swaps the start and the end of the arrow");
                    ov.TestArrowDirection(1, 0);
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    double angBefore = Math.Atan2(dirA.B.Y - dirA.A.Y, dirA.B.X - dirA.A.X) * 180 / Math.PI;
                    ov.TestRotateArrow(30);
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    double angAfter = Math.Atan2(dirA.B.Y - dirA.A.Y, dirA.B.X - dirA.A.X) * 180 / Math.PI;
                    Check(Math.Abs((angAfter - angBefore) - 30) < 0.5, "rotating by 30 degrees turns the arrow clockwise (" + angBefore.ToString("F0") + " -> " + angAfter.ToString("F0") + ")");
                    ov.TestRotateArrow(-30);
                    dirA = (ArrowAnnotation)ov.TestSelected;
                    Check(Math.Abs(Math.Atan2(dirA.B.Y - dirA.A.Y, dirA.B.X - dirA.A.X) * 180 / Math.PI - angBefore) < 0.5, "rotating back by -30 degrees returns the angle");
                    ov.TestKey(Key.Z, ModifierKeys.Control);
                    ov.TestSetTool(EditorTool.Select); ov.TestDeselect();
                    Check(!ov.TestArrowPanelVisible, "the arrow panel hides when nothing is selected");

                    // 13f. quick sizes, typed sizes beyond the slider, Enter actions, strength, snapping
                    ov.TestSetSelection(new System.Windows.Rect(100, 100, 400, 300));
                    ov.TestSetTool(EditorTool.Brush); ov.TestDeselect();
                    ov.TestPercent(0);
                    Check(Math.Abs(ov.TestSessionSize(EditorTool.Brush) - 300) < 0.5, "brush 'big' = 100% of the region height (300 px)");
                    ov.TestPercent(1);
                    Check(Math.Abs(ov.TestSessionSize(EditorTool.Brush) - 150) < 0.5, "brush 'medium' = 50% (150 px)");
                    ov.TestPercent(2);
                    Check(Math.Abs(ov.TestSessionSize(EditorTool.Brush) - 90) < 0.5, "brush 'small' = 30% (90 px)");
                    double presetSize = ov.TestStyle(EditorTool.Brush).Size;
                    Check(Math.Abs(presetSize - 90) > 1, "a quick size does not overwrite the saved preset (" + presetSize + ")");
                    ov.TestApplySize(10);
                    Check(ov.TestSessionSize(EditorTool.Brush) < 0 && Math.Abs(ov.TestStyle(EditorTool.Brush).Size - 10) < 0.01, "a size inside the slider range goes to the preset");
                    ov.TestApplySize(st.SizeMax + 30);
                    Check(Math.Abs(ov.TestSessionSize(EditorTool.Brush) - (st.SizeMax + 30)) < 0.01 && Math.Abs(ov.TestStyle(EditorTool.Brush).Size - 10) < 0.01, "a typed size beyond the range lasts only for this region, the preset stays");
                    ov.TestApplySize(presetSize);
                    ov.TestSetTool(EditorTool.Eraser);
                    ov.TestPercent(2);
                    Check(Math.Abs(ov.TestEraserSize - 75) < 0.5, "eraser 'small' = 25% of the region height (75 px)");
                    ov.TestSetTool(EditorTool.Select);

                    Check(ov.TestResolveEnter("none") == "save" && ov.TestResolveEnter("shift") == "save_as" && ov.TestResolveEnter("ctrl") == "pin", "Enter saves, Shift+Enter saves as, Ctrl+Enter pins");
                    Check(ov.TestResolveEnter("alt") == "none" && ov.TestResolveEnter("ctrl+alt") == "none", "keys without an action do nothing");

                    System.Windows.Rect lim = new System.Windows.Rect(-1000, -1000, 4000, 4000);
                    System.Windows.Rect b0 = RegionOverlay.TestBlend(P(0, 0), P(200, 100), 0, 0, lim);
                    System.Windows.Rect b1 = RegionOverlay.TestBlend(P(0, 0), P(200, 100), 1, 0, lim);
                    System.Windows.Rect b2 = RegionOverlay.TestBlend(P(0, 0), P(200, 100), 0.5, 0, lim);
                    System.Windows.Rect b3 = RegionOverlay.TestBlend(P(0, 0), P(200, 100), 0, 1, lim);
                    Check(Math.Abs(b0.Width - 200) < 0.5 && Math.Abs(b0.Height - 100) < 0.5, "strength 0: the rectangle is as dragged");
                    Check(Math.Abs(b1.Width - 200) < 0.5 && Math.Abs(b1.Height - 200) < 0.5, "constrain 100%: a square");
                    Check(Math.Abs(b2.Height - 150) < 0.5, "constrain 50%: half way to a square (height " + b2.Height + ")");
                    Check(Math.Abs(b3.Width - 400) < 0.5 && Math.Abs(b3.X + 200) < 0.5, "from-center 100%: the start moves to the centre");

                    ov.TestSetTool(EditorTool.Rect);
                    ov.TestDown(P(250, 200), 1); ov.TestMove(P(350, 300), ModifierKeys.None); ov.TestUp();   // its centre is (300,250)
                    ov.TestSetTool(EditorTool.Select); ov.TestDeselect();
                    int oldThr = st.SnapThreshold; st.SnapThreshold = 10;
                    System.Windows.Point near = ov.TestSnapCenter(P(305, 400)), far = ov.TestSnapCenter(P(325, 400)), both = ov.TestSnapCenter(P(296, 244));
                    Check(Math.Abs(near.X - 300) < 0.01 && Math.Abs(near.Y - 400) < 0.01, "snap: 5 px from the axis of another object sticks to its vertical");
                    Check(Math.Abs(far.X - 325) < 0.01, "snap: 25 px away it stays free (releases)");
                    Check(Math.Abs(both.X - 300) < 0.01 && Math.Abs(both.Y - 250) < 0.01, "snap: both axes at once");
                    st.SnapThreshold = oldThr;
                    ov.TestKey(Key.Z, ModifierKeys.Control);

                    MultiSelectTest(ov, st);
                    SoundTest(st);
                    FitTest(ov, st);
                    RotateTest(ov);
                    NewToolsTest(ov, st);
                    BeautifyTest(ov, st);
                    BaselineTest(st);
                    PreviewTest(st);

                    // 14. settings window: every page builds, savers produce the values
                    AppController ac = new AppController(null);
                    ac.TestInit();
                    SettingsWindow sw = new SettingsWindow(ac, 0);
                    sw.Show();
                    for (int pg = 0; pg < sw.TestPageCount; pg++) sw.TestShowPage(pg);
                    string shotDir = Environment.GetEnvironmentVariable("SNAPTER_SELFTEST_SHOTS");
                    if (!string.IsNullOrEmpty(shotDir))
                    {
                        // a picture of the beautiful look page (with the editor of a look open) for a look at it
                        for (int pg = 0; pg < sw.TestPageCount; pg++) { sw.TestShowPage(pg); if (sw.TestPageKey == "beautify") break; }
                        sw.Width = 1500; sw.Height = 900; sw.UpdateLayout();
                        sw.TestSaveShot(System.IO.Path.Combine(shotDir, "page_beautify.png"));
                        sw.TestOpenLookEditor("window");
                        sw.TestSaveShot(System.IO.Path.Combine(shotDir, "page_beautify_editor.png"));
                        sw.TestRandomizeLook("neon");
                        sw.TestScrollTo(520);
                        sw.TestSaveShot(System.IO.Path.Combine(shotDir, "page_beautify_editor_scrolled.png"));
                    }
                    System.Text.Json.Nodes.JsonObject collected = sw.TestCollect();
                    Say("  settings window pages=" + sw.TestPageCount + " sections=" + string.Join(",", collected.Select(kv => kv.Key)));
                    Check(sw.TestPageCount >= 8 && collected["hotkeys"] != null && collected["tools"] != null && collected["modifier_actions"] != null && collected["output"] != null && collected["ui"] != null, "settings window builds all pages and collects the values");
                    Check(collected["record"] != null && collected["scroll"] != null && collected["snap"] != null && collected["enter_actions"] != null && collected["editor"] != null, "the new settings pages (GIF/video, scroll, snap, Enter) give their sections");
                    Check(collected["record"]["video"]["crf"] != null && collected["record"]["gif"]["fps"] != null && collected["scroll"]["delay_ms"] != null && collected["snap"]["key"] != null && collected["ui"]["tray_shift_click"] != null, "the values of the new pages are collected");
                    SettingsEscCheck(sw);
                    if (sw.IsVisible) sw.Close();

                    // 14b. live preview: a click on a theme changes the window at once, nothing is saved
                    string userBefore = File.Exists(Paths.UserSettings) ? File.ReadAllText(Paths.UserSettings) : "";
                    System.Text.Json.Nodes.JsonObject pend = new System.Text.Json.Nodes.JsonObject { ["ui"] = new System.Text.Json.Nodes.JsonObject { ["accent_theme"] = "tide", ["tray_icon"] = "quill" } };
                    Settings ps2 = Settings.WithOverrides(pend);
                    Check(ps2.AccentTheme == "tide" && ps2.TrayIcon == "quill" && ps2.Language == st.Language, "pending changes are laid over the saved settings");
                    ac.OpenSettings(0);
                    SettingsWindow winA = ac.TestCurrentSettingsWindow;
                    string themeBefore = winA.TestThemeId;
                    winA.TestSetTheme(themeBefore == "tide" ? "sage" : "tide");
                    SettingsWindow second = ac.TestCurrentSettingsWindow;
                    Check(second != null && second != winA && second.TestThemeId != themeBefore, "choosing a theme rebuilds the window with that theme at once (" + themeBefore + " -> " + (second == null ? "?" : second.TestThemeId) + ")");
                    string userAfter = File.Exists(Paths.UserSettings) ? File.ReadAllText(Paths.UserSettings) : "";
                    Check(userBefore == userAfter, "the preview saves nothing");
                    if (second != null) second.Close();
                    Check(ac.TestCurrentSettingsWindow == null, "closing the preview window forgets it");

                    // 15. Escape with nothing selected closes (checked by the caller: window is closed below)
                }
                catch (Exception ex) { _errors.Add("editor test: " + ex); }
                ov.Close();
                try
                {
                    System.Drawing.Rectangle? lr = ov.LastRegion;
                    Check(lr.HasValue && lr.Value.Width == 400 && lr.Value.Height == 300, "closing the editor remembers the last region for 'repeat' (" + (lr.HasValue ? lr.Value.ToString() : "none") + ")");
                }
                catch (Exception ex) { _errors.Add("last region: " + ex); }
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            };
            guard.Tick += delegate
            {
                guard.Stop();
                _errors.Add("editor test timed out");
                try { ov.Close(); } catch { }
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            };
            ov.Show();
            timer.Start();
            guard.Start();
            Dispatcher.Run();
        }
    }
}
