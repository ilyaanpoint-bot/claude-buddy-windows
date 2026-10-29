// Buddy's tantrum: a transparent overlay on every screen where the little devil lobs bombs.
// Click a bomb before its fuse runs out to defuse it (only the bombs catch the mouse).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeBuddy;

class RageShow
{
    public class Bomb
    {
        public PointF Target;
        public double SpawnAt, FuseEnd, StateAt;
        public int State;      // 0 live, 1 exploded, 2 defused
        public string Word;
    }

    public readonly List<Bomb> Bombs = new();
    public PointF Origin;
    public double StartedAt, ShakeUntil = -100;
    public int Defused;
    public Action OnDefuse;
    public Action<int> OnFinished;
    readonly List<RageWindow> _windows = new();
    Timer _timer;
    static readonly Random Rng = new();

    public bool Running => _timer != null;

    public void Start(PointF from)
    {
        Origin = from;
        StartedAt = Clock.Now;
        Defused = 0;
        Bombs.Clear();
        string[] words = { "BOOM!", "KABOOM!", "POW!", "BLAM!", "KAPOW!", "BANG!" };
        var screens = Screen.AllScreens;
        int count = 12 + screens.Length * 5;
        for (int i = 0; i < count; i++)
        {
            var area = screens[i % screens.Length].WorkingArea;
            area.Inflate(-90, -90);
            double spawn = StartedAt + 0.15 + i * 0.11;
            Bombs.Add(new Bomb
            {
                Target = new PointF(area.Left + (float)Rng.NextDouble() * area.Width, area.Top + (float)Rng.NextDouble() * area.Height),
                SpawnAt = spawn, FuseEnd = spawn + 2.4 + Rng.NextDouble() * 1.8, Word = words[Rng.Next(words.Length)],
            });
        }
        foreach (var s in screens)
        {
            var w = new RageWindow(this, s.Bounds);
            w.Show();
            _windows.Add(w);
        }
        _timer = new Timer { Interval = 33 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// Where a bomb is right now (it flies from buddy to its spot first). Screen pixels.
    public (PointF pos, bool landed) Position(Bomb b, double t)
    {
        double k = Math.Clamp((t - b.SpawnAt) / 0.55, 0, 1);
        float x = Origin.X + (b.Target.X - Origin.X) * (float)k;
        float y = Origin.Y + (b.Target.Y - Origin.Y) * (float)k - (float)Math.Sin(k * Math.PI) * 160;
        return (new PointF(x, y), k >= 1);
    }

    void Tick()
    {
        double t = Clock.Now;
        for (int i = 0; i < Bombs.Count; i++)
        {
            var b = Bombs[i];
            if (b.State == 0 && t >= b.FuseEnd)
            {
                b.State = 1; b.StateAt = t; ShakeUntil = t + 0.18;
                Synth.Boom(i == Bombs.Count - 1);
            }
        }
        foreach (var w in _windows) w.Render();
        double last = Bombs.Max(b => b.State == 0 ? double.PositiveInfinity : b.StateAt);
        if (t - last > 1.6) Finish();
    }

    public void Click(Point screen)
    {
        double t = Clock.Now;
        var live = Bombs.Where(b => b.State == 0 && t > b.SpawnAt + 0.55)
            .OrderBy(b => Dist(b.Target, screen)).FirstOrDefault();
        if (live == null || Dist(live.Target, screen) > 34) return;
        live.State = 2; live.StateAt = t; Defused++;
        Synth.Fizzle();
        OnDefuse?.Invoke();
    }

    static double Dist(PointF a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    void Finish()
    {
        _timer.Stop(); _timer.Dispose(); _timer = null;
        foreach (var w in _windows) { w.Close(); w.Dispose(); }
        _windows.Clear();
        OnFinished?.Invoke(Defused);
    }
}

class RageWindow : LayeredForm
{
    readonly RageShow _show;
    readonly Rectangle _bounds;
    static readonly Random Rng = new();

    public RageWindow(RageShow show, Rectangle bounds)
    {
        _show = show;
        _bounds = bounds;
        Bounds = bounds;
        MouseDown += (_, e) => _show.Click(new Point(e.X + _bounds.Left, e.Y + _bounds.Top));
    }

    public void Render()
    {
        double t = Clock.Now;
        float sc = DpiScale;
        using var bmp = new Bitmap(_bounds.Width, _bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            double flash = Math.Max(0, 1 - (t - _show.StartedAt) / 0.8);
            if (flash > 0)
                using (var b = new SolidBrush(Color.FromArgb((int)(70 * flash), 230, 25, 12))) g.FillRectangle(b, 0, 0, _bounds.Width, _bounds.Height);
            if (t < _show.ShakeUntil) g.TranslateTransform(Rng.Next(-6, 7), Rng.Next(-6, 7));
            foreach (var b in _show.Bombs)
            {
                if (t < b.SpawnAt) continue;
                var (sp, landed) = _show.Position(b, t);
                var p = new PointF(sp.X - _bounds.Left, sp.Y - _bounds.Top);
                if (p.X < -200 || p.Y < -200 || p.X > _bounds.Width + 200 || p.Y > _bounds.Height + 200) continue;
                switch (b.State)
                {
                    case 0: DrawBomb(g, p, b.FuseEnd - t, b.FuseEnd - b.SpawnAt, landed, t, sc); break;
                    case 1: DrawExplosion(g, p, t - b.StateAt, b.Word, sc); break;
                    default: DrawDefused(g, p, t - b.StateAt, sc); break;
                }
            }
        }
        Present(bmp);
    }

    static void Disc(Graphics g, Color c, float x, float y, float r) { using var b = new SolidBrush(c); g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r); }

    static void DrawBomb(Graphics g, PointF p, double left, double total, bool landed, double t, float sc)
    {
        bool danger = left < 0.8 && t % 0.2 < 0.1;
        float r = 15 * sc * (landed ? 1 + 0.06f * (float)Math.Sin(t * 12) : 0.8f);
        if (landed) using (var sh = new SolidBrush(Color.FromArgb(46, 0, 0, 0))) g.FillEllipse(sh, p.X - r, p.Y + r - 1, 2 * r, 7 * sc);
        Disc(g, danger ? Palette.Rgb(0xB71C1C) : Palette.Rgb(0x1E1E24), p.X, p.Y, r);
        using (var hl = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillEllipse(hl, p.X - r * 0.55f, p.Y - r * 0.55f, r * 0.5f, r * 0.35f);
        using (var cap = new SolidBrush(Palette.Rgb(0x44444C))) g.FillRectangle(cap, p.X - 4 * sc, p.Y - r - 3 * sc, 8 * sc, 5 * sc);
        float frac = (float)Math.Clamp(left / total, 0, 1);
        var end = new PointF(p.X + (4 + 8 * frac) * sc, p.Y - r - (4 + 10 * frac) * sc);
        using (var fuse = new Pen(Palette.Rgb(0xA1887F), 2 * sc)) g.DrawBezier(fuse, p.X, p.Y - r - 3 * sc, p.X - 2 * sc, p.Y - r - 10 * frac * sc, end.X - 4 * sc, end.Y, end.X, end.Y);
        float s = (4 + (float)Rng.NextDouble() * 3) * sc;
        Disc(g, Palette.Rgb(0xFFF59D), end.X, end.Y, s);
        Disc(g, Palette.Rgb(0xFF9800), end.X, end.Y, s * 0.55f);
    }

    static void DrawExplosion(Graphics g, PointF p, double e, string word, float sc)
    {
        if (e < 0.55)
        {
            float k = (float)(e / 0.55);
            foreach (var (c, s) in new[] { (0xFF5722u, 1f), (0xFF9800u, 0.75f), (0xFFEB3Bu, 0.45f) })
                Disc(g, Palette.Rgb(c, (int)(255 * (1 - k))), p.X, p.Y, (20 + 110 * k) * s * sc);
            for (int i = 0; i < 12; i++)
            {
                double a = i / 12.0 * 2 * Math.PI + 0.3;
                float d = (30 + 150 * k) * sc;
                Disc(g, Palette.Rgb(i % 2 == 0 ? 0xFFC107u : 0x5D4037u, (int)(255 * (1 - k))), p.X + (float)Math.Cos(a) * d, p.Y + (float)Math.Sin(a) * d, 3 * sc);
            }
        }
        if (e < 3)
            using (var scorch = new SolidBrush(Palette.Rgb(0x2B1D16, (int)(115 * Math.Max(0, 1 - e / 3))))) g.FillEllipse(scorch, p.X - 34 * sc, p.Y - 16 * sc, 68 * sc, 32 * sc);
        if (e < 1)
        {
            float k = (float)e;
            using var f = new Font("Segoe UI Black", (22 + 18 * Math.Min(1, k * 4)) * sc, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(word, f);
            using var shadow = new SolidBrush(Color.FromArgb((int)(150 * (1 - k)), 0, 0, 0));
            using var b = new SolidBrush(Palette.Rgb(0xFFEB3B, (int)(255 * (1 - k * 0.8))));
            float x = p.X - size.Width / 2, y = p.Y - 40 * sc - k * 30 * sc - size.Height;
            g.DrawString(word, f, shadow, x + 2, y + 2);
            g.DrawString(word, f, b, x, y);
        }
    }

    static void DrawDefused(Graphics g, PointF p, double e, float sc)
    {
        if (e >= 1.4) return;
        float k = (float)(e / 1.4);
        Disc(g, Palette.Rgb(0x1E1E24, (int)(255 * (1 - k))), p.X, p.Y, 15 * sc);
        for (int i = 0; i < 3; i++)
            Disc(g, Color.FromArgb((int)(180 * (1 - k)), 217, 217, 217), p.X + (i - 1) * 10 * sc, p.Y - (14 + k * 40 + i * 4) * sc, (6 + k * 10) * sc);
        using var f = new Font("Segoe UI", 14 * sc, FontStyle.Bold, GraphicsUnit.Pixel);
        using var b = new SolidBrush(Palette.Rgb(0x66BB6A, (int)(255 * (1 - k))));
        g.DrawString("fzzt ✓", f, b, p.X - 22 * sc, p.Y + 22 * sc);
    }
}
