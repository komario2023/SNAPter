using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Path = System.Windows.Shapes.Path;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
namespace Snapter
{
    internal sealed partial class RegionOverlay
    {
        // ================= multi-select: Shift / Ctrl + click, a rubber band, group move / delete / color =================
        // _selected stays the main selected object (the panels show its style); _group holds the other selected objects.
        private readonly List<Annotation> _group = new List<Annotation>();
        private List<Annotation> _groupOrig;                   // copies of the group at the start of a move
        private Rectangle _marquee;                            // the dashed rubber band
        private bool _marqueeAdd;                              // Shift / Ctrl was held at the start: add to the selection
        private List<Annotation> _marqueeBase = new List<Annotation>();

        /// <summary>For the self test: the modifier keys the overlay should see instead of the keyboard.</summary>
        internal ModifierKeys? TestMods;

        private bool GroupActive { get { return _selected != null && _group.Count > 0; } }

        private ModifierKeys CurrentMods() { return TestMods ?? Keyboard.Modifiers; }

        /// <summary>Shift or Ctrl held: a click adds the object to the selection (or takes it out).</summary>
        private bool SelectAddHeld()
        {
            return _editor && (CurrentMods() & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
        }

        /// <summary>The main object and all the others, in the order they are drawn.</summary>
        private List<Annotation> SelectedAll()
        {
            List<Annotation> r = new List<Annotation>();
            if (_selected == null) return r;
            foreach (Annotation a in _items) if (a == _selected || _group.Contains(a)) r.Add(a);
            if (!r.Contains(_selected)) r.Add(_selected);
            return r;
        }

        private bool IsSelected(Annotation a) { return a != null && (a == _selected || _group.Contains(a)); }

        /// <summary>Forget group members that are gone (deleted, undone) or became the main object.</summary>
        private void PruneGroup()
        {
            if (_selected == null) { _group.Clear(); return; }
            _group.RemoveAll(g => g == _selected || !_items.Contains(g));
        }

        /// <summary>Put the whole list into the selection (the first one becomes the main object).</summary>
        private void SetSelection(List<Annotation> list)
        {
            _group.Clear();
            _selected = list.Count > 0 ? list[0] : null;
            for (int i = 1; i < list.Count; i++) _group.Add(list[i]);
            UpdateChrome();
            RefreshPanels();
        }

        private void ToggleSelect(Annotation hit)
        {
            List<Annotation> all = SelectedAll();
            if (all.Contains(hit)) all.Remove(hit); else all.Add(hit);
            SetSelection(all);
        }

        private void DrawGroupChrome()
        {
            Brush accent = Brush(_theme.Accent);
            foreach (Annotation a in SelectedAll())
            {
                Rect b = a.Bounds();
                b.Inflate(3, 3);
                Rectangle r = new Rectangle();
                r.Width = Math.Max(1, b.Width); r.Height = Math.Max(1, b.Height);
                r.Stroke = accent;
                r.StrokeThickness = a == _selected ? 2 : 1.5;
                r.StrokeDashArray = new DoubleCollection(new double[] { 4, 3 });
                Canvas.SetLeft(r, b.X);
                Canvas.SetTop(r, b.Y);
                _chrome.Children.Add(r);
            }
        }

        /// <summary>While one object of the group is dragged, the others follow by the same distance.</summary>
        private void MoveFollowers()
        {
            if (_groupOrig == null || _orig == null || _selected == null || _groupOrig.Count != _group.Count) return;
            Point a = CenterOf(_orig), b = CenterOf(_selected);
            for (int i = 0; i < _group.Count; i++)
            {
                _group[i].CopyFrom(_groupOrig[i]);
                _group[i].Translate(b.X - a.X, b.Y - a.Y);
            }
        }

        private void DeleteGroup()
        {
            List<int> ids = SelectedAll().Select(a => a.Id).ToList();
            _items.RemoveAll(x => ids.Contains(x.Id));
            _selected = null;
            _group.Clear();
        }

        private void DuplicateGroup()
        {
            List<Annotation> copies = new List<Annotation>();
            foreach (Annotation a in SelectedAll())
            {
                Annotation d = a.Clone();
                d.Id = Annotation.NewId();
                d.Translate(14, 14);
                _items.Add(d);
                copies.Add(d);
            }
            SetSelection(copies);
        }

        private void ReorderGroup(bool toFront)
        {
            List<int> ids = SelectedAll().Select(a => a.Id).ToList();
            List<Annotation> moved = _items.Where(x => ids.Contains(x.Id)).ToList();
            _items.RemoveAll(x => ids.Contains(x.Id));
            if (toFront) _items.AddRange(moved); else _items.InsertRange(0, moved);
        }

        // ---- the rubber band: drag over empty space with the select tool ----
        private void BeginMarquee(Point p, bool add)
        {
            _mode = DragMode.Marquee;
            _start = ClampToSel(p);
            _marqueeAdd = add;
            _marqueeBase = add ? SelectedAll() : new List<Annotation>();
            if (_marquee == null)
            {
                WpfColor ac = _theme.Accent;
                _marquee = new Rectangle
                {
                    Stroke = Brush(ac), StrokeThickness = 1, StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
                    Fill = new SolidColorBrush(WpfColor.FromArgb(40, ac.R, ac.G, ac.B)), IsHitTestVisible = false
                };
                _canvas.Children.Add(_marquee);
            }
            _marquee.Width = 0; _marquee.Height = 0;
            Canvas.SetLeft(_marquee, _start.X); Canvas.SetTop(_marquee, _start.Y);
            _marquee.Visibility = Visibility.Visible;
            _canvas.CaptureMouse();
        }

        private Rect MarqueeRect(Point c)
        {
            Point q = ClampToSel(c);
            return new Rect(new Point(Math.Min(_start.X, q.X), Math.Min(_start.Y, q.Y)), new Point(Math.Max(_start.X, q.X), Math.Max(_start.Y, q.Y)));
        }

        private void MarqueeMove(Point c)
        {
            Rect r = MarqueeRect(c);
            Canvas.SetLeft(_marquee, r.X); Canvas.SetTop(_marquee, r.Y);
            _marquee.Width = r.Width; _marquee.Height = r.Height;
        }

        private void MarqueeEnd()
        {
            if (_marquee == null) return;
            Rect r = new Rect(Canvas.GetLeft(_marquee), Canvas.GetTop(_marquee), _marquee.Width, _marquee.Height);   // what the last move drew
            _marquee.Visibility = Visibility.Collapsed;
            if (r.Width < 3 && r.Height < 3) return;                // a plain click: nothing to add
            List<Annotation> pick = new List<Annotation>(_marqueeBase);
            foreach (Annotation a in _items)
                if (a != _editing && !pick.Contains(a) && r.IntersectsWith(a.Bounds())) pick.Add(a);
            SetSelection(pick);
        }

        // ---- self test hooks ----
        internal int TestGroupCount { get { return SelectedAll().Count; } }
        internal bool TestIsSelected(int i) { return IsSelected(_items[i]); }
        internal void TestToggleSelect(int i) { ToggleSelect(_items[i]); }
        internal void TestClearAll() { ClearAnnotations(); }
    }
}
