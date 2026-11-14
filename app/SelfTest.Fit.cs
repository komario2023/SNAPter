using System;
using System.Linq;
using System.Windows;
namespace Snapter
{
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ panels for a big region: lines, shrinking, inside the region, the corner key
        private static void FitTest(RegionOverlay ov, Settings st)
        {
            Say("panel fit test:");
            string[] known = { "presets", "colors", "recent", "shape", "sliders", "extra", "copy", "save", "actions2", "close" };
            var parsed = RegionOverlay.ParsePropsLayout("colors, presets | bogus,sliders", known);
            Check(parsed.Count == 2 && parsed[0].SequenceEqual(new[] { "colors", "presets" }) && parsed[1].Contains("sliders") && !parsed[1].Contains("bogus"), "the line layout is parsed (unknown names are ignored)");
            Check(!parsed.SelectMany(l => l).Contains("copy") && !parsed.SelectMany(l => l).Contains("close"), "Copy, Save and Close can be left out of the layout");
            var oldName = RegionOverlay.ParsePropsLayout("colors,actions,close", known);
            Check(oldName.Count == 1 && oldName[0].SequenceEqual(new[] { "colors", "copy", "save", "close" }), "the old group name 'actions' still means Copy + Save");
            Check(RegionOverlay.ParsePropsLayout("-", known).Count == 0, "the layout '-' has no groups at all (the bottom panel is not shown)");
            var oneLine = RegionOverlay.ParsePropsLayout("presets,colors,recent,shape,sliders,extra,copy,save,actions2,close", known);
            Check(oneLine.Count == 1 && oneLine[0].Count == 10, "everything can be put on one line");

            // the drag editor of the settings: the model behind it
            System.Collections.Generic.List<System.Collections.Generic.List<string>> ml; System.Collections.Generic.List<string> mp;
            FitLayoutModel.Parse("presets,colors,recent,shape,sliders|extra|copy,save,actions2,close", out ml, out mp);
            Check(ml.Count == 3 && mp.Count == 0, "the editor starts with the three default lines and nothing in the pool");
            Check(FitLayoutModel.Move(ml, mp, "recent", -1, 0) && mp.Contains("recent") && !ml[0].Contains("recent"), "dragging a group to 'not shown' takes it off the lines");
            Check(FitLayoutModel.Move(ml, mp, "close", -1, 0) && mp.Contains("close") && !ml[2].Contains("close"), "Close can be dragged away too");
            FitLayoutModel.Move(ml, mp, "close", 2, 0);
            Check(FitLayoutModel.Move(ml, mp, "recent", 1, 0) && ml[1][0] == "recent" && mp.Count == 0, "a group from the pool goes to the start of a line");
            Check(FitLayoutModel.Move(ml, mp, "close", ml.Count, 0) && ml.Count == 4 && ml[3].SequenceEqual(new[] { "close" }), "dropping on 'new line' makes a new line");
            Check(FitLayoutModel.Move(ml, mp, "colors", 0, 4) && ml[0].Last() == "colors" && ml[0].Count == 4, "inside one line a group goes to the place it was dropped (" + FitLayoutModel.ToText(ml) + ")");
            FitLayoutModel.Move(ml, mp, "extra", 0, 0); FitLayoutModel.Move(ml, mp, "recent", 0, 0);
            Check(!ml.Any(l => l.Count == 0), "a line left empty disappears");
            System.Collections.Generic.List<System.Collections.Generic.List<string>> back; System.Collections.Generic.List<string> bp;
            FitLayoutModel.Parse(FitLayoutModel.ToText(ml), out back, out bp);
            Check(FitLayoutModel.ToText(back) == FitLayoutModel.ToText(ml), "the text of the layout gives the same lines back");

            st.FitEnabled = true; st.FitFallback = "inside"; st.StartCorner = "bottom_right";
            Rect work = ov.TestWorkArea;
            double m = 10;

            // a small region: the panels stay outside, as before
            ov.TestSetSelection(new Rect(work.X + 100, work.Y + 100, 400, 300));
            Rect sel = ov.TestSel, b = ov.TestPanelB, a = ov.TestPanelRectA;
            Check(!b.IntersectsWith(sel) && !a.IntersectsWith(sel), "a small region: both panels are outside it");

            // little room below: the bottom panel gets lower (fewer lines / smaller) but stays outside
            ov.TestSetSelection(new Rect(work.X + 100, work.Y + 40, 800, work.Height - 40 - 90));
            sel = ov.TestSel; b = ov.TestPanelB;
            Check(!b.IntersectsWith(sel) && b.Top >= sel.Bottom && b.Bottom <= work.Bottom + 0.5, "90 px below the region: the bottom panel still fits outside (" + (int)b.Height + " px high)");

            // a region over the whole monitor: both panels go inside, the bottom one into the bottom right corner
            ov.TestSetSelection(new Rect(work.X, work.Y, work.Width, work.Height));
            sel = ov.TestSel; b = ov.TestPanelB; a = ov.TestPanelRectA;
            Check(sel.Contains(b) && sel.Contains(a) && !a.IntersectsWith(b), "the region over the whole monitor: the panels are inside it and do not overlap");
            Check(Math.Abs(b.Right - (sel.Right - m)) < 1.5 && Math.Abs(b.Bottom - (sel.Bottom - m)) < 1.5, "the bottom panel starts in the bottom right corner");
            Check(b.Width <= sel.Width - 2 * m + 1, "inside, the panel is not wider than the region");

            // the corner key: bottom left, top left, top right, and round again
            ov.TestCycleCorner(); b = ov.TestPanelB;
            Check(Math.Abs(b.Left - (sel.Left + m)) < 1.5 && Math.Abs(b.Bottom - (sel.Bottom - m)) < 1.5, "the key moves the panel to the bottom left corner");
            ov.TestCycleCorner(); b = ov.TestPanelB;
            Check(Math.Abs(b.Left - (sel.Left + m)) < 1.5 && Math.Abs(b.Top - (sel.Top + m)) < 1.5, "the next press: the top left corner");
            Check(!ov.TestPanelRectA.IntersectsWith(b), "the tool panel moves out of the way");
            ov.TestCycleCorner(); b = ov.TestPanelB;
            Check(Math.Abs(b.Right - (sel.Right - m)) < 1.5 && Math.Abs(b.Top - (sel.Top + m)) < 1.5, "then the top right corner");
            ov.TestCycleCorner(); b = ov.TestPanelB;
            Check(Math.Abs(b.Right - (sel.Right - m)) < 1.5 && Math.Abs(b.Bottom - (sel.Bottom - m)) < 1.5, "and round to the bottom right again");

            // a narrow region with no room outside: the inside panel may be wider than the region (the width of the screen), centered on it, but stays on the screen
            ov.TestSetSelection(new Rect(work.X, work.Y, 600, work.Height));
            sel = ov.TestSel; b = ov.TestPanelB;
            Check(b.Left >= work.Left - 1 && b.Right <= work.Right + 1, "a narrow region: the inside panel stays on the screen (" + (int)b.Width + " of " + (int)sel.Width + ")");
            Check(b.Width > 300, "a narrow region: the inside panel keeps its groups (it is not cut to a few buttons)");

            // the profiles on a key: the order, the minimal profile has no tools, the way back; the names of the keys
            System.Windows.Input.Key pk;
            Check(RegionOverlay.ParseKeyName("Oem3", out pk) && pk == System.Windows.Input.Key.Oem3, "the key name Oem3 (the key left of 1) is understood");
            Check(RegionOverlay.ParseKeyName("Tab", out pk) && pk == System.Windows.Input.Key.Tab && RegionOverlay.ParseKeyName("q", out pk) && pk == System.Windows.Input.Key.Q && RegionOverlay.ParseKeyName("F2", out pk), "other key names are understood (Tab, a letter, F2)");
            Check(!RegionOverlay.ParseKeyName("none", out pk) && !RegionOverlay.ParseKeyName("", out pk), "none means no key");
            System.Windows.Input.ModifierKeys pm;
            Check(RegionOverlay.ParseCombo("Ctrl+Shift+S", out pm, out pk) && pk == System.Windows.Input.Key.S && pm == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift), "the shortcut Ctrl+Shift+S (Save as) is understood");
            Check(RegionOverlay.ParseCombo("Alt+F3", out pm, out pk) && pk == System.Windows.Input.Key.F3 && pm == System.Windows.Input.ModifierKeys.Alt && !RegionOverlay.ParseCombo("none", out pm, out pk), "other shortcuts are understood, none means off");
            Settings fresh = new Settings();
            Check(fresh.ProfileKey == "Oem3" && fresh.RatioKey == "Tab" && fresh.ProfileOrder == "", "the keys of the profiles and of the ratios have their defaults");
            // the bottom panel layout of each profile (the mode "own for each profile")
            Check(Settings.BuiltInPropsLayout("ascetic").IndexOf('|') < 0 && Settings.BuiltInPropsLayout("minimal") == "copy,save,close" && Settings.BuiltInPropsLayout("full").Contains("actions2"), "built-in layouts: ascetic is one line, minimal is Copy / Save / Close, full has everything");
            Settings shared = Settings.FromRoot(new System.Text.Json.Nodes.JsonObject());
            Check(shared.PropsMode == "shared" && shared.PropsLayoutFor("minimal") == shared.PropsLayout, "by default the layout is one for all profiles");
            Settings own = Settings.FromRoot(new System.Text.Json.Nodes.JsonObject { ["layout"] = new System.Text.Json.Nodes.JsonObject { ["props_mode"] = "profile", ["profile_layouts"] = new System.Text.Json.Nodes.JsonObject { ["ascetic"] = "colors,copy" } } });
            Check(own.PropsMode == "profile" && own.PropsLayoutFor("ascetic") == "colors,copy" && own.PropsLayoutFor("minimal") == "copy,save,close" && own.PropsLayoutFor("current") == own.PropsLayout, "own layout per profile: the owner's text wins, otherwise the built-in one, the step 'current' uses the common layout");
            System.Collections.Generic.List<string> prof = new System.Collections.Generic.List<string> { "current", "minimal", "current" };
            ov.TestSetProfiles(prof, n => n == "minimal" ? Settings.WithOverrides(new System.Text.Json.Nodes.JsonObject { ["startup"] = new System.Text.Json.Nodes.JsonObject { ["minimal_ui_mode"] = true } }) : st);
            bool editorBefore = ov.TestEditor;
            ov.TestCycleProfile();
            Check(ov.TestProfileIndex == 1 && !ov.TestEditor && !ov.TestToolButton(EditorTool.Arrow) && ov.TestPanelsVisible, "the profile key switches to the minimal profile: no tools, the panel of actions stays");
            ov.TestCycleProfile(); ov.TestCycleProfile();
            Check(ov.TestProfileIndex == 0 && ov.TestEditor == editorBefore, "and round to the first profile again");
            ov.TestClearProfiles();

            // the old behaviour is still there
            st.FitEnabled = false;
            ov.TestSetSelection(new Rect(work.X + 100, work.Y + 100, 400, 300));
            Check(!ov.TestPanelB.IntersectsWith(ov.TestSel), "with the fit switched off a small region still has its panels outside");
            st.FitEnabled = true;
            ov.TestSetSelection(new Rect(100, 100, 400, 300));
            RatioTest(ov);                                           // aspect ratios on Tab (SelfTest.Ratio.cs)
        }
    }
}
