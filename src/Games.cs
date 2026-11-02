// The arcade: five mini-games that run over buddy's room. Each game draws in its own y-up area
// (460 × 296, just above the room's bottom bar) through the room's drawing helpers, and pays
// out XP and coins through Progress when it ends.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeBuddy;

record GameInfo(string Id, string Name, string Icon, string Blurb);

abstract class MiniGame
{
    public const float GW = 460, GH = 296, B = 44;   // B: where the game area starts in room units
    public static readonly GameInfo[] All =
    {
        new("bugcatch", "Bug Catch", "🐞", "Catch bugs, dodge errors"),
        new("copycat", "Copycat", "🎵", "Copy buddy's tune"),
        new("whack", "Whack-a-Bug", "🔨", "Bonk bugs, not features"),
        new("hop", "Buddy Hop", "🪽", "Dodge merge conflicts"),
        new("type", "Type Rush", "⌨️", "Type before it lands"),
    };

    public static MiniGame Make(string id, RoomForm f, Animator a) => id switch
    {
        "bugcatch" => new BugCatchGame(f, a),
        "copycat" => new CopycatGame(f, a),
        "whack" => new WhackGame(f, a),
        "hop" => new HopGame(f, a),
        "type" => new TypeRushGame(f, a),
        _ => null,
    };

    protected readonly RoomForm F;
    protected readonly Animator Anim;
    protected static readonly Random Rng = new();
    public readonly string Id;
    public readonly string Title;
    public Func<string, int, GameReward> OnFinish;
    public Action OnClose;

    public int Score;
    public bool Over { get; private set; }
    GameReward _reward;
    public double Started = Clock.Now;
    double _last = Clock.Now;

    protected MiniGame(string id, string title, RoomForm f, Animator a) { Id = id; Title = title; F = f; Anim = a; }

    public virtual bool Translucent => false;
    protected virtual string CenterText => $"Score {Score}";
    protected virtual string RightText => "";
    protected virtual string OverTitle => "Game over!";
    protected virtual string OverLine => $"Score {Score}";
    protected int Best => Progress.Shared.Data.Best.TryGetValue(Id, out var b) ? b : 0;

    public void Restart()
    {
        Score = 0; Over = false; _reward = null; Started = Clock.Now; _last = Clock.Now;
        Anim.Asleep = false;
        Reset();
    }

    protected virtual void Reset() { }
    protected virtual void Update(float dt, double t) { }
    protected virtual void DrawGame(double t) { }
    protected virtual void Click(PointF p) { }
    public virtual bool KeyDown(Keys k) => false;
    public virtual void KeyUp(Keys k) { }
    public virtual bool Char(char c) => false;

    protected void End()
    {
        if (Over) return;
        Over = true;
        Synth.GameOver();
        _reward = OnFinish?.Invoke(Id, Score);
        if (_reward is { NewBest: true } && Score > 0) { Anim.Celebrate(); }
    }

    public void Close() => OnClose?.Invoke();

    // Drawing helpers in game coordinates (y up, origin at the game area's bottom-left)
    protected void Fill(Color c, float x, float y, float w, float h, float r = 0) => F.Fill(c, x, y + B, w, h, r);
    protected void Oval(Color c, float x, float y, float w, float h) => F.Oval(c, x, y + B, w, h);
    protected void Text(string s, float x, float y, float size, Color c, bool bold = true, bool mono = false) => F.DrawText(s, x, y + B, size, c, bold, mono);
    protected float Width(string s, float size, bool bold = true) => F.TextWidth(s, size, bold);
    protected void Centered(string s, float y, float size, Color c, bool bold = true) => Text(s, GW / 2 - Width(s, size, bold) / 2, y, size, c, bold);
    protected void LineG(Color c, float w, params (float x, float y)[] pts) => F.Line(c, w, pts.Select(p => (p.x, p.y + B)).ToArray());
    protected PointF Screen(float x, float y) => new(x, RoomForm.H - (y + B));
    protected void DrawBuddy(Pose p, float x, float y, float s, bool shadow = false) => F.DrawBuddy(p, new PointF(x, y + B), s, shadow);
    protected static Color C(uint hex, double a = 1) => Palette.Rgb(hex, (int)(Math.Clamp(a, 0, 1) * 255));
    protected PointF Mouse() { var m = F.MousePoint(); return new PointF(m.X, m.Y - B); }

    static readonly RectangleF AgainRect = new(136, 76, 88, 28), DoneRect = new(236, 76, 88, 28);
    static bool In(RectangleF r, PointF p) => p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom;

    public void Frame()
    {
        double t = Clock.Now;
        float dt = (float)Math.Min(0.05, t - _last);
        _last = t;
        if (!Over) Update(dt, t);
        Fill(Translucent ? C(0x0F1117, 0.55) : C(0x1E1B2E, 0.98), 0, 0, GW, GH);
        DrawGame(t);
        Fill(Color.FromArgb(64, 0, 0, 0), 0, GH - 36, GW, 36);
        Text(Title, 14, GH - 28, 13, Color.White);
        Centered(CenterText, GH - 28, 13, Palette.Spark);
        var r = RightText;
        Text(r, GW - 14 - Width(r, 13), GH - 28, 13, Color.White);
        if (!Over) Text("esc to quit", GW - 70, 4, 9.5f, Color.FromArgb(90, 255, 255, 255), false);
        else
        {
            Fill(C(0x2B2D42, 0.98), 110, 62, 240, 150, 14);
            Centered(OverTitle, 212 - 32, 16, Color.White);
            Centered($"{OverLine}  ·  Best {Math.Max(Best, Score)}", 212 - 54, 11.5f, Color.FromArgb(217, 217, 217));
            if (_reward is GameReward rw)
                Centered($"+{rw.Xp} XP   +{rw.Coins} 🪙" + (rw.NewBest && Score > 0 ? "   🎉 New best!" : ""), 212 - 76, 12, Palette.Spark);
            Fill(Palette.Orange, AgainRect.X, AgainRect.Y, AgainRect.Width, AgainRect.Height, 8);
            Text("Play again", AgainRect.X + 13, AgainRect.Y + 6, 11.5f, Color.White);
            Fill(Color.FromArgb(38, 255, 255, 255), DoneRect.X, DoneRect.Y, DoneRect.Width, DoneRect.Height, 8);
            Text("Done", DoneRect.X + 28, DoneRect.Y + 6, 11.5f, Color.White);
        }
    }

    /// A click in room units.
    public void MouseDown(PointF room)
    {
        var p = new PointF(room.X, room.Y - B);
        if (Over) { if (In(AgainRect, p)) Restart(); else if (In(DoneRect, p)) Close(); return; }
        Click(p);
    }

    public bool HandleKey(Keys k)
    {
        if (k == Keys.Escape) { Close(); return true; }
        if (Over) { if (k is Keys.Space or Keys.Enter) Restart(); return true; }
        return KeyDown(k);
    }

    public void DrawBugSprite(float x, float y, bool golden, double t, float s = 1)
    {
        var dark = C(0x1E1E1E);
        float wig = (float)Math.Sin(t * 20) * 2 * s;
        for (int k = -1; k <= 1; k++)
        {
            float yy = y + k * 4 * s;
            LineG(dark, 1.2f * s, (x - 6 * s, yy), (x - 11 * s, yy + wig));
            LineG(dark, 1.2f * s, (x + 6 * s, yy), (x + 11 * s, yy - wig));
        }
        Fill(golden ? C(0xF2C94C) : C(0x3E9B4F), x - 7 * s, y - 8 * s, 14 * s, 17 * s, 7 * s);
        Fill(dark, x - 4.5f * s, y - 11 * s, 9 * s, 6 * s, 3 * s);
        LineG(dark, s, (x, y - 6 * s), (x, y + 9 * s));
        if (golden) { var sp = Screen(x + 9 * s, y + 9 * s); Renderer.Sparkle(F.G, Color.White, sp.X, sp.Y, (3 + (float)Math.Abs(Math.Sin(t * 8)) * 2) * s); }
    }
}

// MARK: - Bug Catch

class BugCatchGame : MiniGame
{
    struct Bug { public float X, Y, Vy; public int Kind; }   // 0 bug, 1 golden, 2 error
    readonly List<Bug> _bugs = new();
    readonly List<(string text, float x, float y, double at)> _pops = new();
    double _nextSpawn;
    float _x = 230;
    bool _left, _right;

    public BugCatchGame(RoomForm f, Animator a) : base("bugcatch", "🐞 BUG CATCH", f, a) { }
    public override bool Translucent => true;
    protected override string RightText => Over ? "0s" : $"{Math.Max(0, 30 - (Clock.Now - Started)):0}s";
    protected override string OverTitle => "Time's up!";

    protected override void Reset() { _bugs.Clear(); _pops.Clear(); _left = _right = false; _nextSpawn = Clock.Now + 0.6; _x = 230; }

    protected override void Update(float dt, double t)
    {
        double e = t - Started;
        if (e >= 30) { _left = _right = false; End(); return; }
        if (_left) _x -= 260 * dt;
        else if (_right) _x += 260 * dt;
        else { var m = Mouse(); if (m.X > -900) _x += (m.X - _x) * Math.Min(1, 12 * dt); }
        _x = Math.Clamp(_x, 24, GW - 24);
        if (t > _nextSpawn)
        {
            _nextSpawn = t + Math.Max(0.28, 0.7 - e * 0.014);
            double r = Rng.NextDouble();
            _bugs.Add(new Bug { X = 20 + (float)Rng.NextDouble() * (GW - 40), Y = GH - 36, Vy = (float)(90 + Rng.NextDouble() * 60 + e * 3), Kind = r < 0.08 ? 1 : r < 0.25 ? 2 : 0 });
        }
        for (int i = 0; i < _bugs.Count; i++) { var b = _bugs[i]; b.Y -= b.Vy * dt; _bugs[i] = b; }
        _bugs.RemoveAll(b =>
        {
            if (Math.Abs(b.X - _x) < 26 && b.Y < 68 && b.Y > 36)
            {
                int pts = new[] { 1, 5, -3 }[b.Kind];
                Score = Math.Max(0, Score + pts);
                _pops.Add((pts > 0 ? $"+{pts}" : $"{pts}", b.X, b.Y, t));
                if (b.Kind == 2) { Synth.Ouch(); Anim.Grumpy(); } else { Synth.CatchBug(b.Kind == 1); if (b.Kind == 1) Anim.Celebrate(); }
                return true;
            }
            return b.Y < 0;
        });
        _pops.RemoveAll(p => t - p.at > 0.8);
    }

    protected override void DrawGame(double t)
    {
        foreach (var b in _bugs)
        {
            if (b.Kind == 2)
            {
                Oval(C(0xE5484D), b.X - 10, b.Y - 10, 20, 20);
                LineG(Color.White, 2.5f, (b.X - 4, b.Y - 4), (b.X + 4, b.Y + 4));
                LineG(Color.White, 2.5f, (b.X - 4, b.Y + 4), (b.X + 4, b.Y - 4));
            }
            else DrawBugSprite(b.X, b.Y, b.Kind == 1, t);
        }
        var pose = Anim.GetPose(0, 1);
        if (_left || _right) pose.Lift += (float)Math.Abs(Math.Sin(t * 12)) * 3;
        DrawBuddy(pose, _x, 51, 0.7f, true);
        foreach (var p in _pops)
        {
            float ph = (float)(t - p.at) / 0.8f;
            Text(p.text, p.x - 8, p.y + ph * 24, 14, Palette.Alpha(p.text.StartsWith("-") ? C(0xFF6B6B) : Palette.Spark, 1 - ph));
        }
        if (!Over && t - Started < 3)
        {
            Centered("Catch bugs (+1) and golden bugs (+5). Dodge errors (-3)!", 206, 11.5f, Color.White);
            Centered("Move with the mouse or ← →", 188, 11, Color.FromArgb(204, 204, 204), false);
        }
    }

    public override bool KeyDown(Keys k)
    {
        if (k == Keys.Left) { _left = true; return true; }
        if (k == Keys.Right) { _right = true; return true; }
        return false;
    }
    public override void KeyUp(Keys k) { if (k == Keys.Left) _left = false; if (k == Keys.Right) _right = false; }
}

// MARK: - Copycat

class CopycatGame : MiniGame
{
    readonly List<int> _seq = new();
    readonly uint[] _colors = { 0xE5395B, 0x3F7FE0, 0xF2C94C, 0x5DBB63 };
    readonly string[] _arrows = { "↑", "→", "↓", "←" };
    bool _showing;
    int _showIndex, _inputIndex;
    double _nextStepAt, _flashUntil;
    int? _flashPad;
    static readonly PointF Center = new(GW / 2, 128);

    public CopycatGame(RoomForm f, Animator a) : base("copycat", "🎵 COPYCAT", f, a) { }
    protected override string CenterText => $"Round {_seq.Count}";
    protected override string RightText => $"Best {Best + 1}";
    protected override string OverTitle => "Oops!";
    protected override string OverLine => $"You reached round {Score + 1}";

    static PointF Pad(int i) => i switch
    {
        0 => new(Center.X, Center.Y + 80), 1 => new(Center.X + 104, Center.Y), 2 => new(Center.X, Center.Y - 80), _ => new(Center.X - 104, Center.Y),
    };

    protected override void Reset() { _seq.Clear(); _seq.Add(Rng.Next(4)); _showing = true; _showIndex = 0; _nextStepAt = Clock.Now + 1; }

    void Flash(int i) { _flashPad = i; _flashUntil = Clock.Now + 0.32; Synth.Pad(i); }

    protected override void Update(float dt, double t)
    {
        if (!_showing || t < _nextStepAt) return;
        if (_showIndex < _seq.Count) { Flash(_seq[_showIndex++]); _nextStepAt = t + Math.Max(0.34, 0.62 - _seq.Count * 0.025); }
        else { _showing = false; _inputIndex = 0; }
    }

    void Press(int i)
    {
        if (_showing || Over) return;
        Flash(i);
        if (_seq[_inputIndex] == i)
        {
            if (++_inputIndex == _seq.Count)
            {
                Score = _seq.Count; Anim.Celebrate();
                _seq.Add(Rng.Next(4)); _showing = true; _showIndex = 0; _nextStepAt = Clock.Now + 1.1;
            }
        }
        else { Synth.Buzz(); Anim.Dizzy(); End(); }
    }

    protected override void DrawGame(double t)
    {
        Text(Over ? "" : _showing ? "Watch buddy…" : "Your turn! Click the pads or use the arrow keys", 14, 6, 11.5f, Color.FromArgb(191, 191, 191), false);
        for (int i = 0; i < 4; i++)
        {
            var c = Pad(i);
            bool lit = _flashPad == i && t < _flashUntil;
            float r = lit ? 36 : 32;
            if (lit) Oval(Palette.Rgb(_colors[i], 70), c.X - r - 8, c.Y - r - 8, 2 * r + 16, 2 * r + 16);
            Oval(Palette.Rgb(_colors[i], lit ? 255 : 97), c.X - r, c.Y - r, 2 * r, 2 * r);
            Text(_arrows[i], c.X - 8, c.Y - 11, 18, Color.FromArgb(lit ? 255 : 150, 255, 255, 255));
        }
        float lx = 0, ly = 0;
        if (_flashPad is int f && t < _flashUntil) { var pc = Pad(f); lx = (pc.X - Center.X) / 104; ly = (pc.Y - Center.Y) / 80; }
        var pose = Anim.GetPose(lx, ly);
        pose.Lift = Math.Min(pose.Lift, 12);
        DrawBuddy(pose, Center.X, Center.Y, 1.0f);
    }

    protected override void Click(PointF p)
    {
        for (int i = 0; i < 4; i++) { var c = Pad(i); if (Math.Pow(p.X - c.X, 2) + Math.Pow(p.Y - c.Y, 2) < 36 * 36) { Press(i); return; } }
    }

    public override bool KeyDown(Keys k)
    {
        switch (k)
        {
            case Keys.Up: Press(0); return true;
            case Keys.Right: Press(1); return true;
            case Keys.Down: Press(2); return true;
            case Keys.Left: Press(3); return true;
        }
        return false;
    }
}

// MARK: - Whack-a-Bug

class WhackGame : MiniGame
{
    class Mole { public int Hole, Kind; public double UpAt, DownAt; public double? HitAt; }   // 0 bug, 1 golden, 2 feature
    readonly List<Mole> _moles = new();
    readonly List<(string text, PointF p, double at)> _pops = new();
    double _nextSpawn, _swingAt = -100;

    public WhackGame(RoomForm f, Animator a) : base("whack", "🔨 WHACK-A-BUG", f, a) { }
    protected override string RightText => Over ? "0s" : $"{Math.Max(0, 30 - (Clock.Now - Started)):0}s";
    protected override string OverTitle => "Time's up!";

    static RectangleF Screen(int i) => new(150 + (i % 3) * 100, 178 - (i / 3) * 76, 80, 58);
    protected override void Reset() { _moles.Clear(); _pops.Clear(); _nextSpawn = Clock.Now + 0.8; }

    protected override void Update(float dt, double t)
    {
        double e = t - Started;
        if (e >= 30) { End(); return; }
        _moles.RemoveAll(m => t > m.DownAt + 0.25);
        if (t > _nextSpawn)
        {
            _nextSpawn = t + Math.Max(0.32, 0.75 - e * 0.015);
            var busy = _moles.Select(m => m.Hole).ToHashSet();
            var free = Enumerable.Range(0, 9).Where(h => !busy.Contains(h)).ToList();
            if (free.Count > 0)
            {
                double r = Rng.NextDouble();
                _moles.Add(new Mole { Hole = free[Rng.Next(free.Count)], Kind = r < 0.1 ? 1 : r < 0.3 ? 2 : 0, UpAt = t, DownAt = t + Math.Max(0.6, 1.25 - e * 0.02) });
            }
        }
        _pops.RemoveAll(p => t - p.at > 0.8);
    }

    static float Height(Mole m, double t)
    {
        if (m.HitAt is double h) return (float)Math.Max(0, 1 - (t - h) / 0.2);
        return (float)Math.Min(Math.Min(1, (t - m.UpAt) / 0.15), Math.Clamp((m.DownAt - t) / 0.15, 0, 1));
    }

    protected override void DrawGame(double t)
    {
        for (int i = 0; i < 9; i++)
        {
            var r = Screen(i);
            Fill(C(0x3A3F55), r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8, 8);
            Fill(C(0x0F1117), r.X, r.Y, r.Width, r.Height, 5);
            Fill(C(0x3A3F55), r.X + r.Width / 2 - 8, r.Y - 12, 16, 8);
            Text("~ $", r.X + 5, r.Bottom - 14, 8.5f, C(0x7CF29A, 0.6), false, true);
        }
        foreach (var m in _moles)
        {
            float h = Height(m, t);
            if (h < 0.01f) continue;
            var r = Screen(m.Hole);
            var st = F.G.Save();
            F.G.SetClip(RoomForm.R(r.X, r.Y + B, r.Width, r.Height));
            float cx = r.X + r.Width / 2, cy = r.Y - 16 + h * 42;
            if (m.Kind == 2)
            {
                var sp = base.Screen(cx, cy);
                Renderer.Sparkle(F.G, Palette.Spark, sp.X, sp.Y, 14);
                Text("feature", cx - 18, cy - 22, 9, Color.White);
            }
            else DrawBugSprite(cx, cy, m.Kind == 1, t, 1.4f);
            F.G.Restore(st);
        }
        foreach (var p in _pops)
        {
            float ph = (float)(t - p.at) / 0.8f;
            Text(p.text, p.p.X - 10, p.p.Y + ph * 24, 16, Palette.Alpha(p.text.StartsWith("-") ? C(0xFF6B6B) : Palette.Spark, 1 - ph));
        }
        DrawBuddy(Anim.GetPose(1, 0.3f), 66, 100, 0.9f);
        if (!Over && t - Started < 3)
        {
            Text("Bonk bugs (+1),", 18, 40, 11, Color.White, false);
            Text("golden (+3).", 18, 26, 11, Color.White, false);
            Text("Spare features (−2)!", 18, 12, 11, C(0xFFB3B3), false);
        }
        var mp = Mouse();
        if (!Over && mp.X > -900 && mp.Y > 0)
        {
            float swing = (float)Math.Max(0, 1 - (t - _swingAt) / 0.15);
            var at = base.Screen(mp.X, mp.Y);
            var st = F.G.Save();
            F.G.TranslateTransform(at.X, at.Y);
            F.G.RotateTransform(-(0.5f - swing * 0.9f) * 180 / (float)Math.PI);
            using (var b = new SolidBrush(C(0x8A5A36))) F.G.FillRectangle(b, -2, -26, 4, 26);
            using (var b = new SolidBrush(C(0xC0504D))) using (var path = CornerWindow.RoundRect(new RectangleF(-11, -34, 22, 12), 3)) F.G.FillPath(b, path);
            F.G.Restore(st);
        }
    }

    protected override void Click(PointF p)
    {
        double t = Clock.Now;
        _swingAt = t;
        var m = _moles.FirstOrDefault(x => Screen(x.Hole).Contains(p) && x.HitAt == null && Height(x, t) > 0.35f);
        if (m == null) return;
        m.HitAt = t; m.DownAt = t;
        int pts = new[] { 1, 3, -2 }[m.Kind];
        Score = Math.Max(0, Score + pts);
        _pops.Add((pts > 0 ? $"+{pts}" : $"{pts}", p, t));
        if (m.Kind == 2) { Synth.Ouch(); Anim.Grumpy(); } else { Synth.Whack(); if (m.Kind == 1) Anim.Celebrate(); else Anim.Pet(); }
    }
}

// MARK: - Buddy Hop

class HopGame : MiniGame
{
    class Wall { public float X, GapY; public bool Passed; }
    float _y = 150, _vy;
    readonly List<Wall> _walls = new();
    bool _waiting = true;
    const float Bx = 110, Gap = 104, WallW = 50, FloorY = 22;
    static float CeilY => GH - 40;

    public HopGame(RoomForm f, Animator a) : base("hop", "🪽 BUDDY HOP", f, a) { }
    protected override string RightText => $"Best {Best}";
    protected override string OverTitle => "Merge conflict!";

    protected override void Reset() { _y = 150; _vy = 0; _walls.Clear(); _waiting = true; }

    void Flap() { if (Over) return; _waiting = false; _vy = 330; Synth.Flap(); }

    protected override void Update(float dt, double t)
    {
        if (_waiting) { _y = 150 + (float)Math.Sin(t * 3) * 6; return; }
        _vy -= 1100 * dt;
        _y += _vy * dt;
        float speed = 150 + Score * 4;
        foreach (var w in _walls) w.X -= speed * dt;
        _walls.RemoveAll(w => w.X < -WallW);
        if (_walls.Count == 0 || _walls[^1].X < GW - 200)
            _walls.Add(new Wall { X = GW + 10, GapY = FloorY + 70 + (float)Rng.NextDouble() * (CeilY - 70 - (FloorY + 70)) });
        foreach (var w in _walls.Where(w => !w.Passed && w.X + WallW < Bx - 12)) { w.Passed = true; Score++; Synth.Coin(); }
        const float r = 13;
        bool hit = _y - r < FloorY || _y + r > CeilY;
        foreach (var w in _walls)
            if (Bx + r > w.X && Bx - r < w.X + WallW && (_y - r < w.GapY - Gap / 2 || _y + r > w.GapY + Gap / 2)) hit = true;
        if (hit) { Anim.Dizzy(); Synth.Thud(); End(); }
    }

    protected override void DrawGame(double t)
    {
        for (int k = 0; k < 14; k++)
        {
            float ly = FloorY + 10 + k * 17, off = (float)((t * 30 + k * 53) % 520);
            Fill(Color.FromArgb(10, 255, 255, 255), GW - off, ly, 40 + (k * 31) % 90, 5, 2.5f);
        }
        Fill(C(0x3A3F55), 0, 0, GW, FloorY);
        foreach (var w in _walls)
        {
            var top = new RectangleF(w.X, w.GapY + Gap / 2, WallW, CeilY - (w.GapY + Gap / 2));
            var bottom = new RectangleF(w.X, FloorY, WallW, w.GapY - Gap / 2 - FloorY);
            foreach (var (r, col, label) in new[] { (top, C(0xC0504D), "<<<<<<<"), (bottom, C(0x4E9F55), ">>>>>>>") })
            {
                if (r.Height <= 0) continue;
                Fill(col, r.X, r.Y, r.Width, r.Height, 4);
                for (float ly = r.Y + 8; ly < r.Bottom - 6; ly += 10) Fill(Color.FromArgb(46, 255, 255, 255), r.X + 6, ly, 14 + (int)ly % 22, 3, 1.5f);
                Text(label, r.X + 3, label.StartsWith("<") ? r.Y + 2 : r.Bottom - 14, 9, Color.White, true, true);
            }
        }
        var pose = Anim.GetPose(1, Math.Clamp(_vy / 300, -1, 1));
        pose.Lift = 0;
        if (!_waiting) pose.Rot += Math.Clamp(_vy / 600, -0.6f, 0.5f);
        if (!_waiting && _vy > 150) pose.Expr = Expr.Whee;
        DrawBuddy(pose, Bx, _y, 0.55f);
        if (_waiting) Centered("Click or press Space to flap", 90, 13, Color.White);
    }

    protected override void Click(PointF p) => Flap();
    public override bool KeyDown(Keys k) { if (k is Keys.Space or Keys.Up) { Flap(); return true; } return false; }
}

// MARK: - Type Rush

class TypeRushGame : MiniGame
{
    class Word { public string Text; public float X, Y; }
    static readonly string[] Pool = { "let", "var", "func", "async", "await", "return", "struct", "enum", "import", "class", "guard", "defer",
        "git push", "git pull", "commit", "rebase", "merge", "deploy", "npm i", "mkdir", "grep", "sudo", "claude", "cookie", "debug",
        "refactor", "ship it", "null", "true", "false", "while", "json", "yaml", "docker", "lint", "test", "build", "hotfix", "main",
        "branch", "lambda", "regex", "cache", "query", "token", "buddy" };
    readonly List<Word> _words = new();
    readonly List<(string text, PointF p, double at)> _bursts = new();
    string _typed = "";
    int _lives = 3;
    double _nextSpawn, _hurtAt = -100;
    const float DangerY = 56;

    public TypeRushGame(RoomForm f, Animator a) : base("type", "⌨️ TYPE RUSH", f, a) { }
    protected override string RightText => new string('♥', Math.Max(0, _lives)) + new string('♡', Math.Max(0, 3 - _lives));
    protected override string OverTitle => "Segfault!";
    protected override string OverLine => $"{Score} words";

    protected override void Reset() { _words.Clear(); _bursts.Clear(); _typed = ""; _lives = 3; _nextSpawn = Clock.Now + 0.5; }

    float WordW(string s) => F.TextWidth(s, 14, true, true);

    protected override void Update(float dt, double t)
    {
        if (t > _nextSpawn)
        {
            _nextSpawn = t + Math.Max(0.85, 2.2 - Score * 0.06);
            var w = Pool[Rng.Next(Pool.Length)];
            _words.Add(new Word { Text = w, X = 14 + (float)Rng.NextDouble() * (GW - WordW(w) - 28), Y = GH - 50 });
        }
        float speed = 20 + Score * 1.1f;
        foreach (var w in _words) w.Y -= speed * dt;
        int landed = _words.RemoveAll(w => w.Y < DangerY);
        if (landed > 0)
        {
            _lives -= landed; _hurtAt = t;
            Synth.Ouch(); Anim.Grumpy();
            if (!_words.Any(w => w.Text.StartsWith(_typed))) _typed = "";
            if (_lives <= 0) { _lives = 0; End(); }
        }
        _bursts.RemoveAll(b => t - b.at > 0.7);
    }

    protected override void DrawGame(double t)
    {
        if (t - _hurtAt < 0.4) Fill(C(0xE5484D, 0.25 * (1 - (t - _hurtAt) / 0.4)), 0, 0, GW, GH);
        Fill(C(0xE5484D, 0.5), 0, DangerY - 2, GW, 2);
        foreach (var w in _words)
        {
            bool match = _typed.Length > 0 && w.Text.StartsWith(_typed);
            string done = match ? _typed : "", rest = w.Text[done.Length..];
            float dw = done.Length > 0 ? WordW(done) : 0;
            Fill(match ? C(0x3F7FE0, 0.35) : Color.FromArgb(20, 255, 255, 255), w.X - 5, w.Y - 3, WordW(w.Text) + 10, 22, 6);
            if (done.Length > 0) Text(done, w.X, w.Y, 14, Palette.Spark, true, true);
            Text(rest, w.X + dw, w.Y, 14, Color.White, true, true);
        }
        foreach (var b in _bursts)
        {
            float ph = (float)(t - b.at) / 0.7f;
            Text(b.text, b.p.X, b.p.Y + ph * 20, 13, C(0x7CF29A, 1 - ph), true, true);
        }
        Fill(C(0x0F1117), 110, 12, 240, 28, 8);
        Text("> " + _typed + (t % 1 < 0.5 ? "_" : " "), 120, 18, 14, C(0x7CF29A), true, true);
        var pose = Anim.GetPose(0.6f, -0.4f);
        pose.Lift = Math.Min(pose.Lift, 6);
        DrawBuddy(pose, 64, 28, 0.6f);
        if (!Over && t - Started < 3) Centered("Type the words before they hit the red line!", 150, 12.5f, Color.White);
    }

    public override bool KeyDown(Keys k)
    {
        if (k == Keys.Back) { if (_typed.Length > 0) _typed = _typed[..^1]; return true; }
        if (k == Keys.Enter) { _typed = ""; return true; }
        return false;
    }

    public override bool Char(char ch)
    {
        if (Over) return false;
        char c = char.ToLowerInvariant(ch);
        if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '.')) return false;
        var next = _typed + c;
        if (_words.Any(w => w.Text.StartsWith(next)))
        {
            _typed = next;
            Synth.TypeKey();
            var hit = _words.FirstOrDefault(w => w.Text == _typed);
            if (hit != null)
            {
                _words.Remove(hit);
                Score++;
                _bursts.Add(("✓ " + hit.Text, new PointF(hit.X, hit.Y), Clock.Now));
                Synth.CatchBug(hit.Text.Length >= 7);
                if (Score % 5 == 0) Anim.Celebrate();
                _typed = "";
            }
        }
        else { Synth.Grumble(); _typed = ""; }
        return true;
    }
}
