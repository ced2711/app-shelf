// The main window: header with search and filters, the app grid, and a footer for hints and toasts.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AppShelf
{
    class MainForm : Form
    {
        enum Filter { All, Desktop, Store }

        readonly TextBox search = new TextBox();
        readonly TileView grid = new TileView();
        readonly ContextMenuStrip menu = MenuRenderer.Make();
        readonly System.Windows.Forms.Timer toastTimer = new System.Windows.Forms.Timer();
        readonly ToolTip tip = new ToolTip();
        Font fTitle, fBody, fSmall, fIcon, fIconSmall, fBold;
        List<AppEntry> apps = new List<AppEntry>();
        Filter filter = Filter.All;
        bool loading;
        string toast;
        bool toastWarn;
        string hotPart;     // "seg0".."seg2", "refresh", "settings", "clear"
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip trayMenu = MenuRenderer.Make();
        bool allowShow, quitting;
        int loadedIconPx;
        public bool Loaded { get; private set; }

        // Posted by a second launch to bring this window up.
        public static readonly int WM_SHOWAPP = Native.RegisterWindowMessage("AppShelf.ShowWindow");

        Settings S { get { return Settings.Current; } }

        public MainForm() : this(false) { }

        public MainForm(bool startInTray)
        {
            allowShow = !startInTray;
            Text = "AppShelf";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(940), Ui.S(640));
            MinimumSize = new Size(Ui.S(520), Ui.S(380));
            RestoreSavedBounds();
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            fTitle = Theme.MakeFont(22, true);
            fBody = Theme.MakeFont(13.5f);
            fSmall = Theme.MakeFont(12);
            fIcon = Theme.MakeIconFont(15);
            fIconSmall = Theme.MakeIconFont(11);
            fBold = Theme.MakeFont(13, true);
            Font = Theme.MakeFont(13);

            search.BorderStyle = BorderStyle.None;
            search.Font = fBody;
            search.TextChanged += delegate { Fill(); Invalidate(Header); };
            search.KeyDown += OnSearchKeyDown;
            search.GotFocus += delegate { Invalidate(Header); };
            search.LostFocus += delegate { Invalidate(Header); };
            search.HandleCreated += delegate { Native.SendMessage(search.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, "Search apps"); };

            grid.ContextMenuStrip = menu;
            grid.ItemActivated += a => ShowDetails(a);
            grid.SelectionChanged += () => Invalidate(Footer);
            grid.Placeholder = SystemIcons.Application.ToBitmap();

            Controls.Add(grid);
            Controls.Add(search);

            var open = MenuRenderer.Item("Open", Theme.GOpen, delegate { foreach (var a in grid.Selected) Actions.Launch(a, this); });
            open.Font = fBold;
            var details = MenuRenderer.Item("Details", Theme.GInfo, delegate { ShowDetails(grid.FocusedItem); });
            var desktop = MenuRenderer.Item("Add shortcut to desktop", Theme.GDesktop, delegate { AddToDesktop(grid.Selected); });
            var location = MenuRenderer.Item("Open file location", Theme.GFolder, delegate { var a = grid.FocusedItem; if (a != null) Actions.OpenLocation(a); });
            menu.Items.AddRange(new ToolStripItem[] { open, details, new ToolStripSeparator(), desktop, location });
            menu.Opening += (s, e) =>
            {
                var sel = grid.Selected;
                if (sel.Count == 0) { e.Cancel = true; return; }
                open.Text = sel.Count == 1 ? "Open" : "Open " + sel.Count + " apps";
                details.Enabled = sel.Count == 1;
                location.Enabled = sel.Count == 1 && Actions.GetLocation(sel[0]) != null;
                desktop.Text = sel.Count == 1 ? "Add shortcut to desktop" : "Add " + sel.Count + " shortcuts to desktop";
                MenuRenderer.Refresh(menu);
            };

            toastTimer.Interval = 4000;
            toastTimer.Tick += delegate { toastTimer.Stop(); toast = null; Invalidate(Footer); };

            tray.Text = "AppShelf";
            tray.Icon = Icon;
            tray.ContextMenuStrip = trayMenu;
            var trayOpen = MenuRenderer.Item("Open AppShelf", Theme.GOpen, delegate { ShowFromTray(); });
            trayOpen.Font = fBold;
            trayMenu.Items.AddRange(new ToolStripItem[]
            {
                trayOpen,
                MenuRenderer.Item("Settings", Theme.GSettings, delegate { ShowFromTray(); ShowSettings(); }),
                new ToolStripSeparator(),
                MenuRenderer.Item("Quit", Theme.GClose, delegate { Quit(); }),
            });
            trayMenu.Opening += delegate { MenuRenderer.Refresh(trayMenu); };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (Visible && WindowState != FormWindowState.Minimized) HideToTray();
                else ShowFromTray();
            };

            Theme.Changed += OnThemeChanged;
            ApplyColors();
            ApplySettings();
            Shown += delegate { grid.Focus(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyChrome(Handle);
            BeginInvoke((Action)Reload);     // also runs when starting hidden in the tray, so opening is instant
        }

        // ---------- tray & window state ----------

        // Keeps the window hidden when started in the tray (the handle still gets created).
        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow && value)
            {
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWAPP && WM_SHOWAPP != 0) { ShowFromTray(); return; }
            base.WndProc(ref m);
        }

        public void ShowFromTray()
        {
            allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = S.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            UpdateTray();
        }

        void HideToTray()
        {
            SaveBounds();
            Hide();
            UpdateTray();
        }

        void Quit()
        {
            quitting = true;
            Close();
        }

        void UpdateTray()
        {
            tray.Visible = S.UsesTray || !Visible;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && S.MinimizeToTray && Visible) HideToTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None) && S.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            SaveBounds();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void RestoreSavedBounds()
        {
            var b = S.Bounds;
            if (b.IsEmpty || !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(b.X + 40, b.Y + 10, b.Width - 80, 30)))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(b.X, b.Y, Math.Max(b.Width, MinimumSize.Width), Math.Max(b.Height, MinimumSize.Height));
            if (S.Maximized) WindowState = FormWindowState.Maximized;
        }

        void SaveBounds()
        {
            if (!IsHandleCreated || !allowShow) return;
            S.Maximized = WindowState == FormWindowState.Maximized;
            S.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            S.Save();
        }

        // ---------- settings ----------

        void ShowSettings()
        {
            using (var f = new SettingsForm(ApplySettings, () => grid.ActualColumns)) f.ShowDialog(this);
        }

        public int GridColumns { get { return grid.ActualColumns; } }

        public void ApplySettings()
        {
            Theme.Use(S.DarkOverride);
            grid.Columns = S.Columns;
            grid.IconPx = S.IconPixels;
            UpdateTray();
            if (Loaded && loadedIconPx != S.IconPixels) Reload();
            else if (Loaded) Fill(true);
        }

        void OnThemeChanged()
        {
            if (IsDisposed) return;
            if (!IsHandleCreated) { ApplyColors(); return; }
            BeginInvoke((Action)(() =>
            {
                ApplyColors();
                Theme.ApplyChrome(Handle);
                Refresh();
            }));
        }

        void ApplyColors()
        {
            BackColor = Theme.Bg;
            search.BackColor = Theme.Surface;
            search.ForeColor = Theme.Fg;
            grid.Invalidate();
        }

        // ---------- layout ----------

        int HeaderHeight { get { return Ui.S(118); } }
        int FooterHeight { get { return Ui.S(38); } }
        Rectangle Header { get { return new Rectangle(0, 0, ClientSize.Width, HeaderHeight); } }
        Rectangle Footer { get { return new Rectangle(0, ClientSize.Height - FooterHeight, ClientSize.Width, FooterHeight); } }

        Rectangle SettingsButton { get { return new Rectangle(ClientSize.Width - Ui.S(24) - Ui.S(38), Ui.S(20), Ui.S(38), Ui.S(38)); } }
        Rectangle RefreshButton { get { var r = SettingsButton; r.Offset(-Ui.S(46), 0); return r; } }

        Rectangle SearchPill
        {
            get
            {
                int right = RefreshButton.Left - Ui.S(8);
                int w = Math.Min(Ui.S(340), right - Ui.S(200));
                return new Rectangle(right - w, Ui.S(20), w, Ui.S(38));
            }
        }

        Rectangle ClearButton { get { var p = SearchPill; return new Rectangle(p.Right - Ui.S(34), p.Top + Ui.S(5), Ui.S(28), p.Height - Ui.S(10)); } }

        string SegmentLabel(int i)
        {
            if (i == 0) return "All";
            return i == 1 ? "Desktop" : "Store";
        }

        int SegmentCount(int i)
        {
            if (i == 0) return apps.Count;
            return apps.Count(a => a.IsStoreApp == (i == 2));
        }

        Rectangle[] Segments()
        {
            var r = new Rectangle[3];
            int x = Ui.S(24), y = Ui.S(72), h = Ui.S(32);
            using (var g = CreateGraphics())
                for (int i = 0; i < 3; i++)
                {
                    int w = TextRenderer.MeasureText(g, SegmentLabel(i), fBold, Size.Empty, Theme.Center).Width +
                            TextRenderer.MeasureText(g, SegmentCount(i).ToString(), fSmall, Size.Empty, Theme.Center).Width + Ui.S(34);
                    r[i] = new Rectangle(x, y, w, h);
                    x += w + Ui.S(6);
                }
            return r;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var p = SearchPill;
            int th = search.PreferredHeight;
            search.SetBounds(p.Left + Ui.S(38), p.Top + (p.Height - th) / 2 + 1, Math.Max(10, p.Width - Ui.S(38) - Ui.S(38)), th);
            grid.SetBounds(Ui.S(4), HeaderHeight, ClientSize.Width - Ui.S(4), ClientSize.Height - HeaderHeight - FooterHeight);
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);

            // title + count
            var titleR = new Rectangle(Ui.S(24), Ui.S(18), Ui.S(260), Ui.S(40));
            TextRenderer.DrawText(g, "All apps", fTitle, titleR, Theme.Fg, Theme.Left);
            int tw = TextRenderer.MeasureText(g, "All apps", fTitle, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            string count = loading ? "" : (search.Text.Trim().Length > 0 ? grid.Count + " of " + apps.Count : apps.Count.ToString());
            TextRenderer.DrawText(g, count, fBody, new Rectangle(titleR.Left + tw + Ui.S(10), titleR.Top + Ui.S(4), Ui.S(140), titleR.Height), Theme.Sub, Theme.Left);

            // search pill
            var p = SearchPill;
            Theme.FillRound(g, Theme.Surface, p, p.Height / 2f);
            Theme.StrokeRound(g, search.Focused ? Theme.Accent : Theme.Line, p, p.Height / 2f, search.Focused ? Math.Max(1, Ui.S(1.5f)) : 1);
            TextRenderer.DrawText(g, Theme.GSearch, fIcon, new Rectangle(p.Left + Ui.S(14), p.Top, Ui.S(18), p.Height), Theme.Sub, Theme.Center);
            if (search.Text.Length > 0)
            {
                var c = ClearButton;
                if (hotPart == "clear") Theme.FillRound(g, Theme.SurfaceHot, c, c.Height / 2f);
                TextRenderer.DrawText(g, Theme.GClose, fIconSmall, c, Theme.Sub, Theme.Center);
            }

            // refresh
            var rb = RefreshButton;
            Theme.FillRound(g, hotPart == "refresh" ? Theme.SurfaceHot : Theme.Surface, rb, rb.Height / 2f);
            Theme.StrokeRound(g, Theme.Line, rb, rb.Height / 2f);
            TextRenderer.DrawText(g, Theme.GRefresh, fIcon, rb, loading ? Theme.Faint : Theme.Fg, Theme.Center);
            var sb = SettingsButton;
            Theme.FillRound(g, hotPart == "settings" ? Theme.SurfaceHot : Theme.Surface, sb, sb.Height / 2f);
            Theme.StrokeRound(g, Theme.Line, sb, sb.Height / 2f);
            TextRenderer.DrawText(g, Theme.GSettings, fIcon, sb, Theme.Fg, Theme.Center);

            // filter segments
            var segs = Segments();
            for (int i = 0; i < 3; i++)
            {
                var r = segs[i];
                bool on = (int)filter == i, hot = hotPart == "seg" + i;
                if (on) Theme.FillRound(g, Theme.Fg, r, r.Height / 2f);
                else
                {
                    Theme.FillRound(g, hot ? Theme.SurfaceHot : Theme.Surface, r, r.Height / 2f);
                    Theme.StrokeRound(g, Theme.Line, r, r.Height / 2f);
                }
                var label = SegmentLabel(i);
                int lw = TextRenderer.MeasureText(g, label, fBold, Size.Empty, Theme.Center).Width;
                var lr = new Rectangle(r.Left + Ui.S(14), r.Top, lw, r.Height);
                TextRenderer.DrawText(g, label, fBold, lr, on ? Theme.Bg : Theme.Fg, Theme.Center);
                if (!loading)
                    TextRenderer.DrawText(g, SegmentCount(i).ToString(), fSmall, new Rectangle(lr.Right + Ui.S(6), r.Top, r.Right - lr.Right - Ui.S(6), r.Height),
                                          on ? Theme.Mix(Theme.Bg, Theme.Fg, 0.4f) : Theme.Sub, Theme.Left);
            }

            // footer
            var f = Footer;
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, f.Top, f.Width, f.Top);
            var fr = new Rectangle(Ui.S(24), f.Top, f.Width - Ui.S(48), f.Height);
            if (toast != null)
            {
                var ic = new Rectangle(fr.Left, fr.Top, Ui.S(18), fr.Height);
                TextRenderer.DrawText(g, toastWarn ? Theme.GWarn : Theme.GCheck, fIcon, ic, toastWarn ? Theme.Warn : Theme.Accent, Theme.Center);
                TextRenderer.DrawText(g, toast, fBody, new Rectangle(ic.Right + Ui.S(8), fr.Top, fr.Width - ic.Width - Ui.S(8), fr.Height), Theme.Fg, Theme.Left);
            }
            else
            {
                int n = grid.Selected.Count;
                string left = n > 1 ? n + " selected  \u00B7  Right-click to open them or add them to the desktop"
                                    : "Double-click for details  \u00B7  Right-click for more";
                TextRenderer.DrawText(g, left, fSmall, fr, Theme.Sub, Theme.Left);
                if (fr.Width > Ui.S(600))
                    TextRenderer.DrawText(g, "Ctrl+F search  \u00B7  F5 refresh", fSmall, fr, Theme.Faint, Theme.Right);
            }
        }

        // ---------- input ----------

        string PartAt(Point p)
        {
            if (RefreshButton.Contains(p)) return "refresh";
            if (SettingsButton.Contains(p)) return "settings";
            if (search.Text.Length > 0 && ClearButton.Contains(p)) return "clear";
            if (p.Y < HeaderHeight)
            {
                var segs = Segments();
                for (int i = 0; i < 3; i++) if (segs[i].Contains(p)) return "seg" + i;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var part = PartAt(e.Location);
            if (part != hotPart)
            {
                hotPart = part;
                Cursor = part != null ? Cursors.Hand : SearchPill.Contains(e.Location) ? Cursors.IBeam : Cursors.Default;
                Invalidate(Header);
                if (part == "refresh") tip.Show("Refresh (F5)", this, RefreshButton.Left - Ui.S(30), RefreshButton.Bottom + Ui.S(4), 2000);
                else if (part == "settings") tip.Show("Settings (Ctrl+,)", this, SettingsButton.Left - Ui.S(60), SettingsButton.Bottom + Ui.S(4), 2000);
                else tip.Hide(this);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hotPart != null) { hotPart = null; Invalidate(Header); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var part = PartAt(e.Location);
            if (part == "refresh") Reload();
            else if (part == "settings") ShowSettings();
            else if (part == "clear") { search.Clear(); search.Focus(); }
            else if (part != null && part.StartsWith("seg"))
            {
                filter = (Filter)(part[3] - '0');
                Fill();
                Invalidate(Header);
            }
            else if (SearchPill.Contains(e.Location)) search.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F && e.Control) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { Reload(); e.Handled = true; }
            else if (e.KeyCode == Keys.Oemcomma && e.Control) { ShowSettings(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        // Typing while the grid has focus goes to the search box.
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (ActiveControl == grid && !char.IsControl(e.KeyChar) && (ModifierKeys & (Keys.Control | Keys.Alt)) == 0)
            {
                search.Focus();
                search.SelectionStart = search.TextLength;
                search.SelectedText = e.KeyChar.ToString();
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }

        void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { search.Clear(); e.SuppressKeyPress = true; }
            else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) && grid.Count > 0)
            {
                grid.SelectFirst();
                if (e.KeyCode == Keys.Enter && grid.Count == 1) ShowDetails(grid.FocusedItem);
                e.SuppressKeyPress = true;
            }
        }

        // ---------- actions ----------

        public void Search(string text) { search.Text = text; }

        void ShowDetails(AppEntry app)
        {
            if (app == null) return;
            using (var f = new DetailsForm(app)) f.ShowDialog(this);
        }

        void AddToDesktop(IList<AppEntry> list)
        {
            bool warn;
            var msg = Actions.AddToDesktop(list, this, out warn);
            if (msg != null) ShowToast(msg, warn);
        }

        public void ShowToast(string message, bool warn)
        {
            toast = message;
            toastWarn = warn;
            toastTimer.Stop();
            toastTimer.Start();
            Invalidate(Footer);
        }

        public void Reload()
        {
            if (loading) return;
            loading = true;
            Loaded = false;
            AppInfo.Reset();
            apps = new List<AppEntry>();
            grid.SetItems(new List<AppEntry>(), false);
            grid.EmptyGlyph = null;
            grid.EmptyText = "Loading apps\u2026";
            grid.Progress = 0;
            Invalidate();
            loadedIconPx = S.IconPixels;
            int iconSize = Ui.S(loadedIconPx);
            var t = new Thread(() =>
            {
                List<AppEntry> result;
                try
                {
                    result = Shell.EnumerateApps();
                    result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                    for (int i = 0; i < result.Count; i++)
                    {
                        result[i].Icon = Shell.GetIcon(result[i].ShellPath, iconSize);
                        if (i % 8 == 0)
                        {
                            float progress = i / (float)result.Count;
                            Post(() => { grid.Progress = progress; grid.Invalidate(); });
                        }
                    }
                }
                catch (Exception ex)
                {
                    result = new List<AppEntry>();
                    string msg = ex.Message;
                    Post(() => MessageBox.Show(this, "Could not read the app list:\n" + msg, "AppShelf", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
                Post(() => OnLoaded(result));
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // BeginInvoke that is safe after the window has closed.
        void Post(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void OnLoaded(List<AppEntry> result)
        {
            foreach (var a in apps) if (a.Icon != null) a.Icon.Dispose();
            apps = result;
            loading = false;
            grid.Progress = -1;
            Fill();
            Invalidate();
            Loaded = true;
        }

        void Fill() { Fill(false); }

        void Fill(bool keepScroll)
        {
            if (loading) return;
            string q = search.Text.Trim();
            var list = apps.Where(a =>
                (filter == Filter.All || a.IsStoreApp == (filter == Filter.Store)) &&
                (q.Length == 0 ||
                 a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 (a.TargetPath != null && Path.GetFileName(a.TargetPath).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)))
                .ToList();
            grid.EmptyGlyph = Theme.GSearch;
            grid.EmptyText = q.Length > 0 ? "No apps match \u201C" + q + "\u201D"
                           : filter == Filter.Store ? "No Store apps" : filter == Filter.Desktop ? "No desktop apps" : "No apps found";
            int keep = grid.ScrollOffset;
            grid.SetItems(list, q.Length == 0 && S.Sections);
            if (keepScroll) grid.ScrollOffset = keep;
            Invalidate(Header);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                tray.Dispose();
                toastTimer.Dispose();
                tip.Dispose();
                foreach (var a in apps) if (a.Icon != null) a.Icon.Dispose();
                fTitle.Dispose(); fBody.Dispose(); fSmall.Dispose(); fIcon.Dispose(); fIconSmall.Dispose(); fBold.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
