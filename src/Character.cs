// How a buddy looks (skins, hats, faces) and how it moves (the Animator turns mood +
// recent interactions into a Pose that the Renderer draws).

using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ClaudeBuddy;

enum Expr { Normal, Happy, Surprised, Asleep, Dizzy, Blush, Chomp, Grumpy, Focused, Love, Whee, Angry, Furious, Devil, Sick, Scared, Scrunch, Yawn, Whistle }
enum Prop { None, Juggle, Notes, Book, Coffee, Sneeze, Wave, Barf }
enum IdleAnim { Yawn, LookAround, Juggle, Whistle, Sneeze, Spin, Hop, Read, Coffee, Wave }

class Pose
{
    public float Dx, Lift, Rot, Sx = 1, Sy = 1, RayExtend = 1, LookX, LookY, Mouth, Steam, Sick;
    public bool RayWiggle, Blink, Zzz, Lying;
    public Expr Expr = Expr.Normal;
    public double? Sparks;
    public string SkinOverride, HatOverride;
    public Prop Prop = Prop.None;
    public double PropT;
}

class Animator
{
    public Mood Mood = Mood.Idle;
    public bool Asleep, Dragging, Devil, Sick, Flying;
    public double Anger;
    public float Squish;
    readonly double _phase;
    double _celebrateAt = -100, _petAt = -100, _dizzyUntil = -100, _chompUntil = -100, _grumpyUntil = -100, _surprisedAt = -100;
    double _talkUntil = -100, _loveUntil = -100, _danceUntil = -100, _blinkAt = -100, _nextBlink, _scaredAt = -100, _barfAt = -100, _idleAt = -100;
    IdleAnim? _idle;
    static readonly Random Rng = new();

    public Animator(double phase) { _phase = phase; _nextBlink = 1.5 + Rng.NextDouble() * 2.5; }

    static double T => Clock.Now;
    public bool IsCelebrating => T - _celebrateAt < 2.6 || T < _danceUntil;
    public void Celebrate() => _celebrateAt = T;
    public void Pet() => _petAt = T;
    public void Dizzy() => _dizzyUntil = T + 3;
    public void ChompNow() => _chompUntil = T + 1.4;
    public void Grumpy() => _grumpyUntil = T + 2;
    public void Surprise() => _surprisedAt = T;
    public void Talk(double s) => _talkUntil = T + s;
    public void Love() => _loveUntil = T + 3;
    public void Dance() => _danceUntil = T + 6;
    public void Scare() => _scaredAt = T;
    public void Barf() => _barfAt = T;
    public void StartIdle(IdleAnim a) { _idle = a; _idleAt = T; }

    static double IdleLength(IdleAnim a) => a switch
    {
        IdleAnim.Yawn => 2.2, IdleAnim.LookAround => 3, IdleAnim.Juggle => 4, IdleAnim.Whistle => 3.2, IdleAnim.Sneeze => 1.8,
        IdleAnim.Spin => 1.2, IdleAnim.Hop => 1.1, IdleAnim.Read => 5, IdleAnim.Coffee => 4, _ => 1.8,
    };

    public Pose GetPose(float lookX, float lookY)
    {
        double t = T;
        if (t > _nextBlink) { _blinkAt = t; _nextBlink = t + 2.2 + Rng.NextDouble() * 3.5; }
        var p = new Pose { LookX = lookX, LookY = lookY, Blink = t - _blinkAt < 0.13 };

        if (Asleep)
        {
            float b = (float)Math.Sin(t * 1.2 + _phase);
            p.Sy = 1 + 0.05f * b; p.Sx = 1 - 0.02f * b; p.Rot = 0.04f * b;
            p.Expr = Expr.Asleep; p.Zzz = true;
        }
        else
        {
            switch (Mood)
            {
                case Mood.Idle:
                    p.Sx = p.Sy = 1 + 0.035f * (float)Math.Sin(t * 1.6 + _phase);
                    p.Rot = 0.08f * (float)Math.Sin(t * 0.5 + _phase);
                    break;
                case Mood.Working:
                    p.Rot = (float)(t * 1.4 + _phase);
                    p.Sx = p.Sy = 1 + 0.04f * (float)Math.Sin(t * 5 + _phase);
                    p.RayWiggle = true; p.Expr = Expr.Focused;
                    p.LookX = (float)Math.Sin(t * 1.8); p.LookY = -0.3f;
                    break;
                case Mood.Attention:
                    double w = (t + _phase) % 1.4;
                    if (w < 0.5) p.Dx = (float)(Math.Sin(w * 42) * 3.2 * (1 - w / 0.5));
                    p.Lift = 3 * (float)Math.Abs(Math.Sin(t * 3));
                    p.Sx = p.Sy = 1.05f; p.Expr = Expr.Surprised;
                    break;
            }
        }

        double ce = t - _celebrateAt;
        if (ce < 2.6)
        {
            float decay = (float)Math.Exp(-ce * 1.1), hop = (float)Math.Abs(Math.Sin(ce * 6.5));
            p.Lift += hop * 22 * decay; p.RayExtend += 0.35f * decay * hop;
            p.Sx *= 1 + 0.08f * decay; p.Sy *= 1 + 0.08f * decay;
            p.Rot += (float)(2 * Math.PI * (1 - Math.Pow(1 - Math.Min(1, ce / 1.1), 3)));
            p.Expr = Expr.Happy; p.Sparks = ce;
        }
        if (t < _danceUntil)
        {
            p.Lift += (float)Math.Abs(Math.Sin(t * 7)) * 8; p.Dx += (float)Math.Sin(t * 3.5) * 6;
            p.Rot += (float)Math.Sin(t * 7) * 0.35f; p.Expr = Expr.Happy;
        }
        if (t < _loveUntil) p.Expr = Expr.Love;
        double pe = t - _petAt;
        if (pe < 0.6)
        {
            float d = (float)((1 - pe / 0.6) * Math.Abs(Math.Cos(pe * 14)));
            p.Sy *= 1 - 0.16f * d; p.Sx *= 1 + 0.12f * d; p.Expr = Expr.Blush;
        }
        if (t < _chompUntil) { p.Expr = Expr.Chomp; p.Mouth = (float)Math.Abs(Math.Sin(t * 16)); }
        if (t < _grumpyUntil) { p.Expr = Expr.Grumpy; if (_grumpyUntil - t > 1.7) p.Dx += (float)Math.Sin(t * 60) * 1.5f; }
        double se = t - _surprisedAt;
        if (se < 1.2)
        {
            p.Expr = Expr.Surprised;
            p.Lift += (float)(Math.Max(0, Math.Sin(se * 5)) * 26 * Math.Exp(-se * 2));
            p.RayExtend += 0.4f * (float)Math.Exp(-se * 3);
        }
        if (t < _dizzyUntil) { p.Expr = Expr.Dizzy; p.Rot += (float)Math.Sin(t * 3) * 0.6f; p.Dx += (float)Math.Sin(t * 4) * 4; }
        if (Dragging) { p.Expr = Expr.Whee; p.RayExtend = 1.15f; p.Rot += (float)Math.Sin(t * 8) * 0.3f; }
        ApplyPersonality(p, t);
        if (t < _talkUntil && (p.Expr is Expr.Normal or Expr.Focused or Expr.Angry or Expr.Devil or Expr.Sick))
            p.Mouth = (float)Math.Abs(Math.Sin(t * 22)) * 0.8f;
        return p;
    }

    void ApplyPersonality(Pose p, double t)
    {
        if (_idle is IdleAnim a && !Asleep && Mood == Mood.Idle && p.Expr == Expr.Normal && !Devil && !Sick)
        {
            double e = t - _idleAt, len = IdleLength(a), k = e / len;
            if (e < len)
            {
                switch (a)
                {
                    case IdleAnim.Yawn: p.Expr = Expr.Yawn; p.Sy *= 1 + 0.14f * (float)Math.Sin(k * Math.PI); break;
                    case IdleAnim.LookAround: p.LookX = (float)Math.Sin(e * 2.2); p.LookY = 0.2f * (float)Math.Cos(e * 1.3); break;
                    case IdleAnim.Juggle: p.Prop = Prop.Juggle; p.PropT = e; p.LookY = 1; break;
                    case IdleAnim.Whistle: p.Prop = Prop.Notes; p.PropT = e; p.Expr = Expr.Whistle; break;
                    case IdleAnim.Sneeze:
                        p.Expr = Expr.Scrunch;
                        if (e < 1.1) p.Sy *= 1 + 0.12f * (float)(e / 1.1);
                        else { p.Prop = Prop.Sneeze; p.PropT = e - 1.1; p.Dx += (float)Math.Exp(-(e - 1.1) * 8) * 7; }
                        break;
                    case IdleAnim.Spin: p.Rot += (float)(2 * Math.PI * (1 - Math.Pow(1 - k, 3))); p.Expr = Expr.Happy; break;
                    case IdleAnim.Hop: p.Lift += (float)Math.Abs(Math.Sin(k * 2 * Math.PI)) * 9; p.Expr = Expr.Happy; break;
                    case IdleAnim.Read: p.Prop = Prop.Book; p.PropT = e; p.LookY = -1; break;
                    case IdleAnim.Coffee: p.Prop = Prop.Coffee; p.PropT = e; p.Expr = e % 1.6 < 0.8 ? Expr.Blush : Expr.Normal; break;
                    case IdleAnim.Wave: p.Prop = Prop.Wave; p.PropT = e; p.Expr = Expr.Happy; p.Rot += (float)Math.Sin(e * 12) * 0.12f; break;
                }
            }
        }
        if (Sick && !Devil)
        {
            p.Sick = 1;
            if (p.Expr is Expr.Normal or Expr.Focused or Expr.Blush or Expr.Happy) p.Expr = Expr.Sick;
            p.Dx += (float)Math.Sin(t * 2.1) * 1.5f; p.Rot += (float)Math.Sin(t * 1.7) * 0.08f;
        }
        if (t - _barfAt < 2.5) { p.Prop = Prop.Barf; p.PropT = t - _barfAt; }
        if (!Devil && Anger > 35)
        {
            if (p.Expr is Expr.Normal or Expr.Focused or Expr.Blush or Expr.Whistle or Expr.Yawn) p.Expr = Anger > 70 ? Expr.Furious : Expr.Angry;
            p.Steam = (float)Math.Min(1, (Anger - 35) / 65);
            if (Anger > 70) p.Dx += (float)Math.Sin(t * 55) * 1.3f;
        }
        double sc = t - _scaredAt;
        if (sc < 1.1)
        {
            p.Expr = Expr.Scared;
            p.Lift += (float)(Math.Max(0, Math.Sin(sc * 6)) * 20 * Math.Exp(-sc * 2.5));
            p.RayExtend += 0.45f * (float)Math.Exp(-sc * 2);
        }
        if (Squish > 0.01f) { p.Sy *= 1 - 0.42f * Squish; p.Sx *= 1 + 0.32f * Squish; p.Expr = Expr.Scrunch; }
        if (Flying) { p.Expr = t % 0.6 < 0.3 ? Expr.Scared : Expr.Whee; p.Rot += (float)(t * 12); }
        if (Devil)
        {
            p.SkinOverride = "devil"; p.HatOverride = "horns"; p.Expr = Expr.Devil; p.Steam = 0;
            p.Lift += 4 + 3 * (float)Math.Sin(t * 3); p.RayWiggle = true;
        }
    }
}

static class Renderer
{
    static Color C(uint hex, int a = 255) => Palette.Rgb(hex, a);

    public static Color RayColor(string skin, int i, double t) => skin switch
    {
        "sky" => Palette.Blue,
        "mint" => C(0x3FBF94),
        "grape" => C(0x9466DB),
        "midnight" => C(0x6E8EF0),
        "gold" => Math.Sin(t * 3 - i * 0.6) > 0.6 ? C(0xFBE28A) : C(0xE3A92B),
        "rainbow" => FromHsv((i / 12.0 + t * 0.15) % 1, 0.62, 0.95),
        "ghost" => Color.FromArgb(140, 255, 255, 255),
        "devil" => Math.Sin(t * 9 + i * 1.7) > 0.2 ? C(0xF2862E) : C(0xE0402A),
        "slime" => C(0x7BD35B),
        _ => Palette.Orange,
    };

    static Color BodyColor(string skin, double t) => skin switch
    {
        "midnight" => C(0x273463),
        "rainbow" => FromHsv(t * 0.15 % 1, 0.55, 0.95),
        "ghost" => Color.FromArgb(230, 247, 247, 247),
        "devil" => C(0xC8302B),
        "slime" => C(0x6CC24A, 242),
        _ => RayColor(skin, 0, t),
    };

    static Color FromHsv(double h, double s, double v)
    {
        int hi = (int)(h * 6) % 6;
        double f = h * 6 - Math.Floor(h * 6), p = v * (1 - s), q = v * (1 - f * s), u = v * (1 - (1 - f) * s);
        var (r, g, b) = hi switch { 0 => (v, u, p), 1 => (q, v, p), 2 => (p, v, u), 3 => (p, q, v), 4 => (u, p, v), _ => (v, p, q) };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    static Color Tint(Color c, Pose p)
    {
        if (p.Sick > 0) c = Palette.Blend(c, C(0x8BC34A), 0.55 * p.Sick);
        if (p.Steam > 0) c = Palette.Blend(c, C(0xE53935), 0.35 * p.Steam);
        return c;
    }

    static void Fill(Graphics g, Color c, float x, float y, float w, float h) { using var b = new SolidBrush(c); g.FillEllipse(b, x, y, w, h); }
    static Pen RPen(Color c, float w) => new(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    static void FillPath(Graphics g, Color c, GraphicsPath path) { using var b = new SolidBrush(c); g.FillPath(b, path); }

    /// Upright text in the flipped (y-up) body space; (x, y) is the bottom-left corner.
    public static void TextUp(Graphics g, string s, float x, float y, float size, Color c, bool bold = true)
    {
        var st = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(1, -1);
        using var f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, 0, -size * 1.35f);
        g.Restore(st);
    }

    // GDI+ doesn't fall back to an emoji font by itself, so text is drawn in runs: emoji with
    // Segoe UI Emoji (monochrome in GDI+), everything else with the given font.
    static readonly StringFormat Typo = MakeTypo();
    static StringFormat MakeTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    static bool IsEmoji(Rune r) =>
        r.Value >= 0x1F000 || (r.Value >= 0x2600 && r.Value <= 0x27BF) || (r.Value >= 0x2B00 && r.Value <= 0x2BFF) ||
        (r.Value >= 0x2300 && r.Value <= 0x23FF) || r.Value == 0x200D || r.Value == 0xFE0F || r.Value == 0x20E3;

    static IEnumerable<(string text, bool emoji)> Runs(string s)
    {
        var sb = new StringBuilder();
        bool? cur = null;
        foreach (var r in s.EnumerateRunes())
        {
            bool e = IsEmoji(r);
            if (cur != null && e != cur) { yield return (sb.ToString(), cur.Value); sb.Clear(); }
            cur = e;
            sb.Append(r.ToString());
        }
        if (sb.Length > 0) yield return (sb.ToString(), cur ?? false);
    }

    static Font EmojiFont(Font f) => new("Segoe UI Emoji", f.Size, FontStyle.Regular, f.Unit);

    public static float MeasureRich(Graphics g, string s, Font f)
    {
        float w = 0;
        foreach (var (text, emoji) in Runs(s))
        {
            if (!emoji) { w += g.MeasureString(text, f, PointF.Empty, Typo).Width; continue; }
            using var ef = EmojiFont(f);
            w += g.MeasureString(text, ef, PointF.Empty, Typo).Width;
        }
        return w;
    }

    /// Draws one line of text (emoji included) with its top-left at (x, y), cut off at maxWidth.
    public static void DrawRich(Graphics g, string s, Font f, Brush b, float x, float y, float maxWidth = 10000)
    {
        var st = g.Save();
        g.IntersectClip(new RectangleF(x - 2, y - 4, maxWidth + 2, f.GetHeight(g) + 8));
        foreach (var (text, emoji) in Runs(s))
        {
            if (!emoji) { g.DrawString(text, f, b, x, y, Typo); x += g.MeasureString(text, f, PointF.Empty, Typo).Width; continue; }
            using var ef = EmojiFont(f);
            g.DrawString(text, ef, b, x, y, Typo);
            x += g.MeasureString(text, ef, PointF.Empty, Typo).Width;
        }
        g.Restore(st);
    }

    public static void Sparkle(Graphics g, Color c, float x, float y, float s)
    {
        using var path = new GraphicsPath();
        path.AddBezier(x, y + s, x, y, x, y, x + s, y);
        path.AddBezier(x + s, y, x, y, x, y, x, y - s);
        path.AddBezier(x, y - s, x, y, x, y, x - s, y);
        path.AddBezier(x - s, y, x, y, x, y, x, y + s);
        FillPath(g, c, path);
    }

    public static void Heart(Graphics g, Color c, float x, float y, float s)
    {
        using var h = new GraphicsPath();
        h.AddBezier(x, y - s * 0.9f, x - s * 1.3f, y - s * 0.1f, x - s * 0.6f, y + s * 1.1f, x, y + s * 0.35f);
        h.AddBezier(x, y + s * 0.35f, x + s * 0.6f, y + s * 1.1f, x + s * 1.3f, y - s * 0.1f, x, y - s * 0.9f);
        FillPath(g, c, h);
    }

    /// Draws a buddy whose resting body centre is `c` (window pixels, y down) at `s` times natural size.
    public static void Draw(Graphics g, string skin, string hat, Pose p, PointF c, float s, bool shadow = true)
    {
        double t = Clock.Now;
        skin = p.SkinOverride ?? skin;
        hat = p.HatOverride ?? hat;
        float lift = p.Lift;
        if (skin == "ghost" && !p.Lying) lift += 4 + 3 * (float)Math.Sin(t * 2);

        if (shadow && !p.Lying)
        {
            float sw = 46 * s * Math.Max(0.3f, 1 - lift / 45);
            Fill(g, Color.FromArgb((int)(51 * Math.Max(0.2f, 1 - lift / 60)), 0, 0, 0), c.X + p.Dx * s - sw / 2, c.Y + 29 * s, sw, 7 * s);
        }

        var st = g.Save();
        g.TranslateTransform(c.X + p.Dx * s, c.Y - lift * s);
        g.ScaleTransform(s, -s);
        if (p.Lying) g.RotateTransform(90);

        if (p.Sparks is double ce && ce < 1.3)
        {
            float a = (float)(1 - ce / 1.3);
            for (int k = 0; k < 8; k++)
            {
                double ang = k * Math.PI / 4 + 0.3;
                float d = (float)(26 + ce * 46);
                Sparkle(g, Palette.Alpha(k % 2 == 0 ? Palette.Spark : RayColor(skin, k, t), a), (float)Math.Cos(ang) * d, (float)Math.Sin(ang) * d, 4.5f * a + 1.5f);
            }
        }
        if (skin == "midnight") Fill(g, C(0x6E8EF0, (int)(255 * (0.18 + 0.08 * Math.Sin(t * 2)))), -36, -36, 72, 72);
        if (skin == "devil")
        {
            Fill(g, C(0xFF5A1F, (int)(255 * (0.22 + 0.1 * Math.Sin(t * 7)))), -38, -38, 76, 76);
            float sway = (float)Math.Sin(t * 4) * 6;
            using (var pen = RPen(C(0x9E1F1B), 3)) g.DrawBezier(pen, 8, -12, 20, -30, 30, -30 + sway, 34, -18 + sway);
            using var tip = new GraphicsPath();
            tip.AddPolygon(new[] { new PointF(34, -12 + sway), new PointF(40, -20 + sway), new PointF(30, -22 + sway) });
            FillPath(g, C(0x9E1F1B), tip);
        }

        // Rays and body
        var st2 = g.Save();
        g.RotateTransform(p.Rot * 180 / (float)Math.PI);
        g.ScaleTransform(p.Sx, p.Sy);
        float[] lens = { 1.0f, 0.74f, 0.9f, 0.8f, 0.97f, 0.7f, 0.93f, 0.78f, 1.0f, 0.72f, 0.88f, 0.82f };
        float rayScale = skin == "ghost" ? 0.8f : 1;
        for (int i = 0; i < 12; i++)
        {
            double ang = i * Math.PI / 6;
            float len = 30 * lens[i] * p.RayExtend * rayScale;
            if (p.RayWiggle) len *= 1 + 0.13f * (float)Math.Sin(t * 6 - i * 0.52);
            if (skin == "devil") len *= 1 + 0.16f * (float)Math.Sin(t * 14 + i * 1.3);
            using var pen = RPen(Tint(RayColor(skin, i, t), p), 6.5f);
            g.DrawLine(pen, (float)Math.Cos(ang) * 10, (float)Math.Sin(ang) * 10, (float)Math.Cos(ang) * len, (float)Math.Sin(ang) * len);
        }
        Fill(g, Tint(BodyColor(skin, t), p), -15.5f, -15.5f, 31, 31);
        g.Restore(st2);

        var st3 = g.Save();
        g.ScaleTransform(p.Sx, p.Sy);
        Face(g, p, skin == "midnight" ? C(0xE8EEFF) : Palette.Face);
        Hat(g, hat, t);
        g.Restore(st3);

        if (p.Prop != Prop.None) DrawProp(g, p.Prop, p.PropT);
        if (p.Steam > 0.02f) Steam(g, p.Steam, t);
        g.Restore(st);

        if (p.Zzz)
        {
            for (int k = 0; k < 3; k++)
            {
                float ph = (float)((t * 0.5 + k / 3.0) % 1);
                using var f = new Font("Segoe UI", (8 + ph * 7) * s, FontStyle.Bold, GraphicsUnit.Pixel);
                using var b = new SolidBrush(Palette.Alpha(C(0x8CA6F2), 1 - ph));
                g.DrawString("z", f, b, c.X + (p.Lying ? -6 : 14) * s + ph * 14 * s, c.Y - (p.Lying ? 12 : 18) * s - ph * 26 * s - 14 * s);
            }
        }
    }

    // MARK: Face (y-up body coordinates)

    static void Face(Graphics g, Pose p, Color ink)
    {
        float lx = p.LookX * 2.2f, ly = p.LookY * 1.6f;
        using var inkB = new SolidBrush(ink);
        void Eyes(float w, float h) { foreach (var sx in new[] { -1f, 1f }) g.FillEllipse(inkB, sx * 5.5f + lx - w / 2, 2.5f + ly - h / 2, w, h); }
        void HappyEyes() { using var pen = RPen(ink, 2.2f); foreach (var sx in new[] { -1f, 1f }) g.DrawLines(pen, new[] { new PointF(sx * 5.5f - 3, 1), new PointF(sx * 5.5f, 4.5f), new PointF(sx * 5.5f + 3, 1) }); }
        void Grin(float open = 0) { using var path = new GraphicsPath(); float r = 5 + open; path.AddArc(-r, -3 - r, 2 * r, 2 * r, 190, 160); path.CloseFigure(); g.FillPath(inkB, path); }
        void Cheeks() { foreach (var sx in new[] { -1f, 1f }) Fill(g, C(0xFF7FA0, 153), sx * 9.5f - 3, -4.5f, 6, 3.5f); }
        void Smile(float talk)
        {
            if (talk > 0.1f) g.FillEllipse(inkB, -2.5f + lx * 0.5f, -6.5f, 5, 1.5f + talk * 4);
            else { using var pen = RPen(ink, 1.8f); g.DrawArc(pen, lx * 0.5f - 3.5f, -6f, 7, 7, 205, 130); }
        }
        void Line(float w, float x1, float y1, float x2, float y2) { using var pen = RPen(ink, w); g.DrawLine(pen, x1, y1, x2, y2); }

        switch (p.Expr)
        {
            case Expr.Normal: Eyes(2.8f, p.Blink ? 0.6f : 4); Smile(p.Mouth); break;
            case Expr.Focused:
                Eyes(2.8f, p.Blink ? 0.6f : 2.6f);
                if (p.Mouth > 0.1f) Smile(p.Mouth); else Line(1.6f, -2, -5, 2, -5);
                break;
            case Expr.Happy: HappyEyes(); Grin(); break;
            case Expr.Blush: HappyEyes(); Cheeks(); Smile(0); break;
            case Expr.Whee: HappyEyes(); Cheeks(); g.FillEllipse(inkB, -3.5f, -9, 7, 7); break;
            case Expr.Love:
                foreach (var sx in new[] { -1f, 1f }) Heart(g, C(0xE5395B), sx * 5.8f, 2.5f, 4.2f);
                Cheeks(); Grin(); break;
            case Expr.Surprised: Eyes(3.4f, p.Blink ? 0.8f : 5); g.FillEllipse(inkB, -2.2f, -8, 4.4f, 4.4f); break;
            case Expr.Asleep:
                using (var pen = RPen(ink, 1.8f)) foreach (var sx in new[] { -1f, 1f }) g.DrawArc(pen, sx * 5.5f - 2.6f, 0.9f, 5.2f, 5.2f, 200, 140);
                g.FillEllipse(inkB, -1.6f, -6.5f, 3.2f, 2 + (float)(1 + Math.Sin(Clock.Now * 1.2)) * 0.6f);
                break;
            case Expr.Grumpy:
                foreach (var sx in new[] { -1f, 1f }) Line(2, sx * 5.5f - 2.6f, 2 + sx * 0.8f, sx * 5.5f + 2.6f, 2 - sx * 0.8f);
                using (var pen = RPen(ink, 1.8f)) g.DrawArc(pen, -3.5f, -12.5f, 7, 7, 30, 120);
                break;
            case Expr.Dizzy:
                using (var pen = RPen(ink, 1.3f))
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        double spin = Clock.Now * 6 * sx;
                        var pts = Enumerable.Range(0, 25).Select(k => { double a = k / 24.0 * 4 * Math.PI + spin; float r = k / 24f * 3.4f; return new PointF(sx * 5.5f + r * (float)Math.Cos(a), 2.5f + r * (float)Math.Sin(a)); }).ToArray();
                        g.DrawLines(pen, pts);
                    }
                using (var pen = RPen(ink, 1.6f)) g.DrawBeziers(pen, new[] { new PointF(-4, -5), new PointF(-3, -3), new PointF(-1, -7), new PointF(0, -5), new PointF(1, -3), new PointF(3, -7), new PointF(4, -5) });
                break;
            case Expr.Chomp: HappyEyes(); g.FillEllipse(inkB, -3.5f, -8, 7, 1.5f + p.Mouth * 6); Cheeks(); break;
            case Expr.Angry:
            case Expr.Furious:
                bool fur = p.Expr == Expr.Furious;
                Eyes(2.8f, fur ? 2.2f : 3.4f);
                foreach (var sx in new[] { -1f, 1f }) Line(2, sx * 9, fur ? 8.5f : 7.5f, sx * 2.5f, fur ? 4.8f : 5.8f);
                if (fur)
                {
                    g.FillRectangle(Brushes.White, -4.5f, -8, 9, 4);
                    using var pen = new Pen(ink, 1);
                    g.DrawRectangle(pen, -4.5f, -8, 9, 4);
                    g.DrawLine(pen, -1.5f, -8, -1.5f, -4); g.DrawLine(pen, 1.5f, -8, 1.5f, -4);
                }
                else using (var pen = RPen(ink, 1.8f)) g.DrawArc(pen, -3.2f, -11.7f, 6.4f, 6.4f, 35, 110);
                break;
            case Expr.Devil:
                using (var yb = new SolidBrush(C(0xFFE45C)))
                    foreach (var sx in new[] { -1f, 1f }) g.FillPolygon(yb, new[] { new PointF(sx * 2.5f, 1), new PointF(sx * 8.5f, 5), new PointF(sx * 7.5f, 0.5f) });
                using (var grin = new GraphicsPath())
                {
                    grin.AddBezier(-7, -3, -4, -11, 4, -11, 7, -3);
                    grin.CloseFigure();
                    g.FillPath(inkB, grin);
                }
                foreach (var sx in new[] { -1f, 1f }) g.FillPolygon(Brushes.White, new[] { new PointF(sx * 4.5f, -4), new PointF(sx * 2.5f, -4), new PointF(sx * 3.5f, -7) });
                break;
            case Expr.Sick:
                foreach (var sx in new[] { -1f, 1f }) { Line(1.6f, sx * 5.5f - 3, 3.5f, sx * 5.5f + 3, 3.5f); g.FillEllipse(inkB, sx * 5.5f - 1.4f, 0.6f, 2.8f, 2.6f); }
                using (var pen = RPen(ink, 1.6f)) g.DrawLines(pen, Enumerable.Range(0, 5).Select(k => new PointF(-5 + k * 2.5f, k % 2 == 0 ? -6 : -4.6f)).ToArray());
                Fill(g, C(0x7FB3FF), 8.5f, 5, 5, 6);
                break;
            case Expr.Scared:
                foreach (var sx in new[] { -1f, 1f }) g.FillEllipse(Brushes.White, sx * 5.8f - 3.2f, -0.8f, 6.4f, 7.4f);
                foreach (var sx in new[] { -1f, 1f }) g.FillEllipse(inkB, sx * 5.8f - 1, 1.8f, 2, 2);
                g.FillEllipse(inkB, -2.6f, -10, 5.2f, 6.5f);
                break;
            case Expr.Scrunch:
                using (var pen = RPen(ink, 2)) foreach (var sx in new[] { -1f, 1f }) g.DrawLines(pen, new[] { new PointF(sx * 7.8f, 4.5f), new PointF(sx * 3.6f, 2.5f), new PointF(sx * 7.8f, 0.5f) });
                Line(1.8f, -2.5f, -5.5f, 2.5f, -5.5f);
                break;
            case Expr.Yawn:
                foreach (var sx in new[] { -1f, 1f }) Line(1.8f, sx * 5.5f - 2.6f, 3, sx * 5.5f + 2.6f, 3);
                g.FillEllipse(inkB, -3.5f, -10, 7, 8);
                break;
            case Expr.Whistle:
                Eyes(2.8f, p.Blink ? 0.6f : 4);
                using (var pen = new Pen(ink, 1.6f)) g.DrawEllipse(pen, 0.5f, -6.5f, 3.6f, 3.6f);
                Cheeks();
                break;
        }
    }

    // MARK: Props and steam

    static void DrawProp(Graphics g, Prop prop, double e)
    {
        switch (prop)
        {
            case Prop.Juggle:
                uint[] cols = { 0xE5395B, 0x3F7FE0, 0xF2C94C };
                for (int k = 0; k < 3; k++)
                {
                    double ph = e * 4.2 + k * 2.1;
                    Fill(g, C(cols[k]), (float)Math.Cos(ph) * 16 - 3.5f, 34 + (float)Math.Abs(Math.Sin(ph)) * 18 - 3.5f, 7, 7);
                }
                break;
            case Prop.Notes:
                for (int k = 0; k < 3; k++)
                {
                    float ph = (float)((e * 0.9 + k / 3.0) % 1);
                    TextUp(g, k % 2 == 0 ? "♪" : "♫", 10 + ph * 16, 2 + ph * 28 + (float)Math.Sin(e * 5 + k) * 3, 12, Palette.Alpha(C(0x9466DB), 1 - ph));
                }
                break;
            case Prop.Book:
                using (var b1 = new SolidBrush(C(0x3F7FE0))) g.FillRectangle(b1, -13, -22, 12, 15);
                using (var b2 = new SolidBrush(C(0xE5395B))) g.FillRectangle(b2, 1, -22, 12, 15);
                g.FillRectangle(Brushes.White, -1.2f, -22, 2.4f, 15);
                break;
            case Prop.Coffee:
                float cy = -14 + (float)Math.Sin(e * 1.6) * 4;
                using (var cup = new SolidBrush(Color.White)) g.FillRectangle(cup, 16, cy, 9, 10);
                using (var pen = new Pen(Color.White, 1.6f)) g.DrawEllipse(pen, 24, cy + 3, 4, 4);
                using (var cof = new SolidBrush(C(0x6B4A33))) g.FillRectangle(cof, 17, cy + 8, 7, 1.5f);
                for (int k = 0; k < 2; k++) Fill(g, Color.FromArgb(120, 255, 255, 255), 18 + k * 3, cy + 12 + (float)((e * 8 + k * 4) % 8), 2.5f, 2.5f);
                break;
            case Prop.Sneeze:
                float a = (float)Math.Max(0, 1 - e / 0.7);
                for (int k = 0; k < 3; k++) Fill(g, Color.FromArgb((int)(200 * a), 220, 230, 240), -30 - (float)e * 30 - k * 6, -3 + k * 2, 7, 7);
                break;
            case Prop.Wave:
                TextUp(g, "hi!", 20, 14 + (float)Math.Sin(e * 12) * 3, 11, C(0x3F7FE0));
                break;
            case Prop.Barf:
                float ba = (float)Math.Max(0, 1 - e / 2.5);
                Fill(g, Palette.Alpha(C(0x8BC34A), 0.85 * ba), 6, -36, (float)Math.Min(1, e / 0.3) * 22, 6);
                if (e < 0.4) for (int k = 0; k < 4; k++) Fill(g, Palette.Alpha(C(0x8BC34A), 0.85 * ba), 2 + k * 4, -8 - (float)e * 60 + k, 3.5f, 3.5f);
                break;
        }
    }

    static void Steam(Graphics g, float s, double t)
    {
        foreach (var side in new[] { -1f, 1f })
            for (int k = 0; k < 2; k++)
            {
                float ph = (float)((t * 1.6 + k * 0.5 + (side > 0 ? 0.25 : 0)) % 1);
                float r = 3 + ph * 5 * s;
                Fill(g, Color.FromArgb((int)(217 * s * (1 - ph)), 255, 255, 255), side * (20 + ph * 8) - r, 14 + ph * 18 - r, 2 * r, 2 * r);
            }
    }

    // MARK: Hats

    static void Hat(Graphics g, string hat, double t)
    {
        switch (hat)
        {
            case "party":
                using (var cone = new GraphicsPath())
                {
                    cone.AddPolygon(new[] { new PointF(-10, 10), new PointF(10, 10), new PointF(2, 36) });
                    FillPath(g, C(0xF06292), cone);
                    var st = g.Save();
                    g.SetClip(cone);
                    using (var y = new SolidBrush(Palette.Spark)) { g.FillRectangle(y, -12, 16, 24, 3); g.FillRectangle(y, -12, 24, 24, 3); }
                    g.Restore(st);
                }
                Fill(g, Palette.Spark, -1.5f, 33.5f, 7, 7);
                break;
            case "beanie":
                using (var b = new SolidBrush(C(0x2E8C9C))) g.FillPie(b, -15.5f, -5.5f, 31, 31, 0, 180);
                using (var b = new SolidBrush(C(0x1F6672))) g.FillRectangle(b, -16.5f, 7, 33, 7);
                Fill(g, C(0xF2F2F2), -4.5f, 22.5f, 9, 9);
                break;
            case "headphones":
                using (var pen = new Pen(C(0x3A3A3A), 3.5f)) g.DrawArc(pen, -19, -16, 38, 38, 15, 150);
                foreach (var sx in new[] { -1f, 1f })
                {
                    using (var b = new SolidBrush(C(0x444444))) g.FillRectangle(b, sx * 19 - 4.5f, -5, 9, 15);
                    using (var b = new SolidBrush(C(0xE53935))) g.FillRectangle(b, sx * 19 - 2, -2, 4, 9);
                }
                break;
            case "wizard":
                Fill(g, C(0x5B3FA8), -21, 7, 42, 9);
                using (var cone = new GraphicsPath())
                {
                    cone.AddBezier(-12, 11, -8, 30, 0, 40, 9, 46);
                    cone.AddBezier(9, 46, 6, 36, 10, 24, 12, 11);
                    cone.CloseFigure();
                    FillPath(g, C(0x5B3FA8), cone);
                }
                Sparkle(g, Palette.Spark, -3, 21, 3);
                Sparkle(g, Palette.Spark, 4, 31, 2.4f);
                break;
            case "crown":
                var crown = new[] { new PointF(-13, 10), new PointF(13, 10), new PointF(14, 25), new PointF(7, 18), new PointF(0, 28), new PointF(-7, 18), new PointF(-14, 25) };
                using (var b = new SolidBrush(C(0xF2C94C))) g.FillPolygon(b, crown);
                using (var pen = new Pen(C(0xC99A22), 1)) g.DrawPolygon(pen, crown);
                Fill(g, C(0xE53935), -2, 12, 4, 4);
                Fill(g, C(0x3F7FE0), -9.6f, 12.4f, 3.2f, 3.2f);
                Fill(g, C(0x3F7FE0), 6.4f, 12.4f, 3.2f, 3.2f);
                break;
            case "propeller":
                using (var b = new SolidBrush(C(0xE53935))) g.FillPie(b, -14, -4, 28, 28, 0, 180);
                using (var b = new SolidBrush(C(0x3F7FE0))) g.FillRectangle(b, -14, 9, 28, 3);
                using (var b = new SolidBrush(C(0x555555))) g.FillRectangle(b, -1, 23, 2, 5);
                float w = (float)Math.Abs(Math.Cos(t * 18)) * 15 + 2;
                Fill(g, Palette.Spark, -w, 26.5f, w, 4);
                Fill(g, C(0x3F7FE0), 0, 26.5f, w, 4);
                break;
            case "chef":
                foreach (var (x, y, d) in new[] { (-15f, 17f, 14f), (-8f, 21f, 16f), (1f, 17f, 14f) }) Fill(g, Color.White, x, y, d, d);
                g.FillRectangle(Brushes.White, -11, 9, 22, 12);
                break;
            case "nightcap":
                using (var cap = new GraphicsPath())
                {
                    cap.AddBezier(-14, 11, -13, 28, 0, 34, 10, 32);
                    cap.AddBezier(10, 32, 18, 31, 22, 25, 23, 17);
                    cap.AddBezier(23, 17, 18, 22, 15, 18, 14, 11);
                    cap.CloseFigure();
                    FillPath(g, C(0x3F51B5), cap);
                }
                g.FillRectangle(Brushes.White, -15.5f, 7.5f, 31, 6);
                Fill(g, Color.White, 19.5f, 11, 8, 8);
                break;
            case "horns":
                foreach (var sx in new[] { -1f, 1f })
                    using (var h = new GraphicsPath())
                    {
                        h.AddBezier(sx * 6, 11, sx * 7, 20, sx * 11, 24, sx * 15, 26);
                        h.AddBezier(sx * 15, 26, sx * 13, 18, sx * 14, 12, sx * 12, 9);
                        h.CloseFigure();
                        FillPath(g, C(0x7A1512), h);
                    }
                break;
            case "headband":
                using (var b = new SolidBrush(C(0xE5395B))) g.FillRectangle(b, -16, 6, 32, 6);
                g.FillRectangle(Brushes.White, -16, 8, 32, 1.5f);
                using (var b = new SolidBrush(C(0xE5395B)))
                    g.FillPolygon(b, new[] { new PointF(15, 9), new PointF(24, 4 + (float)Math.Sin(t * 5) * 2), new PointF(22, 12) });
                break;
        }
    }
}
