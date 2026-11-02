// Buddy's room: a little world where buddy walks around, works at its desk while Claude Code runs
// and sleeps in bed. From the bar at the bottom you feed buddy, play arcade games, change outfits,
// shop, check daily quests, rearrange the furniture and type console commands.
// Layout is in y-up room units (like the Mac version); Y() / R() flip to the screen.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeBuddy;

record Theme(Color Wall, Color WallLine, string WallPattern, Color Floor, Color FloorLine, string FloorPattern, Color Base);
record DecorSpec(string Id, RectangleF Rect, bool Movable);

class RoomForm : Form
{
    public enum Overlay { None, Style, Shop, Quests, Arcade, Console, Edit }
    enum Place { Floor, Desk, Bed }

    public const float W = 460, H = 340, HudH = 44, FloorTop = 124, GroundY = 95, RoomScale = 0.7f;
    const float DeskX = 365, BedX = 72;

    static Color C(uint hex, double a = 1) => Palette.Rgb(hex, (int)(Math.Clamp(a, 0, 1) * 255));

    // MARK: Themes

    public static Theme RoomTheme(string id) => id switch
    {
        "greenhouse" => new(C(0xCFE8D2), C(0xA9D3B0), "mullions", C(0xD9D2C3), C(0xC2B9A6), "tiles", C(0x6E9C74)),
        "space" => new(C(0x3B4256), C(0x4C556E), "rivets", C(0x2A2F3D), C(0x3D455A), "tiles", C(0x596178)),
        "beach" => new(C(0xF6E7CB), C(0xE8D3AE), "boards", C(0xF0D9A6), C(0xE0C48A), "sand", C(0xC9A877)),
        "cabin" => new(C(0xC89B6D), C(0xAF825A), "logs", C(0x7A5236), C(0x684530), "planks", C(0x5A3A24)),
        "synthwave" => new(C(0x2A1B3D), C(0x4A2A6E), "grid", C(0x1A1028), C(0xFF4FA3), "neon", C(0x3B2358)),
        "reef" => new(C(0x2C6E91), C(0x3E86AB), "waves", C(0xE6D3A3), C(0xD4BE88), "sand", C(0x1F5673)),
        _ => new(C(0xF3E3C8), C(0xEAD6B4), "panels", C(0xB98556), C(0xA27045), "planks", C(0x8A5A36)),
    };

    static (Color, Color, string)? WallpaperStyle(string id) => id switch
    {
        "wp-cream" => (C(0xF5EBDD), C(0xEADBC6), "stripes"), "wp-sage" => (C(0xD5E3D0), C(0xB9CEB2), "dots"),
        "wp-blush" => (C(0xF6D9DA), C(0xEEC3C6), "stripes"), "wp-sun" => (C(0xFFF3C4), C(0xF2D675), "dots"),
        "wp-bricks" => (C(0xC9715A), C(0xB25E48), "bricks"), "wp-navy" => (C(0x2F3B5C), C(0x56669A), "stars"),
        "wp-cosmos" => (C(0x1E1B3A), C(0x4A4590), "stars"), _ => null,
    };

    static (Color, Color, string)? FloorStyle(string id) => id switch
    {
        "fl-oak" => (C(0xC8955F), C(0xB07E4C), "planks"), "fl-tiles" => (C(0xE8E4DC), C(0xD2CCC0), "tiles"),
        "fl-carpet" => (C(0x6E5A8C), C(0x7D6A9C), "carpet"), "fl-checker" => (C(0xF2F2F2), C(0x2B2D42), "checker"),
        "fl-marble" => (C(0xEDEAE6), C(0xCFC9C2), "marble"), _ => null,
    };

    static Theme WithWall(Theme th, string wp) => WallpaperStyle(wp) is var (w, l, p) ? th with { Wall = w, WallLine = l, WallPattern = p } : th;
    static Theme WithFloor(Theme th, string fl) => FloorStyle(fl) is var (f, l, p) ? th with { Floor = f, FloorLine = l, FloorPattern = p } : th;
    static Theme CurrentTheme() => WithFloor(WithWall(RoomTheme(P.Data.Room), P.Data.Wallpaper), P.Data.Floor);

    // MARK: Furniture layout (base rects, y up)

    static readonly DecorSpec[] Decor =
    {
        new("lights", new(0, 306, 460, 30), false), new("poster", new(34, 186, 66, 88), true), new("neon", new(108, 264, 64, 36), true),
        new("trophycase", new(26, 134, 74, 36), true), new("clock", new(298, 258, 28, 28), true), new("shelf", new(330, 222, 118, 42), true),
        new("disco", new(128, 300, 26, 40), true), new("rug", new(150, 54, 170, 30), true), new("cactus", new(18, 50, 20, 36), true),
        new("lavalamp", new(118, 52, 18, 50), true), new("plant", new(145, 56, 36, 74), true), new("fishtank", new(186, 56, 52, 76), true),
        new("beanbag", new(240, 50, 48, 32), true), new("arcade", new(290, 56, 30, 112), true),
    };

    static readonly Dictionary<string, string> DecorIcons = new()
    {
        ["plant"] = "🪴", ["poster"] = "🖼️", ["lamp"] = "💡", ["shelf"] = "📚", ["rug"] = "🧶", ["fishtank"] = "🐠", ["records"] = "💿",
        ["arcade"] = "🕹️", ["neon"] = "🌈", ["cat"] = "🐈", ["cactus"] = "🌵", ["clock"] = "🕰️", ["beanbag"] = "🛋️", ["lavalamp"] = "🫧",
        ["lights"] = "✨", ["disco"] = "🪩", ["trophycase"] = "🏆",
    };

    readonly Animator _anim;
    readonly Timer _timer = new() { Interval = 16 };
    readonly TextBox _input = new() { BorderStyle = BorderStyle.None, Visible = false };
    static readonly Random Rng = new();
    public Action OnPoke, OnFeed, OnMedicine;
    public Func<string, IEnumerable<string>> OnCommand;
    public Func<string, int, GameReward> OnGameFinish;
    public Func<ContextMenuStrip> MenuProvider;

    public Overlay Current { get; private set; } = Overlay.None;
    MiniGame _game;
    float _bx = 240, _targetX = 240;
    Place _place = Place.Floor;
    double _nextWander = Clock.Now + 4, _speechUntil = -100, _lastFrame = Clock.Now, _toastUntil = -100, _plantWiggleAt = -100, _catHeartAt = -100;
    string _arriveLine, _speech = "", _toast = "";
    float? _cookieY;
    bool _lampOn = true, _musicOn;
    int _tab, _page;
    Item _pendingBuy;
    string _buyNote = "";
    string _dragId;
    float _dragStartX, _dragStartDx;
    bool _moved;
    readonly List<string> _consoleLines = new() { "Buddy OS 2.0 — type 'help'" };

    Graphics _g;
    public Graphics G => _g;
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
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; CloseGame(); Hide(); } };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { MenuProvider?.Invoke()?.Show(Cursor.Position); return; }
            if (e.Button == MouseButtons.Left) Click_(Logical(e.Location));
        };
        MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) Drag(Logical(e.Location)); };
        MouseUp += (_, _) => { if (_dragId != null) { _dragId = null; P.Save(); } };
        MouseWheel += (_, e) => Wheel(e.Delta);
    }

    Rectangle Scaled(RectangleF r) => Rectangle.Round(new RectangleF(r.X * Sc, r.Y * Sc, r.Width * Sc, r.Height * Sc));

    /// A client-area point in room units (y up).
    PointF Logical(Point p) => new(p.X / Sc, H - p.Y / Sc);

    public PointF MousePoint()
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

    /// Opens a panel (or closes it if it's already open).
    public void Open(Overlay o)
    {
        CloseGame();
        if (o == Overlay.Edit && Current == Overlay.Edit) FinishEditing();
        Current = Current == o ? Overlay.None : o;
        _tab = 0; _page = 0; _pendingBuy = null; _buyNote = "";
        _input.Visible = Current == Overlay.Console;
        if (Current == Overlay.Console) _input.Focus(); else ActiveControl = null;
    }

    public void SelectTab(int i) { _tab = i; _page = 0; }
    public void SetPage(int p) => _page = p;
    public void SetPending(string id) => _pendingBuy = Catalog.Find(id);

    // MARK: Games

    public void StartGame(string id)
    {
        CloseGame();
        if (Current != Overlay.None) Open(Current);
        _game = MiniGame.Make(id, this, _anim);
        if (_game == null) return;
        _game.OnFinish = (gid, score) => OnGameFinish?.Invoke(gid, score) ?? new GameReward(0, 0, false);
        _game.OnClose = CloseGame;
        _game.Restart();
    }

    public void CloseGame() => _game = null;
    bool GameOpen => _game != null;

    // MARK: Buddy behaviour

    static RectangleF DecorRect(string id)
    {
        var s = Decor.FirstOrDefault(d => d.Id == id);
        if (s == null) return RectangleF.Empty;
        var r = s.Rect; r.Offset(Offset(id), 0); return r;
    }

    static float Offset(string id)
    {
        var s = Decor.FirstOrDefault(d => d.Id == id);
        if (s == null || !s.Movable) return 0;
        float dx = P.Data.DecorX.TryGetValue(id, out var v) ? (float)v : 0;
        return Math.Clamp(dx, 4 - s.Rect.X, W - 4 - s.Rect.Right);
    }

    static float Mid(RectangleF r) => r.X + r.Width / 2;

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
            if (P.Placed("plant")) spots.Add((Mid(DecorRect("plant")) + 16, "*waters plant* 🌱"));
            if (P.Placed("fishtank")) spots.Add((Mid(DecorRect("fishtank")), "blub blub 🐟"));
            if (P.Placed("arcade")) spots.Add((Mid(DecorRect("arcade")), "one more game?"));
            if (P.Placed("beanbag")) spots.Add((Mid(DecorRect("beanbag")), "so squishy 😌"));
            if (P.Placed("lavalamp")) spots.Add((Mid(DecorRect("lavalamp")) + 14, "ooh, blobby ✨"));
            var pick = spots[Rng.Next(spots.Count)];
            _targetX = Math.Clamp(pick.x, 30, W - 30);
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
    public static RectangleF R(float x, float y, float w, float h) => new(x, H - y - h, w, h);

    public void Fill(Color c, float x, float y, float w, float h, float radius = 0)
    {
        if (w <= 0 || h <= 0) return;
        using var b = new SolidBrush(c);
        var r = R(x, y, w, h);
        radius = Math.Min(radius, Math.Min(w, h) / 2);
        if (radius <= 0.5f) { _g.FillRectangle(b, r); return; }
        using var path = CornerWindow.RoundRect(r, radius);
        _g.FillPath(b, path);
    }

    public void Oval(Color c, float x, float y, float w, float h)
    {
        using var b = new SolidBrush(c);
        _g.FillEllipse(b, R(x, y, w, h));
    }

    void StrokeRound(Color c, float width, RectangleF yUp, float radius, bool dashed = false)
    {
        using var pen = new Pen(c, width);
        if (dashed) { pen.DashPattern = new[] { 4f, 2.5f }; pen.DashOffset = (float)(Clock.Now * 8 % 6.5); }
        using var path = CornerWindow.RoundRect(R(yUp.X, yUp.Y, yUp.Width, yUp.Height), radius);
        _g.DrawPath(pen, path);
    }

    static Font MakeFont(float size, bool bold, bool mono = false) =>
        new(mono ? "Consolas" : "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    /// Draws text whose bottom-left corner is at (x, y).
    public void DrawText(string s, float x, float y, float size, Color c, bool bold = false, bool mono = false)
    {
        using var f = MakeFont(size, bold, mono);
        using var b = new SolidBrush(c);
        Renderer.DrawRich(_g, s, f, b, x, Y(y) - size * 1.2f);
    }

    public float TextWidth(string s, float size, bool bold = false, bool mono = false)
    {
        using var f = MakeFont(size, bold, mono);
        return Renderer.MeasureRich(_g, s, f);
    }

    void CenterText(string s, RectangleF r, float y, float size, Color c, bool bold = false) => DrawText(s, Mid(r) - TextWidth(s, size, bold) / 2, y, size, c, bold);

    public void Line(Color c, float width, params (float x, float y)[] pts)
    {
        using var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        _g.DrawLines(pen, pts.Select(p => Pt(p.x, p.y)).ToArray());
    }

    void Polygon(Color c, params (float x, float y)[] pts)
    {
        using var b = new SolidBrush(c);
        _g.FillPolygon(b, pts.Select(p => Pt(p.x, p.y)).ToArray());
    }

    void Spark(Color c, float x, float y, float s) { var p = Pt(x, y); Renderer.Sparkle(_g, c, p.X, p.Y, s); }

    public void DrawBuddy(Pose pose, PointF c, float scale, bool shadow) =>
        Renderer.Draw(_g, P.Data.Skin, P.Data.Hat, pose, Pt(c.X, c.Y), scale, shadow, P.Data.Accessory);

    /// Draws `body` shifted by a piece of furniture's offset (only if it's in the room).
    void WithOffset(string id, Action body)
    {
        if (!P.Placed(id)) return;
        var st = _g.Save();
        _g.TranslateTransform(Offset(id), 0);
        body();
        _g.Restore(st);
    }

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
        var th = CurrentTheme();

        if (!GameOpen) UpdateBuddy(dt);

        PaintWall(th, 0, FloorTop, W, H - FloorTop, t);
        DrawWindow(180, 180, 110, 110);
        DrawWallDecor();
        DrawFloor(th);
        DrawBed();
        DrawFurniture();

        if (!GameOpen)
        {
            var m = MousePoint();
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
        DrawAmbient(t);

        if (!_lampOn || IsNight || P.Data.Room is "space" or "synthwave")
            Fill(Color.FromArgb(_lampOn ? 46 : 97, 13, 15, 51), 0, HudH, W, H - HudH);

        if (_game != null) _game.Frame();
        switch (Current)
        {
            case Overlay.Style: DrawStyle(); break;
            case Overlay.Shop: DrawShop(); break;
            case Overlay.Quests: DrawQuests(); break;
            case Overlay.Arcade: DrawArcade(); break;
            case Overlay.Console: DrawConsole(); break;
            case Overlay.Edit: DrawEdit(); break;
        }
        DrawHud();
        if (t < _toastUntil) DrawToast();
        _g = null;
    }

    // MARK: Walls and floors

    void PaintWall(Theme th, float x0, float y0, float w, float h, double t)
    {
        var st = _g.Save();
        _g.SetClip(R(x0, y0, w, h));
        Fill(th.Wall, x0, y0, w, h);
        var line = th.WallLine;
        float x1 = x0 + w, y1 = y0 + h;
        switch (th.WallPattern)
        {
            case "mullions":
                for (float x = x0; x < x1; x += 46) Fill(line, x, y0, 3, h);
                Fill(line, x0, y1 - 40, w, 3);
                break;
            case "rivets":
                for (float x = x0; x < x1; x += 92) Fill(line, x, y0, 2, h);
                for (float x = x0 + 8; x < x1; x += 92) foreach (var y in new[] { y0 + 10, y1 - 12 }) Oval(C(0x6A7390), x, y, 3, 3);
                break;
            case "boards":
                for (float y = y0 + 18; y < y1; y += 22) Fill(line, x0, y, w, 2);
                break;
            case "logs":
                for (float y = y0 + 4; y < y1; y += 24) { Fill(line, x0 - 6, y, w + 12, 20, 10); Fill(th.Wall, x0 - 6, y + 3, w + 12, 14, 7); }
                break;
            case "grid":
                for (float x = x0; x < x1; x += 30) Fill(line, x, y0, 1, h);
                for (float y = y0; y < y1; y += 30) Fill(line, x0, y, w, 1);
                break;
            case "waves":
                int k = 0;
                for (float y = y0 + 20; y < y1; y += 34, k++)
                {
                    var pts = new List<(float, float)>();
                    for (float x = x0; x <= x1; x += 8) pts.Add((x, y + 4 * (float)Math.Sin(x / 18 + t * 0.8 + k)));
                    Line(line, 2, pts.ToArray());
                }
                break;
            case "stripes":
                for (float x = x0 + 6; x < x1; x += 24) Fill(line, x, y0, 10, h);
                break;
            case "dots":
                int row = 0;
                for (float y = y0 + 10; y < y1; y += 20, row++)
                    for (float x = x0 + (row % 2 == 0 ? 8 : 18); x < x1; x += 20) Oval(line, x, y, 5, 5);
                break;
            case "bricks":
                int r2 = 0;
                for (float y = y0; y < y1; y += 14, r2++)
                {
                    Fill(line, x0, y, w, 2);
                    for (float x = x0 + (r2 % 2 == 0 ? 0 : 16); x < x1; x += 32) Fill(line, x, y, 2, 14);
                }
                break;
            case "stars":
                for (int i = 0; i < 36; i++)
                    Spark(Palette.Alpha(line, 0.5 + 0.5 * Math.Abs(Math.Sin(t + i))), x0 + (i * 89) % Math.Max(1, (int)w), y0 + (i * 47) % Math.Max(1, (int)h), i % 3 == 0 ? 3 : 1.8f);
                break;
            default:
                for (float x = x0 + 10; x < x1; x += 28) Fill(line, x, y0, 9, h);
                break;
        }
        _g.Restore(st);
    }

    void PaintFloor(Theme th, float x0, float y0, float w, float h)
    {
        var st = _g.Save();
        _g.SetClip(R(x0, y0, w, h));
        Fill(th.Floor, x0, y0, w, h);
        var line = th.FloorLine;
        float x1 = x0 + w, y1 = y0 + h;
        switch (th.FloorPattern)
        {
            case "sand":
                for (int i = 0; i < 40; i++) Oval(line, x0 + (i * 97) % Math.Max(1, (int)w), y0 + (i * 53) % Math.Max(1, (int)h), 3, 2);
                break;
            case "tiles":
                for (float x = x0; x < x1; x += 40) Fill(line, x, y0, 1.5f, h);
                for (float y = y0; y < y1; y += 26) Fill(line, x0, y, w, 1.5f);
                break;
            case "carpet":
                for (int i = 0; i < 60; i++) Oval(line, x0 + (i * 71) % Math.Max(1, (int)w), y0 + (i * 37) % Math.Max(1, (int)h), 2, 2);
                break;
            case "checker":
                int row = 0;
                for (float y = y0; y < y1; y += 20, row++)
                {
                    int col = 0;
                    for (float x = x0; x < x1; x += 20, col++) if ((row + col) % 2 == 0) Fill(line, x, y, 20, Math.Min(20, y1 - y));
                }
                break;
            case "marble":
                using (var pen = new Pen(line, 1))
                    for (int k = 0; k < 6; k++)
                    {
                        float bx = x0 + k * w / 6;
                        _g.DrawBezier(pen, Pt(bx, y0), Pt(bx + 30, y0 + h / 2 - 10), Pt(bx + 10, y0 + h / 2 + 20), Pt(bx + 50, y1));
                    }
                break;
            case "neon":
                using (var pen = new Pen(Palette.Alpha(line, 0.7), 1))
                {
                    float hx = x0 + w / 2, hy = y1 + 60, kk = (y1 - y0) / (hy - y0);
                    for (int k = -8; k <= 8; k++) { float bx = hx + k * 60; _g.DrawLine(pen, Pt(bx, y0), Pt(bx + (hx - bx) * kk, y1)); }
                    for (float y = y0 + 8; y < y1; y += 18) _g.DrawLine(pen, Pt(x0, y), Pt(x1, y));
                }
                break;
            default:
                for (float y = y0 + 16; y < y1; y += 16) Fill(line, x0, y, w, 1.5f);
                break;
        }
        _g.Restore(st);
    }

    void DrawFloor(Theme th)
    {
        PaintFloor(th, 0, HudH, W, FloorTop - HudH);
        if (P.Data.Room == "beach") Oval(C(0xF4A6B8), 160, 52, 9, 6);
        Fill(th.Base, 0, FloorTop - 4, W, 6);
        WithOffset("rug", () =>
        {
            Fill(C(0xC0504D, 0.85), 150, 54, 170, 30, 15);
            StrokeRound(C(0xF2C94C, 0.8), 2, new RectangleF(156, 58, 158, 22), 11);
        });
    }

    void DrawWindow(float x, float y, float w, float h)
    {
        double t = Clock.Now;
        int hour = DateTime.Now.Hour;
        string room = P.Data.Room;
        if (room is "space" or "reef")
        {
            float cx = x + w / 2 - 52, cy = y + h / 2 - 52;
            Oval(room == "space" ? C(0x0B0D1F) : C(0x0E3B5C), cx, cy, 104, 104);
            var st = _g.Save();
            using (var clip = new GraphicsPath()) { clip.AddEllipse(R(cx, cy, 104, 104)); _g.SetClip(clip); }
            if (room == "space")
            {
                for (int i = 0; i < 30; i++)
                    Oval(Color.FromArgb((int)(255 * (0.4 + 0.5 * Math.Abs(Math.Sin(t + i)))), 255, 255, 255), cx + (i * 37) % 104, cy + (i * 61) % 104, 2, 2);
                Oval(C(0x3F7FE0), cx + 30 + (float)Math.Sin(t * 0.1) * 6, cy - 30, 90, 90);
                Fill(C(0x5DBB63), cx + 50, cy + 20, 30, 18, 9);
            }
            else
            {
                uint[] fish = { 0xF29A38, 0xFFD54F, 0xE5395B };
                for (int k = 0; k < 3; k++)
                {
                    float fx = cx + (float)((t * (14 + k * 9) + k * 40) % 150) - 20, fy = cy + 26 + k * 22 + (float)Math.Sin(t * 2 + k) * 4;
                    Fill(C(fish[k]), fx, fy, 14, 8, 4);
                    Polygon(C(fish[k]), (fx, fy + 4), (fx - 6, fy + 8), (fx - 6, fy));
                }
                Fill(C(0xE07A9A), cx + 10, cy - 6, 22, 30, 8);
                Fill(C(0x5DBB63), cx + 74, cy - 4, 10, 34, 5);
            }
            _g.Restore(st);
            using var ring = new Pen(room == "space" ? C(0x8A93AE) : C(0xC9A877), 7);
            _g.DrawEllipse(ring, R(cx, cy, 104, 104));
            return;
        }
        bool night = hour >= 20 || hour < 6, dusk = hour >= 17 && hour < 20;
        var top = night ? C(0x0E1640) : dusk ? C(0xF08A5D) : C(0x7EC8F2);
        var bottom = night ? C(0x26306B) : dusk ? C(0xF7C873) : C(0xCDEBFA);
        if (room == "synthwave") { top = C(0x1A0B33); bottom = C(0xFF4FA3); }
        if (room == "cabin" && !night) { top = C(0xA9C7E0); bottom = C(0xE8F1F8); }
        var r = R(x, y, w, h);
        using (var grad = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical)) _g.FillRectangle(grad, r);
        var saved = _g.Save();
        _g.SetClip(r);
        if (room == "synthwave")
        {
            var sun = R(x + w / 2 - 30, y + 22, 60, 60);
            using (var sb = new LinearGradientBrush(sun, C(0xFFD54F), C(0xFF4FA3), LinearGradientMode.Vertical)) _g.FillEllipse(sb, sun);
            for (int k = 0; k < 4; k++) Fill(C(0x1A0B33), x + w / 2 - 30, y + 28 + k * 8, 60, 2 + k * 0.6f);
            var mtn = new (float, float)[] { (x, y), (x + 24, y + 30), (x + 46, y + 12), (x + 76, y + 38), (x + w, y + 8), (x + w, y) };
            Polygon(C(0x2A1B3D), mtn);
            using var pen = new Pen(C(0xFF4FA3), 1);
            _g.DrawLines(pen, mtn.Select(p => Pt(p.Item1, p.Item2)).ToArray());
        }
        else if (night)
        {
            for (int i = 0; i < 14; i++)
                Oval(Color.FromArgb((int)(255 * (0.5 + 0.5 * Math.Abs(Math.Sin(t * 1.3 + i)))), 255, 255, 255), x + (i * 41) % 104, y + 30 + (i * 29) % 76, 2, 2);
            Oval(C(0xF7F3D7), x + w - 34, y + h - 32, 18, 18);
        }
        else if (room != "cabin")
        {
            Oval(C(0xFFE07A), x + w - 36, y + h - 34, 20, 20);
            float cloud = x + (float)(t * 6 % 160) - 30;
            foreach (var (dx, dy, s) in new (float, float, float)[] { (0, 0, 16), (10, 4, 18), (22, 0, 14) })
                Oval(Color.FromArgb(230, 255, 255, 255), cloud + dx, y + h - 50 + dy, s, s * 0.8f);
        }
        if (room == "beach")
        {
            Fill(night ? C(0x1B3A6B) : C(0x2E9CCA), x, y, w, 34);
            for (int i = 0; i < 4; i++) Fill(Color.FromArgb(128, 255, 255, 255), x + (float)((t * 10 + i * 30) % 120) - 10, y + 8 + (i % 2) * 12, 14, 1.5f);
        }
        else if (room == "cabin")
        {
            Fill(Color.FromArgb(248, 248, 248), x, y, w, 20);
            foreach (var (tx, th) in new (float, float)[] { (14, 46), (44, 60), (80, 40) })
                Polygon(night ? C(0x1E3B2C) : C(0x2F6B4A), (x + tx - 14, y + 14), (x + tx, y + 14 + th), (x + tx + 14, y + 14));
            for (int i = 0; i < 22; i++)
                Oval(Color.White, x + (float)((i * 23 + Math.Sin(t + i) * 6) % 110 + 110) % 110, y + h - (float)((t * (14 + i % 4 * 3) + i * 17) % 110), 2.5f, 2.5f);
        }
        else if (room != "synthwave")
        {
            Fill(night ? C(0x1E3B2C) : C(0x6DBE6A), x - 10, y - 30, 80, 54, 27);
            Fill(night ? C(0x234A33) : C(0x84CF7A), x + 50, y - 34, 90, 52, 26);
        }
        _g.Restore(saved);
        var frame = room switch { "greenhouse" => C(0x6E9C74), "synthwave" => C(0xFF4FA3), "cabin" => C(0x6A4630), _ => Color.White };
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
        WithOffset("poster", () =>
        {
            Fill(C(0x2B2D42), 34, 186, 66, 88, 3);
            Renderer.Draw(_g, "classic", "none", new Pose(), Pt(67, 240), 0.5f, false);
            DrawText("SHIP IT", 43, 192, 12, Palette.Spark, true);
        });
        WithOffset("neon", () =>
        {
            double glow = 0.75 + 0.25 * Math.Sin(t * 3);
            for (int k = 3; k >= 1; k--) DrawText("</>", 116 - k, 268 - k, 26, C(0xFF4FA3, glow * 0.12), true, true);
            for (int k = 3; k >= 1; k--) DrawText("</>", 116 + k, 268 + k, 26, C(0xFF4FA3, glow * 0.12), true, true);
            DrawText("</>", 116, 268, 26, C(0xFF7FC1, glow), true, true);
        });
        WithOffset("clock", () =>
        {
            float cx = 312, cy = 272;
            Oval(C(0x5A3A24), cx - 14, cy - 14, 28, 28);
            Oval(C(0xFFF8E7), cx - 11, cy - 11, 22, 22);
            var now = DateTime.Now;
            double m = now.Minute, hr = now.Hour % 12 + m / 60, s = now.Second;
            foreach (var (ang, len, wd, col) in new (double, float, float, Color)[] { (hr / 12, 6, 2, Palette.Face), (m / 60, 9, 1.4f, Palette.Face), (s / 60, 9.5f, 0.8f, C(0xE5395B)) })
                Line(col, wd, (cx, cy), (cx + (float)Math.Sin(ang * 2 * Math.PI) * len, cy + (float)Math.Cos(ang * 2 * Math.PI) * len));
        });
        WithOffset("trophycase", () =>
        {
            Fill(C(0x6B4A33), 26, 134, 74, 5, 2);
            int n = Math.Clamp(P.Data.Achievements.Count / 5, 1, 5);
            for (int k = 0; k < n; k++)
            {
                float x = 32 + k * 13;
                Fill(C(0xF2C94C), x, 147, 9, 9, 3);
                Fill(C(0xC99A22), x + 3, 141, 3, 7);
                Fill(C(0xC99A22), x, 139, 9, 3, 1);
            }
        });
        WithOffset("shelf", () =>
        {
            Fill(C(0x8A5A36), 330, 222, 118, 6, 2);
            foreach (var (bx, col, bh) in new (float, uint, float)[] { (334, 0xC0504D, 26), (344, 0x3F7FE0, 30), (355, 0x5DBB63, 24), (364, 0xF2C94C, 28), (375, 0x9466DB, 22) })
                Fill(C(col), bx, 228, 9, bh, 1);
            if (P.Placed("records"))
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
        });
        if (P.Data.Room == "greenhouse")
        {
            foreach (var (px, len) in new (float, float)[] { (30, 40), (130, 28), (320, 34) })
            {
                Line(C(0x6E9C74), 1.5f, (px + 10, H), (px + 10, H - len));
                Fill(C(0xC8734E), px, H - len - 14, 20, 14, 3);
                for (int k = 0; k < 4; k++) Oval(C(0x4E9F55), px - 6 + k * 8 + (float)Math.Sin(t * 1.2 + k) * 2, H - len - 30 + (k % 2) * 4, 8, 18);
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
        if (P.Data.Room == "cabin")
        {
            Fill(C(0x6A4630), 196, 124, 78, 40, 4);
            Fill(C(0x2B1D16), 206, 124, 58, 30, 3);
            for (int k = 0; k < 3; k++)
                Fill(k == 1 ? C(0xFFD54F) : C(0xFF7A1F), 218 + k * 12, 126, 10, 12 + 8 * (float)Math.Abs(Math.Sin(t * 6 + k * 1.7)), 5);
        }
        WithOffset("disco", () =>
        {
            Line(Color.FromArgb(153, 153, 153), 1, (141, H), (141, 326));
            Oval(C(0xB0BEC5), 128, 300, 26, 26);
            for (int k = 0; k < 12; k++)
                Fill(Color.FromArgb((int)(255 * (0.3 + 0.6 * Math.Abs(Math.Sin(t * 4 + k)))), 255, 255, 255), 131 + (k % 4) * 5.5f, 303 + (k / 4) * 6.5f, 4, 4.5f);
        });
        if (P.Placed("lights"))
        {
            var wire = new List<(float, float)>();
            for (float x = 0; x <= W; x += 6) wire.Add((x, 332 - (float)Math.Sin(x % 66 / 66 * Math.PI) * 13));
            Line(C(0x3A3A3A), 1, wire.ToArray());
            uint[] cols = { 0xFF6B9A, 0xFFD54F, 0x7CF29A, 0x6EC6FF, 0xC59BFF };
            for (int k = 0; k < 21; k++)
            {
                float x = k * 22 + 8, y = 332 - (float)Math.Sin(x % 66 / 66 * Math.PI) * 13;
                bool on = Math.Sin(t * 2.5 + k) > -0.3;
                var c = C(cols[k % cols.Length], on ? 1 : 0.35);
                if (on) Oval(Palette.Alpha(c, 0.3), x - 5, y - 10, 10, 13);
                Fill(c, x - 2.5f, y - 7, 5, 7, 2.5f);
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
        if (_place != Place.Bed || GameOpen) DrawBlanket();
    }

    void DrawBlanket()
    {
        Fill(C(0x5B7FD6), 62, 70, 76, 28, 8);
        for (float x = 70; x < 134; x += 16) Fill(C(0x7E9DE6), x, 72, 6, 24);
    }

    void DrawFurniture()
    {
        double t = Clock.Now;
        WithOffset("cactus", () =>
        {
            Fill(C(0xC8734E), 20, 50, 16, 12, 3);
            Fill(C(0x4E9F55), 23, 60, 10, 24, 5);
            Fill(C(0x4E9F55), 16, 68, 8, 5, 2.5f); Fill(C(0x4E9F55), 16, 68, 4, 11, 2);
            Fill(C(0x4E9F55), 32, 72, 6, 4, 2); Fill(C(0x4E9F55), 35, 72, 4, 9, 2);
            Fill(C(0xFF8FC7), 25, 82, 6, 5, 2.5f);
        });
        WithOffset("lavalamp", () =>
        {
            Fill(C(0x5A5A6E), 120, 52, 14, 10, 2);
            Fill(C(0x7E57C2, 0.85), 121, 61, 12, 32, 6);
            for (int k = 0; k < 3; k++) Fill(C(0xFF7A1F), 123 + (k % 2) * 3, 64 + (float)((t * (5 + k * 2) + k * 9) % 24), 6 + k, 6 + k, 3.5f);
            Fill(C(0x5A5A6E), 122, 92, 10, 6, 2);
        });
        WithOffset("plant", () =>
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
        });
        WithOffset("fishtank", () =>
        {
            Fill(C(0x6B4A33), 188, 56, 48, 40, 3);
            Fill(C(0x7FD3F0, 0.75), 186, 96, 52, 34, 3);
            Fill(C(0xE8D3AE), 186, 96, 52, 6, 2);
            for (int k = 0; k < 2; k++) Fill(k == 0 ? C(0xF29A38) : C(0xE5395B), 196 + (float)(t * (12 + k * 7) % 34), 108 + k * 9, 9, 5, 2.5f);
            Oval(Color.FromArgb(153, 255, 255, 255), 228, 100 + (float)(t * 14 % 30), 3, 3);
        });
        WithOffset("beanbag", () =>
        {
            Fill(C(0x3F7FE0), 240, 50, 48, 28, 14);
            Fill(C(0x5B95EA), 246, 64, 34, 16, 8);
        });
        WithOffset("arcade", () =>
        {
            Fill(C(0x3B2A6B), 290, 56, 30, 112, 4);
            Fill(C(0x111122), 294, 126, 22, 26, 2);
            Fill(C(0x7CF29A, 0.4 + 0.3 * Math.Abs(Math.Sin(t * 4))), 297, 130, 16, 18, 1);
            Fill(C(0xE5395B), 300, 110, 5, 5, 2.5f);
            Fill(C(0xF2C94C), 308, 110, 5, 5, 2.5f);
        });
    }

    void DrawDesk()
    {
        double t = Clock.Now;
        Fill(C(0x5A3A24), 326, 106, 128, 8, 2);
        Fill(C(0x6B4A33), 330, 56, 10, 52);
        Fill(C(0x6B4A33), 440, 56, 10, 52);
        Fill(C(0x6B4A33), 340, 84, 100, 22);
        Fill(C(0x8A6448), 380, 92, 20, 4, 2);
        Fill(C(0xBFC4CC), 392, 114, 40, 28, 3);
        double glow = _anim.Mood == Mood.Working ? 0.7 + 0.3 * Math.Abs(Math.Sin(t * 3)) : 0.85;
        Spark(Palette.Alpha(Renderer.RayColor(P.Data.Skin, 0, t), glow), 412, 128, 6);
        if (P.Placed("lamp"))
        {
            Fill(C(0x444444), 436, 114, 12, 3, 1);
            Line(C(0x444444), 2.5f, (442, 116), (446, 140), (434, 150));
            Fill(C(0xF2C94C), 424, 144, 18, 9, 4);
            if (_lampOn) Polygon(C(0xFFE9A0, 0.25), (426, 145), (400, 112), (446, 112), (440, 145));
        }
    }

    void DrawCat()
    {
        if (!P.Placed("cat")) return;
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

    void DrawAmbient(double t)
    {
        if (P.Data.Room == "reef")
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1))
                for (int i = 0; i < 10; i++)
                {
                    float x = (i * 47) % 440 + 10 + (float)Math.Sin(t + i) * 5, y = HudH + (float)((t * (16 + i % 3 * 6) + i * 37) % (H - HudH)), s = 5 + i % 3;
                    _g.DrawEllipse(pen, R(x, y, s, s));
                }
        if (P.Placed("disco") && _musicOn)
        {
            uint[] cols = { 0xFF6B9A, 0xFFD54F, 0x7CF29A, 0x6EC6FF, 0xC59BFF };
            for (int i = 0; i < 14; i++)
            {
                double a = t * 0.8 + i * 0.45;
                Oval(C(cols[i % cols.Length], 0.35), W / 2 + (float)Math.Cos(a) * (80 + (i * 23) % 150), 190 + (float)Math.Sin(a * 1.3) * (40 + (i * 11) % 90), 7, 7);
            }
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
        if (y <= c.Y + 14) { _cookieY = null; OnFeed?.Invoke(); return; }
        _cookieY = y;
        Oval(C(0xD9A066), c.X - 7, y, 14, 14);
        foreach (var (dx, dy) in new (float, float)[] { (3, 4), (8, 9), (5, 10), (9, 3) }) Oval(C(0x5A3A24), c.X - 7 + dx, y + dy - 1, 2.5f, 2.5f);
    }

    void DrawSpeech(PointF pt)
    {
        using var f = MakeFont(11, true);
        float w = Math.Min(220, Renderer.MeasureRich(_g, _speech, f) + 16);
        float x = Math.Max(6, Math.Min(W - w - 6, pt.X - w / 2));
        float y = Math.Min(H - 30, pt.Y + 4);
        var r = R(x, y, w, 22);
        using var bg = new SolidBrush(Color.FromArgb(247, 252, 252, 252));
        using (var path = CornerWindow.RoundRect(r, 10)) _g.FillPath(bg, path);
        _g.FillPolygon(bg, new[] { Pt(pt.X - 5, y + 1), Pt(pt.X, y - 6), Pt(pt.X + 5, y + 1) });
        using var tb = new SolidBrush(Color.FromArgb(31, 31, 31));
        Renderer.DrawRich(_g, _speech, f, tb, r.X + 8, r.Y + 4, r.Width - 14);
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

    static readonly string[] HudIcons = { "🍪", "🕹️", "👕", "🛍️", "📋", "🛠️", "⌨️" };
    static readonly string[] HudLabels = { "Feed", "Arcade", "Style", "Shop", "Daily quests", "Edit room", "Console" };
    static readonly Overlay?[] HudOverlays = { null, Overlay.Arcade, Overlay.Style, Overlay.Shop, Overlay.Quests, Overlay.Edit, Overlay.Console };
    static RectangleF HudButton(int i) => new(232 + i * 32, 8, 29, 28);
    static readonly RectangleF CoinRect = new(168, 10, 58, 24);

    void DrawHud()
    {
        Fill(C(0x1E1B2E), 0, 0, W, HudH);
        Fill(Palette.Orange, 8, 11, 42, 22, 11);
        string lv = $"Lv {P.Level}";
        DrawText(lv, 29 - TextWidth(lv, 12, true) / 2, 14, 12, Color.White, true);
        DrawText(P.Data.Name, 56, 22, 10.5f, Color.White, true);
        Fill(Color.FromArgb(38, 255, 255, 255), 56, 12, 104, 7, 3.5f);
        Fill(Palette.Spark, 56, 12, Math.Max(7, 104 * (float)Math.Clamp(P.LevelFraction, 0, 1)), 7, 3.5f);
        var m = MousePoint();
        Fill(Color.FromArgb(26, 255, 255, 255), CoinRect.X, CoinRect.Y, CoinRect.Width, CoinRect.Height, 12);
        string coins = $"🪙 {P.Data.Coins}";
        CenterText(coins, CoinRect, CoinRect.Y + 5, 11, Palette.Spark, true);

        (string label, RectangleF r)? tip = null;
        for (int i = 0; i < HudIcons.Length; i++)
        {
            var r = HudButton(i);
            bool active = (HudOverlays[i] is Overlay o && o == Current) || (i == 1 && GameOpen);
            Fill(active ? Palette.Orange : Color.FromArgb(In(r, m) ? 51 : 26, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 8);
            string icon = i == 0 && _anim.Sick ? "💊" : HudIcons[i];
            DrawText(icon, r.X + 6, r.Y + 6, 13, Color.White);
            if (In(r, m)) tip = (i == 0 && _anim.Sick ? "Give medicine" : HudLabels[i], r);
        }
        if (In(new RectangleF(8, 6, 156, 32), m)) tip = ($"Level {P.Level} · {P.Data.Xp} / {Progress.XpNeeded(P.Level + 1)} XP", new RectangleF(8, 8, 156, 28));
        if (In(CoinRect, m)) tip = ("Coins: earn them from sessions, games and quests", CoinRect);
        if (tip is var (label, tr))
        {
            float w = TextWidth(label, 10.5f, true) + 16, x = Math.Clamp(Mid(tr) - w / 2, 4, W - w - 4);
            Fill(C(0x2B2D42, 0.96), x, HudH + 4, w, 20, 6);
            DrawText(label, x + 8, HudH + 7, 10.5f, Color.White, true);
        }
    }

    // MARK: Shared panel pieces

    static readonly RectangleF Panel = new(10, 52, 440, 280);
    static RectangleF CloseRect => new(Panel.Right - 28, Panel.Bottom - 30, 22, 22);
    const int PerPage = 10;
    static RectangleF PrevRect => new(Panel.Right - 76, Panel.Y + 6, 24, 22);
    static RectangleF NextRect => new(Panel.Right - 30, Panel.Y + 6, 24, 22);
    static RectangleF BuyRect => new(Panel.Right - 152, Panel.Y + 6, 60, 22);
    static RectangleF CancelRect => new(Panel.Right - 86, Panel.Y + 6, 60, 22);
    static RectangleF CellRect(int i) => new(Panel.X + 10 + (i % 5) * 85, Panel.Bottom - 130 - (i / 5) * 98, 80, 92);
    static RectangleF TabRect(int i, int count) { float tw = (Panel.Width - 50) / count; return new(Panel.X + 10 + i * tw, Panel.Bottom - 32, tw - 4, 24); }

    void DrawPanel(string title, string[] tabs)
    {
        Fill(C(0x1E1B2E, 0.95), Panel.X, Panel.Y, Panel.Width, Panel.Height, 14);
        var m = MousePoint();
        if (tabs.Length == 0) DrawText(title, Panel.X + 16, Panel.Bottom - 30, 14, Color.White, true);
        else
            for (int i = 0; i < tabs.Length; i++)
            {
                var r = TabRect(i, tabs.Length);
                Fill(i == _tab ? Palette.Orange : Color.FromArgb(In(r, m) ? 46 : 26, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 11);
                CenterText(tabs[i], r, r.Y + 5, 10.5f, Color.White, true);
            }
        DrawText("×", CloseRect.X + 4, CloseRect.Y + 2, 18, In(CloseRect, m) ? Color.White : Color.FromArgb(179, 179, 179), true);
    }

    int Pages(int count) => Math.Max(1, (count + PerPage - 1) / PerPage);

    void DrawPager(int count)
    {
        int pages = Pages(count);
        _page = Math.Min(_page, pages - 1);
        if (pages <= 1 || _pendingBuy != null) return;
        var m = MousePoint();
        string label = $"{_page + 1}/{pages}";
        DrawText(label, PrevRect.Right + 11 - TextWidth(label, 10.5f) / 2, PrevRect.Y + 4, 10.5f, Color.FromArgb(204, 204, 204));
        foreach (var (r, s, on) in new[] { (PrevRect, "‹", _page > 0), (NextRect, "›", _page < pages - 1) })
        {
            Fill(Color.FromArgb(on ? (In(r, m) ? 64 : 36) : 13, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 6);
            DrawText(s, Mid(r) - 4, r.Y + 2, 15, Color.FromArgb(on ? 255 : 77, 255, 255, 255), true);
        }
    }

    bool PagerHit(PointF pt, int count)
    {
        int pages = Pages(count);
        if (pages <= 1 || _pendingBuy != null) return false;
        if (In(PrevRect, pt)) { if (_page > 0) { _page--; Synth.Pop(); } return true; }
        if (In(NextRect, pt)) { if (_page < pages - 1) { _page++; Synth.Pop(); } return true; }
        return false;
    }

    /// Draws one item's preview centred at c (y-up), faded when locked.
    void DrawPreview(Item it, PointF c, bool locked)
    {
        if (locked)
        {
            // Draw into a bitmap and fade it, since GDI+ has no global alpha.
            using var bmp = new Bitmap(80, 80);
            var keep = _g;
            using (var bg = Graphics.FromImage(bmp))
            {
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                bg.TranslateTransform(40 - c.X, 44 - (H - c.Y));
                _g = bg;
                DrawPreview(it, c, false);
            }
            _g = keep;
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.3f };
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            var at = Pt(c.X, c.Y);
            _g.DrawImage(bmp, new Rectangle((int)at.X - 40, (int)at.Y - 44, 80, 80), 0, 0, 80, 80, GraphicsUnit.Pixel, ia);
            return;
        }
        var d = P.Data;
        switch (it.Kind)
        {
            case ItemKind.Skin: Renderer.Draw(_g, it.Id, "none", new Pose(), Pt(c.X, c.Y), 0.55f, false); break;
            case ItemKind.Hat: Renderer.Draw(_g, d.Skin, it.Id, new Pose(), Pt(c.X, c.Y - 6), 0.55f, false); break;
            case ItemKind.Accessory: Renderer.Draw(_g, d.Skin, "none", new Pose(), Pt(c.X, c.Y), 0.62f, false, it.Id); break;
            case ItemKind.Room:
                var th = RoomTheme(it.Id);
                PaintWall(th, c.X - 28, c.Y - 4, 56, 28, 0);
                PaintFloor(th, c.X - 28, c.Y - 22, 56, 18);
                break;
            case ItemKind.Wallpaper: PaintWall(WithWall(RoomTheme(d.Room), it.Id), c.X - 28, c.Y - 22, 56, 46, 0); break;
            case ItemKind.Floor: PaintFloor(WithFloor(RoomTheme(d.Room), it.Id), c.X - 28, c.Y - 22, 56, 46); break;
            case ItemKind.Decor: DrawText(DecorIcons.TryGetValue(it.Id, out var ic) ? ic : "📦", c.X - 15, c.Y - 16, 26, Color.White); break;
        }
    }

    string DrawGrid(Item[] items, bool shop)
    {
        var m = MousePoint();
        string hover = null;
        var slice = items.Skip(_page * PerPage).Take(PerPage).ToArray();
        string deal = P.DailyDeal?.Id;
        for (int i = 0; i < slice.Length; i++)
        {
            var it = slice[i];
            var r = CellRect(i);
            bool unlocked = P.IsUnlocked(it), equipped = unlocked && P.IsEquipped(it);
            Fill(Color.FromArgb(In(r, m) ? 38 : 18, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 10);
            if (equipped || _pendingBuy?.Id == it.Id) StrokeRound(_pendingBuy?.Id == it.Id ? Palette.Spark : Palette.Orange, 2, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), 10);
            var c = new PointF(Mid(r), r.Y + 54);
            bool secret = !unlocked && it.Level == null && it.Price == null;
            if (secret) DrawText("?", c.X - 7, c.Y - 16, 26, Color.FromArgb(89, 255, 255, 255), true);
            else DrawPreview(it, c, !unlocked);
            string name = secret ? "Secret" : it.Name;
            CenterText(name, r, r.Y + 17, 10.5f, Color.White);
            string tag = "";
            var tagColor = Color.FromArgb(179, 179, 179);
            if (unlocked)
            {
                tag = equipped ? (it.Kind == ItemKind.Decor ? "In room" : it.Kind is ItemKind.Room or ItemKind.Wallpaper or ItemKind.Floor ? "Using" : "Wearing")
                               : it.Kind == ItemKind.Decor ? "Put away" : shop ? "Owned" : "";
            }
            else if (P.Price(it) is int price) { tag = $"🪙 {price}"; tagColor = P.Data.Coins >= price ? Palette.Spark : C(0xFF8A80); }
            else tag = it.Level is int l ? $"🔒 Lv {l}" : "🔒 secret";
            if (tag.Length > 0) CenterText(tag, r, r.Y + 4, 9.5f, tagColor, true);
            if (!unlocked && deal == it.Id)
            {
                Fill(C(0xE5395B), r.Right - 32, r.Bottom - 16, 30, 14, 7);
                DrawText("-30%", r.Right - 29, r.Bottom - 15, 9, Color.White, true);
            }
            if (In(r, m))
                hover = unlocked ? (it.Kind == ItemKind.Decor ? (equipped ? $"Click to put {it.Name} away." : $"Click to put {it.Name} in the room.") : $"Click to use {it.Name}.")
                      : P.Price(it) is int pr ? $"{it.Name} costs 🪙{pr}. Click to buy."
                      : it.Level is int lv ? $"Reach level {lv} to unlock {it.Name}." : $"Hint: {it.Hint}";
        }
        DrawPager(items.Length);
        return hover;
    }

    void DrawFooter(string s)
    {
        if (_pendingBuy is Item it && P.Price(it) is int price)
        {
            bool afford = P.Data.Coins >= price;
            DrawText(afford ? $"Buy {it.Name} for 🪙{price}?" : $"You need 🪙{price - P.Data.Coins} more for {it.Name}.", Panel.X + 14, Panel.Y + 9, 11.5f, afford ? Palette.Spark : C(0xFF8A80), true);
            if (afford) { Fill(Palette.Orange, BuyRect.X, BuyRect.Y, BuyRect.Width, BuyRect.Height, 7); CenterText("Buy", BuyRect, BuyRect.Y + 4, 11, Color.White, true); }
            Fill(Color.FromArgb(38, 255, 255, 255), CancelRect.X, CancelRect.Y, CancelRect.Width, CancelRect.Height, 7);
            CenterText(afford ? "Cancel" : "OK", CancelRect, CancelRect.Y + 4, 11, Color.White, true);
            return;
        }
        DrawText(_buyNote.Length > 0 ? _buyNote : s, Panel.X + 14, Panel.Y + 9, 11, Palette.Spark, true);
    }

    bool GridClick(PointF pt, Item[] items)
    {
        var slice = items.Skip(_page * PerPage).Take(PerPage).ToArray();
        for (int i = 0; i < slice.Length; i++)
        {
            if (!In(CellRect(i), pt)) continue;
            var it = slice[i];
            _buyNote = "";
            if (P.IsUnlocked(it)) { _pendingBuy = null; P.Equip(it); Synth.Unlock(); if (it.Kind != ItemKind.Decor) _anim.Celebrate(); }
            else if (P.Price(it) != null) { _pendingBuy = it; Synth.Pop(); }
            else { _pendingBuy = null; Synth.Grumble(); }
            return true;
        }
        return false;
    }

    bool BuyClick(PointF pt)
    {
        if (_pendingBuy is not Item it) return false;
        if (In(BuyRect, pt) && P.Buy(it))
        {
            _pendingBuy = null;
            P.Equip(it);
            Synth.Coin();
            _anim.Celebrate();
            _buyNote = $"Yours! {it.Name} is " + (it.Kind == ItemKind.Decor ? "in the room. Use 🛠️ to move it." : "on. ✨");
            Say(new[] { "ooh, fancy! ✨", "I love it!!", "for ME?! 🥹", "shopping spree! 🛍️" }[Rng.Next(4)]);
            return true;
        }
        if (In(CancelRect, pt)) { _pendingBuy = null; return true; }
        return false;
    }

    // MARK: Style

    static readonly string[] StyleTabs = { "Skins", "Hats", "Extras", "Rooms", "Walls", "Floors", "Decor", "Trophies" };
    static readonly ItemKind[] StyleKinds = { ItemKind.Skin, ItemKind.Hat, ItemKind.Accessory, ItemKind.Room, ItemKind.Wallpaper, ItemKind.Floor, ItemKind.Decor };
    Item[] StyleItems => _tab < StyleKinds.Length ? Catalog.Items.Where(i => i.Kind == StyleKinds[_tab]).ToArray() : Array.Empty<Item>();

    void DrawStyle()
    {
        DrawPanel("", StyleTabs);
        if (_tab == 7) { DrawTrophies(); return; }
        DrawFooter(DrawGrid(StyleItems, false) ?? "Click something you've unlocked to use it.");
    }

    void DrawTrophies()
    {
        var list = Catalog.Achievements.Skip(_page * PerPage).Take(PerPage).ToArray();
        for (int i = 0; i < list.Length; i++)
        {
            var a = list[i];
            bool got = P.HasAchievement(a.Id);
            float x = Panel.X + 16 + (i % 2) * 214, y = Panel.Bottom - 66 - (i / 2) * 41;
            Fill(Color.FromArgb(got ? 31 : 13, 255, 255, 255), x - 6, y - 6, 206, 36, 8);
            DrawText(got ? "🏆" : "🔒", x, y + 3, 15, Color.White);
            DrawText(got ? a.Name : "???", x + 26, y + 12, 11.5f, Color.White, true);
            DrawText(a.Hint, x + 26, y - 1, 9.5f, Color.FromArgb(166, 166, 166));
        }
        DrawPager(Catalog.Achievements.Length);
        DrawText($"{P.Data.Achievements.Count}/{Catalog.Achievements.Length} trophies", Panel.X + 14, Panel.Y + 9, 11, Palette.Spark, true);
    }

    // MARK: Shop

    static readonly string[] ShopTabs = { "Furniture", "Walls", "Floors", "Outfits", "Rooms" };
    Item[] ShopItems
    {
        get
        {
            var kinds = new[] { new[] { ItemKind.Decor }, new[] { ItemKind.Wallpaper }, new[] { ItemKind.Floor }, new[] { ItemKind.Skin, ItemKind.Hat, ItemKind.Accessory }, new[] { ItemKind.Room } }[Math.Min(_tab, 4)];
            return Catalog.Items.Where(i => i.Price != null && kinds.Contains(i.Kind)).ToArray();
        }
    }

    void DrawShop()
    {
        DrawPanel("", ShopTabs);
        var hover = DrawGrid(ShopItems, true);
        string footer = $"🪙 {P.Data.Coins} to spend";
        if (P.DailyDeal is Item d && P.Price(d) is int pr) footer += $"  ·  🔥 Deal: {d.Name} for 🪙{pr}";
        DrawFooter(hover ?? footer);
    }

    // MARK: Quests

    void DrawQuests()
    {
        DrawPanel("📋 Daily quests", Array.Empty<string>());
        P.RollDay();
        var d = P.Data;
        string streak = $"🔥 {d.Streak}-day streak";
        DrawText(streak, Panel.Right - 40 - TextWidth(streak, 12, true), Panel.Bottom - 29, 12, C(0xFFB74D), true);
        for (int i = 0; i < d.Quests.Count; i++)
        {
            var q = d.Quests[i];
            var def = Catalog.Quests.FirstOrDefault(x => x.Id == q.Id);
            if (def == null) continue;
            float x = Panel.X + 12, y = Panel.Bottom - 84 - i * 48, w = Panel.Width - 24;
            Fill(Color.FromArgb(q.Done ? 36 : 18, 255, 255, 255), x, y, w, 42, 9);
            DrawText(q.Done ? "✅" : "⬜", x + 10, y + 12, 15, Color.White);
            DrawText(def.Text, x + 38, y + 21, 12, Color.White, true);
            float frac = Math.Min(1, (float)q.Progress / def.Goal);
            Fill(Color.FromArgb(31, 255, 255, 255), x + 38, y + 9, 240, 7, 3.5f);
            Fill(q.Done ? C(0x7CF29A) : Palette.Spark, x + 38, y + 9, Math.Max(7, 240 * frac), 7, 3.5f);
            DrawText($"{q.Progress}/{def.Goal}", x + 286, y + 5, 10, Color.FromArgb(191, 191, 191));
            string reward = q.Done ? "Done!" : $"🪙 {def.Coins}";
            DrawText(reward, x + w - 12 - TextWidth(reward, 12, true), y + 13, 12, q.Done ? C(0x7CF29A) : Palette.Spark, true);
        }
        var left = DateTime.Today.AddDays(1) - DateTime.Now;
        bool all = d.Quests.All(q => q.Done);
        DrawText((all ? "All done today! 🎉 " : "Finish all three for a 🪙25 bonus · ") + $"new quests in {(int)left.TotalHours}h {left.Minutes}m",
                 Panel.X + 14, Panel.Bottom - 206, 10.5f, Color.FromArgb(191, 191, 191));
        Fill(Color.FromArgb(15, 255, 255, 255), Panel.X + 12, Panel.Y + 8, Panel.Width - 24, 56, 9);
        DrawText("Today", Panel.X + 24, Panel.Y + 42, 11, Palette.Orange, true);
        DrawText($"{d.SessionsToday} sessions · {d.FocusToday} min focus · {d.CookiesToday} cookies", Panel.X + 70, Panel.Y + 42, 11, Color.White);
        DrawText("All time", Panel.X + 24, Panel.Y + 20, 11, Palette.Orange, true);
        DrawText($"{d.Sessions} sessions · {d.GamesPlayed} games · {d.QuestsDone} quests · {d.FocusMinutes} min focus", Panel.X + 80, Panel.Y + 20, 11, Color.White);
    }

    // MARK: Arcade

    static RectangleF ArcadeCard(int i) => i < 3 ? new(Panel.X + 12 + i * 140, Panel.Bottom - 148, 132, 100) : new(Panel.X + 82 + (i - 3) * 140, Panel.Bottom - 256, 132, 100);

    void DrawArcade()
    {
        DrawPanel("🕹️ Arcade", Array.Empty<string>());
        var m = MousePoint();
        for (int i = 0; i < MiniGame.All.Length; i++)
        {
            var g = MiniGame.All[i];
            var r = ArcadeCard(i);
            Fill(Color.FromArgb(In(r, m) ? 46 : 20, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 12);
            if (In(r, m)) StrokeRound(Palette.Orange, 2, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), 12);
            CenterText(g.Icon, r, r.Bottom - 40, 26, Color.White);
            CenterText(g.Name, r, r.Y + 40, 12.5f, Color.White, true);
            CenterText(g.Blurb, r, r.Y + 24, 9.5f, Color.FromArgb(184, 184, 184));
            int best = P.Data.Best.TryGetValue(g.Id, out var b) ? b : 0;
            CenterText(best > 0 ? $"Best {(g.Id == "copycat" ? best + 1 : best)}" : "New!", r, r.Y + 6, 10, Palette.Spark, true);
        }
        DrawText("Every game pays XP and 🪙 coins.", Panel.X + 14, Panel.Y + 8, 10.5f, Color.FromArgb(179, 179, 179));
    }

    // MARK: Edit room

    static RectangleF DoneEditRect => new(W - 74, H - 32, 64, 24);
    static RectangleF XButton(string id) { var r = DecorRect(id); return new(r.Right - 8, r.Bottom - 8, 16, 16); }
    static IEnumerable<DecorSpec> Editable => Decor.Where(d => d.Movable && P.Placed(d.Id));
    static Item[] PutAway => Catalog.Items.Where(i => i.Kind == ItemKind.Decor && P.Has(i.Id) && !P.Placed(i.Id)).ToArray();

    List<(Item it, RectangleF r)> TrayChips()
    {
        var list = new List<(Item, RectangleF)>();
        float x = 10;
        foreach (var it in PutAway)
        {
            float w = TextWidth("+ " + it.Name, 10.5f, true) + 16;
            if (x + w > W - 6) break;
            list.Add((it, new RectangleF(x, H - 64, w, 22)));
            x += w + 6;
        }
        return list;
    }

    void DrawEdit()
    {
        var m = MousePoint();
        Fill(C(0x1E1B2E, 0.9), 0, H - 38, W, 38);
        DrawText("🛠️ Drag furniture to move it. × puts it away.", 12, H - 27, 11.5f, Color.White, true);
        Fill(Palette.Orange, DoneEditRect.X, DoneEditRect.Y, DoneEditRect.Width, DoneEditRect.Height, 8);
        CenterText("Done", DoneEditRect, DoneEditRect.Y + 4, 11.5f, Color.White, true);
        foreach (var s in Editable)
        {
            var r = DecorRect(s.Id); r.Inflate(3, 3);
            bool hot = _dragId == s.Id || In(r, m);
            StrokeRound(hot ? Palette.Spark : Color.FromArgb(179, 255, 255, 255), hot ? 2 : 1.2f, r, 6, true);
            if (hot)
            {
                var x = XButton(s.Id);
                Oval(C(0xE5484D), x.X, x.Y, x.Width, x.Height);
                DrawText("×", x.X + 3.5f, x.Y + 1, 12, Color.White, true);
            }
        }
        Fill(C(0x1E1B2E, 0.75), 0, H - 68, W, 30);
        var chips = TrayChips();
        if (chips.Count == 0) DrawText("Everything you own is in the room. Buy more in the 🛍️ shop!", 12, H - 60, 10.5f, Color.FromArgb(191, 191, 191));
        foreach (var (it, r) in chips)
        {
            Fill(Color.FromArgb(In(r, m) ? 64 : 31, 255, 255, 255), r.X, r.Y, r.Width, r.Height, 11);
            DrawText("+ " + it.Name, r.X + 8, r.Y + 4, 10.5f, Color.White, true);
        }
    }

    void EditClick(PointF pt)
    {
        if (In(DoneEditRect, pt)) { Open(Overlay.Edit); return; }
        foreach (var (it, r) in TrayChips()) if (In(r, pt)) { P.Equip(it); Synth.Pop(); return; }
        foreach (var s in Editable.Reverse())
        {
            var big = DecorRect(s.Id); big.Inflate(10, 10);
            if (In(XButton(s.Id), pt) && In(big, pt)) { if (Catalog.Find(s.Id) is Item it) { P.Equip(it); Synth.Pop(); } return; }
            var r = DecorRect(s.Id); r.Inflate(3, 3);
            if (In(r, pt)) { _dragId = s.Id; _dragStartX = pt.X; _dragStartDx = Offset(s.Id); Synth.Pop(); return; }
        }
    }

    void Drag(PointF pt)
    {
        if (Current != Overlay.Edit || _dragId == null) return;
        var s = Decor.First(d => d.Id == _dragId);
        P.Data.DecorX[_dragId] = Math.Clamp(_dragStartDx + pt.X - _dragStartX, 4 - s.Rect.X, W - 4 - s.Rect.Right);
        _moved = true;
    }

    void FinishEditing()
    {
        _dragId = null;
        P.Save();
        if (_moved) P.Achieve("decorator");
        _moved = false;
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
        if (_consoleLines.Count > 200) _consoleLines.RemoveRange(0, _consoleLines.Count - 200);
        if (cmd == "clear") { _consoleLines.Clear(); return; }
        if (cmd == "exit") { Open(Overlay.Console); return; }
        var output = OnCommand?.Invoke(cmd)?.ToList() ?? new List<string>();
        if (Current == Overlay.Console) _consoleLines.AddRange(output);
    }

    // MARK: Input

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_game != null && _game.HandleKey(keyData)) return true;
        if (keyData == Keys.Escape) { if (Current != Overlay.None) Open(Current); return true; }
        if (Current is Overlay.Style or Overlay.Shop && keyData is Keys.Left or Keys.Right)
        {
            int count = Current == Overlay.Shop ? ShopItems.Length : _tab == 7 ? Catalog.Achievements.Length : StyleItems.Length;
            _page = Math.Clamp(_page + (keyData == Keys.Right ? 1 : -1), 0, Pages(count) - 1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_game != null && !char.IsControl(e.KeyChar) && e.KeyChar != ' ' && _game.Char(e.KeyChar)) { e.Handled = true; return; }
        if (_game is TypeRushGame && e.KeyChar == ' ' && _game.Char(' ')) { e.Handled = true; return; }
        base.OnKeyPress(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _game?.KeyUp(e.KeyCode);
        base.OnKeyUp(e);
    }

    protected override void OnDeactivate(EventArgs e) { _game?.KeyUp(Keys.Left); _game?.KeyUp(Keys.Right); base.OnDeactivate(e); }

    void Wheel(int delta)
    {
        if (Current is not (Overlay.Style or Overlay.Shop)) return;
        int count = Current == Overlay.Shop ? ShopItems.Length : _tab == 7 ? Catalog.Achievements.Length : StyleItems.Length;
        int np = Math.Clamp(_page + (delta < 0 ? 1 : -1), 0, Pages(count) - 1);
        if (np != _page) { _page = np; Synth.Pop(); }
    }

    void Click_(PointF pt)
    {
        double t = Clock.Now;
        if (pt.Y < HudH)
        {
            for (int i = 0; i < HudIcons.Length; i++)
            {
                if (!In(HudButton(i), pt)) continue;
                if (i == 0) Feed();
                else if (HudOverlays[i] is Overlay o) { if (i == 1 && GameOpen) { CloseGame(); return; } Open(o); Synth.Pop(); }
                return;
            }
            if (In(CoinRect, pt)) Open(Overlay.Shop);
            return;
        }
        if (_game != null) { _game.MouseDown(pt); return; }

        switch (Current)
        {
            case Overlay.Style:
            case Overlay.Shop:
                if (In(CloseRect, pt)) { Open(Current); return; }
                if (BuyClick(pt)) return;
                var tabs = Current == Overlay.Style ? StyleTabs : ShopTabs;
                for (int i = 0; i < tabs.Length; i++) if (In(TabRect(i, tabs.Length), pt)) { _tab = i; _page = 0; _pendingBuy = null; _buyNote = ""; Synth.Pop(); return; }
                if (Current == Overlay.Style && _tab == 7) { PagerHit(pt, Catalog.Achievements.Length); return; }
                var items = Current == Overlay.Style ? StyleItems : ShopItems;
                if (PagerHit(pt, items.Length)) return;
                GridClick(pt, items);
                return;
            case Overlay.Quests:
                if (In(CloseRect, pt) || !In(Panel, pt)) Open(Overlay.Quests);
                return;
            case Overlay.Arcade:
                if (In(CloseRect, pt) || !In(Panel, pt)) { Open(Overlay.Arcade); return; }
                for (int i = 0; i < MiniGame.All.Length; i++) if (In(ArcadeCard(i), pt)) { Synth.Pop(); StartGame(MiniGame.All[i].Id); return; }
                return;
            case Overlay.Console:
                if (!In(ConsoleRect, pt)) Open(Overlay.Console);
                return;
            case Overlay.Edit:
                EditClick(pt);
                return;
        }

        var c = BuddyCenter();
        var pose = _anim.GetPose(0, 0);
        float dx = pt.X - c.X, dy = pt.Y - (c.Y + pose.Lift * RoomScale);
        if (dx * dx + dy * dy < 26 * 26) { OnPoke?.Invoke(); return; }

        void WalkTo(float x, string line)
        {
            if (_anim.Asleep || _anim.Mood != Mood.Idle) { Say(line); return; }
            _targetX = Math.Clamp(x, 30, W - 30); _arriveLine = line; _nextWander = t + 12;
        }
        bool Hit(string id) => P.Placed(id) && In(DecorRect(id), pt);
        if (P.Placed("cat") && In(new(96, 96, 30, 32), pt)) { Synth.Meow(); _catHeartAt = t; return; }
        if (Hit("arcade")) { Open(Overlay.Arcade); return; }
        if (P.Placed("lamp") && In(new(420, 112, 32, 44), pt)) { _lampOn = !_lampOn; Synth.Pop(); return; }
        if (P.Placed("records") && P.Placed("shelf") && In(new(392 + Offset("shelf"), 226, 52, 16), pt))
        {
            _musicOn = !_musicOn;
            if (_musicOn) { Synth.Done(0.75); _anim.Dance(); }
            return;
        }
        if (Hit("disco")) { _musicOn = !_musicOn; if (_musicOn) { Synth.Done(0.75); _anim.Dance(); Say("let's boogie! 🪩"); } return; }
        if (Hit("clock")) { Say($"it's {DateTime.Now:t} ⏰"); Synth.Tick(); return; }
        if (P.Placed("trophycase") && In(RectangleF.Inflate(DecorRect("trophycase"), 0, 10), pt)) { Open(Overlay.Style); _tab = 7; return; }
        if (Hit("plant")) { _plantWiggleAt = t; WalkTo(Mid(DecorRect("plant")) + 16, "*waters plant* 🌱"); return; }
        if (Hit("fishtank")) { WalkTo(Mid(DecorRect("fishtank")), "blub blub 🐟"); return; }
        if (Hit("beanbag")) { WalkTo(Mid(DecorRect("beanbag")), "*flops onto the beanbag* 😌"); return; }
        if (Hit("lavalamp")) { WalkTo(Mid(DecorRect("lavalamp")) + 16, "so mesmerising… 🫧"); return; }
        if (Hit("cactus")) { Say("ouch! pointy 🌵"); _anim.Grumpy(); return; }
        if (Hit("poster")) { Say("SHIP IT! 🚀"); _anim.Celebrate(); return; }
        if (In(new(176, 172, 118, 124), pt))
        {
            WalkTo(235, P.Data.Room switch
            {
                "space" => "Hi Earth! 🌍", "reef" => "hi fishies! 🐠", "cabin" => "it's so cold out there ❄️", "synthwave" => "outrun vibes 🌆",
                _ => IsNight ? "Look at the stars ✨" : "What a nice day ☀️",
            });
            return;
        }
        if (In(new(386, 112, 50, 32), pt)) { Say(_anim.Mood == Mood.Idle ? "Nothing running. Break time!" : "Working on it… ⌨️"); return; }
        if (In(new(14, 60, 126, 46), pt)) { WalkTo(120, "So comfy… 😴"); return; }
    }

    public void Feed()
    {
        if (_anim.Sick) { OnMedicine?.Invoke(); return; }
        if (_cookieY != null || GameOpen) return;
        if (Current != Overlay.None && Current != Overlay.Console) Open(Current);
        _cookieY = H - 20;
        Synth.Pop();
    }
}
