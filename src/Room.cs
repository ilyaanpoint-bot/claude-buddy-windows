// Buddy's room: a little world where buddy walks around, works at its desk while Claude
// Code runs, sleeps in bed, and where you change outfits, type console commands and play
// Bug Catch. Layout is in y-up room units (like the Mac version); Y() / R() flip to the screen.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeBuddy;

class RoomForm : Form
{
    public enum Overlay { None, Wardrobe, Console, Game }
    enum Place { Floor, Desk, Bed }

    const float W = 460, H = 340, HudH = 44, FloorTop = 124, GroundY = 95, RoomScale = 0.7f;
    const float DeskX = 365, BedX = 72;

    record Theme(Color Wall, Color WallLine, Color Floor, Color FloorLine, Color Base);

    static Color C(uint hex, double a = 1) => Palette.Rgb(hex, (int)(a * 255));

    static Theme ThemeOf(string id) => id switch
    {
        "greenhouse" => new(C(0xCFE8D2), C(0xA9D3B0), C(0xD9D2C3), C(0xC2B9A6), C(0x6E9C74)),
        "space" => new(C(0x3B4256), C(0x4C556E), C(0x2A2F3D), C(0x3D455A), C(0x596178)),
        "beach" => new(C(0xF6E7CB), C(0xE8D3AE), C(0xF0D9A6), C(0xE0C48A), C(0xC9A877)),
        _ => new(C(0xF3E3C8), C(0xEAD6B4), C(0xB98556), C(0xA27045), C(0x8A5A36)),
    };

    readonly Animator _anim;
    readonly Timer _timer = new() { Interval = 16 };
    readonly TextBox _input = new() { BorderStyle = BorderStyle.None, Visible = false };
    static readonly Random Rng = new();
    public Action OnPoke, OnFeed, OnMedicine, OnCopycat;
    public Func<string, IEnumerable<string>> OnCommand;
    public Action<int> OnGameOver;

    public Overlay Current { get; private set; } = Overlay.None;
    float _bx = 240, _targetX = 240;
    Place _place = Place.Floor;
    double _nextWander = Clock.Now + 4, _speechUntil = -100, _lastFrame = Clock.Now, _toastUntil = -100, _plantWiggleAt = -100, _catHeartAt = -100;
    string _arriveLine, _speech = "", _toast = "";
    float? _cookieY;
    bool _lampOn = true, _musicOn;
    int _wardrobeTab;
    readonly List<string> _consoleLines = new() { "Buddy OS 1.0 — type 'help'" };

    struct Bug { public float X, Y, Vy; public int Kind; }   // 0 bug, 1 golden, 2 error
    readonly List<Bug> _bugs = new();
    double _gameStart, _nextSpawn;
    int _gameScore;
    bool _gameEnded, _left, _right;
    float _gameX = 230;
    readonly List<(string text, float x, float y, double at)> _popups = new();

    Graphics _g;
    static Progress P => Progress.Shared;
    float Sc => DeviceDpi / 96f;

    public RoomForm(Animator anim)
    {
        _anim = anim;
        Text = "Buddy's Room";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size((int)(W * Sc), (int)(H * Sc));
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = C(0x1E1B2E);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _input.Font = new Font("Consolas", 12, GraphicsUnit.Pixel);
        _input.BackColor = C(0x0F1117);
        _input.ForeColor = C(0x7CF29A);
        _input.Bounds = Scaled(R(128, 60, 310, 18));
        _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); } };
        Controls.Add(_input);

        _timer.Tick += (_, _) => Invalidate();
        VisibleChanged += (_, _) => { if (Visible) _timer.Start(); else _timer.Stop(); };
        // Closing just hides the room, so buddy's spot and toggles are kept for next time.
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Click_(Logical(e.Location)); };
    }

    Rectangle Scaled(RectangleF r) => Rectangle.Round(new RectangleF(r.X * Sc, r.Y * Sc, r.Width * Sc, r.Height * Sc));

    /// A client-area point in room units (y up).
    PointF Logical(Point p) => new(p.X / Sc, H - p.Y / Sc);

    PointF Mouse()
    {
        var p = PointToClient(Cursor.Position);
        return ClientRectangle.Contains(p) ? Logical(p) : new PointF(-999, -999);
    }

    static bool In(RectangleF yUp, PointF p) => p.X >= yUp.X && p.X <= yUp.Right && p.Y >= yUp.Y && p.Y <= yUp.Bottom;

    public void Say(string text, double seconds = 3.5)
    {
        _speech = text;
        _speechUntil = Clock.Now + seconds;
        Synth.Babble(text);
        _anim.Talk(Math.Clamp(text.Length / 3, 2, 9) * 0.072);
    }

    public void ShowToast(string text) { _toast = text; _toastUntil = Clock.Now + 4.5; }

    public void Open(Overlay o)
    {
        Current = Current == o ? Overlay.None : o;
        _input.Visible = Current == Overlay.Console;
        if (Current == Overlay.Console) _input.Focus(); else ActiveControl = null;
        if (Current == Overlay.Game) StartGame();
    }

    // MARK: Buddy behaviour

    void UpdateBuddy(float dt)
    {
        double t = Clock.Now;
        if (_anim.Asleep || _anim.Sick) _targetX = BedX;
        else if (_anim.Mood != Mood.Idle) _targetX = DeskX;
        else if (_place != Place.Floor) { _targetX = 240; _place = Place.Floor; }
        else if (t > _nextWander)
        {
            _nextWander = t + 5 + Rng.NextDouble() * 5;
            var spots = new List<(float x, string line)> { (150 + (float)Rng.NextDouble() * 170, null), (235, null), (150 + (float)Rng.NextDouble() * 170, null) };
            if (P.Has("plant")) spots.Add((178, "*waters plant* 🌱"));
            if (P.Has("fishtank")) spots.Add((211, "blub blub 🐟"));
            if (P.Has("arcade")) spots.Add((300, "one more game?"));
            var pick = spots[Rng.Next(spots.Count)];
            _targetX = pick.x;
            _arriveLine = pick.line != null && Rng.Next(2) == 0 ? pick.line : null;
        }

        float d = _targetX - _bx, step = 70 * dt;
        if (Math.Abs(d) > step) _bx += d > 0 ? step : -step;
        else
        {
            _bx = _targetX;
            if (_anim.Asleep || _anim.Sick) _place = Place.Bed; else if (_anim.Mood != Mood.Idle) _place = Place.Desk;
            if (_arriveLine is string line) { _arriveLine = null; Say(line); if (line.Contains("plant")) _plantWiggleAt = t; }
        }
        if (Math.Abs(d) > step && _place != Place.Floor) _place = Place.Floor;
    }

    bool Walking => Math.Abs(_targetX - _bx) > 1;

    PointF BuddyCenter() => _place switch
    {
        Place.Desk => new(_bx, 140),
        Place.Bed => new(44, 100),
        _ => new(_bx, GroundY),
    };

    static bool IsNight { get { int h = DateTime.Now.Hour; return h >= 20 || h < 6; } }

    // MARK: Drawing helpers (y-up room units)

    static float Y(float y) => H - y;
    static PointF Pt(float x, float y) => new(x, H - y);
    static RectangleF R(float x, float y, float w, float h) => new(x, H - y - h, w, h);

    void Fill(Color c, float x, float y, float w, float h, float radius = 0)
    {
        using var b = new SolidBrush(c);
        var r = R(x, y, w, h);
        radius = Math.Min(radius, Math.Min(w, h) / 2);
        if (radius <= 0.5f) { _g.FillRectangle(b, r); return; }
        using var path = CornerWindow.RoundRect(r, radius);
        _g.FillPath(b, path);
    }

    void Oval(Color c, float x, float y, float w, float h)
    {
        using var b = new SolidBrush(c);
        _g.FillEllipse(b, R(x, y, w, h));
    }

    static Font MakeFont(float size, bool bold, bool mono = false) =>
        new(mono ? "Consolas" : "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    /// Draws text whose bottom-left corner is at (x, y).
    void DrawText(string s, float x, float y, float size, Color c, bool bold = false, bool mono = false)
    {
        using var f = MakeFont(size, bold, mono);
        using var b = new SolidBrush(c);
        _g.DrawString(s, f, b, x, Y(y) - size * 1.3f);
    }

    float TextWidth(string s, float size, bool bold = false)
    {
        using var f = MakeFont(size, bold);
        return _g.MeasureString(s, f).Width;
    }

    void Line(Color c, float width, params (float x, float y)[] pts)
    {
        using var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        _g.DrawLines(pen, pts.Select(p => Pt(p.x, p.y)).ToArray());
    }

    void Polygon(Color c, params (float x, float y)[] pts)
    {
        using var b = new SolidBrush(c);
        _g.FillPolygon(b, pts.Select(p => Pt(p.x, p.y)).ToArray());
    }

    void DrawBuddy(Pose pose, PointF c, float scale, bool shadow) =>
        Renderer.Draw(_g, P.Data.Skin, P.Data.Hat, pose, Pt(c.X, c.Y), scale, shadow);

    // MARK: Drawing

    protected override void OnPaint(PaintEventArgs e)
    {
        _g = e.Graphics;
        _g.SmoothingMode = SmoothingMode.AntiAlias;
        _g.TextRenderingHint = TextRenderingHint.AntiAlias;
        _g.ScaleTransform(Sc, Sc);
        double t = Clock.Now;
        float dt = (float)Math.Min(0.05, t - _lastFrame);
        _lastFrame = t;
        var th = ThemeOf(P.Data.Room);

        if (Current == Overlay.Game) UpdateGame(dt); else UpdateBuddy(dt);

        DrawWall(th);
        DrawWindow(180, 180, 110, 110);
        DrawWallDecor();
        DrawFloor(th);
        DrawBed();
        DrawFurniture();

        if (Current != Overlay.Game)
        {
            var m = Mouse();
            float lx = 0, ly = 0;
            if (m.X > -999)
            {
                float vx = m.X - _bx, vy = m.Y - GroundY, len = Math.Max(80, (float)Math.Sqrt(vx * vx + vy * vy));
                lx = vx / len; ly = vy / len;
            }
            var pose = _anim.GetPose(lx, ly);
            if (Walking && !_anim.Asleep)
            {
                pose.Lift += (float)Math.Abs(Math.Sin(t * 12)) * 4;
                pose.LookX = _targetX > _bx ? 1 : -1; pose.LookY = 0;
            }
            if (_place == Place.Bed) pose.Lying = true;
            var c = BuddyCenter();
            if (_place == Place.Desk) DrawBuddy(pose, c, RoomScale, false);
            DrawDesk();
            if (_place != Place.Desk) DrawBuddy(pose, c, RoomScale, _place == Place.Floor);
            if (_place == Place.Bed) DrawBlanket();
            if (_place == Place.Desk && _anim.Mood == Mood.Working) TypingParticles(c);
            DrawCat();
            DrawCookie(c);
            if (t < _speechUntil) DrawSpeech(new PointF(c.X, c.Y + 30 + pose.Lift * RoomScale));
        }
        else { DrawDesk(); DrawCat(); }

        if (!_lampOn || IsNight || P.Data.Room == "space")
            Fill(Color.FromArgb(_lampOn ? 46 : 97, 13, 15, 51), 0, HudH, W, H - HudH);

        switch (Current)
        {
            case Overlay.Wardrobe: DrawWardrobe(); break;
            case Overlay.Console: DrawConsole(); break;
            case Overlay.Game: DrawGame(); break;
        }
        DrawHud();
        if (t < _toastUntil) DrawToast();
        _g = null;
    }

    void DrawWall(Theme th)
    {
        Fill(th.Wall, 0, FloorTop, W, H - FloorTop);
        var line = th.WallLine;
        switch (P.Data.Room)
        {
            case "greenhouse":
                for (float x = 0; x < W; x += 46) Fill(line, x, FloorTop, 3, H);
                Fill(line, 0, 300, W, 3);
                break;
            case "space":
                for (float x = 0; x < W; x += 92) Fill(line, x, FloorTop, 2, H);
                for (float x = 8; x < W; x += 92) foreach (var y in new[] { FloorTop + 10, H - 12 }) Oval(C(0x6A7390), x, y, 3, 3);
                break;
            case "beach":
                for (float y = FloorTop + 18; y < H; y += 22) Fill(line, 0, y, W, 2);
                break;
            default:
                for (float x = 10; x < W; x += 28) Fill(line, x, FloorTop, 9, H);
                break;
        }
    }

    void DrawFloor(Theme th)
    {
        Fill(th.Floor, 0, HudH, W, FloorTop - HudH);
        var line = th.FloorLine;
        switch (P.Data.Room)
        {
            case "beach":
                for (int i = 0; i < 40; i++) Oval(line, (i * 97) % 460, HudH + (i * 53) % 78, 3, 2);
                Oval(C(0xF4A6B8), 160, 52, 9, 6);
                break;
            case "space": case "greenhouse":
                for (float x = 0; x < W; x += 40) Fill(line, x, HudH, 1.5f, FloorTop - HudH);
                for (float y = HudH; y < FloorTop; y += 26) Fill(line, 0, y, W, 1.5f);
                break;
            default:
                for (float y = HudH + 16; y < FloorTop; y += 16) Fill(line, 0, y, W, 1.5f);
                break;
        }
        Fill(th.Base, 0, FloorTop - 4, W, 6);
        if (P.Has("rug"))
        {
            Fill(C(0xC0504D, 0.85), 150, 54, 170, 30, 15);
            using var pen = new Pen(C(0xF2C94C, 0.8), 2);
            using var path = CornerWindow.RoundRect(R(156, 58, 158, 22), 11);
            _g.DrawPath(pen, path);
        }
    }

    void DrawWindow(float x, float y, float w, float h)
    {
        double t = Clock.Now;
        int hour = DateTime.Now.Hour;
        if (P.Data.Room == "space")
        {
            float cx = x + w / 2 - 52, cy = y + h / 2 - 52;
            Oval(C(0x0B0D1F), cx, cy, 104, 104);
            var st = _g.Save();
            using (var clip = new GraphicsPath()) { clip.AddEllipse(R(cx, cy, 104, 104)); _g.SetClip(clip); }
            for (int i = 0; i < 30; i++)
                Oval(Color.FromArgb((int)(255 * (0.4 + 0.5 * Math.Abs(Math.Sin(t + i)))), 255, 255, 255), cx + (i * 37) % 104, cy + (i * 61) % 104, 2, 2);
            Oval(C(0x3F7FE0), cx + 30 + (float)Math.Sin(t * 0.1) * 6, cy - 30, 90, 90);
            Fill(C(0x5DBB63), cx + 50, cy + 20, 30, 18, 9);
            _g.Restore(st);
            using var ring = new Pen(C(0x8A93AE), 7);
            _g.DrawEllipse(ring, R(cx, cy, 104, 104));
            return;
        }
        bool night = hour >= 20 || hour < 6, dusk = hour >= 17 && hour < 20;
        var top = night ? C(0x0E1640) : dusk ? C(0xF08A5D) : C(0x7EC8F2);
        var bottom = night ? C(0x26306B) : dusk ? C(0xF7C873) : C(0xCDEBFA);
        var r = R(x, y, w, h);
        using (var grad = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical)) _g.FillRectangle(grad, r);
        var saved = _g.Save();
        _g.SetClip(r);
        if (night)
        {
            for (int i = 0; i < 14; i++)
                Oval(Color.FromArgb((int)(255 * (0.5 + 0.5 * Math.Abs(Math.Sin(t * 1.3 + i)))), 255, 255, 255), x + (i * 41) % 104, y + 30 + (i * 29) % 76, 2, 2);
            Oval(C(0xF7F3D7), x + w - 34, y + h - 32, 18, 18);
        }
        else
        {
            Oval(C(0xFFE07A), x + w - 36, y + h - 34, 20, 20);
            float cloud = x + (float)(t * 6 % 160) - 30;
            foreach (var (dx, dy, s) in new (float, float, float)[] { (0, 0, 16), (10, 4, 18), (22, 0, 14) })
                Oval(Color.FromArgb(230, 255, 255, 255), cloud + dx, y + h - 50 + dy, s, s * 0.8f);
        }
        if (P.Data.Room == "beach")
        {
            Fill(night ? C(0x1B3A6B) : C(0x2E9CCA), x, y, w, 34);
            for (int i = 0; i < 4; i++) Fill(Color.FromArgb(128, 255, 255, 255), x + (float)((t * 10 + i * 30) % 120) - 10, y + 8 + (i % 2) * 12, 14, 1.5f);
        }
        else
        {
            Fill(night ? C(0x1E3B2C) : C(0x6DBE6A), x - 10, y - 30, 80, 54, 27);
            Fill(night ? C(0x234A33) : C(0x84CF7A), x + 50, y - 34, 90, 52, 26);
        }
        _g.Restore(saved);
        var frame = P.Data.Room == "greenhouse" ? C(0x6E9C74) : Color.White;
        using (var pen = new Pen(frame, 6)) _g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        using (var pen = new Pen(frame, 4))
        {
            _g.DrawLine(pen, Pt(x + w / 2, y), Pt(x + w / 2, y + h));
            _g.DrawLine(pen, Pt(x, y + h / 2), Pt(x + w, y + h / 2));
        }
        Fill(frame, x - 8, y - 8, w + 16, 6, 2);
    }

    void DrawWallDecor()
    {
        double t = Clock.Now;
        if (P.Has("poster"))
        {
            Fill(C(0x2B2D42), 34, 186, 66, 88, 3);
            Renderer.Draw(_g, "classic", "none", new Pose(), Pt(67, 240), 0.5f, false);
            DrawText("SHIP IT", 43, 192, 12, Palette.Spark, true);
        }
        if (P.Has("neon"))
        {
            double glow = 0.75 + 0.25 * Math.Sin(t * 3);
            for (int k = 3; k >= 1; k--) DrawText("</>", 112 - k, 270 - k, 26, C(0xFF4FA3, glow * 0.12), true, true);
            for (int k = 3; k >= 1; k--) DrawText("</>", 112 + k, 270 + k, 26, C(0xFF4FA3, glow * 0.12), true, true);
            DrawText("</>", 112, 270, 26, C(0xFF7FC1, glow), true, true);
        }
        if (P.Has("shelf"))
        {
            Fill(C(0x8A5A36), 330, 222, 118, 6, 2);
            foreach (var (bx, col, bh) in new (float, uint, float)[] { (334, 0xC0504D, 26), (344, 0x3F7FE0, 30), (355, 0x5DBB63, 24), (364, 0xF2C94C, 28), (375, 0x9466DB, 22) })
                Fill(C(col), bx, 228, 9, bh, 1);
            if (P.Has("records"))
            {
                Fill(C(0x4A3B33), 394, 228, 48, 10, 2);
                double spin = _musicOn ? t * 4 : 0;
                Fill(C(0x111111), 400, 232, 30, 9, 4.5f);
                Fill(C(0xC0504D), 412 + (float)Math.Cos(spin) * 2, 235, 6, 3, 1.5f);
                if (_musicOn)
                    for (int k = 0; k < 3; k++)
                    {
                        float ph = (float)((t * 0.6 + k / 3.0) % 1);
                        DrawText(k % 2 == 0 ? "♪" : "♫", 410 + ph * 20 + (float)Math.Sin(t * 3 + k) * 6, 244 + ph * 40, 13, C(0x9466DB, 1 - ph), true);
                    }
            }
        }
        if (P.Data.Room == "greenhouse")
        {
            foreach (var (px, len) in new (float, float)[] { (30, 40), (130, 28), (320, 34) })
            {
                Line(C(0x6E9C74), 1.5f, (px + 10, H), (px + 10, H - len));
                Fill(C(0xC8734E), px, H - len - 14, 20, 14, 3);
                for (int k = 0; k < 4; k++)
                {
                    float sway = (float)Math.Sin(t * 1.2 + k) * 2;
                    Oval(C(0x4E9F55), px - 6 + k * 8 + sway, H - len - 30 + (k % 2) * 4, 8, 18);
                }
            }
        }
        if (P.Data.Room == "beach")
        {
            Fill(C(0x8A5A36), 18, 124, 6, 70);
            using var leafBrush = new SolidBrush(C(0x4E9F55));
            for (int k = 0; k < 5; k++)
            {
                double a = k / 4.0 * Math.PI;
                var st = _g.Save();
                var at = Pt(21 + (float)Math.Cos(a) * 18, 196 + (float)Math.Sin(a) * 8);
                _g.TranslateTransform(at.X, at.Y);
                _g.RotateTransform(-(float)(a * 180 / Math.PI));
                _g.FillEllipse(leafBrush, -20, -5, 40, 10);
                _g.Restore(st);
            }
        }
    }

    void DrawBed()
    {
        Fill(C(0x7A4E2D), 14, 60, 8, 60, 2);
        Fill(C(0x7A4E2D), 14, 60, 126, 10, 2);
        Fill(C(0x7A4E2D), 134, 60, 6, 34, 2);
        Fill(Color.White, 20, 70, 116, 22, 6);
        Fill(C(0xF5F0E6), 22, 90, 36, 14, 7);
        if (_place != Place.Bed || Current == Overlay.Game) DrawBlanket();
    }

    void DrawBlanket()
    {
        Fill(C(0x5B7FD6), 62, 70, 76, 28, 8);
        for (float x = 70; x < 134; x += 16) Fill(C(0x7E9DE6), x, 72, 6, 24);
    }

    void DrawFurniture()
    {
        double t = Clock.Now;
        if (P.Has("plant"))
        {
            float wig = (float)(Math.Sin((t - _plantWiggleAt) * 20) * 3 * Math.Max(0, 1 - (t - _plantWiggleAt)));
            using var leafBrush = new SolidBrush(C(0x4E9F55));
            foreach (var (dx, lh, a) in new (float, float, float)[] { (-8, 34, -0.35f), (0, 44, 0), (8, 36, 0.35f), (-3, 26, -0.7f), (4, 28, 0.7f) })
            {
                var st = _g.Save();
                var at = Pt(162 + dx + wig, 80);
                _g.TranslateTransform(at.X, at.Y);
                _g.RotateTransform(-(a + wig * 0.03f) * 180 / (float)Math.PI);
                _g.FillEllipse(leafBrush, -5, -lh, 10, lh);
                _g.Restore(st);
            }
            Fill(C(0xC8734E), 150, 56, 26, 26, 4);
            Fill(C(0xB0613F), 147, 76, 32, 7, 2);
        }
        if (P.Has("fishtank"))
        {
            Fill(C(0x6B4A33), 188, 56, 48, 40, 3);
            Fill(C(0x7FD3F0, 0.75), 186, 96, 52, 34, 3);
            Fill(C(0xE8D3AE), 186, 96, 52, 6, 2);
            for (int k = 0; k < 2; k++)
                Fill(k == 0 ? C(0xF29A38) : C(0xE5395B), 196 + (float)(t * (12 + k * 7) % 34), 108 + k * 9, 9, 5, 2.5f);
            Oval(Color.FromArgb(153, 255, 255, 255), 228, 100 + (float)(t * 14 % 30), 3, 3);
        }
        if (P.Has("arcade"))
        {
            Fill(C(0x3B2A6B), 290, 56, 30, 112, 4);
            Fill(C(0x111122), 294, 126, 22, 26, 2);
            Fill(C(0x7CF29A, 0.4 + 0.3 * Math.Abs(Math.Sin(t * 4))), 297, 130, 16, 18, 1);
            Fill(C(0xE5395B), 300, 110, 5, 5, 2.5f);
            Fill(C(0xF2C94C), 308, 110, 5, 5, 2.5f);
        }
    }

    void DrawDesk()
    {
        double t = Clock.Now;
        Fill(C(0x5A3A24), 326, 106, 128, 8, 2);
        Fill(C(0x6B4A33), 330, 56, 10, 52);
        Fill(C(0x6B4A33), 440, 56, 10, 52);
        Fill(C(0x6B4A33), 340, 84, 100, 22);
        Fill(C(0x8A6448), 380, 92, 20, 4, 2);
        // Laptop, seen from behind, with a glowing sticker
        Fill(C(0xBFC4CC), 392, 114, 40, 28, 3);
        double glow = _anim.Mood == Mood.Working ? 0.7 + 0.3 * Math.Abs(Math.Sin(t * 3)) : 0.85;
        var sticker = Pt(412, 128);
        Renderer.Sparkle(_g, Palette.Alpha(Renderer.RayColor(P.Data.Skin, 0, t), glow), sticker.X, sticker.Y, 6);
        if (P.Has("lamp"))
        {
            Fill(C(0x444444), 436, 114, 12, 3, 1);
            Line(C(0x444444), 2.5f, (442, 116), (446, 140), (434, 150));
            Fill(C(0xF2C94C), 424, 144, 18, 9, 4);
            if (_lampOn) Polygon(C(0xFFE9A0, 0.25), (426, 145), (400, 112), (446, 112), (440, 145));
        }
    }

    void DrawCat()
    {
        if (!P.Has("cat")) return;
        double t = Clock.Now;
        const float x = 112, y = 98;
        var cat = C(0x3A3A3A);
        using (var pen = new Pen(cat, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            _g.DrawBezier(pen, Pt(x + 10, y + 2), Pt(x + 20, y), Pt(x + 22, y + 8), Pt(x + 20, y + 14 + (float)Math.Sin(t * 2) * 4));
        Fill(cat, x - 10, y, 22, 16, 8);
        Fill(cat, x - 14, y + 10, 16, 14, 7);
        foreach (var ex in new float[] { -12, -2 }) Polygon(cat, (x + ex, y + 21), (x + ex + 2, y + 28), (x + ex + 5, y + 21));
        bool blink = t % 4 < 0.15;
        Fill(C(0xB7F06A), x - 11, y + 15, 3, blink ? 0.6f : 3, 1);
        Fill(C(0xB7F06A), x - 4, y + 15, 3, blink ? 0.6f : 3, 1);
        if (t - _catHeartAt < 1.5)
        {
            float ph = (float)(t - _catHeartAt) / 1.5f;
            var hp = Pt(x - 6, y + 32 + ph * 18);
            Renderer.Heart(_g, C(0xE5395B, 1 - ph), hp.X, hp.Y, 5);
        }
    }

    void TypingParticles(PointF c)
    {
        double t = Clock.Now;
        string[] glyphs = { "{", "}", ";", "</>" };
        for (int k = 0; k < 4; k++)
        {
            float ph = (float)((t * 0.8 + k / 4.0) % 1);
            DrawText(glyphs[k], c.X + 22 + ph * 14 + k * 4, c.Y + 4 + ph * 34, 10, C(0x3F7FE0, 1 - ph), true, true);
        }
    }

    void DrawCookie(PointF c)
    {
        if (_cookieY is not float y) return;
        y -= 4;
        if (y <= c.Y + 14)
        {
            _cookieY = null;
            OnFeed?.Invoke();
            return;
        }
        _cookieY = y;
        Oval(C(0xD9A066), c.X - 7, y, 14, 14);
        foreach (var (dx, dy) in new (float, float)[] { (3, 4), (8, 9), (5, 10), (9, 3) }) Oval(C(0x5A3A24), c.X - 7 + dx, y + dy - 1, 2.5f, 2.5f);
    }

    void DrawSpeech(PointF pt)
    {
        using var f = MakeFont(11, true);
        float w = Math.Min(220, _g.MeasureString(_speech, f).Width + 16);
        float x = Math.Max(6, Math.Min(W - w - 6, pt.X - w / 2));
        var r = R(x, pt.Y + 4, w, 22);
        using var bg = new SolidBrush(Color.FromArgb(247, 252, 252, 252));
        using (var path = CornerWindow.RoundRect(r, 10)) _g.FillPath(bg, path);
        _g.FillPolygon(bg, new[] { Pt(pt.X - 5, pt.Y + 5), Pt(pt.X, pt.Y - 2), Pt(pt.X + 5, pt.Y + 5) });
        using var tb = new SolidBrush(Color.FromArgb(31, 31, 31));
        using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        _g.DrawString(_speech, f, tb, new RectangleF(r.X + 8, r.Y + 3, r.Width - 10, r.Height - 4), fmt);
    }

    void DrawToast()
    {
        float w = Math.Min(W - 20, TextWidth(_toast, 12.5f, true) + 28);
        double e = Clock.Now - (_toastUntil - 4.5);
        float drop = (float)Math.Max(0, 1 - e / 0.3) * 30;
        float x = (W - w) / 2, y = H - 40 + drop;
        Fill(C(0x2B2D42, 0.95), x, y, w, 28, 14);
        DrawText(_toast, x + 14, y + 5, 12.5f, Color.White, true);
    }

    // MARK: HUD

    static readonly string[] HudButtons = { "Feed", "Play", "Style", "Cmd" };
    static RectangleF HudButton(int i) => new(246 + i * 53, 7, 50, 30);

    void DrawHud()
    {
        Fill(C(0x1E1B2E), 0, 0, W, HudH);
        Fill(Palette.Orange, 10, 11, 44, 22, 11);
        string lv = $"Lv {P.Level}";
        DrawText(lv, 32 - TextWidth(lv, 12, true) / 2, 14, 12, Color.White, true);
        int next = Progress.XpNeeded(P.Level + 1);
        DrawText($"{P.Data.Xp} / {next} XP", 64, 22, 10, Color.FromArgb(191, 191, 191), true);
        Fill(Color.FromArgb(38, 255, 255, 255), 64, 11, 166, 8, 4);
        Fill(Palette.Spark, 64, 11, Math.Max(8, 166 * (float)Math.Clamp(P.LevelFraction, 0, 1)), 8, 4);
        int active = Current switch { Overlay.Wardrobe => 2, Overlay.Console => 3, Overlay.Game => 1, _ => -1 };
        for (int i = 0; i < HudButtons.Length; i++)
        {
            string label = i == 0 && _anim.Sick ? "Meds" : HudButtons[i];
            var r = HudButton(i);
            Fill(active == i ? Palette.Orange : Color.FromArgb(26, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 8);
            DrawText(label, r.X + r.Width / 2 - TextWidth(label, 11, true) / 2, r.Y + 7, 11, Color.White, true);
        }
    }

    // MARK: Wardrobe

    static readonly string[] Tabs = { "Skins", "Hats", "Rooms", "Trophies" };
    static readonly RectangleF WardrobePanel = new(10, 52, 440, 280);
    static RectangleF TabRect(int i) => new(WardrobePanel.X + 10 + i * 84, WardrobePanel.Bottom - 32, 80, 24);
    static RectangleF CellRect(int i) => new(WardrobePanel.X + 10 + (i % 5) * 85, WardrobePanel.Bottom - 130 - (i / 5) * 98, 80, 92);
    static RectangleF CloseRect => new(WardrobePanel.Right - 30, WardrobePanel.Bottom - 32, 26, 26);
    static RectangleF TrophyRect(int i) => new(WardrobePanel.X + 10 + (i % 2) * 214, WardrobePanel.Bottom - 64 - (i / 2) * 23, 206, 20);
    Item[] TabItems => Catalog.Items.Where(x => x.Kind == new[] { ItemKind.Skin, ItemKind.Hat, ItemKind.Room }[Math.Min(_wardrobeTab, 2)]).ToArray();

    void DrawWardrobe()
    {
        Fill(C(0x1E1B2E, 0.94), WardrobePanel.X, WardrobePanel.Y, WardrobePanel.Width, WardrobePanel.Height, 14);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = TabRect(i);
            Fill(i == _wardrobeTab ? Palette.Orange : Color.FromArgb(26, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 12);
            DrawText(Tabs[i], r.X + r.Width / 2 - TextWidth(Tabs[i], 11.5f, true) / 2, r.Y + 4, 11.5f, Color.White, true);
        }
        DrawText("×", WardrobePanel.Right - 23, WardrobePanel.Bottom - 28, 18, Color.FromArgb(204, 204, 204), true);

        var mouse = Mouse();
        if (_wardrobeTab == 3) { DrawTrophies(mouse); return; }
        string hoverHint = null;
        var items = TabItems;
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var r = CellRect(i);
            bool unlocked = P.IsUnlocked(it);
            bool equipped = new[] { P.Data.Skin, P.Data.Hat, P.Data.Room }.Contains(it.Id);
            bool hover = In(r, mouse);
            Fill(Color.FromArgb(hover ? 36 : 18, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 10);
            if (equipped)
            {
                using var pen = new Pen(Palette.Orange, 2);
                using var path = CornerWindow.RoundRect(R(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), 10);
                _g.DrawPath(pen, path);
            }
            var c = new PointF(r.X + r.Width / 2, r.Y + 54);
            if (unlocked || it.Level != null)
            {
                int a = unlocked ? 255 : 64;
                switch (it.Kind)
                {
                    case ItemKind.Skin: DrawPreview(it.Id, "none", c, a); break;
                    case ItemKind.Hat: DrawPreview(P.Data.Skin, it.Id, new PointF(c.X, c.Y - 6), a); break;
                    case ItemKind.Room:
                        var th = ThemeOf(it.Id);
                        Fill(Color.FromArgb(a, th.Wall), c.X - 26, c.Y - 6, 52, 30, 4);
                        Fill(Color.FromArgb(a, th.Floor), c.X - 26, c.Y - 20, 52, 14, 4);
                        break;
                }
            }
            else DrawText("?", c.X - 7, c.Y - 16, 26, Color.FromArgb(89, 255, 255, 255), true);
            string name = unlocked || it.Level != null ? it.Name : "Secret";
            DrawText(name, r.X + r.Width / 2 - TextWidth(name, 10.5f) / 2, r.Y + 17, 10.5f, Color.White);
            string tag = unlocked ? (equipped ? "Wearing" : "") : it.Level is int l ? $"Lv {l}" : "secret";
            if (tag.Length > 0) DrawText(tag, r.X + r.Width / 2 - TextWidth(tag, 9.5f) / 2, r.Y + 4, 9.5f, Color.FromArgb(179, 179, 179));
            if (hover && !unlocked) hoverHint = it.Level is int lvl ? $"Reach level {lvl} to unlock {it.Name}." : $"Hint: {it.Hint}";
        }
        DrawText(hoverHint ?? "Click something you've unlocked to wear it.", WardrobePanel.X + 14, WardrobePanel.Y + 8, 11, Palette.Spark, true);
    }

    /// Draws a buddy preview; a faded one for things that are still locked.
    void DrawPreview(string skin, string hat, PointF c, int alpha)
    {
        if (alpha == 255) { Renderer.Draw(_g, skin, hat, new Pose(), Pt(c.X, c.Y), 0.55f, false); return; }
        using var bmp = new Bitmap(80, 80);
        using (var bg = Graphics.FromImage(bmp))
        {
            bg.SmoothingMode = SmoothingMode.AntiAlias;
            Renderer.Draw(bg, skin, hat, new Pose(), new PointF(40, 44), 0.55f, false);
        }
        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha / 255f };
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        ia.SetColorMatrix(cm);
        var at = Pt(c.X, c.Y);
        _g.DrawImage(bmp, new Rectangle((int)at.X - 40, (int)at.Y - 44, 80, 80), 0, 0, 80, 80, GraphicsUnit.Pixel, ia);
    }

    void DrawTrophies(PointF mouse)
    {
        string hover = null;
        for (int i = 0; i < Catalog.Achievements.Length; i++)
        {
            var a = Catalog.Achievements[i];
            bool got = P.HasAchievement(a.Id);
            var r = TrophyRect(i);
            Fill(Color.FromArgb(got ? 31 : 13, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 6);
            Oval(got ? Palette.Spark : Color.FromArgb(70, 255, 255, 255), r.X + 6, r.Y + 6, 8, 8);
            DrawText(got ? a.Name : "???", r.X + 20, r.Y + 3, 11, got ? Color.White : Color.FromArgb(150, 255, 255, 255), got);
            if (In(r, mouse)) hover = (got ? a.Name : "Locked") + ": " + a.Hint;
        }
        var d = P.Data;
        DrawText(hover ?? $"Sessions {d.Sessions}   Cookies {d.Cookies}   Pets {d.Pets}   Best Bug Catch {d.BestScore}   Best Copycat {d.BestCopycat}",
             WardrobePanel.X + 14, WardrobePanel.Y + 8, 10.5f, Palette.Spark, true);
    }

    // MARK: Console

    static readonly RectangleF ConsoleRect = new(10, 52, 440, 150);

    void DrawConsole()
    {
        Fill(C(0x0F1117, 0.95), ConsoleRect.X, ConsoleRect.Y, ConsoleRect.Width, ConsoleRect.Height, 10);
        uint[] dots = { 0xFF5F57, 0xFEBC2E, 0x28C840 };
        for (int k = 0; k < 3; k++) Oval(C(dots[k]), ConsoleRect.X + 10 + k * 14, ConsoleRect.Bottom - 16, 9, 9);
        var lines = _consoleLines.Skip(Math.Max(0, _consoleLines.Count - 7)).ToList();
        for (int k = 0; k < lines.Count; k++) DrawText(lines[k], ConsoleRect.X + 12, ConsoleRect.Bottom - 38 - k * 15, 11, C(0xCFE8D2), false, true);
        DrawText("buddy@room ~ %", ConsoleRect.X + 12, 60, 12, Palette.Orange, true, true);
    }

    public void SelectTab(int i) => _wardrobeTab = i;

    void Submit()
    {
        var cmd = _input.Text.Trim();
        _input.Clear();
        RunCommand(cmd);
    }

    public void RunCommand(string cmd)
    {
        if (cmd.Length == 0) return;
        _consoleLines.Add("% " + cmd);
        if (cmd == "clear") { _consoleLines.Clear(); return; }
        if (cmd == "exit") { Open(Overlay.Console); return; }
        if (OnCommand != null) _consoleLines.AddRange(OnCommand(cmd));
    }

    // MARK: Bug Catch

    public void StartGame()
    {
        if (Current != Overlay.Game) { Open(Overlay.Game); return; }
        _anim.Asleep = false;
        _bugs.Clear(); _popups.Clear();
        _gameStart = Clock.Now; _gameScore = 0; _gameEnded = false; _nextSpawn = Clock.Now + 0.6; _gameX = 230;
    }

    void UpdateGame(float dt)
    {
        double t = Clock.Now;
        if (_gameEnded) return;
        double elapsed = t - _gameStart;
        if (elapsed >= 30)
        {
            _gameEnded = true;
            Synth.GameOver();
            OnGameOver?.Invoke(_gameScore);
            return;
        }
        // Keys move buddy; otherwise it follows the mouse.
        if (_left) _gameX -= 260 * dt;
        else if (_right) _gameX += 260 * dt;
        else if (Mouse() is var m && m.X > -999) _gameX += (m.X - _gameX) * Math.Min(1, 12 * dt);
        _gameX = Math.Clamp(_gameX, 24, W - 24);

        if (t > _nextSpawn)
        {
            _nextSpawn = t + Math.Max(0.28, 0.7 - elapsed * 0.014);
            double r = Rng.NextDouble();
            _bugs.Add(new Bug { X = 20 + (float)Rng.NextDouble() * (W - 40), Y = H, Vy = (float)(90 + Rng.NextDouble() * 60 + elapsed * 3), Kind = r < 0.08 ? 1 : r < 0.25 ? 2 : 0 });
        }
        for (int i = 0; i < _bugs.Count; i++) { var b = _bugs[i]; b.Y -= b.Vy * dt; _bugs[i] = b; }
        _bugs.RemoveAll(b =>
        {
            if (Math.Abs(b.X - _gameX) < 26 && b.Y < 112 && b.Y > 80)
            {
                int pts = new[] { 1, 5, -3 }[b.Kind];
                _gameScore = Math.Max(0, _gameScore + pts);
                _popups.Add((pts > 0 ? $"+{pts}" : $"{pts}", b.X, b.Y, t));
                if (b.Kind == 2) { Synth.Ouch(); _anim.Grumpy(); }
                else { Synth.CatchBug(b.Kind == 1); if (b.Kind == 1) _anim.Celebrate(); }
                return true;
            }
            return b.Y < HudH;
        });
        _popups.RemoveAll(p => t - p.at > 0.8);
    }

    static readonly RectangleF AgainRect = new(136, 156, 88, 28), DoneRect = new(236, 156, 88, 28);

    void DrawGame()
    {
        double t = Clock.Now;
        Fill(C(0x0F1117, 0.55), 0, HudH, W, H - HudH);
        foreach (var b in _bugs) DrawBug(b, t);
        var pose = _anim.GetPose(0, 1);
        if (_left || _right) pose.Lift += (float)Math.Abs(Math.Sin(t * 12)) * 3;
        DrawBuddy(pose, new PointF(_gameX, GroundY), RoomScale, true);
        foreach (var pop in _popups)
        {
            float ph = (float)(t - pop.at) / 0.8f;
            DrawText(pop.text, pop.x - 8, pop.y + ph * 24, 14, Palette.Alpha(pop.text.StartsWith("-") ? C(0xFF6B6B) : Palette.Spark, 1 - ph), true);
        }
        double left = Math.Max(0, 30 - (t - _gameStart));
        DrawText("BUG CATCH", 14, H - 28, 13, Color.White, true);
        DrawText($"Score {_gameScore}", 190, H - 28, 13, Palette.Spark, true);
        DrawText($"{(_gameEnded ? 0 : left):0}s", W - 46, H - 28, 13, Color.White, true);
        if (!_gameEnded && t - _gameStart < 3)
        {
            DrawText("Catch bugs (+1) and golden bugs (+5). Dodge errors (-3)!", 50, 250, 11.5f, Color.White, true);
            DrawText("Move with the mouse or ← →", 150, 232, 11, Color.FromArgb(204, 204, 204));
        }
        if (_gameEnded)
        {
            Fill(C(0x1E1B2E, 0.97), 120, 140, 220, 130, 14);
            DrawText("Time's up!", 120 + 66, 270 - 32, 16, Color.White, true);
            DrawText($"Score {_gameScore}  ·  Best {P.Data.BestScore}  ·  +{Math.Min(60, _gameScore)} XP", 120 + 22, 270 - 56, 11.5f, Palette.Spark, true);
            Fill(Palette.Orange, AgainRect.X, AgainRect.Y, AgainRect.Width, AgainRect.Height, 8);
            DrawText("Play again", AgainRect.X + 12, AgainRect.Y + 6, 11.5f, Color.White, true);
            Fill(Color.FromArgb(38, 255, 255, 255), DoneRect.X, DoneRect.Y, DoneRect.Width, DoneRect.Height, 8);
            DrawText("Done", DoneRect.X + 28, DoneRect.Y + 6, 11.5f, Color.White, true);
        }
    }

    void DrawBug(Bug b, double t)
    {
        if (b.Kind == 2)
        {
            Oval(C(0xE5484D), b.X - 10, b.Y - 10, 20, 20);
            Line(Color.White, 2.5f, (b.X - 4, b.Y - 4), (b.X + 4, b.Y + 4));
            Line(Color.White, 2.5f, (b.X - 4, b.Y + 4), (b.X + 4, b.Y - 4));
            return;
        }
        var dark = C(0x1E1E1E);
        float wig = (float)Math.Sin(t * 20) * 2;
        for (int k = -1; k <= 1; k++)
        {
            float y = b.Y + k * 4;
            Line(dark, 1.2f, (b.X - 6, y), (b.X - 11, y + wig));
            Line(dark, 1.2f, (b.X + 6, y), (b.X + 11, y - wig));
        }
        Line(dark, 1.2f, (b.X - 2, b.Y - 8), (b.X - 5, b.Y - 13));
        Line(dark, 1.2f, (b.X + 2, b.Y - 8), (b.X + 5, b.Y - 13));
        Fill(b.Kind == 1 ? C(0xF2C94C) : C(0x3E9B4F), b.X - 7, b.Y - 8, 14, 17, 7);
        Fill(dark, b.X - 4.5f, b.Y - 11, 9, 6, 3);
        Line(dark, 1, (b.X, b.Y - 6), (b.X, b.Y + 9));
        if (b.Kind == 1)
        {
            var sp = Pt(b.X + 9, b.Y + 9);
            Renderer.Sparkle(_g, Color.White, sp.X, sp.Y, 3 + (float)Math.Abs(Math.Sin(t * 8)) * 2);
        }
    }

    // MARK: Input

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (Current != Overlay.None) Open(Current); return true; }
        if (Current == Overlay.Game)
        {
            if (keyData == Keys.Left) { _left = true; return true; }
            if (keyData == Keys.Right) { _right = true; return true; }
            if (keyData == Keys.Space) { if (_gameEnded) StartGame(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) _left = false;
        if (e.KeyCode == Keys.Right) _right = false;
        base.OnKeyUp(e);
    }

    protected override void OnDeactivate(EventArgs e) { _left = _right = false; base.OnDeactivate(e); }

    void ShowGameMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Bug Catch", null, (_, _) => { if (Current != Overlay.Game) Open(Overlay.Game); else StartGame(); });
        menu.Items.Add("Copycat", null, (_, _) => OnCopycat?.Invoke());
        var r = Scaled(new RectangleF(HudButton(1).X, H - HudH, 50, 1));
        menu.Show(this, new Point(r.X, r.Y), ToolStripDropDownDirection.AboveRight);
    }

    void Click_(PointF pt)
    {
        double t = Clock.Now;
        for (int i = 0; i < HudButtons.Length; i++)
        {
            if (!In(HudButton(i), pt)) continue;
            switch (i)
            {
                case 0: Feed(); break;
                case 1: ShowGameMenu(); break;
                case 2: Open(Overlay.Wardrobe); break;
                default: Open(Overlay.Console); break;
            }
            return;
        }

        switch (Current)
        {
            case Overlay.Wardrobe:
                if (In(CloseRect, pt)) { Open(Overlay.Wardrobe); return; }
                for (int i = 0; i < Tabs.Length; i++) if (In(TabRect(i), pt)) { _wardrobeTab = i; Synth.Pop(); return; }
                if (_wardrobeTab < 3)
                {
                    var items = TabItems;
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (!In(CellRect(i), pt)) continue;
                        if (P.IsUnlocked(items[i])) { P.Equip(items[i]); Synth.Unlock(); _anim.Celebrate(); } else Synth.Grumble();
                        return;
                    }
                }
                return;
            case Overlay.Game:
                if (_gameEnded) { if (In(AgainRect, pt)) StartGame(); else if (In(DoneRect, pt)) Open(Overlay.Game); }
                return;
            case Overlay.Console:
                if (!In(ConsoleRect, pt)) Open(Overlay.Console);
                return;
        }

        var c = BuddyCenter();
        var pose = _anim.GetPose(0, 0);
        float dx = pt.X - c.X, dy = pt.Y - (c.Y + pose.Lift * RoomScale);
        if (dx * dx + dy * dy < 26 * 26) { OnPoke?.Invoke(); return; }

        void WalkTo(float x, string line)
        {
            if (_anim.Asleep || _anim.Mood != Mood.Idle) { Say(line); return; }
            _targetX = x; _arriveLine = line; _nextWander = t + 12;
        }
        if (P.Has("cat") && In(new(96, 96, 30, 32), pt)) { Synth.Meow(); _catHeartAt = t; return; }
        if (P.Has("arcade") && In(new(290, 56, 30, 112), pt)) { Open(Overlay.Game); return; }
        if (P.Has("lamp") && In(new(420, 112, 32, 44), pt)) { _lampOn = !_lampOn; Synth.Pop(); return; }
        if (P.Has("records") && In(new(392, 226, 52, 16), pt))
        {
            _musicOn = !_musicOn;
            if (_musicOn) { Synth.Done(0.75); _anim.Dance(); }
            return;
        }
        if (P.Has("plant") && In(new(145, 56, 36, 74), pt)) { _plantWiggleAt = t; WalkTo(182, "*waters plant* 🌱"); return; }
        if (P.Has("fishtank") && In(new(186, 56, 52, 76), pt)) { WalkTo(211, "blub blub 🐟"); return; }
        if (P.Has("poster") && In(new(34, 186, 66, 88), pt)) { Say("SHIP IT! 🚀"); _anim.Celebrate(); return; }
        if (In(new(176, 172, 118, 124), pt)) { WalkTo(235, P.Data.Room == "space" ? "Hi Earth! 🌍" : IsNight ? "Look at the stars ✨" : "What a nice day ☀️"); return; }
        if (In(new(386, 112, 50, 32), pt)) { Say(_anim.Mood == Mood.Idle ? "Nothing running. Break time!" : "Working on it… ⌨️"); return; }
        if (In(new(14, 60, 126, 46), pt)) { WalkTo(120, "So comfy… 😴"); return; }
    }

    public void Feed()
    {
        if (_anim.Sick) { OnMedicine?.Invoke(); return; }
        if (_cookieY != null) return;
        if (Current != Overlay.None && Current != Overlay.Console) Open(Current);
        _cookieY = H - 20;
        Synth.Pop();
    }
}
