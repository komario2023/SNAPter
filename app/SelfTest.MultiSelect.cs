using System;
using System.Linq;
using System.Windows.Input;
namespace Snapter
{
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ multi-select (Shift/Ctrl + click, rubber band, group actions)
        private static void MultiSelectTest(RegionOverlay ov, Settings st)
        {
            System.Windows.Point P(double x, double y) { return new System.Windows.Point(x, y); }
            Say("multi-select test:");
            ov.TestSetSelection(new System.Windows.Rect(100, 100, 400, 300));
            ov.TestClearAll();
            ov.TestSetTool(EditorTool.Steps);
            foreach (System.Windows.Point pt in new[] { P(150, 150), P(250, 150), P(350, 150), P(150, 250), P(250, 250) })
            { ov.TestDown(pt, 1); ov.TestUp(); }
            Check(ov.TestItemCount == 5, "five numbered steps are placed (" + ov.TestItemCount + ")");
            ov.TestSetTool(EditorTool.Select);
            ov.TestMods = ModifierKeys.None;

            ov.TestDown(P(150, 150), 1); ov.TestUp();
            Check(ov.TestGroupCount == 1 && ov.TestIsSelected(0), "a plain click selects one object");
            ov.TestMods = ModifierKeys.Shift;
            ov.TestDown(P(250, 150), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.Control;
            ov.TestDown(P(350, 150), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.None;
            Check(ov.TestGroupCount == 3 && ov.TestIsSelected(0) && ov.TestIsSelected(1) && ov.TestIsSelected(2) && !ov.TestIsSelected(3), "Shift+click and Ctrl+click add objects to the selection");

            System.Windows.Point s1 = ((StepAnnotation)ov.TestItem(0)).Pos, s3 = ((StepAnnotation)ov.TestItem(2)).Pos;
            ov.TestDown(P(250, 150), 1);
            ov.TestMove(P(270, 180), ModifierKeys.None);
            ov.TestUp();
            System.Windows.Point m1 = ((StepAnnotation)ov.TestItem(0)).Pos, m3 = ((StepAnnotation)ov.TestItem(2)).Pos;
            Check(Math.Abs(m1.X - (s1.X + 20)) < 0.5 && Math.Abs(m1.Y - (s1.Y + 30)) < 0.5 && Math.Abs(m3.X - (s3.X + 20)) < 0.5 && Math.Abs(m3.Y - (s3.Y + 30)) < 0.5, "dragging one object of the group moves all of them by the same distance");
            Check(ov.TestGroupCount == 3, "the group stays selected after the move");
            ov.TestKey(Key.Z, ModifierKeys.Control);
            System.Windows.Point u1 = ((StepAnnotation)ov.TestItem(0)).Pos;
            Check(Math.Abs(u1.X - s1.X) < 0.5 && Math.Abs(u1.Y - s1.Y) < 0.5, "one Ctrl+Z puts the whole group back");
            Check(ov.TestGroupCount == 3, "the group survives undo");

            ov.TestMods = ModifierKeys.Shift;
            ov.TestDown(P(250, 150), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.None;
            Check(ov.TestGroupCount == 2 && !ov.TestIsSelected(1), "Shift+click on a selected object takes it out of the selection");

            System.Windows.Media.Color blue = System.Windows.Media.Color.FromRgb(30, 136, 229);
            ov.TestSetColor(blue);
            Check(ov.TestItem(0).Color == blue && ov.TestItem(2).Color == blue && ov.TestItem(1).Color != blue, "a color change goes to every selected object and to nothing else");

            ov.TestKey(Key.Delete, ModifierKeys.None);
            Check(ov.TestItemCount == 3 && ov.TestGroupCount == 0, "Delete removes the whole selection");
            ov.TestKey(Key.Z, ModifierKeys.Control);
            Check(ov.TestItemCount == 5, "undo brings all the deleted objects back at once");
            ov.TestKey(Key.Y, ModifierKeys.Control);
            Check(ov.TestItemCount == 3, "redo removes them again");

            // items now: step 2 (250,150), steps (150,250) and (250,250)
            ov.TestDeselect();
            ov.TestDown(P(110, 215), 1);
            ov.TestMove(P(200, 260), ModifierKeys.None);
            ov.TestMove(P(290, 290), ModifierKeys.None);
            ov.TestUp();
            Check(ov.TestGroupCount == 2 && ov.TestIsSelected(1) && ov.TestIsSelected(2) && !ov.TestIsSelected(0), "a rubber band over empty space selects the objects inside it");
            double px = ((StepAnnotation)ov.TestItem(1)).Pos.X;
            ov.TestKey(Key.Right, ModifierKeys.Shift);
            Check(Math.Abs(((StepAnnotation)ov.TestItem(1)).Pos.X - (px + 10)) < 0.5 && Math.Abs(((StepAnnotation)ov.TestItem(2)).Pos.X - (250 + 10)) < 0.5, "Shift+arrow moves the whole selection by 10 px");

            ov.TestMods = ModifierKeys.Shift;
            ov.TestDown(P(220, 120), 1);
            ov.TestMove(P(250, 160), ModifierKeys.None);
            ov.TestMove(P(280, 200), ModifierKeys.None);
            ov.TestUp();
            ov.TestMods = ModifierKeys.None;
            Check(ov.TestGroupCount == 3, "Shift + rubber band adds to the existing selection");

            ov.TestKey(Key.D, ModifierKeys.Control);
            Check(ov.TestItemCount == 6 && ov.TestGroupCount == 3, "Ctrl+D duplicates the whole selection");

            ov.TestDown(P(450, 380), 1); ov.TestUp();
            Check(ov.TestGroupCount == 0, "a click on empty space clears the selection");
            SnapGroupTest(ov, st);
            AlignTest(ov);
            ov.TestMods = null;
            ov.TestClearAll();
        }

        /// <summary>The three steps of a "1-2-3" row: put them on one line and spread them evenly.</summary>
        private static void AlignTest(RegionOverlay ov)
        {
            System.Windows.Point P(double x, double y) { return new System.Windows.Point(x, y); }
            ov.TestClearAll();
            ov.TestSetTool(EditorTool.Steps);
            foreach (System.Windows.Point pt in new[] { P(150, 150), P(200, 180), P(400, 160) }) { ov.TestDown(pt, 1); ov.TestUp(); }
            ov.TestSetTool(EditorTool.Select);
            ov.TestMods = ModifierKeys.None;
            ov.TestDown(P(150, 150), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.Shift;
            ov.TestDown(P(200, 180), 1); ov.TestUp();
            ov.TestDown(P(400, 160), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.None;
            Check(ov.TestGroupCount == 3 && ov.TestAlignPanelVisible, "with three objects selected the align panel is shown");

            ov.TestAlign("vcenter");
            double y0 = ((StepAnnotation)ov.TestItem(0)).Pos.Y, y1 = ((StepAnnotation)ov.TestItem(1)).Pos.Y, y2 = ((StepAnnotation)ov.TestItem(2)).Pos.Y;
            Check(Math.Abs(y0 - 165) < 0.5 && Math.Abs(y1 - 165) < 0.5 && Math.Abs(y2 - 165) < 0.5, "vertical centers: the steps stand in one row (" + y0 + "," + y1 + "," + y2 + ")");
            ov.TestAlign("disth");
            double x0 = ((StepAnnotation)ov.TestItem(0)).Pos.X, x1 = ((StepAnnotation)ov.TestItem(1)).Pos.X, x2 = ((StepAnnotation)ov.TestItem(2)).Pos.X;
            Check(Math.Abs(x1 - (x0 + x2) / 2) < 0.5 && Math.Abs(x0 - 150) < 0.5 && Math.Abs(x2 - 400) < 0.5, "equal gaps: the middle step is in the middle (" + x0 + "," + x1 + "," + x2 + ")");
            ov.TestAlign("left");
            Check(Math.Abs(ov.TestItem(0).Bounds().Left - ov.TestItem(2).Bounds().Left) < 0.5, "aligning to the left edge puts the left sides on one line");
            ov.TestKey(Key.Z, ModifierKeys.Control);
            Check(Math.Abs(((StepAnnotation)ov.TestItem(1)).Pos.X - x1) < 0.5 && ov.TestGroupCount == 3, "one Ctrl+Z undoes one alignment and keeps the group");
            ov.TestDeselect();
            Check(!ov.TestAlignPanelVisible, "the align panel goes away with the selection");
            Say("  region after the align test: " + ov.TestSel + " hasSel=" + ov.TestPanelsVisible);
            ov.TestMods = null;
            ov.TestClearAll();
        }

        /// <summary>The snap key must not stick a moving group to its own members, only to objects outside it.</summary>
        private static void SnapGroupTest(RegionOverlay ov, Settings st)
        {
            System.Windows.Point P(double x, double y) { return new System.Windows.Point(x, y); }
            ov.TestClearAll();
            ov.TestSetTool(EditorTool.Steps);
            foreach (System.Windows.Point pt in new[] { P(150, 150), P(250, 250), P(400, 150) }) { ov.TestDown(pt, 1); ov.TestUp(); }
            ov.TestSetTool(EditorTool.Select);
            ov.TestMods = ModifierKeys.None;
            ov.TestDown(P(150, 150), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.Shift;
            ov.TestDown(P(250, 250), 1); ov.TestUp();
            ov.TestMods = ModifierKeys.None;
            int oldThr = st.SnapThreshold; st.SnapThreshold = 10;
            System.Windows.Point own = ov.TestSnapCenter(P(153, 200)), outer = ov.TestSnapCenter(P(403, 200));
            st.SnapThreshold = oldThr;
            Check(Math.Abs(own.X - 153) < 0.01, "snap: a member of the moving group is not a snap target (x stays " + own.X + ")");
            Check(Math.Abs(outer.X - 400) < 0.01, "snap: an object outside the group still is (x=" + outer.X + ")");
            ov.TestMods = null;
            ov.TestClearAll();
        }
    }
}
