// The settings window: custom-drawn cards with switches, a stepper and segment pills.
// Every change applies right away and is saved automatically.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AppShelf
{
    class SettingsForm : Form
    {
        enum Kind { Switch, Stepper, Segments }

        class Row
        {
            public Kind Kind;
            public string Title, Hint;
            public Rectangle R;
            public Func<bool> GetB;
            public Action<bool> SetB;
            public string[] Options;
            public Func<int> GetI;
            public Action<int> SetI;
            public Func<string> Value;      // stepper label
            public Action<int> Step;        // stepper: -1 / +1
        }

        class Card { public string Title; public Rectangle R; }

        readonly Action changed;
        readonly Func<int> actualColumns;
        readonly List<Row> rows = new List<Row>();
        readonly List<Card> cards = new List<Card>();
        readonly Font fTitle, fBody, fHint, fSection, fSmall, fIcon;
        string hot;     // "<row>:<part>"

        Settings S { get { return Settings.Current; } }

        public SettingsForm(Action changed, Func<int> actualColumns)
        {
            this.changed = changed;
            this.actualColumns = actualColumns;
            Text = "AppShelf settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            fTitle = Theme.MakeFont(20, true);
            fBody = Theme.MakeFont(13.5f);
            fHint = Theme.MakeFont(12);
            fSection = Theme.MakeFont(12, true);
            fSmall = Theme.MakeFont(12.5f, true);
            fIcon = Theme.MakeIconFont(12);

            rows.Add(new Row { Kind = Kind.Switch, Title = "Start with Windows", Hint = "Opens quietly in the tray when you sign in",
                               GetB = Autostart.Get, SetB = v => Autostart.Set(v) });
            rows.Add(new Row { Kind = Kind.Switch, Title = "Minimize to tray", Hint = "The minimize button hides AppShelf in the tray",
                               GetB = () => S.MinimizeToTray, SetB = v => S.MinimizeToTray = v });
            rows.Add(new Row { Kind = Kind.Switch, Title = "Close to tray", Hint = "The close button keeps AppShelf running in the tray",
                               GetB = () => S.CloseToTray, SetB = v => S.CloseToTray = v });
            rows.Add(new Row { Kind = Kind.Stepper, Title = "Apps per row", Hint = "Auto fits as many as the window allows",
                               Value = () => S.Columns == 0 ? "Auto" : S.Columns.ToString(), Step = StepColumns });
            rows.Add(new Row { Kind = Kind.Segments, Title = "Icon size", Options = new[] { "Small", "Medium", "Large" },
                               GetI = () => S.IconSize, SetI = v => S.IconSize = v });
            rows.Add(new Row { Kind = Kind.Switch, Title = "A\u2013Z sections", Hint = "Group apps under their first letter",
                               GetB = () => S.Sections, SetB = v => S.Sections = v });
            rows.Add(new Row { Kind = Kind.Segments, Title = "Theme", Options = new[] { "System", "Light", "Dark" },
                               GetI = () => S.Theme, SetI = v => S.Theme = v });
            BuildLayout(new[] { "Startup & tray", "Layout", "Appearance" }, new[] { 3, 3, 1 });
            Theme.Changed += OnThemeChanged;
        }

        void StepColumns(int dir)
        {
            int cur = S.Columns == 0 ? actualColumns() : S.Columns;
            int next = cur + dir;
            if (S.Columns != 0 && dir < 0 && cur <= Settings.MinColumns) next = 0;          // below the minimum -> Auto
            else next = Settings.Clamp(next, Settings.MinColumns, Settings.MaxColumns);
            S.Columns = next;
        }

        void BuildLayout(string[] titles, int[] counts)
        {
            int w = Ui.S(460), x = Ui.S(24), cw = w - 2 * x, y = Ui.S(86), i = 0;
            for (int c = 0; c < titles.Length; c++)
            {
                var card = new Card { Title = titles[c] };
                y += Ui.S(26);
                int top = y;
                for (int k = 0; k < counts[c]; k++, i++)
                {
                    int h = rows[i].Hint != null ? Ui.S(62) : Ui.S(52);
                    rows[i].R = new Rectangle(x, y, cw, h);
                    y += h;
                }
                card.R = new Rectangle(x, top, cw, y - top);
                cards.Add(card);
                y += Ui.S(18);
            }
            ClientSize = new Size(w, y + Ui.S(28));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyChrome(Handle);
        }

        void OnThemeChanged()
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() => { BackColor = Theme.Bg; Theme.ApplyChrome(Handle); Invalidate(); }));
        }

        // ---------- controls inside a row ----------

        Rectangle SwitchRect(Row r) { int w = Ui.S(40), h = Ui.S(22); return new Rectangle(r.R.Right - Ui.S(16) - w, r.R.Top + (r.R.Height - h) / 2, w, h); }

        Rectangle[] StepperRects(Row r)
        {
            int b = Ui.S(30), vw = Ui.S(56), y = r.R.Top + (r.R.Height - b) / 2, right = r.R.Right - Ui.S(16);
            var plus = new Rectangle(right - b, y, b, b);
            var val = new Rectangle(plus.Left - vw, y, vw, b);
            var minus = new Rectangle(val.Left - b, y, b, b);
            return new[] { minus, val, plus };
        }

        Rectangle[] SegmentRects(Row r)
        {
            int sw = Ui.S(72), h = Ui.S(30), n = r.Options.Length, y = r.R.Top + (r.R.Height - h) / 2;
            var res = new Rectangle[n];
            for (int i = 0; i < n; i++) res[i] = new Rectangle(r.R.Right - Ui.S(16) - (n - i) * sw, y, sw, h);
            return res;
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            TextRenderer.DrawText(g, "Settings", fTitle, new Rectangle(Ui.S(24), Ui.S(22), Ui.S(300), Ui.S(36)), Theme.Fg, Theme.Left);
            TextRenderer.DrawText(g, "Changes apply right away", fHint, new Rectangle(Ui.S(24), Ui.S(54), Ui.S(300), Ui.S(18)), Theme.Sub, Theme.Left);

            foreach (var c in cards)
            {
                TextRenderer.DrawText(g, c.Title.ToUpperInvariant(), fSection, new Rectangle(c.R.Left + Ui.S(4), c.R.Top - Ui.S(22), c.R.Width, Ui.S(18)), Theme.Sub, Theme.Left);
                Theme.FillRound(g, Theme.Surface, c.R, Ui.S(10));
                Theme.StrokeRound(g, Theme.Line, c.R, Ui.S(10));
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (i > 0 && rows[i - 1].R.Bottom == r.R.Top)
                    using (var p = new Pen(Theme.Mix(Theme.Surface, Theme.Line, 0.7f)))
                        g.DrawLine(p, r.R.Left + Ui.S(16), r.R.Top, r.R.Right - Ui.S(16), r.R.Top);
                int tx = r.R.Left + Ui.S(16), tw = r.R.Width / 2;
                if (r.Hint != null)
                {
                    TextRenderer.DrawText(g, r.Title, fBody, new Rectangle(tx, r.R.Top + Ui.S(11), r.R.Width - Ui.S(80), Ui.S(22)), Theme.Fg, Theme.Left);
                    int hintW = r.R.Width - Ui.S(32) - (r.Kind == Kind.Stepper ? Ui.S(130) : Ui.S(56));
                    TextRenderer.DrawText(g, r.Hint, fHint, new Rectangle(tx, r.R.Top + Ui.S(33), hintW, Ui.S(18)), Theme.Sub, Theme.Left);
                }
                else TextRenderer.DrawText(g, r.Title, fBody, new Rectangle(tx, r.R.Top, tw, r.R.Height), Theme.Fg, Theme.Left);

                switch (r.Kind)
                {
                    case Kind.Switch:
                        Theme.Switch(g, SwitchRect(r), r.GetB(), hot == i + ":sw");
                        break;
                    case Kind.Stepper:
                    {
                        var s = StepperRects(r);
                        var pill = Rectangle.Union(s[0], s[2]);
                        Theme.FillRound(g, Theme.Bg, pill, pill.Height / 2f);
                        Theme.StrokeRound(g, Theme.Line, pill, pill.Height / 2f);
                        for (int k = 0; k < 3; k += 2)
                        {
                            if (hot == i + ":" + k) Theme.FillRound(g, Theme.SurfaceHot, Rectangle.Inflate(s[k], -Ui.S(3), -Ui.S(3)), s[k].Height / 2f);
                            TextRenderer.DrawText(g, k == 0 ? "\uE738" : "\uE710", fIcon, s[k], Theme.Fg, Theme.Center);
                        }
                        string v = r.Value();
                        TextRenderer.DrawText(g, v, fSmall, s[1], S.Columns == 0 ? Theme.Sub : Theme.Fg, Theme.Center);
                        break;
                    }
                    case Kind.Segments:
                    {
                        var s = SegmentRects(r);
                        var pill = Rectangle.Union(s[0], s[s.Length - 1]);
                        Theme.FillRound(g, Theme.Bg, pill, pill.Height / 2f);
                        Theme.StrokeRound(g, Theme.Line, pill, pill.Height / 2f);
                        for (int k = 0; k < s.Length; k++)
                        {
                            bool on = r.GetI() == k;
                            var sr = Rectangle.Inflate(s[k], -Ui.S(3), -Ui.S(3));
                            if (on) Theme.FillRound(g, Theme.Fg, sr, sr.Height / 2f);
                            else if (hot == i + ":" + k) Theme.FillRound(g, Theme.SurfaceHot, sr, sr.Height / 2f);
                            TextRenderer.DrawText(g, r.Options[k], fSmall, s[k], on ? Theme.Bg : Theme.Fg, Theme.Center);
                        }
                        break;
                    }
                }
            }
        }

        // ---------- input ----------

        string PartAt(Point p)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (!r.R.Contains(p)) continue;
                switch (r.Kind)
                {
                    case Kind.Switch: return i + ":sw";     // the whole row toggles
                    case Kind.Stepper:
                    {
                        var s = StepperRects(r);
                        if (s[0].Contains(p)) return i + ":0";
                        if (s[2].Contains(p)) return i + ":2";
                        return null;
                    }
                    case Kind.Segments:
                    {
                        var s = SegmentRects(r);
                        for (int k = 0; k < s.Length; k++) if (s[k].Contains(p)) return i + ":" + k;
                        return null;
                    }
                }
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = PartAt(e.Location);
            if (h == hot) return;
            hot = h;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != null) { hot = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var part = PartAt(e.Location);
            if (part == null) return;
            var bits = part.Split(':');
            var r = rows[int.Parse(bits[0])];
            switch (r.Kind)
            {
                case Kind.Switch: r.SetB(!r.GetB()); break;
                case Kind.Stepper: r.Step(bits[1] == "0" ? -1 : 1); break;
                case Kind.Segments: r.SetI(int.Parse(bits[1])); break;
            }
            S.Save();
            changed();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                fTitle.Dispose(); fBody.Dispose(); fHint.Dispose(); fSection.Dispose(); fSmall.Dispose(); fIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
