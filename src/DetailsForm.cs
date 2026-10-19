// The details window: a header with the app's icon and actions, then its properties grouped in cards.
// Click a row to copy its value.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AppShelf
{
    class DetailsForm : Form
    {
        readonly AppEntry app;
        readonly DetailsList list;
        readonly Bitmap icon;
        readonly List<FlatButton> buttons = new List<FlatButton>();
        readonly Font fTitle, fSub;
        readonly string subtitle;

        public DetailsForm(AppEntry app)
        {
            this.app = app;
            Text = app.Name;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(700), Ui.S(620));
            MinimumSize = new Size(Ui.S(520), Ui.S(400));
            ShowInTaskbar = false;
            ShowIcon = false;
            MinimizeBox = false;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Font = Theme.MakeFont(13);
            fTitle = Theme.MakeFont(22, true);
            fSub = Theme.MakeFont(13);
            icon = Shell.GetIcon(app.ShellPath, Ui.S(64));

            var details = AppInfo.GetDetails(app);
            Func<string, string> get = k => details.Where(d => d.Key == k).Select(d => d.Value).FirstOrDefault();
            var parts = new List<string> { app.IsStoreApp ? "Store app" : "Desktop app" };
            var version = get("Installed version") ?? get("Version") ?? get("Product version") ?? get("File version");
            if (version != null) parts.Add(version);
            var publisher = get("Publisher") ?? get("Company");
            if (publisher != null) parts.Add(publisher);
            subtitle = string.Join("  \u00B7  ", parts);

            var open = new FlatButton("Open", Theme.GOpen, true);
            open.Click += delegate { Actions.Launch(app, this); };
            var location = new FlatButton("Open file location", Theme.GFolder, false);
            location.Click += delegate { Actions.OpenLocation(app); };
            location.Enabled = Actions.GetLocation(app) != null;
            var desktop = new FlatButton("Add to desktop", Theme.GDesktop, false);
            desktop.Click += delegate
            {
                bool warn;
                var msg = Actions.AddToDesktop(new List<AppEntry> { app }, this, out warn);
                if (msg != null) list.ShowToast(msg, warn);
            };
            buttons.AddRange(new[] { open, location, desktop });
            int x = Ui.S(28);
            foreach (var b in buttons)
            {
                b.Location = new Point(x, Ui.S(116));
                x += b.Width + Ui.S(8);
                Controls.Add(b);
            }

            list = new DetailsList(details);
            Controls.Add(list);
            ActiveControl = open;

            Theme.Changed += OnThemeChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyChrome(Handle);
        }

        void OnThemeChanged()
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() => { BackColor = Theme.Bg; Theme.ApplyChrome(Handle); Refresh(); }));
        }

        int HeaderHeight { get { return Ui.S(168); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (list != null) list.SetBounds(Ui.S(4), HeaderHeight, ClientSize.Width - Ui.S(4), ClientSize.Height - HeaderHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            int isz = Ui.S(64);
            var ir = new Rectangle(Ui.S(28), Ui.S(28), isz, isz);
            var img = icon ?? app.Icon;
            if (img != null) g.DrawImage(img, ir);
            int tx = ir.Right + Ui.S(18), tw = ClientSize.Width - tx - Ui.S(24);
            TextRenderer.DrawText(g, app.Name, fTitle, new Rectangle(tx, ir.Top + Ui.S(2), tw, Ui.S(36)), Theme.Fg, Theme.Left);
            TextRenderer.DrawText(g, subtitle, fSub, new Rectangle(tx, ir.Top + Ui.S(40), tw, Ui.S(22)), Theme.Sub, Theme.Left);
            using (var p = new Pen(Theme.Line))
                g.DrawLine(p, 0, HeaderHeight - 1, ClientSize.Width, HeaderHeight - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C && list.Focused) { list.CopyCurrent(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                if (icon != null) icon.Dispose();
                fTitle.Dispose(); fSub.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Properties grouped into rounded cards. Hover shows a copy glyph; click copies the value.
    class DetailsList : ScrollPane
    {
        class Row { public Detail D; public Rectangle R; }
        class Card { public string Title; public Rectangle R; }

        readonly List<Detail> details;
        readonly List<Row> rows = new List<Row>();
        readonly List<Card> cards = new List<Card>();
        readonly ContextMenuStrip menu = MenuRenderer.Make();
        readonly Timer toastTimer = new Timer();
        readonly Font fKey, fValue, fSection, fIcon, fToast;
        int hot = -1, current = -1;
        string toast;
        bool toastWarn;

        const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                                     TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public DetailsList(List<Detail> details)
        {
            this.details = details;
            fKey = Theme.MakeFont(12.5f);
            fValue = Theme.MakeFont(13);
            fSection = Theme.MakeFont(12, true);
            fIcon = Theme.MakeIconFont(14);
            fToast = Theme.MakeFont(13, true);
            menu.Items.Add(MenuRenderer.Item("Copy value", Theme.GCopy, delegate { CopyCurrent(); }));
            menu.Items.Add(MenuRenderer.Item("Copy all details", Theme.GCopy, delegate { CopyAll(); }));
            menu.Opening += (s, e) => { menu.Items[0].Enabled = current >= 0; MenuRenderer.Refresh(menu); };
            ContextMenuStrip = menu;
            toastTimer.Interval = 2500;
            toastTimer.Tick += delegate { toastTimer.Stop(); toast = null; Invalidate(); };
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Relayout();
            Invalidate();
        }

        int KeyColumn { get { return Ui.S(150); } }

        void Relayout()
        {
            rows.Clear();
            cards.Clear();
            int x = Ui.S(24), w = ClientSize.Width - Ui.S(24) - Ui.S(20), y = Ui.S(20);
            int pad = Ui.S(14), valueW = Math.Max(Ui.S(80), w - 2 * pad - KeyColumn);
            string section = null;
            Card card = null;
            foreach (var d in details)
            {
                if (d.Section != section)
                {
                    if (card != null) { card.R.Height = y - card.R.Top + Ui.S(6); y = card.R.Bottom + Ui.S(22); }
                    section = d.Section;
                    card = new Card { Title = section, R = new Rectangle(x, y + Ui.S(24), w, 0) };
                    cards.Add(card);
                    y += Ui.S(24) + Ui.S(6);
                }
                int h = TextRenderer.MeasureText(d.Value, fValue, new Size(valueW, int.MaxValue), Wrap).Height;
                h = Math.Max(h, fValue.Height) + Ui.S(16);
                rows.Add(new Row { D = d, R = new Rectangle(x + Ui.S(6), y, w - Ui.S(12), h) });
                y += h;
            }
            if (card != null) { card.R.Height = y - card.R.Top + Ui.S(6); y = card.R.Bottom; }
            contentHeight = y + Ui.S(24);
            ClampScroll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.TranslateTransform(0, -scroll);
            int pad = Ui.S(14), r = Ui.S(10);
            foreach (var c in cards)
            {
                TextRenderer.DrawText(g, c.Title.ToUpperInvariant(), fSection, new Rectangle(c.R.Left + Ui.S(4), c.R.Top - Ui.S(24), c.R.Width, Ui.S(18)), Theme.Sub, Theme.Left);
                Theme.FillRound(g, Theme.Surface, c.R, r);
                Theme.StrokeRound(g, Theme.Line, c.R, r);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool isHot = i == hot, isCur = i == current && Focused;
                if (isHot || isCur) Theme.FillRound(g, isCur ? Theme.AccentSoft : Theme.SurfaceHot, row.R, Ui.S(6));
                else if (i > 0 && rows[i - 1].D.Section == row.D.Section)
                    using (var p = new Pen(Theme.Mix(Theme.Surface, Theme.Line, 0.7f)))
                        g.DrawLine(p, row.R.Left + pad - Ui.S(6), row.R.Top, row.R.Right - pad + Ui.S(6), row.R.Top);
                var kr = new Rectangle(row.R.Left + pad - Ui.S(6), row.R.Top + Ui.S(8), KeyColumn - Ui.S(8), fValue.Height);
                TextRenderer.DrawText(g, row.D.Key, fKey, kr, Theme.Sub, Theme.Left);
                var vr = new Rectangle(row.R.Left + pad - Ui.S(6) + KeyColumn, row.R.Top + Ui.S(8),
                                       row.R.Width - KeyColumn - 2 * pad + Ui.S(12) - (isHot ? Ui.S(24) : 0), row.R.Height - Ui.S(16));
                TextRenderer.DrawText(g, row.D.Value, fValue, vr, Theme.Fg, Wrap);
                if (isHot)
                    TextRenderer.DrawText(g, Theme.GCopy, fIcon, new Rectangle(row.R.Right - Ui.S(34), row.R.Top, Ui.S(28), Math.Min(row.R.Height, Ui.S(38))), Theme.Sub, Theme.Center);
            }
            g.ResetTransform();

            if (toast != null)
            {
                int iw = Ui.S(20);
                int tw = TextRenderer.MeasureText(g, toast, fToast, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + iw + Ui.S(40);
                tw = Math.Min(tw, ClientSize.Width - Ui.S(40));
                var tr = new Rectangle((ClientSize.Width - tw) / 2, ClientSize.Height - Ui.S(58), tw, Ui.S(38));
                Theme.FillRound(g, Theme.Fg, tr, tr.Height / 2f);
                TextRenderer.DrawText(g, toastWarn ? Theme.GWarn : Theme.GCheck, fIcon, new Rectangle(tr.Left + Ui.S(16), tr.Top, iw, tr.Height),
                                      toastWarn ? Theme.Warn : Theme.Bg, Theme.Center);
                TextRenderer.DrawText(g, toast, fToast, new Rectangle(tr.Left + Ui.S(16) + iw + Ui.S(8), tr.Top, tr.Width - iw - Ui.S(40), tr.Height), Theme.Bg, Theme.Left);
            }
            PaintBar(g);
        }

        int HitTest(Point p)
        {
            var q = new Point(p.X, p.Y + scroll);
            for (int i = 0; i < rows.Count; i++) if (rows[i].R.Contains(q)) return i;
            return -1;
        }

        protected override void OnScrolled() { SetHot(HitTest(PointToClient(Cursor.Position))); }

        void SetHot(int h)
        {
            if (h == hot) return;
            hot = h;
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (BarMouseMove(e)) return;
            SetHot(OverBar(e.Location) ? -1 : HitTest(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHot(-1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (BarMouseDown(e)) return;
            current = HitTest(e.Location);
            Invalidate();
            if (e.Button == MouseButtons.Left && current >= 0) CopyCurrent();
        }

        protected override bool IsInputKey(Keys k)
        {
            var c = k & Keys.KeyCode;
            return c == Keys.Up || c == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (rows.Count == 0) return;
            int next = current;
            if (e.KeyCode == Keys.Down) next = Math.Min(rows.Count - 1, current + 1);
            else if (e.KeyCode == Keys.Up) next = Math.Max(0, current - 1);
            else if (e.KeyCode == Keys.Enter && current >= 0) { CopyCurrent(); return; }
            else return;
            current = next;
            EnsureVisible(rows[current].R.Top - Ui.S(30), rows[current].R.Bottom + Ui.S(8));
            Invalidate();
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        public void CopyCurrent()
        {
            if (current < 0 || current >= rows.Count) return;
            var d = rows[current].D;
            if (SetClipboard(d.Value)) ShowToast("Copied \u201C" + d.Key + "\u201D", false);
        }

        void CopyAll()
        {
            var text = string.Join(Environment.NewLine, details.Select(d => d.Key + ": " + d.Value));
            if (SetClipboard(text)) ShowToast("Copied all details", false);
        }

        bool SetClipboard(string text)
        {
            try { Clipboard.SetText(text); return true; }
            catch (Exception) { ShowToast("The clipboard is busy, try again", true); return false; }
        }

        public void ShowToast(string message, bool warn)
        {
            toast = message;
            toastWarn = warn;
            toastTimer.Stop();
            toastTimer.Start();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toastTimer.Dispose();
                fKey.Dispose(); fValue.Dispose(); fSection.Dispose(); fIcon.Dispose(); fToast.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
