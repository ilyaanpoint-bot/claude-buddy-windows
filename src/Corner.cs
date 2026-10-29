// The always-on-top corner widget: orange buddy, plus a blue friend for a second session.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeBuddy;

class Slot
{
    public readonly Animator Anim;
    public readonly float Inset;
    public bool Present;
    public double PresentChangedAt = -100, BubbleAt = -100, BubbleDuration;
    public string BubbleTitle = "", BubbleSub = "";
    public double LastActivity = Clock.Now;

    public Slot(float inset, bool present, double phase) { Inset = inset; Present = present; Anim = new Animator(phase); }
}

class CornerWindow : LayeredForm
{
    public const float W = 320, H = 200;
    public readonly Slot[] Slots = { new(46, true, 0), new(130, false, 1.3) };

    public Action<int> OnPoke, OnFiveClicks, OnBubbleClick;
    public Action OnShake, OnDoubleClick, OnMoved;
    public Action<PointF> OnFlick;
    public Action<int, float> OnSquishRelease;
    public Func<ContextMenuStrip> MenuProvider;

    public int? HoldIndex { get; private set; }
    public double HoldStart { get; private set; }

    (int index, bool bubble)? _pressed;
    Point _dragOffset;
    bool _down, _dragged;
    readonly List<(double t, Point p)> _samples = new();
    readonly List<double> _reversals = new();
    readonly List<double> _clicks = new();
    int _lastDragX, _lastDragSign;
    bool _shook;

    static readonly Font TitleFont = new("Segoe UI Semibold", 12.5f, GraphicsUnit.Pixel);
    static readonly Font SubFont = new("Segoe UI", 11f, GraphicsUnit.Pixel);

    public CornerWindow()
    {
        Size = new Size((int)(W * DpiScale), (int)(H * DpiScale));
        MouseDown += Down;
        MouseMove += Move_;
        MouseUp += Up;
    }

    PointF Home(int i) => new(W - Slots[i].Inset, H - 44);
    bool BubbleVisible(int i) => Slots[i].Present && Clock.Now - Slots[i].BubbleAt < Slots[i].BubbleDuration;

    public string SkinOf(int i) => i == 0 ? Progress.Shared.Data.Skin : "sky";
    public string HatOf(int i) => i == 0 ? Progress.Shared.Data.Hat : "none";

    public void SetPresent(int i, bool present)
    {
        var s = Slots[i];
        if (s.Present == present) return;
        s.Present = present;
        s.PresentChangedAt = Clock.Now;
        if (!present) s.BubbleDuration = 0; else s.LastActivity = Clock.Now;
    }

    public void Say(int i, string title, string sub = "", double duration = 8, bool voice = true)
    {
        var s = Slots[i];
        s.BubbleTitle = title; s.BubbleSub = sub; s.BubbleAt = Clock.Now; s.BubbleDuration = duration;
        if (voice)
        {
            Synth.Babble(title, i == 0 ? 1 : 1.25);
            s.Anim.Talk(Math.Clamp(title.Length / 3, 2, 9) * 0.072);
        }
    }

    public void DismissBubble(int i)
    {
        var s = Slots[i];
        if (BubbleVisible(i)) s.BubbleDuration = Math.Min(s.BubbleDuration, Clock.Now - s.BubbleAt + 0.4);
    }

    RectangleF BubbleRect(int i, Graphics g)
    {
        var s = Slots[i];
        float tw = Math.Min(200, Renderer.MeasureRich(g, s.BubbleTitle, TitleFont));
        float sw = s.BubbleSub.Length == 0 ? 0 : Math.Min(200, Renderer.MeasureRich(g, s.BubbleSub, SubFont));
        float w = Math.Max(tw, sw) + 34, h = s.BubbleSub.Length == 0 ? 28 : 42;
        int other = 1 - i;
        bool stacked = BubbleVisible(other) && Slots[other].BubbleAt < s.BubbleAt;
        float right = Math.Min(W - 4, Home(i).X + 22);
        float bottom = H - 100 - (stacked ? 48 : 0);
        return new RectangleF(Math.Max(4, right - w), bottom - h, w, h);
    }

    static readonly Bitmap Measure = new(1, 1);

    /// Which character (or its bubble) is under a point in logical coordinates.
    public (int index, bool bubble)? Hit(PointF p)
    {
        using var g = Graphics.FromImage(Measure);
        for (int i = Slots.Length - 1; i >= 0; i--)
            if (BubbleVisible(i)) { var r = BubbleRect(i, g); r.Inflate(4, 4); if (r.Contains(p)) return (i, true); }
        for (int i = 0; i < Slots.Length; i++)
        {
            if (!Slots[i].Present) continue;
            var h = Home(i);
            if (Math.Sqrt(Math.Pow(p.X - h.X, 2) + Math.Pow(p.Y - h.Y, 2)) < 38) return (i, false);
        }
        return null;
    }

    // MARK: Drawing

    public void Render()
    {
        float sc = DpiScale;
        int pw = (int)(W * sc), ph = (int)(H * sc);
        if (Width != pw || Height != ph)
        {
            // Keep the bottom-right corner where it is when the display scale changes.
            var br = new Point(Right, Bottom);
            Size = new Size(pw, ph);
            Location = new Point(br.X - pw, br.Y - ph);
        }
        using var bmp = new Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            g.ScaleTransform(sc, sc);
            var mouse = PointToClient(Cursor.Position);
            var m = new PointF(mouse.X / sc, mouse.Y / sc);
            for (int i = 0; i < Slots.Length; i++)
            {
                var s = Slots[i];
                float appear = Appearance(s);
                if (appear < 0.01f) continue;
                var h = Home(i);
                float vx = m.X - h.X, vy = -(m.Y - h.Y);
                float len = Math.Max(80, (float)Math.Sqrt(vx * vx + vy * vy));
                var pose = s.Anim.GetPose(vx / len, vy / len);
                pose.Sx *= appear; pose.Sy *= appear;
                Renderer.Draw(g, SkinOf(i), HatOf(i), pose, h, 1);
                if (s.Anim.Mood == Mood.Working && !s.Anim.IsCelebrating && !s.Anim.Asleep) ThinkingDots(g, i);
                if (s.Anim.Mood == Mood.Attention && !s.Anim.Asleep)
                {
                    using var f = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var b = new SolidBrush(Renderer.RayColor(SkinOf(i), 0, Clock.Now));
                    g.DrawString("!", f, b, h.X + 22, h.Y - 50 - (float)Math.Sin(Clock.Now * 6) * 2);
                }
            }
            for (int i = 0; i < Slots.Length; i++) if (BubbleVisible(i)) DrawBubble(g, i);
        }
        Present(bmp);
    }

    static float Appearance(Slot s)
    {
        double e = Clock.Now - s.PresentChangedAt;
        if (s.Present)
        {
            double x = Math.Min(1, e / 0.45);
            return (float)(1 + 2.7 * Math.Pow(x - 1, 3) + 1.7 * Math.Pow(x - 1, 2));
        }
        return (float)Math.Max(0, 1 - e / 0.3);
    }

    void ThinkingDots(Graphics g, int i)
    {
        var h = Home(i);
        for (int d = 0; d < 3; d++)
        {
            float hop = (float)Math.Max(0, Math.Sin(Clock.Now * 5 - d * 0.8)) * 4;
            using var b = new SolidBrush(Palette.Alpha(Renderer.RayColor(SkinOf(i), d, Clock.Now), 0.85));
            g.FillEllipse(b, h.X - 44 + d * 8, h.Y - 33 - hop, 5, 5);
        }
    }

    public static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void DrawBubble(Graphics g, int i)
    {
        var s = Slots[i];
        double e = Clock.Now - s.BubbleAt;
        float a = (float)(Math.Min(1, e / 0.25) * Math.Min(1, (s.BubbleDuration - e) / 0.4));
        var r = BubbleRect(i, g);
        r.Y += (float)(1 - Math.Min(1, e / 0.25)) * 6;
        float tipX = Home(i).X + 6;
        using (var shadow = RoundRect(new RectangleF(r.X, r.Y + 2, r.Width, r.Height), 12))
        using (var sb = new SolidBrush(Color.FromArgb((int)(60 * a), 0, 0, 0))) g.FillPath(sb, shadow);
        using (var path = RoundRect(r, 12))
        {
            path.AddPolygon(new[] { new PointF(tipX - 12, r.Bottom - 2), new PointF(tipX, r.Bottom + 10), new PointF(tipX + 2, r.Bottom - 2) });
            path.FillMode = FillMode.Winding;
            using var wb = new SolidBrush(Color.FromArgb((int)(252 * a), 252, 252, 252));
            g.FillPath(wb, path);
        }
        using (var dot = new SolidBrush(Palette.Alpha(Renderer.RayColor(SkinOf(i), 0, Clock.Now), a))) g.FillEllipse(dot, r.X + 10, r.Y + 11, 7, 7);
        using var tb = new SolidBrush(Color.FromArgb((int)(255 * a), 30, 30, 30));
        using var gb = new SolidBrush(Color.FromArgb((int)(255 * a), 97, 97, 97));
        if (s.BubbleSub.Length == 0) Renderer.DrawRich(g, s.BubbleTitle, TitleFont, tb, r.X + 24, r.Y + 7, r.Width - 30);
        else
        {
            Renderer.DrawRich(g, s.BubbleTitle, TitleFont, tb, r.X + 24, r.Y + 6, r.Width - 30);
            Renderer.DrawRich(g, s.BubbleSub, SubFont, gb, r.X + 24, r.Y + 22, r.Width - 30);
        }
    }

    // MARK: Mouse

    PointF Logical(Point p) => new(p.X / DpiScale, p.Y / DpiScale);

    void Down(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { MenuProvider?.Invoke()?.Show(Cursor.Position); return; }
        _pressed = Hit(Logical(e.Location));
        if (_pressed is { bubble: false } p) { HoldIndex = p.index; HoldStart = Clock.Now; }
        _down = true; _dragged = false; _shook = false;
        _dragOffset = e.Location;
        _samples.Clear(); _samples.Add((Clock.Now, Cursor.Position));
        _reversals.Clear(); _lastDragX = Cursor.Position.X; _lastDragSign = 0;
    }

    void Move_(object sender, MouseEventArgs e)
    {
        if (!_down || e.Button != MouseButtons.Left) return;
        var m = Cursor.Position;
        var origin = new Point(m.X - _dragOffset.X, m.Y - _dragOffset.Y);
        if (!_dragged && Math.Abs(origin.X - Left) + Math.Abs(origin.Y - Top) < 4) return;
        if (!_dragged) foreach (var s in Slots) s.Anim.Dragging = true;
        _dragged = true;
        HoldIndex = null;
        Location = origin;
        _samples.Add((Clock.Now, m));
        _samples.RemoveAll(x => Clock.Now - x.t > 0.08);
        int dx = m.X - _lastDragX;
        if (Math.Abs(dx) > 4)
        {
            int sign = Math.Sign(dx);
            if (_lastDragSign != 0 && sign != _lastDragSign) _reversals.Add(Clock.Now);
            _lastDragSign = sign; _lastDragX = m.X;
        }
        _reversals.RemoveAll(t => Clock.Now - t > 1.5);
        if (_reversals.Count >= 6 && !_shook) { _shook = true; OnShake?.Invoke(); }
    }

    void Up(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_down) return;
        _down = false;
        var held = HoldIndex;
        HoldIndex = null;
        if (held is int h && Slots[h].Anim.Squish > 0.05f)
        {
            float amt = Slots[h].Anim.Squish;
            OnSquishRelease?.Invoke(h, amt);
            if (amt > 0.3f) return;
        }
        if (_dragged)
        {
            foreach (var s in Slots) s.Anim.Dragging = false;
            OnMoved?.Invoke();
            if (_samples.Count >= 2)
            {
                var (t0, p0) = _samples[0];
                var (t1, p1) = _samples[^1];
                if (t1 - t0 > 0.01)
                {
                    var v = new PointF((float)((p1.X - p0.X) / (t1 - t0)), (float)((p1.Y - p0.Y) / (t1 - t0)));
                    if (Math.Sqrt(v.X * v.X + v.Y * v.Y) > 1100 * DpiScale) OnFlick?.Invoke(v);
                }
            }
            return;
        }
        if (_pressed is not { } p) return;
        if (p.bubble) { OnBubbleClick?.Invoke(p.index); return; }
        OnPoke?.Invoke(p.index);
        _clicks.Add(Clock.Now);
        _clicks.RemoveAll(t => Clock.Now - t > 1.4);
        if (_clicks.Count == 5) { _clicks.Clear(); _pendingDouble = null; OnFiveClicks?.Invoke(p.index); }
        else if (_clicks.Count == 2 && _clicks[1] - _clicks[0] < SystemInformation.DoubleClickTime / 1000.0) _pendingDouble = Clock.Now;
        else if (_clicks.Count > 2) _pendingDouble = null;
    }

    double? _pendingDouble;

    /// A double-click only counts once no third click follows (so 5 fast clicks stay a secret).
    public void CheckDoubleClick()
    {
        if (_pendingDouble is double t && Clock.Now - t > SystemInformation.DoubleClickTime / 1000.0)
        {
            _pendingDouble = null;
            if (_clicks.Count == 2) OnDoubleClick?.Invoke();
        }
    }
}
