// The app grid: custom-drawn tiles with A-Z section headers, multi-select, keyboard navigation
// and a slim overlay scrollbar.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AppShelf
{
    class TileView : ScrollPane
    {
        class Header { public string Text; public Rectangle R; }

        List<AppEntry> items = new List<AppEntry>();
        Rectangle[] rects = new Rectangle[0];
        List<Header> headers = new List<Header>();
        readonly HashSet<AppEntry> selected = new HashSet<AppEntry>();
        int focus = -1, anchor = -1, hot = -1;
        bool grouped = true;
        readonly ToolTip tip = new ToolTip();
        int tipFor = -1;

        Font fName, fHeader, fEmpty, fEmptyIcon;
        public Bitmap Placeholder;
        public string EmptyText, EmptyGlyph;
        public float Progress = -1;     // 0..1 while loading, -1 otherwise

        public event Action<AppEntry> ItemActivated;
        public event Action SelectionChanged;

        int TileW { get { return Ui.S(112); } }
        int TileH { get { return Ui.S(118); } }
        int IconSize { get { return Ui.S(48); } }
        int PadX { get { return Ui.S(16); } }

        public TileView()
        {
            fName = Theme.MakeFont(12.5f);
            fHeader = Theme.MakeFont(14, true);
            fEmpty = Theme.MakeFont(14);
            fEmptyIcon = Theme.MakeIconFont(36);
        }

        // ---------- data ----------

        public void SetItems(List<AppEntry> list, bool group)
        {
            var keepFocus = focus >= 0 && focus < items.Count ? items[focus] : null;
            items = list;
            grouped = group;
            selected.IntersectWith(list);
            focus = keepFocus != null ? list.IndexOf(keepFocus) : -1;
            if (focus < 0 && selected.Count > 0) focus = list.IndexOf(selected.First());
            anchor = focus;
            hot = -1;
            scroll = 0;
            Relayout();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        public int Count { get { return items.Count; } }

        public List<AppEntry> Selected
        {
            get { return items.Where(a => selected.Contains(a)).ToList(); }
        }

        public AppEntry FocusedItem
        {
            get
            {
                if (focus >= 0 && focus < items.Count && selected.Contains(items[focus])) return items[focus];
                return Selected.FirstOrDefault();
            }
        }

        public void SelectFirst()
        {
            if (items.Count == 0) return;
            if (selected.Count == 0) SelectOnly(0);
            Focus();
        }

        static string GroupKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#";
            var d = name.Substring(0, 1).Normalize(NormalizationForm.FormD);
            char c = char.ToUpperInvariant(d[0]);
            return c >= 'A' && c <= 'Z' ? c.ToString() : "#";
        }

        // ---------- layout ----------

        void Relayout()
        {
            headers.Clear();
            rects = new Rectangle[items.Count];
            int avail = Math.Max(TileW, ClientSize.Width - 2 * PadX - Ui.S(6));
            // as many columns as fit at ~136 px each, never narrower than TileW; tiles stretch to fill the row
            int cols = Math.Max(1, (avail + Ui.S(136) - 1) / Ui.S(136));
            if (cols > 1 && avail / cols < TileW) cols--;
            int w = avail / cols;
            int y = Ui.S(4), col = 0;

            // '#' (digits, symbols, other scripts) first, then A-Z, each in the incoming (sorted) order.
            var order = Enumerable.Range(0, items.Count).ToList();
            if (grouped)
            {
                var keys = items.Select(a => GroupKey(a.Name)).ToArray();
                order = order.OrderBy(i => keys[i] == "#" ? " " : keys[i], StringComparer.Ordinal).ThenBy(i => i).ToList();
                var sorted = order.Select(i => items[i]).ToList();
                var keepFocus = focus >= 0 && focus < items.Count ? items[focus] : null;
                items = sorted;
                focus = keepFocus != null ? items.IndexOf(keepFocus) : -1;
                anchor = focus;
            }

            string last = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (grouped)
                {
                    string key = GroupKey(items[i].Name);
                    if (key != last)
                    {
                        if (col > 0) { y += TileH; col = 0; }
                        if (last != null) y += Ui.S(10);
                        headers.Add(new Header { Text = key, R = new Rectangle(PadX + Ui.S(8), y, avail - Ui.S(8), Ui.S(34)) });
                        y += Ui.S(38);
                        last = key;
                    }
                }
                rects[i] = new Rectangle(PadX + col * w, y, w - Ui.S(4), TileH - Ui.S(4));
                if (++col == cols) { col = 0; y += TileH; }
            }
            if (col > 0) y += TileH;
            contentHeight = y + Ui.S(12);
            ClampScroll();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Relayout();
        }

        protected override void OnScrolled() { UpdateHot(PointToClient(Cursor.Position)); }

        void EnsureVisible(int i)
        {
            if (i < 0 || i >= rects.Length) return;
            var r = rects[i];
            int top = r.Top - Ui.S(8), bottom = r.Bottom + Ui.S(8);
            // keep a section header visible when the tile is the first of its section
            foreach (var h in headers) if (h.R.Bottom <= r.Top && r.Top - h.R.Bottom < Ui.S(8)) top = h.R.Top;
            EnsureVisible(top, bottom);
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (items.Count == 0)
            {
                PaintEmpty(g);
                return;
            }

            foreach (var h in headers)
            {
                var r = h.R; r.Offset(0, -scroll);
                if (r.Bottom < 0 || r.Top > ClientSize.Height) continue;
                var size = TextRenderer.MeasureText(g, h.Text, fHeader, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, h.Text, fHeader, r, Theme.Fg, Theme.Left);
                int ly = r.Top + r.Height / 2 + 1;
                using (var p = new Pen(Theme.Line))
                    g.DrawLine(p, r.Left + size.Width + Ui.S(12), ly, r.Right - Ui.S(8), ly);
            }

            bool focused = Focused;
            var nameFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                            TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                            TextFormatFlags.NoPadding;
            int lineH = fName.Height;
            for (int i = 0; i < items.Count; i++)
            {
                var r = rects[i]; r.Offset(0, -scroll);
                if (r.Bottom < 0 || r.Top > ClientSize.Height) continue;
                var a = items[i];
                bool sel = selected.Contains(a);
                float rad = Ui.S(8);
                if (sel)
                {
                    Theme.FillRound(g, focused ? Theme.AccentSoft : Theme.SurfaceHot, r, rad);
                    if (focused) Theme.StrokeRound(g, Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.55f), r, rad);
                }
                else if (i == hot) Theme.FillRound(g, Theme.Surface, r, rad);
                if (i == focus && focused && !sel && ShowFocusCues) Theme.StrokeRound(g, Theme.Line, r, rad);

                var img = a.Icon ?? Placeholder;
                var ir = new Rectangle(r.Left + (r.Width - IconSize) / 2, r.Top + Ui.S(14), IconSize, IconSize);
                if (img != null) g.DrawImage(img, ir);
                var tr = new Rectangle(r.Left + Ui.S(6), ir.Bottom + Ui.S(8), r.Width - Ui.S(12), lineH * 2);
                TextRenderer.DrawText(g, a.Name, fName, tr, sel ? Theme.Fg : Theme.Mix(Theme.Fg, Theme.Sub, 0.15f), nameFlags);
            }

            PaintBar(g);
        }

        void PaintEmpty(Graphics g)
        {
            var c = ClientRectangle;
            int cy = c.Height / 2 - Ui.S(30);
            if (EmptyGlyph != null)
                TextRenderer.DrawText(g, EmptyGlyph, fEmptyIcon, new Rectangle(0, cy - Ui.S(50), c.Width, Ui.S(50)), Theme.Faint, Theme.Center);
            if (EmptyText != null)
                TextRenderer.DrawText(g, EmptyText, fEmpty, new Rectangle(Ui.S(20), cy + Ui.S(8), c.Width - Ui.S(40), Ui.S(26)), Theme.Sub, Theme.Center);
            if (Progress >= 0)
            {
                int w = Ui.S(220), h = Ui.S(4);
                var r = new Rectangle((c.Width - w) / 2, cy + Ui.S(48), w, h);
                Theme.FillRound(g, Theme.Line, r, h / 2f);
                if (Progress > 0) Theme.FillRound(g, Theme.Accent, new Rectangle(r.X, r.Y, Math.Max(h, (int)(w * Progress)), h), h / 2f);
            }
        }

        // ---------- hit testing ----------

        int HitTest(Point p)
        {
            var q = new Point(p.X, p.Y + scroll);
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(q)) return i;
            return -1;
        }

        void UpdateHot(Point p)
        {
            int h = ClientRectangle.Contains(p) && !OverBar(p) ? HitTest(p) : -1;
            if (h != hot) { hot = h; Invalidate(); }
            if (h != tipFor)
            {
                tipFor = h;
                tip.Hide(this);
                if (h >= 0 && IsTruncated(items[h].Name, rects[h].Width - Ui.S(12)))
                {
                    var r = rects[h];
                    tip.Show(items[h].Name, this, r.Left + Ui.S(4), r.Bottom - scroll + Ui.S(2), 4000);
                }
            }
        }

        bool IsTruncated(string text, int width)
        {
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            return TextRenderer.MeasureText(text, fName, new Size(width, int.MaxValue), flags).Height > fName.Height * 2;
        }

        // ---------- selection ----------

        void SelectOnly(int i)
        {
            selected.Clear();
            if (i >= 0) selected.Add(items[i]);
            focus = anchor = i;
            Changed(i);
        }

        void SelectRange(int from, int to, bool add)
        {
            if (!add) selected.Clear();
            for (int k = Math.Min(from, to); k <= Math.Max(from, to); k++) selected.Add(items[k]);
            focus = to;
            Changed(to);
        }

        void Changed(int ensure)
        {
            EnsureVisible(ensure);
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        public void SelectAll()
        {
            selected.UnionWith(items);
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        // ---------- mouse ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (BarMouseDown(e)) return;
            int i = HitTest(e.Location);
            bool ctrl = (ModifierKeys & Keys.Control) != 0, shift = (ModifierKeys & Keys.Shift) != 0;
            if (e.Button == MouseButtons.Right)
            {
                if (i >= 0 && !selected.Contains(items[i])) SelectOnly(i);
                else if (i >= 0) { focus = i; Invalidate(); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (i < 0) { if (!ctrl && selected.Count > 0) SelectOnly(-1); return; }
            if (shift && anchor >= 0) SelectRange(anchor, i, ctrl);
            else if (ctrl)
            {
                if (!selected.Remove(items[i])) selected.Add(items[i]);
                focus = anchor = i;
                Changed(i);
            }
            else SelectOnly(i);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || OverBar(e.Location)) return;
            int i = HitTest(e.Location);
            if (i >= 0 && ItemActivated != null) ItemActivated(items[i]);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (BarMouseMove(e)) return;
            UpdateHot(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (BarActive) return;
            tip.Hide(this);
            tipFor = -1;
            if (hot != -1) { hot = -1; Invalidate(); }
        }

        // ---------- keyboard ----------

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: case Keys.Enter:
                    return true;
            }
            return base.IsInputKey(k);
        }

        // Next tile straight above/below: the nearest row in that direction, closest column.
        int Vertical(int from, int dir, int distance)
        {
            var r = rects[from];
            int targetY = r.Top + dir * distance, cx = r.Left + r.Width / 2, best = -1;
            long bestScore = long.MaxValue;
            for (int i = 0; i < rects.Length; i++)
            {
                int dy = (rects[i].Top - r.Top) * dir;
                if (dy <= 0) continue;
                long rowScore = Math.Abs(rects[i].Top - targetY);
                long score = rowScore * 100000 + Math.Abs(rects[i].Left + rects[i].Width / 2 - cx);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Count == 0) return;
            if (e.KeyCode == Keys.A && e.Control) { SelectAll(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Enter)
            {
                var a = FocusedItem;
                if (a != null && ItemActivated != null) ItemActivated(a);
                e.Handled = true;
                return;
            }
            if ((e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)) && ContextMenuStrip != null && selected.Count > 0)
            {
                int f = focus >= 0 ? focus : items.IndexOf(selected.First());
                var r = rects[f];
                ContextMenuStrip.Show(this, new Point(r.Left + r.Width / 2, r.Bottom - scroll - Ui.S(10)));
                e.Handled = true;
                return;
            }

            int cur = focus < 0 ? -1 : focus, next = cur;
            int page = Math.Max(TileH, ClientSize.Height - TileH);
            switch (e.KeyCode)
            {
                case Keys.Left: next = cur < 0 ? 0 : Math.Max(0, cur - 1); break;
                case Keys.Right: next = cur < 0 ? 0 : Math.Min(items.Count - 1, cur + 1); break;
                case Keys.Up: next = cur < 0 ? 0 : Vertical(cur, -1, TileH); break;
                case Keys.Down: next = cur < 0 ? 0 : Vertical(cur, 1, TileH); break;
                case Keys.PageUp: next = cur < 0 ? 0 : Vertical(cur, -1, page); if (next < 0) next = 0; break;
                case Keys.PageDown: next = cur < 0 ? 0 : Vertical(cur, 1, page); if (next < 0) next = items.Count - 1; break;
                case Keys.Home: next = 0; break;
                case Keys.End: next = items.Count - 1; break;
                default: return;
            }
            e.Handled = true;
            if (next < 0) next = cur;
            if (next < 0) return;
            if (e.Shift && anchor >= 0) SelectRange(anchor, next, e.Control);
            else if (e.Control) { focus = next; Changed(next); }
            else SelectOnly(next);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tip.Dispose();
                fName.Dispose(); fHeader.Dispose(); fEmpty.Dispose(); fEmptyIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
