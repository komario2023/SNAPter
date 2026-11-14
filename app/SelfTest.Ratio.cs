using System;
using System.Linq;
using System.Windows;
namespace Snapter
{
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ aspect ratios on Tab
        private static void RatioTest(RegionOverlay ov)
        {
            Say("aspect ratio test:");
            var list = RegionOverlay.ParseRatios("free, 16:9, A4, 2.39:1, bad, 4/3");
            Check(list.Count == 5 && list[0].Free && Math.Abs(list[1].V - 16.0 / 9) < 0.001 && list[2].Paper && Math.Abs(list[2].V - 0.7071) < 0.001 && Math.Abs(list[3].V - 2.39) < 0.001 && Math.Abs(list[4].V - 4.0 / 3) < 0.001,
                "the ratio list is parsed (free, 16:9, A4, 2.39:1, 4/3; the broken entry is ignored)");

            ov.TestSetRatios("16:9", 0, true);
            Rect r = ov.TestRatioDrag(new Point(100, 100), new Point(500, 200));
            Check(Math.Abs(r.Width / r.Height - 16.0 / 9) < 0.01 && r.Width >= 399, "16:9: a wide drag gives a 16:9 frame that covers the pointer (" + (int)r.Width + "x" + (int)r.Height + ")");
            r = ov.TestRatioDrag(new Point(100, 100), new Point(200, 500));
            Check(Math.Abs(r.Width / r.Height - 9.0 / 16) < 0.01 && r.Height >= 399, "pulled downwards the frame jumps to 9:16 (" + (int)r.Width + "x" + (int)r.Height + ")");
            r = ov.TestRatioDrag(new Point(100, 100), new Point(560, 300));
            Check(Math.Abs(r.Width / r.Height - 16.0 / 9) < 0.01, "and back to 16:9 when the hand goes sideways again");
            ov.TestSetRatios("4:3", 0, true);
            r = ov.TestRatioDrag(new Point(100, 100), new Point(200, 500));
            Check(Math.Abs(r.Width / r.Height - 3.0 / 4) < 0.01, "4:3 pulled downwards becomes 3:4");
            ov.TestSetRatios("1:1", 0, true);
            r = ov.TestRatioDrag(new Point(100, 100), new Point(200, 500));
            Check(Math.Abs(r.Width - r.Height) < 0.5, "a square stays a square");
            ov.TestSetRatios("16:9", 0, false);
            r = ov.TestRatioDrag(new Point(100, 100), new Point(200, 500));
            Check(Math.Abs(r.Width / r.Height - 16.0 / 9) < 0.01, "with the flip switched off the orientation stays as listed");

            ov.TestSetRatios("16:9", 0, true);
            Rect big = ov.TestRatioDrag(new Point(100, 100), new Point(100000, 100000));
            Check(big.Left >= 0 && big.Top >= 0 && Math.Abs(big.Width / big.Height - 16.0 / 9) < 0.01, "a frame never leaves the screen and keeps its ratio (" + (int)big.Width + "x" + (int)big.Height + ")");

            Rect start = new Rect(100, 100, 320, 180);
            foreach (int h in new int[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            {
                Rect rr = ov.TestRatioResize(start, h, new Point(start.X + start.Width * (h == 0 || h == 6 || h == 7 ? -0.3 : 1.4), start.Y + start.Height * (h == 0 || h == 1 || h == 2 ? -0.3 : 1.4)));
                Check(rr.Width > 10 && Math.Abs(rr.Width / rr.Height - 16.0 / 9) < 0.02, "stretching by handle " + h + " keeps 16:9 (" + (int)rr.Width + "x" + (int)rr.Height + ")");
            }

            ov.TestSetRatios("free,16:9,A4", 0, true);
            ov.TestCycleRatio(false);
            Check(ov.TestRatioIndex == 1 && ov.TestRatioLabel() == "16:9", "Tab goes to the next ratio (" + ov.TestRatioLabel() + ")");
            ov.TestCycleRatio(false);
            Check(ov.TestRatioIndex == 2 && ov.TestRatioLabel().StartsWith("A4"), "then A4");
            ov.TestCycleRatio(false);
            Check(ov.TestRatioIndex == 0, "and round to the start of the list");
            ov.TestCycleRatio(true);
            Check(ov.TestRatioIndex == 2, "Shift+Tab goes back");
            ov.TestSetRatios("free", 0, true);
        }
    }
}
