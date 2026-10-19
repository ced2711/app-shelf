// Base for custom-drawn scrolling views: a slim overlay scrollbar that widens on hover, wheel and drag.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace AppShelf
{
    class ScrollPane : Control
    {
        protected int scroll, contentHeight;
        bool barHot, barDrag;
        int dragOffset;

        public ScrollPane()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected int MaxScroll { get { return Math.Max(0, contentHeight - ClientSize.Height); } }

        protected void ClampScroll() { scroll = Math.Max(0, Math.Min(MaxScroll, scroll)); }

        protected void ScrollTo(int y)
        {
            int old = scroll;
            scroll = y;
            ClampScroll();
            if (scroll != old) { OnScrolled(); Invalidate(); }
        }

        protected virtual void OnScrolled() { }

        protected void EnsureVisible(int top, int bottom)
        {
            if (top < scroll) ScrollTo(top);
            else if (bottom > scroll + ClientSize.Height) ScrollTo(bottom - ClientSize.Height);
        }

        Rectangle BarTrack { get { return new Rectangle(ClientSize.Width - Ui.S(12), Ui.S(4), Ui.S(12), ClientSize.Height - Ui.S(8)); } }

        Rectangle BarThumb
        {
            get
            {
                var t = BarTrack;
                if (contentHeight <= ClientSize.Height) return Rectangle.Empty;
                int h = Math.Max(Ui.S(36), (int)((long)t.Height * ClientSize.Height / contentHeight));
                int y = t.Top + (int)((long)(t.Height - h) * scroll / Math.Max(1, MaxScroll));
                return new Rectangle(t.X, y, t.Width, h);
            }
        }

        protected bool OverBar(Point p) { return !BarThumb.IsEmpty && BarTrack.Contains(p); }
        protected bool BarActive { get { return barDrag; } }

        protected void PaintBar(Graphics g)
        {
            var thumb = BarThumb;
            if (thumb.IsEmpty) return;
            bool wide = barHot || barDrag;
            int w = wide ? Ui.S(8) : Ui.S(4);
            var tr = new Rectangle(thumb.Right - w - Ui.S(2), thumb.Top, w, thumb.Height);
            if (wide) Theme.FillRound(g, Theme.Mix(Theme.Bg, Theme.Line, 0.6f), new Rectangle(tr.X, BarTrack.Top, w, BarTrack.Height), w / 2f);
            Theme.FillRound(g, wide ? Theme.Sub : Theme.Faint, tr, w / 2f);
        }

        void DragBar(int y)
        {
            var t = BarTrack;
            int h = BarThumb.Height;
            float f = (y - dragOffset - t.Top) / (float)Math.Max(1, t.Height - h);
            ScrollTo((int)(f * MaxScroll));
        }

        // Returns true when the press started a scrollbar drag (subclasses then ignore it).
        protected bool BarMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !OverBar(e.Location)) return false;
            var t = BarThumb;
            if (t.Contains(e.Location)) dragOffset = e.Y - t.Top;
            else { dragOffset = t.Height / 2; DragBar(e.Y); }
            barDrag = true;
            Capture = true;
            Invalidate();
            return true;
        }

        // Returns true while dragging the bar.
        protected bool BarMouseMove(MouseEventArgs e)
        {
            if (barDrag) { DragBar(e.Y); return true; }
            bool b = OverBar(e.Location);
            if (b != barHot) { barHot = b; Invalidate(); }
            return false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (barDrag) { barDrag = false; Capture = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (barHot && !barDrag) { barHot = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(scroll - e.Delta * Ui.S(110) / 120);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ClampScroll();
        }
    }
}
