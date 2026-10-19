// Colors, fonts and drawing helpers shared by every window. Follows the Windows light/dark app setting.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppShelf
{
    static class Theme
    {
        public static bool Dark;
        public static Color Bg, Surface, SurfaceHot, Line, Fg, Sub, Faint, Accent, AccentFg, AccentSoft, Warn;

        public static event Action Changed;

        static Theme()
        {
            Load();
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                bool was = Dark;
                Load();
                if (was != Dark && Changed != null) Changed();
            };
        }

        // Forces light or dark (self-test screenshots); null follows Windows again.
        public static void Use(bool? dark)
        {
            forced = dark;
            Load();
            if (Changed != null) Changed();
        }

        static bool? forced;

        static void Load()
        {
            Dark = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    Dark = k != null && (k.GetValue("AppsUseLightTheme") as int?) == 0;
            }
            catch { }
            if (forced.HasValue) Dark = forced.Value;

            if (Dark)
            {
                Bg = Color.FromArgb(23, 23, 27);
                Surface = Color.FromArgb(34, 34, 40);
                SurfaceHot = Color.FromArgb(46, 46, 54);
                Line = Color.FromArgb(48, 48, 56);
                Fg = Color.FromArgb(236, 236, 240);
                Sub = Color.FromArgb(140, 140, 152);
                Faint = Color.FromArgb(92, 92, 104);
                Accent = Color.FromArgb(122, 162, 255);
                AccentFg = Color.FromArgb(16, 18, 26);
                Warn = Color.FromArgb(240, 140, 110);
            }
            else
            {
                Bg = Color.FromArgb(246, 246, 249);
                Surface = Color.FromArgb(255, 255, 255);
                SurfaceHot = Color.FromArgb(234, 234, 240);
                Line = Color.FromArgb(226, 226, 232);
                Fg = Color.FromArgb(26, 26, 32);
                Sub = Color.FromArgb(104, 104, 116);
                Faint = Color.FromArgb(160, 160, 172);
                Accent = Color.FromArgb(52, 98, 230);
                AccentFg = Color.FromArgb(255, 255, 255);
                Warn = Color.FromArgb(200, 80, 50);
            }
            AccentSoft = Mix(Bg, Accent, Dark ? 0.22f : 0.13f);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // ---------- fonts ----------

        public const string FontName = "Segoe UI";
        public static readonly string IconFontName = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        static bool FontExists(string name)
        {
            using (var f = new Font(name, 10)) return f.Name == name;
        }

        public static Font MakeFont(float px, bool semibold = false)
        {
            return new Font(semibold ? "Segoe UI Semibold" : FontName, px * Ui.Scale, GraphicsUnit.Pixel);
        }

        public static Font MakeIconFont(float px)
        {
            return new Font(IconFontName, px * Ui.Scale, GraphicsUnit.Pixel);
        }

        // Segoe Fluent Icons / MDL2 code points.
        public const string GSearch = "\uE721", GRefresh = "\uE72C", GOpen = "\uE8A7", GFolder = "\uE838",
                            GDesktop = "\uE7F4", GInfo = "\uE946", GCopy = "\uE8C8", GCheck = "\uE73E",
                            GClose = "\uE711", GWarn = "\uE7BA";

        // ---------- drawing ----------

        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                             TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                              TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = width / 2;
            using (var p = Round(new RectangleF(r.X + h, r.Y + h, r.Width - width, r.Height - width), radius))
            using (var pen = new Pen(c, width))
                g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }

        // Renders an icon-font glyph into a bitmap (for menu items).
        public static Bitmap GlyphBitmap(string glyph, int size, Color c)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font(IconFontName, size * 0.8f, GraphicsUnit.Pixel))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var b = new SolidBrush(c))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }

        // ---------- window chrome ----------

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string ids);

        static void Dwm(IntPtr h, int attr, int val)
        {
            try { DwmSetWindowAttribute(h, attr, ref val, 4); } catch { }
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Dark/light title bar whose caption blends into the window (Windows 11), rounded corners.
        public static void ApplyChrome(IntPtr h)
        {
            Dwm(h, 20, Dark ? 1 : 0);          // DWMWA_USE_IMMERSIVE_DARK_MODE
            Dwm(h, 35, ColorRef(Bg));          // DWMWA_CAPTION_COLOR
            Dwm(h, 36, ColorRef(Fg));          // DWMWA_TEXT_COLOR
            Dwm(h, 34, ColorRef(Dark ? Line : Color.FromArgb(214, 214, 222))); // DWMWA_BORDER_COLOR
            Dwm(h, 33, 2);                     // DWMWA_WINDOW_CORNER_PREFERENCE = round
        }

        public static void RoundPopup(IntPtr h)
        {
            Dwm(h, 33, 3);                     // round small
            Dwm(h, 34, ColorRef(Line));
        }

        public static void DarkScrollbars(IntPtr h)
        {
            try { SetWindowTheme(h, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }

    static class Ui
    {
        public static float Scale = 1f;
        public static int S(float px) { return (int)Math.Round(px * Scale); }
    }

    // Context menus that match the window: flat surface, rounded hover, glyph icons.
    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        public static ContextMenuStrip Make()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new MenuRenderer();
            m.ShowImageMargin = true;
            m.Padding = new Padding(Ui.S(4));
            m.Font = Theme.MakeFont(13);
            m.ImageScalingSize = new Size(Ui.S(16), Ui.S(16));
            m.HandleCreated += delegate { Theme.RoundPopup(m.Handle); };
            m.Opening += delegate { m.BackColor = Theme.Surface; };
            return m;
        }

        public static ToolStripMenuItem Item(string text, string glyph, EventHandler click)
        {
            var it = new ToolStripMenuItem(text, null, click);
            it.Padding = new Padding(0, Ui.S(5), 0, Ui.S(5));
            it.Tag = glyph;
            return it;
        }

        // Re-colors item glyphs for the current theme (call before showing).
        public static void Refresh(ContextMenuStrip m)
        {
            foreach (ToolStripItem i in m.Items)
            {
                var glyph = i.Tag as string;
                if (glyph == null) continue;
                var old = i.Image;
                i.Image = Theme.GlyphBitmap(glyph, Ui.S(16), i.Enabled ? Theme.Fg : Theme.Faint);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Line))
                e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(Point.Empty, e.Item.Size);
            r.Inflate(-Ui.S(2), 0);
            Theme.FillRound(e.Graphics, Theme.SurfaceHot, r, Ui.S(5));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Fg : Theme.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Line))
                e.Graphics.DrawLine(p, Ui.S(8), y, e.Item.Width - Ui.S(8), y);
        }

        class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Line; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Theme.SurfaceHot; } }
            public override Color SeparatorDark { get { return Theme.Line; } }
            public override Color SeparatorLight { get { return Theme.Line; } }
        }
    }

    // A rounded, custom-drawn button: primary (accent) or quiet (surface), with an optional glyph.
    class FlatButton : Control
    {
        public bool Primary;
        public string Glyph;
        bool hot, down;
        Font iconFont;

        public FlatButton(string text, string glyph, bool primary)
        {
            Text = text;
            Glyph = glyph;
            Primary = primary;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.MakeFont(13, true);
            iconFont = Theme.MakeIconFont(14);
            Height = Ui.S(34);
            Width = PreferredWidth;
        }

        public int PreferredWidth
        {
            get
            {
                int w = TextRenderer.MeasureText(Text, Font, Size.Empty, Theme.Center).Width + Ui.S(32);
                if (Glyph != null) w += Ui.S(22);
                return w;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new Rectangle(0, 0, Width, Height);
            Color fill, fg;
            if (!Enabled) { fill = Theme.Surface; fg = Theme.Faint; }
            else if (Primary) { fill = down ? Theme.Mix(Theme.Accent, Theme.Bg, 0.25f) : hot ? Theme.Mix(Theme.Accent, Theme.Fg, 0.12f) : Theme.Accent; fg = Theme.AccentFg; }
            else { fill = down ? Theme.Line : hot ? Theme.SurfaceHot : Theme.Surface; fg = Theme.Fg; }
            Theme.FillRound(g, fill, r, Ui.S(7));
            if (!Primary) Theme.StrokeRound(g, Theme.Line, r, Ui.S(7));
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, Theme.Accent, r, Ui.S(7), Math.Max(1, Ui.S(1.5f)));

            int textW = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            int gw = Glyph != null ? Ui.S(22) : 0;
            int x = (Width - textW - gw) / 2;
            if (Glyph != null)
                TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(x, 0, Ui.S(16), Height), fg, Theme.Center);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw, 0, textW, Height), fg, Theme.Center);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) iconFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
