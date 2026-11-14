using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;

namespace Snapter
{
    /// <summary>Two helpers of the settings window: the "Apply" button gets an orange outline while something is changed and not saved,
    /// and the search box above the pages (it keeps only the pages and rows that contain the typed words).</summary>
    internal sealed partial class SettingsWindow
    {
        private Border _applyBtn;
        private string _baselineJson;                 // the values on the pages when the window was opened (a rebuilt preview window inherits them)
        private DispatcherTimer _dirtyTimer;

        private string CollectJson()
        {
            try
            {
                JsonObject o = new JsonObject();
                foreach (Action<JsonObject> sv in _savers) { try { sv(o); } catch { } }
                return o.ToJsonString();
            }
            catch { return ""; }
        }

        private static readonly WpfColor DirtyColor = WpfColor.FromRgb(255, 138, 0);

        private void StartDirtyWatch()
        {
            Loaded += delegate
            {
                if (_baselineJson == null) _baselineJson = CollectJson();
                _lastSeen = CollectJson();
                if (_hist.Count > 0) _rebuiltSig = _lastSeen;                          // a window built again for a step: what it shows now is not a new change
                _dirtyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                _dirtyTimer.Tick += delegate { UpdateDirty(); RefreshDock(); };
                _dirtyTimer.Start();
                UpdateDirty();
            };
            Closed += delegate { if (_dirtyTimer != null) _dirtyTimer.Stop(); };
        }

        // ----------------------------------------------------------------- undo / redo of the changes that are not saved yet
        // Every stable change of the values on the pages is one step. The arrows in the title bar go back and forward through the steps: the window is
        // built again with the values of that step (the same way the theme preview does). Saving (Apply) or closing the window starts a new history.
        private static readonly List<string> _hist = new List<string>();
        private static int _histIdx = -1;
        private string _lastSeen, _rebuiltSig;
        private Border _undoBtn, _redoBtn;

        private static void ResetHistory() { _hist.Clear(); _histIdx = -1; }

        private void HistoryTick(string cur)
        {
            if (_replacing || string.IsNullOrEmpty(cur)) return;
            if (_hist.Count == 0) { _hist.Add(cur); _histIdx = 0; _rebuiltSig = null; }
            else if (cur == _lastSeen && cur != _hist[_histIdx] && cur != _rebuiltSig)
            {
                while (_hist.Count > _histIdx + 1) _hist.RemoveAt(_hist.Count - 1);    // a new change after a step back: the steps ahead are gone
                _hist.Add(cur); _histIdx = _hist.Count - 1;
                if (_hist.Count > 200) { _hist.RemoveAt(0); _histIdx--; }
                _rebuiltSig = null;
            }
            _lastSeen = cur;
            UpdateHistoryButtons();
        }

        /// <summary>Makes the values that are on the pages now a step at once (a rebuild of the window is about to happen and would swallow the change).</summary>
        private void RecordHistoryNow(string cur)
        {
            if (string.IsNullOrEmpty(cur)) return;
            if (_hist.Count == 0) _hist.Add(_baselineJson ?? cur);                  // the first step is how the window was opened
            if (_hist.Count > 0 && _histIdx < 0) _histIdx = 0;
            if (cur != _hist[_histIdx])
            {
                while (_hist.Count > _histIdx + 1) _hist.RemoveAt(_hist.Count - 1);
                _hist.Add(cur); _histIdx = _hist.Count - 1;
                if (_hist.Count > 200) { _hist.RemoveAt(0); _histIdx--; }
            }
            _rebuiltSig = cur;
        }

        private void UpdateHistoryButtons()
        {
            if (_undoBtn != null) { _undoBtn.Opacity = _histIdx > 0 ? 1 : 0.3; _undoBtn.ToolTip = _i.T(_histIdx > 0 ? "settings.undo_tip" : "settings.undo_none"); }
            if (_redoBtn != null) { _redoBtn.Opacity = _histIdx >= 0 && _histIdx < _hist.Count - 1 ? 1 : 0.3; _redoBtn.ToolTip = _i.T(_histIdx >= 0 && _histIdx < _hist.Count - 1 ? "settings.redo_tip" : "settings.redo_none"); }
        }

        private void HistoryStep(int dir)
        {
            string cur = CollectJson();
            HistoryTick(cur); HistoryTick(cur);                                    // what was changed a moment ago becomes a step first
            int to = _histIdx + dir;
            if (to < 0 || to >= _hist.Count) return;
            _histIdx = to;
            JsonObject pending = null;
            try { pending = JsonNode.Parse(_hist[to]) as JsonObject; } catch { }
            if (pending == null) return;
            _rebuiltSig = _hist[to];
            Rebuild(pending);
        }

        private void UpdateDirty()
        {
            if (_applyBtn == null || _baselineJson == null) return;
            string curJson = CollectJson();
            HistoryTick(curJson);
            bool dirty = curJson != _baselineJson;
            _applyBtn.BorderBrush = dirty ? new SolidColorBrush(DirtyColor) : Brushes.Transparent;
            _applyBtn.BorderThickness = new Thickness(dirty ? 2.5 : 1);
            _applyBtn.ToolTip = dirty ? _i.T("settings.unsaved_tip") : null;
        }

        // ----------------------------------------------------------------- self test hooks (a picture of the window for a look at it)
        internal string TestPageKey { get { return CurrentPageKey(); } }
        internal void TestOpenLookEditor(string id) { OpenLookEditor(id); }
        internal void TestScrollTo(double y) { if (_scroll != null) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(y); UpdateLayout(); } }
        internal void TestRandomizeLook(string id) { RandomizeLook(id); }
        internal void TestSaveShot(string file)
        {
            UpdateLayout();
            FrameworkElement c = Content as FrameworkElement;
            if (c == null) return;
            int w = (int)Math.Max(1, c.ActualWidth), h = (int)Math.Max(1, c.ActualHeight);
            System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(c) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
            }
            rtb.Render(dv);
            System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (System.IO.FileStream fs = System.IO.File.Create(file)) enc.Save(fs);
        }

        // ----------------------------------------------------------------- the current profile (shown in the title bar)
        /// <summary>Which profile the saved tools and panel look match: a built-in one, one of the user's, or none (own setup).</summary>
        private string CurrentProfileText()
        {
            string key = CurrentProfileKey();
            string name = key == null ? _i.T("settings.profile_custom") : _app.ProfileLabel(key);
            return _i.T("settings.profile_current") + ": " + name;
        }

        /// <summary>The key of the profile that matches the saved setup now, or null.</summary>
        private string CurrentProfileKey()
        {
            return _app.ProfileKeyOf(_s);
        }

        // ----------------------------------------------------------------- search
        private TextBox _search;

        private FrameworkElement BuildSearchBox()
        {
            _search = new TextBox { Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 8), Background = Br(_t.Bg), Foreground = Br(_t.Text), BorderBrush = Br(_t.Border), CaretBrush = Br(_t.Text), VerticalContentAlignment = VerticalAlignment.Center };
            TextBlock hint = new TextBlock { Text = _i.T("settings.search"), Foreground = Br(_t.TextSecondary), IsHitTestVisible = false, Margin = new Thickness(9, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
            Grid g = new Grid();
            g.Children.Add(_search);
            g.Children.Add(hint);
            _search.TextChanged += delegate { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplySearch(); };
            return g;
        }

        private static void CollectText(DependencyObject d, List<string> into)
        {
            TextBlock tb = d as TextBlock;
            if (tb != null && !string.IsNullOrEmpty(tb.Text)) into.Add(tb.Text);
            ContentControl cc = d as ContentControl;
            if (cc != null && cc.Content is string) into.Add((string)cc.Content);
            FrameworkElement fe = d as FrameworkElement;
            if (fe != null && fe.ToolTip != null)
            {
                TextBlock tt = fe.ToolTip as TextBlock;
                if (tt != null) into.Add(tt.Text); else if (fe.ToolTip is string) into.Add((string)fe.ToolTip);
            }
            foreach (object c in LogicalTreeHelper.GetChildren(d))
            {
                DependencyObject cd = c as DependencyObject;
                if (cd != null) CollectText(cd, into);
            }
        }

        private static bool Matches(UIElement el, string[] words)
        {
            List<string> texts = new List<string>();
            CollectText(el, texts);
            string all = string.Join(" ", texts).ToLowerInvariant();
            return words.All(w => all.Contains(w));
        }

        private string[] SearchWords()
        {
            return (_search == null ? "" : _search.Text).ToLowerInvariant().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Hides the pages without a match in the list and the rows without a match on the open page; an empty box shows everything.</summary>
        private void ApplySearch()
        {
            string[] words = SearchWords();
            int firstHit = -1;
            for (int k = 0; k < _pages.Count; k++)
            {
                bool hit = words.Length == 0 || Matches(_pages[k].Value, words) || _pages[k].Key.ToLowerInvariant().Contains(string.Join(" ", words));
                _nav[k].Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                if (hit && firstHit < 0) firstHit = k;
            }
            if (words.Length > 0 && firstHit >= 0 && _nav[_current].Visibility != Visibility.Visible) ShowPage(firstHit);
            FilterRows(words);
        }

        private void FilterRows(string[] words)
        {
            foreach (KeyValuePair<string, StackPanel> kv in _pages)
            {
                StackPanel page = kv.Value;
                bool titleMatch = words.Length > 0 && kv.Key.ToLowerInvariant().Contains(string.Join(" ", words));
                for (int k = 1; k < page.Children.Count; k++)          // child 0 is the title of the page
                    page.Children[k].Visibility = (words.Length == 0 || titleMatch || Matches(page.Children[k], words)) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
