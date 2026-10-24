using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PotatoMixer
{
    // ---------------------------------------------------------------- glass theme
    static class T
    {
        public const int W = 1250, H = 842;
        public static Color Text = Color.FromArgb(226, 240, 230), Dim = Color.FromArgb(126, 160, 142), Green = Color.FromArgb(52, 199, 89),
            GreenDark = Color.FromArgb(22, 120, 46), Red = Color.FromArgb(224, 72, 72), Yellow = Color.FromArgb(240, 196, 70);

        public const string Face = "Bahnschrift";   // fallback only
        static Dictionary<int, Font> fontCache = new Dictionary<int, Font>();
        public static Font F(float s, FontStyle st)   // fonts are created once and reused (they are never disposed by callers)
        {
            int key = (int)(s * 10) * 8 + (int)st; Font f;
            if (fontCache.TryGetValue(key, out f)) return f;
            FontFamily ff = Fonts.Family;
            f = ff != null && ff.IsStyleAvailable(st) ? new Font(ff, s, st) : new Font(Face, s, st);
            fontCache[key] = f; return f;
        }
        public static Font F(float s) { return F(s, FontStyle.Regular); }

        public static GraphicsPath RR(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // Window-wide dark green gradient, drawn relative to the control so every panel lines up.
        static Bitmap bg;
        public static void Back(Graphics g, Control c)
        {
            if (bg == null)   // the window gradient is rendered once and blitted afterwards
            {
                bg = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics bgg = Graphics.FromImage(bg))
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(14, 36, 27), Color.FromArgb(30, 62, 46), 55f))
                    bgg.FillRectangle(b, 0, 0, W, H);
            }
            g.DrawImageUnscaled(bg, -c.Left, -c.Top);
        }

        // Frosted card: translucent fill, soft highlight on top, hairline border.
        public static void Card(Graphics g, RectangleF r, float rad)
        {
            using (GraphicsPath p = RR(r, rad))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), Color.FromArgb(46, 255, 255, 255), Color.FromArgb(16, 255, 255, 255), 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(46, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
        }

        public static void Pill(Graphics g, RectangleF r, bool on, Color? tint)
        {
            using (GraphicsPath p = RR(r, r.Height / 2.4f))
            {
                if (on)
                {
                    Color a = tint ?? Green, b2 = tint.HasValue ? Color.FromArgb(tint.Value.R * 3 / 4, tint.Value.G * 3 / 4, tint.Value.B * 3 / 4) : GreenDark;
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), a, b2, 90f)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawPath(pen, p);
                }
                else
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), Color.FromArgb(50, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90f)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f)) g.DrawPath(pen, p);
                }
            }
        }

        // Normal button, plus a simple hover hint: the button gets a shade and a thin edge (white on coloured buttons, near-black green on grey ones). Nothing moves, no shadow.
        public static void PillH(Graphics g, RectangleF r, bool on, Color? tint, bool hover)
        {
            Pill(g, r, on, tint);
            if (!hover) return;
            // coloured (active) buttons: white inner shade and white edge; grey (off) buttons: near-black green shade and edge
            Color fill = on ? Color.FromArgb(46, 217, 217, 217) : Color.FromArgb(106, 3, 20, 11);   // 125 -> 106: 15 % less dark   // 217 = 85 % of white (255)
            Color edge = on ? Color.FromArgb(215, 217, 217, 217) : Color.FromArgb(200, 4, 22, 12);
            using (GraphicsPath p = RR(r, r.Height / 2.4f))
            {
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(edge, 0.88f)) g.DrawPath(pen, p);
            }
        }
        public static void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h)
        {
            using (StringFormat sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (Brush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        public static void Setup(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; }

        public static void DeviceMenu(Control at, string prefix, bool render)
        {
            string cur = Eng.GetStr(prefix + ".Device.name");
            ContextMenuStrip m = T.NewMenu();
            ToolStripMenuItem none = new ToolStripMenuItem("(none)") { Checked = cur.Length == 0 };
            none.Click += delegate { Eng.Script(prefix + ".device.wdm=\"\";"); };
            m.Items.Add(none);
            foreach (string n in Eng.Devices(render))
            {
                string name = n;
                ToolStripMenuItem it = new ToolStripMenuItem(name) { Checked = name == cur };
                it.Click += delegate { Eng.Script(prefix + ".device.wdm=\"" + name + "\";"); };
                m.Items.Add(it);
            }
            m.Show(at, new Point(0, at.Height));
        }

        // Pick a running program whose sound is captured on its own (no virtual device needed).
        public static void AppMenu(Control at, string prefix)
        {
            string cur = Eng.GetStr(prefix + ".App");
            ContextMenuStrip m = NewMenu();
            ToolStripMenuItem none = new ToolStripMenuItem("(none)") { Checked = cur.Length == 0 };
            none.Click += delegate { Eng.Script(prefix + ".app=\"\";"); };
            m.Items.Add(none);
            foreach (KeyValuePair<string, string> a in Eng.Apps())
            {
                string key = a.Key, title = a.Value.Length > 46 ? a.Value.Substring(0, 46) + "..." : a.Value;
                ToolStripMenuItem it = new ToolStripMenuItem(key + "   -   " + title) { Checked = key == cur };
                it.Click += delegate { Eng.Script(prefix + ".app=\"" + key + "\";"); };
                m.Items.Add(it);
            }
            if (cur.Length > 0)
            {
                m.Items.Add(new ToolStripSeparator());
                float gain = Eng.Get(prefix + ".Gain");
                string[] vn = { "Volume 100%", "Volume 75%", "Volume 50%", "Volume 25%" }; float[] vg = { 0f, -2.5f, -6f, -12f };
                for (int i = 0; i < vn.Length; i++)
                {
                    float v = vg[i]; ToolStripMenuItem vi = new ToolStripMenuItem(vn[i]) { Checked = Math.Abs(gain - v) < 0.6f };
                    vi.Click += delegate { Eng.Set(prefix + ".Gain", v); };
                    m.Items.Add(vi);
                }
            }
            m.Show(at, new Point(0, at.Height));
        }

        public static string Ask(string title, string cur)
        {
            using (AskForm f = new AskForm(title, cur))
                return f.ShowDialog(Form.ActiveForm) == DialogResult.OK ? f.Value : null;
        }

        // Context menu in the app's glass style.
        public static ContextMenuStrip NewMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new GlassRenderer(); m.Font = F(9.5f); m.ForeColor = Text; m.BackColor = Color.FromArgb(24, 52, 39);
            m.ShowImageMargin = false; m.ShowCheckMargin = true; m.DropShadowEnabled = false; 
            m.Opened += delegate { m.Region = new Region(RR(new RectangleF(0, 0, m.Width, m.Height), 14)); };
            return m;
        }
    }

    class GlassRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height + 1), Color.FromArgb(34, 68, 51), Color.FromArgb(20, 44, 33), 90f))
                e.Graphics.FillRectangle(b, 0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = T.RR(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1.5f, e.ToolStrip.Height - 1.5f), 14))
            using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f)) e.Graphics.DrawPath(pen, p);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            if (e.Item.Selected && e.Item.Enabled)
                using (GraphicsPath p = T.RR(r, 9))
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, r.Height + 1), Color.FromArgb(150, 52, 199, 89), Color.FromArgb(150, 22, 120, 46), 90f)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawPath(pen, p);
                }
            ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
            if (mi != null && mi.Checked)
                using (Brush b = new SolidBrush(e.Item.Selected ? Color.White : T.Green)) g.FillEllipse(b, 12, e.Item.Height / 2 - 4, 8, 8);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : T.Text;
            base.OnRenderItemText(e);
        }
    }

    // Rename dialog in the same glass look (no system window chrome).
    class AskForm : Form
    {
        public string Value; TextBox tb;
        [System.Runtime.InteropServices.DllImport("user32")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        public AskForm(string title, string cur)
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            ClientSize = new Size(360, 168); BackColor = Color.FromArgb(20, 44, 33);
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 20));
            Surface s = new Surface { Dock = DockStyle.Fill };
            s.Painter = delegate(Graphics g, int w, int h)
            {
                T.Card(g, new RectangleF(1, 1, w - 3, h - 3), 20);
                T.Txt(g, title == "Label" ? "Rename" : title, T.F(14, FontStyle.Bold), T.Text, new RectangleF(26, 16, w - 52, 28), StringAlignment.Near);
                using (GraphicsPath p = T.RR(new RectangleF(24, 56, w - 48, 40), 12))
                {
                    using (Brush b = new SolidBrush(Color.FromArgb(22, 48, 36))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(90, 52, 199, 89), 1.5f)) g.DrawPath(pen, p);
                }
            };
            s.MouseDown += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            Controls.Add(s);
            tb = new TextBox { BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(22, 48, 36), ForeColor = T.Text, Font = T.F(11.5f), Text = cur, Left = 38, Top = 66, Width = w2() };
            s.Controls.Add(tb);
            Cmd cancel = new Cmd("Cancel", delegate { DialogResult = DialogResult.Cancel; }, false) { Bounds = new Rectangle(168, 112, 82, 34) };
            Cmd ok = new Cmd("OK", Accept, true) { Bounds = new Rectangle(258, 112, 82, 34) };
            s.Controls.Add(cancel); s.Controls.Add(ok);
            Shown += delegate { tb.Focus(); tb.SelectAll(); };
        }
        int w2() { return 360 - 76; }
        void Accept() { Value = tb.Text; DialogResult = DialogResult.OK; }
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Enter) { Accept(); return true; }
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref m, k);
        }
    }
    // thmanyah Sans, embedded in the exe as an obfuscated resource (the font licence allows embedding only inside a compiled product).
    static class Fonts
    {
        static PrivateFontCollection pfc; static FontFamily fam; static List<IntPtr> pins = new List<IntPtr>();
        public static FontFamily Family { get { Init(); return fam; } }
        static void Init()
        {
            if (pfc != null) return;
            pfc = new PrivateFontCollection();
            try
            {
                foreach (string r in new string[] { "f1.bin", "f2.bin" })
                {
                    byte[] b = Read(r); IntPtr p = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, p, b.Length); pins.Add(p); pfc.AddMemoryFont(p, b.Length);
                }
                foreach (FontFamily f in pfc.Families) if (f.Name == "thmanyah sans") fam = f;
            }
            catch { fam = null; }
        }
        static byte[] Read(string res)
        {
            byte[] key = { 0x5A, 0xC3, 0x27, 0x91 };
            using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                s.CopyTo(ms); byte[] b = ms.ToArray();
                for (int i = 0; i < b.Length; i++) b[i] ^= key[i & 3];
                return b;
            }
        }
    }

    // The program name in ornamental Arabic lettering, stored as a baked vector outline (resource "dasman.bin").
    static class NameArt
    {
        static GraphicsPath path;
        public static GraphicsPath Path
        {
            get
            {
                if (path != null) return path;
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("dasman.bin"))
                using (System.IO.BinaryReader r = new System.IO.BinaryReader(s))
                {
                    int n = r.ReadInt32(); PointF[] pts = new PointF[n];
                    for (int i = 0; i < n; i++) pts[i] = new PointF(r.ReadSingle(), r.ReadSingle());
                    byte[] types = r.ReadBytes(n);
                    path = new GraphicsPath(pts, types, FillMode.Winding);
                }
                return path;
            }
        }        // Fill the lettering into the rectangle r (aspect ratio kept, left aligned) with a light-to-green gradient.
        public static void Draw(Graphics g, RectangleF r)
        {
            RectangleF b = Path.GetBounds(); float k = Math.Min(r.Width / b.Width, r.Height / b.Height);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y + (r.Height - b.Height * k) / 2); g.ScaleTransform(k, k); g.TranslateTransform(-b.X, -b.Y);
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(b.X, b.Y, b.Width, b.Height), Color.FromArgb(246, 255, 246), Color.FromArgb(70, 212, 104), 0f))
            using (Brush sh = new SolidBrush(Color.FromArgb(110, 0, 30, 10)))
            {
                g.TranslateTransform(5, 7); g.FillPath(sh, Path); g.TranslateTransform(-5, -7);
                g.FillPath(br, Path);
            }
            g.Restore(st);
        }
        static GraphicsPath cartoon;
        static GraphicsPath Cartoon
        {
            get
            {
                if (cartoon != null) return cartoon;
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("name.bin"))
                using (System.IO.BinaryReader r = new System.IO.BinaryReader(s))
                {
                    int n = r.ReadInt32(); PointF[] pts = new PointF[n];
                    for (int i = 0; i < n; i++) pts[i] = new PointF(r.ReadSingle(), r.ReadSingle());
                    cartoon = new GraphicsPath(pts, r.ReadBytes(n), FillMode.Winding);
                }
                return cartoon;
            }
        }

        // The program name "DsMaN" as a cartoon wordmark: thick dark outline, bright gradient fill, glossy highlight. Returns the drawn width.
        public static float DrawText(Graphics g, float x, float y, float height)
        {
            RectangleF b0 = Cartoon.GetBounds(); float k = height / b0.Height;
            using (GraphicsPath p = (GraphicsPath)Cartoon.Clone())
            {
                using (Matrix m = new Matrix(k, 0, 0, k, x + 4, y + 4)) p.Transform(m);
                RectangleF b = p.GetBounds();
                using (Pen shadow = new Pen(Color.FromArgb(120, 0, 25, 8), 9f) { LineJoin = LineJoin.Round })
                using (Pen outline = new Pen(Color.FromArgb(8, 46, 20), 7f) { LineJoin = LineJoin.Round })
                using (Pen shine = new Pen(Color.FromArgb(150, 255, 255, 255), 2f) { LineJoin = LineJoin.Round })
                using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(b.X, b.Y, b.Width, b.Height), Color.FromArgb(255, 252, 150), Color.FromArgb(52, 199, 89), 90f))
                {
                    GraphicsState st = g.Save(); g.TranslateTransform(2, 4); g.DrawPath(shadow, p); g.Restore(st);
                    g.DrawPath(outline, p); g.FillPath(br, p);
                    using (GraphicsPath hl = (GraphicsPath)p.Clone()) using (Matrix mm = new Matrix(1, 0, 0, 1, -1.5f, -1.5f)) { hl.Transform(mm); g.DrawPath(shine, hl); }
                }
                return b.Width + 8;
            }
        }        public static float Aspect { get { RectangleF b = Path.GetBounds(); return b.Width / b.Height; } }
    }

    abstract class VC : Control
    {
        public static List<VC> All = new List<VC>();
        protected VC() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; All.Add(this); }
        public virtual void Sync() { }
        protected bool Hover;   // mouse is over the control
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover = false; Invalidate(); }
    }

    class Surface : Panel
    {
        public Action<Graphics, int, int> Painter;
        public Surface() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            T.Setup(e.Graphics); T.Back(e.Graphics, this);
            if (Painter != null) Painter(e.Graphics, Width, Height);
        }
    }

    // ---------------------------------------------------------------- widgets
    class Knob : VC
    {
        public string Param; public float Min, Max, Def, Val; bool drag; int y0; float v0;
        public Knob(string p, float min, float max, float def, int size) { Param = p; Min = min; Max = max; Def = def; Size = new Size(size, size); }
        public override void Sync() { if (drag) return; Val = Eng.Get(Param); Invalidate(); }
        void Put(float v) { Val = Math.Max(Min, Math.Min(Max, (float)Math.Round(v, 1))); Eng.Set(Param, Val); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            RectangleF r = new RectangleF(4, 4, Width - 8, Height - 8);
            using (Brush b = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillEllipse(b, r);
            using (Pen p = new Pen(Color.FromArgb(52, 255, 255, 255), 3)) g.DrawEllipse(p, r);
            float frac = (Val - Min) / (Max - Min), start, sweep;
            if (Min < 0) { start = 270; sweep = (frac - 0.5f) * 270f; } else { start = 135; sweep = frac * 270f; }
            if (Math.Abs(sweep) > 0.5f) using (Pen p = new Pen(T.Green, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, start, sweep);
            T.Txt(g, Val.ToString("0.0").Replace("-0.0", "0.0"), T.F(Width > 30 ? 8.5f : 6.5f, FontStyle.Bold), T.Text, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) { drag = true; y0 = e.Y; v0 = Val; Capture = true; } }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) Put(v0 + (y0 - e.Y) * (Max - Min) / 120f); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { Put(Val + (e.Delta > 0 ? 1 : -1) * (Max - Min) / 40f); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { Put(Def); }
    }

    class Pad : VC
    {
        public string PX, PY; public float MinX = -0.5f, MaxX = 0.5f, MinY = 0, MaxY = 1, X, Y; public bool ThreeD; bool drag;
        public Pad(string px, string py, bool threeD) { PX = px; PY = py; ThreeD = threeD; }
        public override void Sync() { if (drag) return; X = Eng.Get(PX); Y = Eng.Get(PY); Invalidate(); }
        void Move2(int mx, int my)
        {
            float fx = Math.Max(0f, Math.Min(1f, (mx - 10f) / (Width - 20f))), fy = Math.Max(0f, Math.Min(1f, 1f - (my - 10f) / (Height - 20f)));
            X = MinX + fx * (MaxX - MinX); Y = MinY + fy * (MaxY - MinY);
            Eng.Set(PX, X); Eng.Set(PY, Y); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = T.RR(r, 14))
            {
                using (Brush b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(44, 255, 255, 255))) g.DrawPath(pen, p);
            }
            Font f = T.F(8); Color d = Color.FromArgb(110, 150, 130);
            float px = 10 + (X - MinX) / (MaxX - MinX) * (Width - 20), py = Height - 10 - (Y - MinY) / (MaxY - MinY) * (Height - 20);
            using (Pen pen = new Pen(Color.FromArgb(34, 255, 255, 255))) { g.DrawLine(pen, Width / 2, 8, Width / 2, Height - 8); g.DrawLine(pen, 8, Height / 2, Width - 8, Height / 2); }
            if (!ThreeD)
            {
                T.Txt(g, "L", f, d, new RectangleF(10, Height / 2 - 20, 20, 14), StringAlignment.Near);
                T.Txt(g, "R", f, d, new RectangleF(Width - 30, Height / 2 - 20, 20, 14), StringAlignment.Far);
                T.Txt(g, "Pan", f, d, new RectangleF(0, 8, Width, 14), StringAlignment.Center);
            }
            else
            {
                T.Txt(g, "Front", f, d, new RectangleF(0, 3, Width, 12), StringAlignment.Center);
                T.Txt(g, "Rear", f, d, new RectangleF(0, Height - 15, Width, 12), StringAlignment.Center);
                T.Txt(g, "L", f, d, new RectangleF(6, 0, 14, Height), StringAlignment.Near);
                T.Txt(g, "R", f, d, new RectangleF(Width - 20, 0, 14, Height), StringAlignment.Far);
            }
            using (Brush b = new SolidBrush(T.Green)) g.FillEllipse(b, px - 7, py - 7, 14, 14);
            using (Pen pen = new Pen(Color.White, 2)) g.DrawEllipse(pen, px - 7, py - 7, 14, 14);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; Move2(e.X, e.Y); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) Move2(e.X, e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { X = 0; Y = 0; Eng.Set(PX, 0); Eng.Set(PY, 0); Invalidate(); }
    }

    class Fader : VC
    {
        public string Param, Prefix; public int Ch, MeterType, BarW = 6; public float Lo = -60, Hi = 12, Val; public bool AllowDevice;
        float la, lb; bool drag; const int Th = 32;
        public Fader(string prefix, int ch, int type) { Prefix = prefix; Param = prefix + ".Gain"; Ch = ch; MeterType = type; }
        // When a virtual strip has a playback device chosen, its fader is that device's Windows volume (0-100%), exactly like the speaker slider in Windows.
        string Dev() { return AllowDevice ? Eng.GetStr(Prefix + ".Device.name") : ""; }
        void Commit() { string dn = Dev(); if (dn.Length > 0) Eng.SetDeviceVolume(dn, Val / 100f); else Eng.Set(Param, Val); }
        public override void Sync()
        {
            if (drag) return; string dn = Dev();
            if (dn.Length > 0) { float v; bool m; if (Eng.GetDeviceVolume(dn, out v, out m)) { Lo = 0; Hi = 100; Val = (float)Math.Round(v * 100); } }
            else { Lo = -60; Hi = 12; Val = Eng.Get(Param); }
            Invalidate();
        }
        public void RefreshDevice() { if (!drag && Dev().Length > 0) { float old = Val; Sync(); } }
        public void UpdateLevel()
        {
            float a = Eng.Level(MeterType, Ch), b = Eng.Level(MeterType, Ch + 1);
            if (Math.Abs(a - la) < 0.0008f && Math.Abs(b - lb) < 0.0008f) return;   // nothing visible changed: skip the repaint
            la = a; lb = b; Invalidate();
        }
        float CX { get { return 2 * BarW + 12 + Th / 2f; } }
        float Frac(float v) { return Math.Max(0f, Math.Min(1f, (v - Lo) / (Hi - Lo))); }
        void FromY(int y)
        {
            float f = 1f - (y - 8f - Th / 2f) / (Height - 16f - Th);
            Val = (float)Math.Round(Lo + Math.Max(0f, Math.Min(1f, f)) * (Hi - Lo), Dev().Length > 0 ? 0 : 1);
            Commit(); Invalidate();
        }
        void Bar(Graphics g, int x, float lv)
        {
            RectangleF r = new RectangleF(x, 10, BarW, Height - 20);
            using (GraphicsPath p = T.RR(r, BarW / 2f)) using (Brush b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillPath(b, p);
            float db = lv > 0.00001f ? (float)(20 * Math.Log10(lv)) : -90f;
            float h = Math.Max(0f, Math.Min(1f, (db + 60f) / 72f)) * r.Height;
            if (h > 1)
            {
                RectangleF f = new RectangleF(r.X, r.Bottom - h, r.Width, h);
                using (GraphicsPath p = T.RR(f, BarW / 2f)) using (Brush b = new SolidBrush(db > -1 ? T.Red : (db > -9 ? T.Yellow : T.Green))) g.FillPath(b, p);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            float ox = Math.Max(0, (Width - 58) / 2); g.TranslateTransform(ox, 0);
            Bar(g, 2, la); Bar(g, 4 + BarW, lb);
            float cx = CX, top = 8 + Th / 2f, bot = Height - 8 - Th / 2f, cy = top + (1f - Frac(Val)) * (bot - top);
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, cx, top, cx, bot);
            using (Pen p = new Pen(T.Green, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, cx, cy, cx, bot);
            using (Brush b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(b, cx - Th / 2f + 1, cy - Th / 2f + 3, Th, Th);
            using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, cx - Th / 2f, cy - Th / 2f, Th, Th);
            T.Txt(g, Dev().Length > 0 ? ((int)Val + "%") : (Math.Abs(Val) < 0.05f ? "0dB" : Val.ToString("0.0")), T.F(7.5f, FontStyle.Bold), Color.FromArgb(24, 80, 44), new RectangleF(cx - Th / 2f, cy - 8, Th, 16), StringAlignment.Center);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.X - Math.Max(0, (Width - 58) / 2) >= 2 * BarW + 6) { drag = true; Capture = true; FromY(e.Y); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) FromY(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { Val = Math.Max(Lo, Math.Min(Hi, Val + (e.Delta > 0 ? 1 : -1) * (Dev().Length > 0 ? 2 : 1))); Commit(); Invalidate(); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { Val = Dev().Length > 0 ? 100 : 0; Commit(); Invalidate(); }
    }

    class Tog : VC
    {
        public string Param, Label, DevPrefix; public int Kind; public bool On; // 0 route, 1 mute, 2 solo, 3 other
        public Tog(string param, string label, int kind) { Param = param; Label = label; Kind = kind; }
        string Dev() { return DevPrefix != null ? Eng.GetStr(DevPrefix + ".Device.name") : ""; }
        public override void Sync()
        {
            string dn = Dev(); bool now;
            if (dn.Length > 0) { float v; bool m; now = Eng.GetDeviceVolume(dn, out v, out m) && m; } else now = Eng.Get(Param) > 0;
            if (now != On) { On = now; Invalidate(); } else if (dn.Length == 0) Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            Color? tint = Kind == 1 ? (Color?)T.Red : (Kind == 2 ? (Color?)T.Yellow : null);
            T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), On, tint, Hover);
            T.Txt(g, Kind == 0 ? "▶" + Label : Label, T.F(9f, FontStyle.Bold), On ? (Kind == 2 ? Color.FromArgb(40, 34, 10) : Color.White) : T.Dim, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            On = !On; string dn = Dev();
            if (dn.Length > 0) Eng.SetDeviceMute(dn, On); else Eng.Set(Param, On ? 1 : 0);
            Invalidate();
        }
    }

    class Cmd : VC
    {
        public string Text2; public Action Act; public bool Primary;
        public Cmd(string text, Action act, bool primary) { Text2 = text; Act = act; Primary = primary; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), Primary, null, Hover);
            T.Txt(g, Text2, T.F(9.5f, FontStyle.Bold), T.Text, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (Act != null) Act(); }
    }

    // Strip header: label + device. Left click = choose device, right click = rename.
    class NameBox : VC
    {
        public string Prefix, Default; public bool Hw, App; string name = "", sub = "";
        public NameBox(string prefix, string def, bool hw) { Prefix = prefix; Default = def; Hw = hw; Cursor = Cursors.Hand; }
        public override void Sync()
        {
            name = Eng.GetStr(Prefix + ".Label"); if (name.Length == 0) name = Default;
            sub = App ? Eng.GetStr(Prefix + ".App") : Eng.GetStr(Prefix + ".Device.name");
            if (sub.Length == 0) sub = App ? "Pick a program to share its sound" : (Hw ? "Select input device" : "Select output to capture");
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.Txt(g, name, T.F(13, FontStyle.Bold), T.Text, new RectangleF(2, 0, Width - 4, 26), StringAlignment.Near);
            using (StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
            using (Brush b = new SolidBrush(T.Dim)) g.DrawString(sub, T.F(8), b, new RectangleF(2, 28, Width - 4, Height - 28), sf);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                string n = T.Ask("Label", name == Default ? "" : name);
                if (n != null) Eng.Script(Prefix + ".Label=\"" + n.Replace("\"", "").Replace(";", "") + "\";");
            }
            else if (App) T.AppMenu(this, Prefix);
            else T.DeviceMenu(this, Prefix, !Hw);
        }
    }

    // Bus tag + device chooser.
    class BusBtn : VC
    {
        public int Bus; string tag, dev = "", full = "", lbl = ""; static ToolTip tip = new ToolTip();
        public BusBtn(int bus, string t) { Bus = bus; tag = t; Cursor = Cursors.Hand; }
        public override void Sync() { full = Eng.GetStr("Bus[" + Bus + "].Device.name"); int pi = full.IndexOf(" ("); dev = pi > 0 ? full.Substring(0, pi) : full; lbl = Eng.GetStr("Bus[" + Bus + "].Label"); tip.SetToolTip(this, full.Length > 0 ? full : "Click to choose a playback device"); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), dev.Length > 0, null, Hover);
            T.Txt(g, (lbl.Length > 0 ? lbl : tag) + " ▾", T.F(10.5f, FontStyle.Bold), dev.Length > 0 ? Color.White : T.Dim, new RectangleF(2, 0, Width - 4, Height), StringAlignment.Center);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                string n = T.Ask("Label", lbl);
                if (n != null) Eng.Script("Bus[" + Bus + "].Label=\"" + n.Replace("\"", "").Replace(";", "") + "\";");
            }
            else T.DeviceMenu(this, "Bus[" + Bus + "]", true);
        }
    }

    // Update check: a small text manifest ("version=2.1" and "url=<link or path to the new DsMaN.exe>") at a link or path set in Settings.
    static class Updater
    {
        public const string Version = "2.2";
        // Where new versions are announced: a small text file in the project's GitHub repository (version=... and url=... lines).
        // The "Update source" setting in the menu overrides it (a link or a file path).
        public const string DefaultSource = "https://raw.githubusercontent.com/abo5lood10/DsMaN/main/update.txt";
        public static volatile bool Available, Busy; public static string NewVersion = "", DownloadUrl = "", Message = "";

        public static void Check(Action done)
        {
            if (Busy) return; Busy = true;
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try
                {
                    string src = Eng.GetStr("App.UpdateUrl").Trim();
                    if (src.Length == 0) src = DefaultSource;
                    string txt;
                    if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                        using (System.Net.WebClient wc = new System.Net.WebClient()) txt = wc.DownloadString(src);
                    }
                    else txt = System.IO.File.ReadAllText(src);
                    string v = "", url = "";
                    foreach (string line in txt.Split('\n'))
                    {
                        int i = line.IndexOf('='); if (i < 0) continue;
                        string k = line.Substring(0, i).Trim().ToLower(), val = line.Substring(i + 1).Trim();
                        if (k == "version") v = val; else if (k == "url") url = val;
                    }
                    Version nv, cur = new Version(Version);
                    if (v.Length > 0 && url.Length > 0 && System.Version.TryParse(v, out nv) && nv > cur) { NewVersion = v; DownloadUrl = url; Available = true; Message = "Update " + v + " available"; }
                    else { Available = false; Message = "Up to date (v" + Version + ")"; }
                }
                catch (Exception ex) { Available = false; Message = "Update check failed: " + ex.Message; }
                finally { Busy = false; if (done != null) done(); }
            }) { IsBackground = true };
            th.Start();
        }

        // Download the new exe next to the running one, then swap it in after this process exits and start it again.
        public static void Apply()
        {
            string exe = Application.ExecutablePath, tmp = exe + ".new";
            if (DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                using (System.Net.WebClient wc = new System.Net.WebClient()) wc.DownloadFile(DownloadUrl, tmp);
            }
            else System.IO.File.Copy(DownloadUrl, tmp, true);
            string bat = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsman_update.bat");
            // sanity check: must look like a Windows program of a sensible size before it replaces the running one
            using (System.IO.FileStream fs = System.IO.File.OpenRead(tmp))
                if (fs.Length < 200000 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') { System.IO.File.Delete(tmp); throw new Exception("downloaded file is not a valid DsMaN.exe"); }
            System.IO.File.WriteAllText(bat, "@echo off\r\nping 127.0.0.1 -n 3 >nul\r\nmove /y \"" + tmp + "\" \"" + exe + "\" >nul\r\nstart \"\" \"" + exe + "\"\r\ndel \"%~f0\"\r\n");
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"") { CreateNoWindow = true, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi);
        }
    }

    // Header button shown only while an update is available; colour of the Y button on an Xbox 360 pad.
    class UpdateBtn : VC
    {
        public Action Act; bool hot;
        public UpdateBtn() { Cursor = Cursors.Hand; Visible = false; }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            RectangleF r = hot ? new RectangleF(1, 0, Width - 3, Height - 5) : new RectangleF(1, 1, Width - 3, Height - 3);
            if (hot)
            {
                // soft drop shadow under the raised button
                for (int i = 3; i >= 0; i--)
                    using (GraphicsPath sp = T.RR(new RectangleF(r.X + 1 - i * 0.5f, r.Y + 3 - i * 0.2f, r.Width - 2 + i, r.Height + i * 0.8f), r.Height / 2.4f))
                    using (Brush sb = new SolidBrush(Color.FromArgb(34 + (3 - i) * 8, 0, 0, 0))) g.FillPath(sb, sp);
            }
            using (GraphicsPath p = T.RR(r, r.Height / 2.4f))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), hot ? Color.FromArgb(255, 226, 96) : Color.FromArgb(255, 214, 54), hot ? Color.FromArgb(245, 180, 10) : Color.FromArgb(232, 164, 0), 90f)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
            T.Txt(g, "Update  v" + Updater.NewVersion, T.F(9.5f, FontStyle.Bold), Color.FromArgb(52, 36, 0), new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (Act != null) Act(); }
    }

    // Horizontal slider in the glass style (0..1).
    class HSlide : Control
    {
        public float Value; public event Action<float> Changed; bool drag;
        public HSlide() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Cursor = Cursors.Hand; }
        float X(float v) { return 10 + v * (Width - 20); }
        void Set(int mx) { Value = Math.Max(0f, Math.Min(1f, (mx - 10f) / (Width - 20f))); Invalidate(); if (Changed != null) Changed(Value); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g); float cy = Height / 2f, x = X(Value);
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, 10, cy, Width - 10, cy);
            using (Pen p = new Pen(T.Green, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, 10, cy, x, cy);
            using (Brush b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(b, x - 9, cy - 8, 20, 20);
            using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, x - 10, cy - 10, 20, 20);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; Set(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) Set(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
    }

    // Pop-up with one row per program that currently has sound: volume slider and mute. Changes are the same as in Windows' Volume mixer and are remembered.
    class AppVolForm : Form
    {
        class Row { public string Name; public Label Title; public HSlide Slide; public Label Pct; public Cmd Mute; public bool Muted; }
        List<Row> rows = new List<Row>(); Timer tm = new Timer(); Surface bg; Label empty;
        public AppVolForm(Point where, int rightEdge)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(20, 44, 33);
            ClientSize = new Size(420, 120); Location = new Point(rightEdge - 420, where.Y);
            bg = new Surface { Dock = DockStyle.Fill }; bg.Painter = delegate(Graphics g, int w, int h)
            {
                T.Card(g, new RectangleF(1, 1, w - 3, h - 3), 18);
                T.Txt(g, "App volume", T.F(11, FontStyle.Bold), T.Text, new RectangleF(18, 8, 250, 24), StringAlignment.Near);
            };
            Controls.Add(bg);
            empty = new Label { Text = "No program is playing sound right now.", ForeColor = T.Dim, BackColor = Color.Transparent, Font = T.F(9.5f), Left = 18, Top = 50, Width = 380, Height = 24 };
            bg.Controls.Add(empty);
            Deactivate += delegate { Close(); };
            tm.Interval = 600; tm.Tick += delegate { Refresh2(); }; tm.Start();
            FormClosed += delegate { tm.Stop(); tm.Dispose(); };
            Refresh2();
        }

        void Refresh2()
        {
            List<AppVol> apps = Eng.AppVolumes(); apps.RemoveAll(delegate(AppVol a) { return a.Name == "System sounds" && a.Peak < 0.001f; });
            bool same = apps.Count == rows.Count;
            if (same) for (int i = 0; i < apps.Count; i++) if (apps[i].Name != rows[i].Name) same = false;
            if (!same) Rebuild(apps);
            for (int i = 0; i < rows.Count && i < apps.Count; i++)
            {
                Row r = rows[i]; if (r.Slide.Capture) continue;
                r.Slide.Value = apps[i].Volume; r.Slide.Invalidate(); r.Pct.Text = (int)Math.Round(apps[i].Volume * 100) + "%";
                r.Muted = apps[i].Mute; r.Mute.Text = r.Muted ? "Muted" : "Mute"; r.Mute.Primary = r.Muted; r.Mute.Invalidate();
            }
        }

        void Rebuild(List<AppVol> apps)
        {
            foreach (Row r in rows) { bg.Controls.Remove(r.Title); bg.Controls.Remove(r.Slide); bg.Controls.Remove(r.Pct); bg.Controls.Remove(r.Mute); r.Title.Dispose(); r.Slide.Dispose(); r.Pct.Dispose(); r.Mute.Dispose(); }
            rows.Clear(); empty.Visible = apps.Count == 0;
            for (int i = 0; i < apps.Count; i++)
            {
                Row r = new Row { Name = apps[i].Name }; int y = 44 + i * 58; string nm = r.Name.Length > 0 ? char.ToUpper(r.Name[0]) + r.Name.Substring(1) : r.Name;
                r.Title = new Label { Text = nm, ForeColor = T.Text, BackColor = Color.Transparent, Font = T.F(10, FontStyle.Bold), Left = 18, Top = y, Width = 300, Height = 20 };
                r.Slide = new HSlide { Left = 8, Top = y + 22, Width = 290, Height = 26 };
                r.Pct = new Label { ForeColor = T.Dim, BackColor = Color.Transparent, Font = T.F(9), Left = 304, Top = y + 26, Width = 44, Height = 18 };
                r.Mute = new Cmd("Mute", null, false) { Left = 350, Top = y + 20, Width = 58, Height = 28 };
                Row rr = r;
                r.Slide.Changed += delegate(float v) { Eng.SetAppVolume(rr.Name, v, rr.Muted && v > 0 ? false : rr.Muted); rr.Pct.Text = (int)Math.Round(v * 100) + "%"; };
                r.Mute.Act = delegate { rr.Muted = !rr.Muted; Eng.SetAppVolume(rr.Name, rr.Slide.Value, rr.Muted); rr.Mute.Text2 = rr.Muted ? "Muted" : "Mute"; rr.Mute.Primary = rr.Muted; rr.Mute.Invalidate(); };
                bg.Controls.Add(r.Title); bg.Controls.Add(r.Slide); bg.Controls.Add(r.Pct); bg.Controls.Add(r.Mute); rows.Add(r);
            }
            ClientSize = new Size(420, Math.Max(90, 52 + Math.Max(1, apps.Count) * 58 + 8));
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 20));
        }
    }

    // Header button: pick the program whose sound is shared (mixed into whatever Mic 1 / Mic 2 are sent to). Remembered even when the program is closed.
    class AppBtn : VC
    {
        string app = "";
        public AppBtn() { Cursor = Cursors.Hand; }
        public override void Sync() { app = Eng.GetStr("Strip[2].App"); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), app.Length > 0, null, Hover);
            string label = app.Length > 0 ? (app.Length > 14 ? app.Substring(0, 14) + ".." : app) : "Share app";
            T.Txt(g, label + " ▾", T.F(9.5f, FontStyle.Bold), app.Length > 0 ? Color.White : T.Text, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
        protected override void OnMouseClick(MouseEventArgs e) { T.AppMenu(this, "Strip[2]"); }
    }

    // Window controls: minimize (dash) and close (cross), close turns red on hover.
    class WinBtn : VC
    {
        bool close, hot; Action act;
        public WinBtn(bool isClose, Action a) { close = isClose; act = a; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.Pill(g, new RectangleF(1, 1, Width - 3, Height - 3), hot && close, hot && close ? (Color?)T.Red : null);
            if (hot && !close)
            {
                using (GraphicsPath hp = T.RR(new RectangleF(1, 1, Width - 3, Height - 3), 12))
                {
                    using (Brush hb = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(hb, hp);
                    using (Pen hpn = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f)) g.DrawPath(hpn, hp);
                }
            }
            using (Pen p = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cx = Width / 2f, cy = Height / 2f;
                if (close) { g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx - 5, cy + 5, cx + 5, cy - 5); }
                else g.DrawLine(p, cx - 6, cy + 3, cx + 6, cy + 3);
            }
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (act != null) act(); }
    }

    // The clock-tower logo. The small clock-head image ("head.png") is what the running program uses; the huge full logo is only
    // read on demand (to build the .ico) and is never kept in memory.
    static class Logo
    {
        static Bitmap head;
        static Bitmap Head
        {
            get
            {
                if (head == null)
                    using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("head.png")) head = new Bitmap(s);
                return head;
            }
        }
        static Bitmap LoadFull()
        {
            using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")) return new Bitmap(s);
        }
        public static void DrawHead(Graphics g, RectangleF r)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float k = Math.Min(r.Width / Head.Width, r.Height / Head.Height), w = Head.Width * k, h = Head.Height * k;
            g.DrawImage(Head, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        }
        // Square icon image: small sizes use the clock head so it stays readable.
        public static Bitmap Bmp(int n)
        {
            Bitmap b = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float m = n * 0.04f;
                if (n <= 32)
                {
                    float k = Math.Min((n - 2 * m) / Head.Width, (n - 2 * m) / Head.Height), w = Head.Width * k, h = Head.Height * k;
                    g.DrawImage(Head, new RectangleF((n - w) / 2, (n - h) / 2, w, h));
                }
                else using (Bitmap full = LoadFull())
                {
                    float k = Math.Min((n - 2 * m) / full.Width, (n - 2 * m) / full.Height), w = full.Width * k, h = full.Height * k;
                    g.DrawImage(full, new RectangleF((n - w) / 2, (n - h) / 2, w, h));
                }
            }
            return b;
        }
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 }; List<byte[]> png = new List<byte[]>();
            foreach (int n in sizes) using (Bitmap b = Bmp(n)) using (System.IO.MemoryStream ms = new System.IO.MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); png.Add(ms.ToArray()); }
            using (System.IO.BinaryWriter w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(png[i].Length); w.Write(off); off += png[i].Length;
                }
                foreach (byte[] p in png) w.Write(p);
            }
        }
    }
    // "Start with Windows": a Run entry for the current user, toggled from the Settings menu.
    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "DsMaN";
        public static bool On
        {
            get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue(ValueName) != null; }
        }
        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --startup");
                else k.DeleteValue(ValueName, false);
            }
        }
    }
    class Title : VC
    {
        public override void Sync() { Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            Logo.DrawHead(g, new RectangleF(0, 4, 56, 46));
            float nw = NameArt.DrawText(g, 64, 8, 38);
            float sx = 66 + nw + 24;
            T.Txt(g, "v" + Updater.Version, T.F(9f, FontStyle.Bold), T.Dim, new RectangleF(66 + nw + 8, 26, 60, 18), StringAlignment.Near);
            if (Eng.Notice.Length > 0 && (DateTime.Now - Eng.NoticeAt).TotalSeconds < 6)
                T.Txt(g, Eng.Notice, T.F(9.5f), T.Yellow, new RectangleF(sx + 56, 20, Width - sx - 56, 22), StringAlignment.Near);
        }
    }
    // ---------------------------------------------------------------- main form
    class MainForm : Form
    {
        List<Fader> faders = new List<Fader>(); List<Tog> devTogs = new List<Tog>(); UpdateBtn upd; Cmd appVolBtn; bool posReady, autoPending, hideOnce; NotifyIcon tray;
        Timer timer = new Timer(); Title title; int ticks;
        const int TOP = 70;

        public MainForm(bool startup)
        {
            Text = "DsMaN"; Icon = Icon.FromHandle(Logo.Bmp(32).GetHicon()); BackColor = Color.FromArgb(20, 44, 33);
            FormBorderStyle = FormBorderStyle.None; DoubleBuffered = true;
            ClientSize = new Size(T.W, T.H); StartPosition = FormStartPosition.CenterScreen;
            BuildHeader(); BuildStrips(); BuildMaster();
            Surface wallpaper = new Surface { Bounds = new Rectangle(0, 0, T.W, T.H) };   // full-window gradient behind everything: no flat-coloured strip at the window edges
            Controls.Add(wallpaper); wallpaper.SendToBack();
            if (startup) { WindowState = FormWindowState.Minimized; hideOnce = true; Shown += delegate { if (hideOnce) { hideOnce = false; HideToTray(); } }; }   // at Windows start: go straight to the tray
            BuildTray();
            Load += delegate { Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 22)); try { Eng.Start(); } catch (Exception ex) { Eng.LogError(ex); Eng.LogStart("Start failed: " + ex.Message); }
                SyncAll(); Updater.Check(delegate { AutoUpdate(); });
                if (Eng.Get("Window.Saved") > 0)   // reopen where the window was left
                {
                    Point pt = new Point((int)Eng.Get("Window.X"), (int)Eng.Get("Window.Y")); Rectangle wr = new Rectangle(pt, Size);
                    foreach (Screen sc in Screen.AllScreens) if (sc.WorkingArea.IntersectsWith(wr)) { StartPosition = FormStartPosition.Manual; Location = pt; break; }
                }
                posReady = true; };
            LocationChanged += delegate { if (posReady && WindowState == FormWindowState.Normal) { Eng.Set("Window.X", Left); Eng.Set("Window.Y", Top); Eng.Set("Window.Saved", 1); } };
            timer.Interval = 50; timer.Tick += Tick; timer.Start();
            SizeChanged += delegate
            {
                if (WindowState == FormWindowState.Minimized) { Mem.Trim(); return; }
                // rounded corners follow the real window size (the window may have started minimised, with a tiny size)
                if (Width > 600 && (Region == null || Region.GetBounds(CreateGraphics()).Width < Width - 2)) Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 22));
            };
            FormClosing += delegate { if (tray != null) { tray.Visible = false; tray.Dispose(); } Eng.Shutdown(); };
        }

        [System.Runtime.InteropServices.DllImport("user32")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        void DragMe(Control c) { c.MouseDown += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } }; }

        static void Place(Control parent, Control c, int x, int y, int w, int h) { c.Bounds = new Rectangle(x, y, w, h); parent.Controls.Add(c); }

        void BuildHeader()
        {
            Surface s = new Surface { Left = 0, Top = 0, Width = T.W, Height = TOP };
            Controls.Add(s);
            upd = new UpdateBtn { Act = DoUpdate }; Place(s, upd, T.W - 684, 18, 130, 32);
            appVolBtn = new Cmd("App volume ▾", null, false); appVolBtn.Act = ShowAppVol; Place(s, appVolBtn, T.W - 546, 18, 138, 32);
            title = new Title(); Place(s, title, 30, 8, T.W - 684 - 30 - 10, 58); DragMe(s); DragMe(title);
            Place(s, new AppBtn(), T.W - 400, 18, 130, 32);
            
            Place(s, new Cmd("Settings", ShowMenu, true), T.W - 262, 18, 100, 32);
            Place(s, new WinBtn(false, delegate { HideToTray(); }), T.W - 140, 18, 38, 32);
            Place(s, new WinBtn(true, delegate { Close(); }), T.W - 96, 18, 38, 32);
        }

        void BuildStrips()
        {
            string[] defNames = { "Mic 1", "Mic 2", "App audio", "", "", "Virtual 1", "Virtual 2", "Virtual 3" };
            string[] routes = { "A1", "A2", "A3", "A4", "B1" };
            int[] ids = { 0, 1, 5, 6, 7 };
            const int SH = T.H - TOP, FY = 352;
            int x = 6;
            foreach (int i in ids)
            {
                bool hw = i < 2; int w = hw ? 190 : 150; string pre = "Strip[" + i + "]";
                Surface s = new Surface { Left = x, Top = TOP, Width = w, Height = SH };
                s.Painter = delegate(Graphics g, int sw, int sh)
                {
                    T.Card(g, new RectangleF(3, 2, sw - 6, sh - 8), 18);
                    using (Pen p = new Pen(Color.FromArgb(34, 255, 255, 255))) { g.DrawLine(p, 14, hw ? 72 : 92, sw - 14, hw ? 72 : 92); float ln = (sw - 12) - 74, trim = ln * 0.025f; g.DrawLine(p, 74 + trim, FY + 242, sw - 12 - trim, FY + 242); }   // 5 % shorter (2.5 % off each end)   // second divider: only over the button column (starts after the fader), between the routing buttons and mono / solo / Mute
                    Font f = T.F(8, FontStyle.Bold);
                    if (hw)
                    {
                        Font kf = T.F(8.5f);   // 2 x 2 knobs: Gate | Denoise on top, Comp. | Clicks below
                        string[] kn = { "Gate", "Denoise", "Comp.", "Clicks" };
                        for (int q = 0; q < 4; q++) T.Txt(g, kn[q], kf, T.Dim, new RectangleF(sw / 2f + (q % 2 == 0 ? -1 : 1) * (q < 2 ? 24 : 46) - 30, q < 2 ? 236 : 296, 60, 14), StringAlignment.Center);

                    }
                    else
                    {
                        T.Txt(g, "Equalizer", f, T.Dim, new RectangleF(0, 98, sw, 18), StringAlignment.Center);
                        T.Txt(g, "Treble", T.F(8), T.Dim, new RectangleF(sw - 62, 116, 54, 12), StringAlignment.Center);
                        T.Txt(g, "Med", T.F(8), T.Dim, new RectangleF(8, 158, 46, 12), StringAlignment.Center);
                        T.Txt(g, "Bass", T.F(8), T.Dim, new RectangleF(sw - 62, 212, 54, 12), StringAlignment.Center);
                    }
                };
                Controls.Add(s);
                NameBox nbx = new NameBox(pre, defNames[i], hw) { App = i == 2 }; Place(s, nbx, 12, 10, w - 24, hw ? 58 : 78);
                int fw = 62;
                Fader fd = new Fader(pre, i < 5 ? i * 2 : 10 + (i - 5) * 8, 1) { BarW = 6, AllowDevice = i >= 5 };
                Place(s, fd, 8, FY + 4, fw, 392); faders.Add(fd);   // meter bars run FY+14 .. FY+386, the buttons line up with them
                int bx = 8 + fw + 4, bw = w - bx - 12;
                for (int k = 0; k < 5; k++) Place(s, new Tog(pre + "." + routes[k], routes[k], 0), bx, FY + 14 + k * 42, bw, 35);
                Place(s, new Tog(pre + (hw ? ".Mono" : ".MC"), hw ? "mono" : "M.C", 3), bx, FY + 267, bw, 35);
                Place(s, new Tog(pre + ".Solo", "solo", 2), bx, FY + 309, bw, 35);
                Tog muteTog = new Tog(pre + ".Mute", "Mute", 1); if (i >= 5) { muteTog.DevPrefix = pre; devTogs.Add(muteTog); }
                Place(s, muteTog, bx, FY + 351, bw, 35);
                if (hw)
                {
                    Place(s, new Pad(pre + ".Pan_x", pre + ".Pan_y", false), 12, 84, w - 24, 148);
                    int kxL = w / 2 - 24 - 21, kxR = w / 2 + 24 - 21, kyL = w / 2 - 46 - 21, kyR = w / 2 + 46 - 21;   // knob size 42 = same as the equalizer knobs   // top row (Gate, Denoise) close together, bottom row (Comp, Clicks) wider apart
                    Place(s, new Knob(pre + ".Gate", 0, 10, 0, 42), kxL, 252, 42, 42); Place(s, new Knob(pre + ".Denoiser", 0, 10, 0, 42), kxR, 252, 42, 42);
                    Place(s, new Knob(pre + ".Comp", 0, 10, 0, 42), kyL, 312, 42, 42); Place(s, new Knob(pre + ".Clicks", 0, 10, 0, 42), kyR, 312, 42, 42);
                }
                else
                {
                    Place(s, new Knob(pre + ".EQGain3", -12, 12, 0, 42), w - 62, 130, 42, 42);
                    Place(s, new Knob(pre + ".EQGain2", -12, 12, 0, 42), 10, 172, 42, 42);
                    Place(s, new Knob(pre + ".EQGain1", -12, 12, 0, 42), w - 62, 226, 42, 42);
                    Place(s, new Pad(pre + ".Pan_x", pre + ".Pan_y", true), 12, 290, w - 24, 56);
                }
                x += w;
            }
        }

        void BuildMaster()
        {
            int mx = 6 + 2 * 190 + 3 * 150, mw = T.W - mx - 6;   // mw: layout width of the card; the surface itself runs to the window edge
            Surface s = new Surface { Left = mx, Top = TOP, Width = T.W - mx, Height = T.H - TOP };
            string[] bn = { "A1", "A2", "A3", "A4", "B1" }; int[] busIds = { 0, 1, 2, 3, 5 };
            s.Painter = delegate(Graphics g, int sw, int sh)
            {
                T.Card(g, new RectangleF(3, 2, sw - 12, sh - 8), 18);
                T.Txt(g, "Outputs", T.F(11, FontStyle.Bold), T.Text, new RectangleF(20, 12, 200, 22), StringAlignment.Near);
                T.Txt(g, "Click to choose a device. Right-click to rename.   B1 = your microphone for Discord / games (pick CABLE Output there).", T.F(8.5f), T.Dim, new RectangleF(20, 34, sw - 40, 18), StringAlignment.Near);
            };
            Controls.Add(s);
            int cw = (mw - 24) / 5;
            for (int q = 0; q < 5; q++)
            {
                int i = busIds[q]; string pre = "Bus[" + i + "]"; int bx = 12 + q * cw;
                Place(s, new BusBtn(i, bn[q]), bx + 4, 90, cw - 8, 40);
                Place(s, new Tog(pre + ".Mono", "mono", 3), bx + 4, 142, cw - 8, 32);
                Place(s, new Tog(pre + ".Mute", "Mute", 1), bx + 4, 182, cw - 8, 32);
                Fader fd = new Fader(pre, i * 8, 3) { BarW = 6 };
                Place(s, fd, bx + 2, 226, cw - 4, s.Height - 226 - 24); faders.Add(fd);
            }
        }
        // Virtual cable (VB-Cable, shipped in the program folder). Once installed, "Virtual mic" sends Mic 1 to the cable so
        // Discord / games can use "CABLE Output" as their microphone and hear the processed sound live.
        void AddCableItems(ContextMenuStrip m)
        {
            if (Eng.FindCable() != null) return;   // cable already installed: nothing to show
            string setup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "VBCable", "VBCABLE_Setup_x64.exe");
            bool bundled = System.IO.File.Exists(setup);
            ToolStripMenuItem ins = new ToolStripMenuItem(bundled ? "Install virtual cable (needs admin)" : "Get virtual cable (opens vb-audio.com)");
            ins.Click += delegate
            {
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = bundled
                        ? new System.Diagnostics.ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = System.IO.Path.GetDirectoryName(setup) }
                        : new System.Diagnostics.ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex) { Eng.Notify("Cable: " + ex.Message); }
            };
            m.Items.Add(ins);
            m.Items.Add(new ToolStripSeparator());
        }

        // Opens the usage guide (embedded in the program) in Notepad.
        void OpenGuide()
        {
            try
            {
                // Notepad (Store version) cannot open files inside AppData, so the guide is saved next to the program (or in Documents).
                string text;
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("guide.txt"))
                using (System.IO.StreamReader r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8)) text = r.ReadToEnd();
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "DsMaN-Guide.txt");
                try { System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(true)); }
                catch
                {
                    string docs = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DsMaN");
                    System.IO.Directory.CreateDirectory(docs); path = System.IO.Path.Combine(docs, "DsMaN-Guide.txt");
                    System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(true));
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { Eng.Notify("Guide: " + ex.Message); }
        }

        // ---- system tray (the small icons next to the clock): DsMaN lives there while it is hidden or running at Windows start
        public static readonly int WM_SHOWME = RegisterWindowMessage("DsMaN.ShowWindow");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string s);
        void BuildTray()
        {
            tray = new NotifyIcon { Icon = Icon, Text = "DsMaN  v" + Updater.Version, Visible = true };
            ContextMenuStrip m = T.NewMenu();
            m.Items.Add("Open DsMaN", null, delegate { RestoreFromTray(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, delegate { Close(); });
            tray.ContextMenuStrip = m;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
        }
        void HideToTray() { ShowInTaskbar = false; Hide(); }
        void RestoreFromTray()
        {
            hideOnce = false;
            ShowInTaskbar = true; if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (Width > 600) Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 22));
            // Windows only lets the program in front steal focus; the Alt key press makes it accept our request, and TopMost lifts the window above the rest.
            keybd_event(0x12, 0, 0, 0); SetForegroundWindow(Handle); keybd_event(0x12, 0, 2, 0);
            TopMost = true; TopMost = false; Activate(); BringToFront();
        }
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte k, byte s, int f, int e);
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWME) { RestoreFromTray(); return; }   // a second copy was started: show this one instead
            base.WndProc(ref m);
        }

        void ShowAppVol()
        {
            Point p = appVolBtn.PointToScreen(new Point(0, appVolBtn.Height + 6));
            new AppVolForm(p, appVolBtn.PointToScreen(new Point(appVolBtn.Width, 0)).X).Show(this);
        }

        // Updates by itself: when a newer version is announced, download it, restart into it. Each version is tried only once per computer
        // (so a bad release can never cause a restart loop), and while the microphone is in use the update waits.
        void AutoUpdate()
        {
            if (!Updater.Available || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!Updater.Available || Eng.GetStr("App.LastUpdateTried") == Updater.NewVersion) return;
                    if (!Eng.MicIdle()) { autoPending = true; return; }
                    autoPending = false; Eng.SetStr("App.LastUpdateTried", Updater.NewVersion);
                    Eng.Notify("Updating to v" + Updater.NewVersion + "...");
                    try { Eng.SaveIfChanged(true); Updater.Apply(); Close(); }
                    catch (Exception ex) { Eng.LogError(ex); Eng.Notify("Update failed: " + ex.Message); }
                }));
            }
            catch { }
        }

        void DoUpdate()
        {
            if (MessageBox.Show("Update to v" + Updater.NewVersion + " now?\nDsMaN will restart.", "DsMaN", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Eng.SaveIfChanged(true); Updater.Apply(); Close(); }
            catch (Exception ex) { Eng.LogError(ex); Eng.Notify("Update failed: " + ex.Message); }
        }

        void ShowMenu()
        {
            ContextMenuStrip m = T.NewMenu();
            ToolStripMenuItem ver = new ToolStripMenuItem("DsMaN  v" + Updater.Version) { Enabled = false }; m.Items.Add(ver);
            ToolStripMenuItem chk = new ToolStripMenuItem("Check for updates");
            chk.Click += delegate { Eng.Notify("Checking for updates..."); Updater.Check(delegate { Eng.Notify(Updater.Message); }); };
            m.Items.Add(chk);
            ToolStripMenuItem src = new ToolStripMenuItem("Update source (link or file)...");
            src.Click += delegate { string v = T.Ask("Update source (link or file path)", Eng.GetStr("App.UpdateUrl")); if (v != null) { Eng.SetStr("App.UpdateUrl", v.Trim()); Eng.Notify(v.Trim().Length > 0 ? "Update source saved" : "Update source cleared"); Updater.Check(null); } };
            m.Items.Add(src);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem theme = new ToolStripMenuItem("Theme");
            theme.DropDownItems.Add(new ToolStripMenuItem("(empty - themes coming later)") { Enabled = false });
            m.Items.Add(theme);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem guide = new ToolStripMenuItem("دليل استخدام"); guide.Click += delegate { OpenGuide(); }; m.Items.Add(guide);
            m.Items.Add(new ToolStripSeparator());
            AddCableItems(m);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem st = new ToolStripMenuItem("Start with Windows") { Checked = Startup.On };
            st.Click += delegate { try { Startup.Set(!Startup.On); } catch (Exception ex) { MessageBox.Show(ex.Message); } };
            m.Items.Add(st);
            m.Items.Add("Save Settings...", null, delegate { SaveLoad(true); });
            m.Items.Add("Load Settings...", null, delegate { SaveLoad(false); });
            m.Show(Cursor.Position);
        }

        void SaveLoad(bool save)
        {
            FileDialog d = save ? (FileDialog)new SaveFileDialog() : new OpenFileDialog();
            d.Filter = "Potato Mixer settings (*.cfg)|*.cfg";
            if (d.ShowDialog() == DialogResult.OK)
                Eng.Script("Command." + (save ? "Save" : "Load") + "=\"" + d.FileName + "\";");
        }

        void SyncAll() { foreach (VC v in VC.All.ToArray()) v.Sync(); }

        void Tick(object s, EventArgs e)
        {
            ticks++;
            if (Eng.Dirty()) SyncAll();
            foreach (Fader f in faders) f.UpdateLevel();
            if (ticks % 12 == 0) { foreach (Fader f in faders) f.RefreshDevice(); foreach (Tog tg in devTogs) tg.Sync(); }
            if (ticks == 120) Mem.Trim();
            if (autoPending && ticks % 40 == 0) AutoUpdate();                       // an update was waiting for a quiet moment
            if (ticks % 72000 == 0 && ticks > 0) Updater.Check(delegate { AutoUpdate(); });   // re-check about once an hour
            if (upd.Visible != Updater.Available) { upd.Visible = Updater.Available; upd.Invalidate(); }
            if (ticks % 40 == 0) { Eng.SaveIfChanged(false); title.Invalidate(); }
        }
    }

        static class Mem
    {
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        public static void Trim()
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1)); } catch { }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte k, byte s, int f, int e);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        // Asks the copy that is already running (even when it is hidden in the tray) to show its window.
        static void BringExistingToFront()
        {
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id; int msg = MainForm.WM_SHOWME;
            EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                uint pid; GetWindowThreadProcessId(h, out pid); if (pid == me) return true;
                System.Diagnostics.Process pr = null; try { pr = System.Diagnostics.Process.GetProcessById((int)pid); } catch { return true; }
                if (pr.ProcessName != System.Diagnostics.Process.GetCurrentProcess().ProcessName) return true;
                System.Text.StringBuilder cls = new System.Text.StringBuilder(256); GetClassName(h, cls, 256);
                if (cls.ToString().StartsWith("WindowsForms10")) PostMessage(h, msg, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--make-icon") { Logo.WriteIco(args[1]); return; }
            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { Eng.LogError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Eng.LogError(e.ExceptionObject as Exception); };
            // Only one copy may run: two copies would each keep their own settings and overwrite each other's changes.
            bool first; System.Threading.Mutex single = new System.Threading.Mutex(true, "Local\\DsMaN-SingleInstance", out first);
            if (!first) { BringExistingToFront(); return; }
            bool startup = Array.IndexOf(args, "--startup") >= 0;
            Application.Run(new MainForm(startup));
            GC.KeepAlive(single);
        }
    }
}
