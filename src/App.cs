// Claude Buddy for Windows — app entry: reads Claude Code hook events, runs the corner widget,
// and turns everything that happens into XP, unlocks, moods, tantrums and secrets.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using FormsTimer = System.Windows.Forms.Timer;

namespace ClaudeBuddy;

static class Program
{
    const string MutexName = "ClaudeBuddy.SingleInstance";
    const string ComeBackEvent = "ClaudeBuddy.ComeBack";

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: tell that copy to come back to the corner, then quit.
            try { EventWaitHandle.OpenExisting(ComeBackEvent).Set(); } catch { }
            return;
        }
        using var comeBack = new EventWaitHandle(false, EventResetMode.AutoReset, ComeBackEvent);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log(e.Exception);
        var app = new BuddyApp(comeBack);
        int selfTest = Array.IndexOf(args, "--self-test");
        if (selfTest >= 0) app.SelfTest(selfTest + 1 < args.Length ? args[selfTest + 1] : "self-test");
        Application.Run(app.Corner);
    }

    public static void Log(Exception e)
    {
        try { File.AppendAllText(Path.Combine(Paths.Dir, "error.log"), $"{DateTime.Now:s} {e}\n"); } catch { }
    }
}

class Session
{
    public Mood Mood;
    public string Project = "";
    public DateTime Updated = DateTime.Now;
    public int Slot;
}

class BuddyApp
{
    public readonly CornerWindow Corner = new();
    readonly Dictionary<string, Session> _sessions = new();
    readonly EventWaitHandle _comeBack;
    readonly Progress _progress = Progress.Shared;
    readonly RageShow _rage = new();
    static readonly Random Rng = new();
    long _offset;
    string _partial = "";
    double _testFriendUntil = -100, _lastPetXp = -100, _lastTick = Clock.Now;
    int _sleepPokes;
    CopycatForm _copycat;
    ConsoleForm _console;
    RoomForm _room;
    bool _selfTesting;

    // Personality state
    double _nextIdle = Clock.Now + 25, _lastPoke = -100, _sickUntil, _nextQueasy, _tickleNext, _tickleTime, _stillSince = Clock.Now, _lastStare = -200;
    readonly List<double> _pokeTimes = new(), _cookieTimes = new();
    bool _raging;
    PointF? _flyVel;
    PointF _flyPos;
    Point _lastMouse;
    float _tickleAccum;

    Animator Buddy => Corner.Slots[0].Anim;

    public BuddyApp(EventWaitHandle comeBack)
    {
        _comeBack = comeBack;
        Directory.CreateDirectory(Paths.Dir);
        if (!File.Exists(Paths.Events)) File.WriteAllText(Paths.Events, "");
        _offset = new FileInfo(Paths.Events).Length;

        Corner.Load += (_, _) =>
        {
            var d = _progress.Data;
            if (d.X is int x && d.Y is int y && Screen.AllScreens.Any(s => s.WorkingArea.Contains(x + Corner.Width / 2, y + Corner.Height / 2))) Corner.Location = new Point(x, y);
            else ToCorner();
        };
        Corner.OnPoke = Poke;
        Corner.OnFiveClicks = DizzyClicks;
        Corner.OnShake = Shaken;
        Corner.OnBubbleClick = i => Corner.DismissBubble(i);
        Corner.OnDoubleClick = OpenRoom;
        Corner.OnMoved = SavePosition;
        Corner.OnFlick = Flick;
        Corner.OnSquishRelease = (i, amt) => { if (i == 0) SquishReleased(amt); };
        Corner.MenuProvider = MakeMenu;

        _progress.OnLevelUp = (level, items) =>
        {
            var names = string.Join(", ", items.Select(i => i.Name));
            Corner.Say(0, $"Level up! Lv {level} 🎉", names.Length == 0 ? "Keep it up!" : "Unlocked: " + names, 10, false);
            Buddy.Love(); Buddy.Celebrate(); Synth.LevelUp();
            _room?.ShowToast($"Level {level}!" + (names.Length == 0 ? "" : " Unlocked: " + names));
        };
        _progress.OnAchievement = (a, it) =>
        {
            Corner.Say(0, "🏆 " + a.Name, it != null ? "Secret unlocked: " + it.Name : $"+{a.Xp} XP", 9, false);
            _room?.ShowToast("🏆 " + a.Name + " · " + (it != null ? "Secret unlocked: " + it.Name : $"+{a.Xp} XP"));
            Synth.Unlock();
        };
        _rage.OnDefuse = () => _progress.Award(2);
        _rage.OnFinished = EndRage;

        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Corner.BeginInvoke((Action)(() => { Wake(0); Buddy.StartIdle(IdleAnim.Wave); Corner.Say(0, Pick("you're back!! 👋", "welcome back!", "oh hi! I missed you")); }));
        };

        var frame = new FormsTimer { Interval = 16 };
        frame.Tick += (_, _) => { try { FrameTick(); } catch (Exception e) { Program.Log(e); } };
        frame.Start();
        var poll = new FormsTimer { Interval = 300 };
        poll.Tick += (_, _) => { try { ReadEvents(); } catch (Exception e) { Program.Log(e); } };
        poll.Start();

        Corner.Shown += (_, _) => Corner.Say(0, $"Hi! Lv {_progress.Level} buddy reporting 👋", "Double-click me to visit my room", 6);
        After(8, () => { if (!_selfTesting) Updater.Check(this, manual: false); });
    }

    static string Pick(params string[] s) => s[Rng.Next(s.Length)];

    /// Buddy talks in his room when you're looking at it, otherwise in the corner.
    void SayHere(string line, double seconds = 3)
    {
        if (_room is { Visible: true } && Form.ActiveForm == _room) _room.Say(line, seconds);
        else Corner.Say(0, line, "", seconds);
    }

    /// Shows a bubble in the corner (used by the updater).
    public void Announce(string title, string sub) { Wake(0); Corner.Say(0, title, sub, 10, false); Synth.Chirp(); }

    // MARK: Position

    void ToCorner()
    {
        var wa = Screen.PrimaryScreen.WorkingArea;
        Corner.Location = new Point(wa.Right - Corner.Width - 6, wa.Bottom - Corner.Height - 6);
        SavePosition();
    }

    void SavePosition() { _progress.Data.X = Corner.Left; _progress.Data.Y = Corner.Top; _progress.Save(); }

    /// Called when buddy is launched again while already running, or ends up off-screen.
    void ComeBack()
    {
        _flyVel = null; Buddy.Flying = false;
        ToCorner();
        Wake(0);
        Buddy.StartIdle(IdleAnim.Wave);
        Synth.Chirp();
        Corner.Say(0, Pick("here I am! 👋", "you called? 😊", "I'm right here!"), "", 4);
    }

    // MARK: Frame loop

    void FrameTick()
    {
        if (_comeBack.WaitOne(0)) ComeBack();
        foreach (var kv in _sessions.ToList())
        {
            var quiet = (DateTime.Now - kv.Value.Updated).TotalMinutes;
            if (quiet > 60) _sessions.Remove(kv.Key);
            else if (quiet > 25 && kv.Value.Mood != Mood.Idle) kv.Value.Mood = Mood.Idle;
        }
        UpdateMoods();
        FunTick();

        bool lateNight = DateTime.Now.Hour < 6;
        foreach (var s in Corner.Slots)
        {
            if (!s.Present || s.Anim.Asleep) continue;
            if (s.Anim.Mood == Mood.Idle && !s.Anim.IsCelebrating && !s.Anim.Devil && !s.Anim.Flying && Clock.Now - s.LastActivity > (lateNight ? 180 : 600))
            {
                s.Anim.Asleep = true; _sleepPokes = 0; Synth.Yawn();
            }
        }
        // If buddy ended up somewhere you can't see (a display got unplugged), bring him back.
        if (_flyVel == null && !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(Corner.Right - 90, Corner.Bottom - 90, 80, 80)))) ComeBack();
        Corner.CheckDoubleClick();
        Corner.Render();
    }

    void UpdateMoods()
    {
        for (int i = 0; i < Corner.Slots.Length; i++)
        {
            var moods = _sessions.Values.Where(s => s.Slot == i).Select(s => s.Mood).ToList();
            Corner.Slots[i].Anim.Mood = moods.Contains(Mood.Attention) ? Mood.Attention : moods.Contains(Mood.Working) ? Mood.Working : Mood.Idle;
        }
        Corner.SetPresent(1, _sessions.Values.Any(s => s.Slot == 1) || Clock.Now < _testFriendUntil);
    }

    void Wake(int i)
    {
        var s = Corner.Slots[i];
        s.LastActivity = Clock.Now;
        if (s.Anim.Asleep) { s.Anim.Asleep = false; s.Anim.Surprise(); }
    }

    int FreeSlot()
    {
        var used = _sessions.Values.Select(s => s.Slot).ToHashSet();
        if (!used.Contains(0)) return 0;
        return used.Contains(1) ? 0 : 1;
    }

    // MARK: Claude Code events

    void ReadEvents()
    {
        var fi = new FileInfo(Paths.Events);
        if (!fi.Exists) return;
        if (fi.Length < _offset) { _offset = 0; _partial = ""; }
        if (fi.Length == _offset) return;
        using (var fs = new FileStream(Paths.Events, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            fs.Seek(_offset, SeekOrigin.Begin);
            var buf = new byte[fs.Length - _offset];
            int n = fs.Read(buf, 0, buf.Length);
            _offset += n;
            _partial += Encoding.UTF8.GetString(buf, 0, n);
        }
        int nl;
        while ((nl = _partial.IndexOf('\n')) >= 0)
        {
            var line = _partial[..nl].Trim();
            _partial = _partial[(nl + 1)..];
            if (line.Length > 0) Handle(line);
        }
        if (_offset > 1_000_000 && _partial.Length == 0)
        {
            try { File.WriteAllText(Paths.Events, ""); _offset = 0; } catch { }
        }
    }

    void Handle(string line)
    {
        JsonElement o;
        try { o = JsonDocument.Parse(line.TrimStart('﻿')).RootElement; } catch { return; }
        string Get(string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        var ev = Get("event");
        var id = Get("session_id"); if (id.Length == 0) id = "default";
        var cwd = Get("cwd");
        var project = cwd.Length == 0 ? "Claude Code" : Path.GetFileName(cwd.TrimEnd('\\', '/'));
        if (ev == "end") { _sessions.Remove(id); UpdateMoods(); return; }
        if (!_sessions.TryGetValue(id, out var s)) { s = new Session { Slot = FreeSlot() }; _sessions[id] = s; }
        s.Project = project; s.Updated = DateTime.Now;
        int i = s.Slot;
        switch (ev)
        {
            case "prompt": s.Mood = Mood.Working; _progress.Award(3); break;
            case "tool": s.Mood = Mood.Working; _progress.ToolUsed(); break;
            case "stop":
                s.Mood = Mood.Idle;
                Wake(i);
                Corner.Slots[i].Anim.Celebrate();
                Synth.Done(i == 0 ? 1 : 1.25);
                int xp = _progress.SessionFinished();
                Corner.Say(i, "All done! ✨", $"{project} · +{xp} XP", 9, false);
                break;
            case "notify":
                var msg = Get("message");
                if (Get("type") == "idle_prompt" || msg.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase)) return;
                s.Mood = Mood.Attention;
                Corner.Say(i, msg.Contains("permission", StringComparison.OrdinalIgnoreCase) ? "Needs your permission" : "Needs your attention", project, 14, false);
                Synth.Boop();
                break;
            default: return;
        }
        Wake(i);
        UpdateMoods();
    }

    // MARK: Interactions, temper, cookies

    void Poke(int i)
    {
        if (_raging || Buddy.Devil) { if (i == 0) Synth.DevilLaugh(); return; }
        var s = Corner.Slots[i];
        double t = Clock.Now;
        if (s.Anim.Asleep)
        {
            if (i == 0) Annoy(12, true);
            _sleepPokes++;
            s.Anim.Grumpy(); Synth.Grumble();
            if (_sleepPokes >= 3) { Wake(i); Corner.Say(i, "FINE. I'm up. 😤", "", 4); _progress.Achieve("grumpy", "nightcap"); }
            else Corner.Say(i, _sleepPokes == 1 ? "5 more minutes…" : "zzz… go away…", "", 3);
            return;
        }
        if (i == 0)
        {
            _pokeTimes.RemoveAll(x => t - x > 2);
            _pokeTimes.Add(t);
            bool gentle = t - _lastPoke > 1.5;
            _lastPoke = t;
            if (_pokeTimes.Count >= 4) { Annoy(9); Buddy.Grumpy(); Synth.Grumble(); return; }
            if (gentle && Buddy.Anger > 0) Calm(6);
        }
        Corner.DismissBubble(i);
        s.LastActivity = t;
        s.Anim.Pet();
        Synth.Giggle(i == 0 ? 1 : 1.25);
        if (t - _lastPetXp > 2) { _lastPetXp = t; _progress.Petted(); }
        if (Rng.Next(5) == 0) Corner.Say(i, Pick("hehe", "that tickles!", "hi!! 👋", "boop!", "*happy noises*", "you're the best"), "", 2.5);
    }

    void DizzyClicks(int i)
    {
        var s = Corner.Slots[i];
        if (s.Anim.Asleep) return;
        s.Anim.Dizzy();
        if (i == 0) Annoy(15, true);
        Synth.Dizzy();
        Corner.Say(i, "whoa… the room is spinning 😵", "", 3.5);
        _progress.Achieve("dizzy", "propeller");
    }

    void Shaken()
    {
        for (int i = 0; i < Corner.Slots.Length; i++) if (Corner.Slots[i].Present) { Wake(i); Corner.Slots[i].Anim.Dizzy(); }
        Synth.Whee();
        Annoy(18, true);
        Corner.Say(0, "WHEEEE! 🎢", "", 3);
        _progress.Achieve("whee");
    }

    void Annoy(double amount, bool quiet = false)
    {
        if (_raging || Buddy.Devil) return;
        double before = Buddy.Anger;
        Buddy.Anger = Math.Min(100, Buddy.Anger + amount);
        if (Buddy.Anger >= 100) { Rage(); return; }
        if (quiet) return;
        if (before < 70 && Buddy.Anger >= 70) { Synth.Growl(); Corner.Say(0, Pick("I'm warning you… 😡", "you're making me MAD", "grrrrr…", "one more time. I DARE you."), "", 3); }
        else if (before < 35 && Buddy.Anger >= 35) Corner.Say(0, Pick("hey!! 😠", "cut it out!", "stoppp", "rude."), "", 2.5);
    }

    void Calm(double amount) => Buddy.Anger = Math.Max(0, Buddy.Anger - amount);

    public void After(double seconds, Action a)
    {
        var t = new FormsTimer { Interval = Math.Max(1, (int)(seconds * 1000)) };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); a(); };
        t.Start();
    }

    void Rage()
    {
        if (_raging) return;
        _raging = true;
        Wake(0);
        Buddy.Sick = false;
        Synth.Growl();
        Corner.Say(0, "THAT'S IT!!! 😡", "", 1.6, false);
        After(1.0, () =>
        {
            Buddy.Devil = true;
            Synth.Transform();
            Corner.Say(0, "MWAHAHAHA 😈", "click the bombs to defuse them!", 3, false);
            After(0.4, Synth.DevilLaugh);
            After(0.9, () => _rage.Start(new PointF(Corner.Right - 46 * Corner.DpiScale, Corner.Bottom - 44 * Corner.DpiScale)));
        });
    }

    void EndRage(int defused)
    {
        _raging = false;
        Buddy.Devil = false; Buddy.Anger = 0;
        Buddy.Love(); Buddy.Celebrate();
        Synth.CalmDown();
        Corner.Say(0, "phew… sorry about that 😇", defused > 0 ? $"you defused {defused} bomb{(defused == 1 ? "" : "s")}!" : "I feel much better now", 6, false);
        _progress.Achieve("rage", "devil");
        if (defused >= 5) _progress.Achieve("defuse", "horns");
    }

    void FeedCookie()
    {
        if (_raging || Buddy.Devil) { SayHere("😈 NO.", 2); return; }
        if (Buddy.Sick)
        {
            Synth.Burp(); Annoy(8, true);
            SayHere(Pick("no more cookies… 🤢", "please… no…", "I can't even look at it"));
            return;
        }
        Wake(0);
        Buddy.ChompNow();
        Synth.Chomp();
        SayHere(_progress.FedCookie() ? Pick("nom nom nom 🍪", "yummy!", "crunchy!") : "I'm stuffed!");
        double t = Clock.Now;
        _cookieTimes.RemoveAll(x => t - x > 600);
        _cookieTimes.Add(t);
        Calm(10);
        if (_cookieTimes.Count == 4) SayHere("getting full… 😮‍💨");
        else if (_cookieTimes.Count == 5) SayHere("one more and I'll pop");
        else if (_cookieTimes.Count >= 6) GetSick();
    }

    void GetSick()
    {
        Buddy.Sick = true;
        _sickUntil = Clock.Now + 180; _nextQueasy = Clock.Now + 6;
        Synth.Burp();
        Corner.Say(0, "uh oh… I don't feel so good 🤢", "right-click me → Give medicine", 6);
        _progress.Achieve("sick", "slime");
    }

    void GiveMedicine()
    {
        if (!Buddy.Sick) { SayHere("I'm not sick! …but thanks 💊"); return; }
        Buddy.Sick = false;
        _cookieTimes.Clear();
        Synth.Heal(); Buddy.Love();
        SayHere("much better!! thank you 🥹", 4);
    }

    void Honk()
    {
        if (_raging) return;
        Wake(0); Synth.Honk(); Buddy.Scare();
        Corner.Say(0, Pick("AAH! 😱", "don't DO that!!", "my heart!! 💔"), "", 2.5);
        Annoy(12, Buddy.Anger < 30);
    }

    void TeaseCookie()
    {
        if (_raging) return;
        Wake(0); Buddy.Love();
        Corner.Say(0, "a cookie?! for ME?? 🤩🍪", "", 1.6);
        After(1.7, () =>
        {
            Synth.Grumble(); Buddy.Grumpy();
            Corner.Say(0, "…HEY! that's just mean 😤", "", 3);
            Annoy(22, true);
            _progress.Achieve("tease");
        });
    }

    void Flick(PointF v)
    {
        if (_raging) return;
        _flyVel = new PointF(Math.Clamp(v.X, -3500, 3500), Math.Clamp(v.Y, -3500, 3500));
        _flyPos = new PointF(Corner.Right - 46 * Corner.DpiScale, Corner.Bottom - 44 * Corner.DpiScale);
        Buddy.Flying = true;
        Wake(0);
        Synth.Whee();
    }

    void SquishReleased(float amount)
    {
        Synth.Boing();
        if (amount > 0.6f)
        {
            _progress.Achieve("squish");
            Annoy(8, true);
            Corner.Say(0, "I'm not a stress ball!! 😤", "", 2.5);
        }
    }

    void FlyStep(float dt)
    {
        if (_flyVel is not PointF v) return;
        var area = Screen.FromPoint(Point.Round(_flyPos)).WorkingArea;
        float sc = Corner.DpiScale;
        var p = _flyPos;
        v.Y += 2600 * sc * dt;
        p.X += v.X * dt; p.Y += v.Y * dt;
        float minX = area.Left + 34 * sc, maxX = area.Right - 34 * sc, minY = area.Top + 36 * sc, maxY = area.Bottom - 40 * sc;
        if (p.X < minX) { p.X = minX; v.X = -v.X * 0.6f; Synth.Thud(); }
        if (p.X > maxX) { p.X = maxX; v.X = -v.X * 0.6f; Synth.Thud(); }
        if (p.Y < minY) { p.Y = minY; v.Y = -v.Y * 0.6f; Synth.Thud(); }
        if (p.Y > maxY)
        {
            p.Y = maxY;
            if (Math.Abs(v.Y) > 260 * sc) Synth.Thud();
            v.Y = Math.Abs(v.Y) < 260 * sc ? 0 : -v.Y * 0.5f;
            v.X *= 0.82f;
        }
        _flyPos = p;
        Corner.Location = new Point((int)(p.X - (Corner.Width - 46 * sc)), (int)(p.Y - (Corner.Height - 44 * sc)));
        if (p.Y >= maxY && v.Y == 0 && Math.Abs(v.X) < 30)
        {
            _flyVel = null;
            Buddy.Flying = false;
            Buddy.Dizzy();
            SavePosition();
            Corner.Say(0, Pick("WHEEE— oof. 😵", "did you just YEET me?!", "again! again!… no wait"), "", 3);
            _progress.Achieve("yeet");
            Annoy(10, true);
        }
        else _flyVel = v;
    }

    void FunTick()
    {
        double t = Clock.Now;
        float dt = (float)Math.Min(0.05, t - _lastTick);
        _lastTick = t;
        if (!_raging) Buddy.Anger = Math.Max(0, Buddy.Anger - dt * 2.2);
        FlyStep(dt);

        if (Corner.HoldIndex == 0) Buddy.Squish = (float)Math.Clamp(t - Corner.HoldStart - 0.25, 0, 1);
        else if (Buddy.Squish > 0) Buddy.Squish = Math.Max(0, Buddy.Squish - dt * 6);

        var m = Cursor.Position;
        float sc = Corner.DpiScale;
        var home = new PointF(Corner.Right - 46 * sc, Corner.Bottom - 44 * sc);
        bool inside = Math.Sqrt(Math.Pow(m.X - home.X, 2) + Math.Pow(m.Y - home.Y, 2)) < 34 * sc && _flyVel == null;
        float moved = (float)Math.Sqrt(Math.Pow(m.X - _lastMouse.X, 2) + Math.Pow(m.Y - _lastMouse.Y, 2)) / sc;
        _lastMouse = m;
        if (inside) _tickleAccum += moved;
        _tickleAccum *= 0.97f;
        if (moved > 1.5f || !inside) _stillSince = t;
        if (_tickleAccum > 700 && !Buddy.Asleep && !_raging)
        {
            _tickleTime += dt;
            if (t > _tickleNext)
            {
                _tickleNext = t + 0.45;
                Buddy.Pet(); Synth.Giggle();
                if (_tickleTime > 3.5) { Annoy(6, true); if (Rng.Next(3) == 0) Corner.Say(0, Pick("okay okay STOP 😂", "hahaha no— stop!!", "I can't breathe 🤣"), "", 1.5); }
            }
            _progress.Achieve("tickle");
        }
        else if (_tickleAccum < 200) _tickleTime = 0;
        if (inside && t - _stillSince > 2.8 && t - _lastStare > 120 && !Buddy.Asleep)
        {
            _lastStare = t; Buddy.Pet();
            Corner.Say(0, "um… why are you staring at me? 😳", "", 3);
        }

        if (Buddy.Sick)
        {
            if (t > _sickUntil) { Buddy.Sick = false; Corner.Say(0, "phew, feeling better 😌", "", 3); }
            else if (t > _nextQueasy)
            {
                _nextQueasy = t + 9 + Rng.NextDouble() * 7;
                if (Rng.Next(2) == 0) { Synth.Burp(); Corner.Say(0, "*burp* 🤢", "", 2); }
                else { Buddy.Barf(); Synth.Hurl(); Corner.Say(0, "hurk! 🤮", "", 2); }
            }
        }

        if (t > _nextIdle)
        {
            _nextIdle = t + 18 + Rng.NextDouble() * 30;
            if (Buddy.Mood != Mood.Idle || Buddy.Asleep || Buddy.Sick || Buddy.Devil || Buddy.Anger >= 35) return;
            var options = new List<IdleAnim> { IdleAnim.Yawn, IdleAnim.LookAround, IdleAnim.Juggle, IdleAnim.Whistle, IdleAnim.Sneeze, IdleAnim.Spin, IdleAnim.Hop, IdleAnim.Read, IdleAnim.Wave };
            int hour = DateTime.Now.Hour;
            if (hour >= 6 && hour < 11) { options.Add(IdleAnim.Coffee); options.Add(IdleAnim.Coffee); }
            var a = options[Rng.Next(options.Count)];
            Buddy.StartIdle(a);
            if (a == IdleAnim.Whistle) Synth.Whistle();
            else if (a == IdleAnim.Yawn) Synth.Yawn();
            else if (a == IdleAnim.Sneeze) After(1.1, Synth.Sneeze);
            else if (a == IdleAnim.Hop) Synth.Boing();
            if (Rng.Next(3) == 0)
            {
                var pool = new List<string> { "is a hotdog a sandwich? 🌭", "I wonder if bugs have feelings 🐞", "semicolons are just winking periods ;)",
                    "I could really go for a cookie 🍪", "do you think Claude dreams in markdown?", "I just counted my rays. still 12.",
                    "you're doing great btw ✨", "have you had water today? 💧", "it works on my machine 🤷", "naming things is hard",
                    "bored… pet me? 👉👈", "git push --force? brave.", "what if the cloud is just someone else's computer ☁️" };
                if (hour >= 1 && hour < 5) pool.Add("it's late… go to bed? 🥱");
                if (DateTime.Now.DayOfWeek == DayOfWeek.Friday) pool.Add("it's FRIDAY!! 🎉");
                Corner.Say(0, pool[Rng.Next(pool.Count)], "", 4.5);
            }
        }
    }

    // MARK: Windows

    void OpenCopycat()
    {
        if (_copycat is { IsDisposed: false }) { _copycat.Activate(); return; }
        _copycat = new CopycatForm(Buddy);
        _copycat.OnEnd = score =>
        {
            if (score > _progress.Data.BestCopycat) _progress.Data.BestCopycat = score;
            _progress.Award(Math.Min(60, score * 3));
            if (score + 1 >= 8) _progress.Achieve("copycat", "headband");
            _progress.Save();
        };
        _copycat.Show();
    }

    void OpenRoom() => OpenRoom(RoomForm.Overlay.None);

    void OpenRoom(RoomForm.Overlay overlay)
    {
        if (_room is not { IsDisposed: false })
        {
            _room = new RoomForm(Buddy)
            {
                OnPoke = () => Poke(0),
                OnFeed = FeedCookie,
                OnMedicine = GiveMedicine,
                OnCopycat = OpenCopycat,
                OnCommand = Command,
                OnGameOver = _progress.GameOver,
            };
        }
        _room.Show();
        if (_room.WindowState == FormWindowState.Minimized) _room.WindowState = FormWindowState.Normal;
        _room.Activate();
        if (overlay != RoomForm.Overlay.None && _room.Current != overlay) _room.Open(overlay);
    }

    /// CI check: unlocks everything, opens every screen of the room, saves a screenshot of
    /// each into `dir`, then quits (exit code 1 if anything logged an error).
    public void SelfTest(string dir)
    {
        _selfTesting = true;
        Directory.CreateDirectory(dir);
        var d = _progress.Data;
        d.Xp = 100_000;
        foreach (var it in Catalog.Items) if (!d.Unlocked.Contains(it.Id)) d.Unlocked.Add(it.Id);
        d.Achievements.AddRange(Catalog.Achievements.Take(6).Select(a => a.Id));
        var steps = new List<(string name, Action act)>
        {
            ("room-studio", () => OpenRoom()),
            ("room-working", () => { _testFriendUntil = 0; _sessions["selftest"] = new Session { Mood = Mood.Working }; }),
            ("room-greenhouse", () => { _sessions.Clear(); d.Room = "greenhouse"; }),
            ("room-space", () => d.Room = "space"),
            ("room-beach", () => { d.Room = "beach"; _room.Say("hi from the beach! 🏖️"); }),
            ("wardrobe-skins", () => { d.Room = "studio"; _room.Open(RoomForm.Overlay.Wardrobe); }),
            ("wardrobe-hats", () => _room.SelectTab(1)),
            ("wardrobe-rooms", () => _room.SelectTab(2)),
            ("wardrobe-trophies", () => _room.SelectTab(3)),
            ("console", () => { _room.Open(RoomForm.Overlay.Console); _room.RunCommand("help"); _room.RunCommand("stats"); }),
            ("bugcatch", () => _room.Open(RoomForm.Overlay.Game)),
            ("bugcatch-2", () => { }),
            ("asleep", () => { _room.Open(RoomForm.Overlay.Game); Buddy.Asleep = true; }),
        };
        int i = 0;
        void Next()
        {
            if (i > 0)
            {
                using var bmp = new Bitmap(_room.Width, _room.Height);
                _room.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(dir, $"{i:00}-{steps[i - 1].name}.png"));
            }
            if (i == steps.Count)
            {
                bool failed = File.Exists(Path.Combine(Paths.Dir, "error.log"));
                Environment.Exit(failed ? 1 : 0);
            }
            try { steps[i].act(); } catch (Exception e) { Program.Log(e); }
            i++;
            After(i == 11 || i == 12 ? 3 : 1.5, Next);
        }
        After(1, Next);
    }

    void OpenConsole()
    {
        if (_console is { IsDisposed: false }) { _console.Activate(); return; }
        _console = new ConsoleForm(Command);
        _console.Show();
    }

    IEnumerable<string> Command(string raw)
    {
        var cmd = raw.ToLowerInvariant().Trim();
        string[] insults = { "bad buddy", "stupid", "dumb", "ugly", "i hate you", "you suck", "idiot", "loser", "useless" };
        if (insults.Any(x => cmd.Contains(x))) { Annoy(25); Buddy.Grumpy(); Synth.Grumble(); return new[] { Pick("RUDE. 😤", "excuse me?!", "I'm telling Claude.", "wow. okay. noted. 📝") }; }
        if (cmd.StartsWith("sudo"))
        {
            if (!cmd.Contains("buddy")) return new[] { "Nice try. 😏" };
            Buddy.Celebrate(); Synth.LevelUp();
            _progress.Achieve("root", "rainbow");
            return new[] { "[sudo] password for buddy: ********", "buddy is not in the sudoers file. This incident will be reported.", "…just kidding. Root access granted 🌈 (try: wear rainbow)" };
        }
        if (cmd.StartsWith("wear "))
        {
            var name = cmd[5..];
            var it = Catalog.Items.FirstOrDefault(x => (x.Id == name || x.Name.ToLowerInvariant() == name) && x.Kind != ItemKind.Decor);
            if (it == null) return new[] { $"no item called '{name}'" };
            if (!_progress.IsUnlocked(it)) return new[] { $"🔒 {it.Name} is still locked" };
            _progress.Equip(it); Buddy.Celebrate(); Synth.Unlock();
            return new[] { $"now wearing {it.Name} ✨" };
        }
        switch (cmd)
        {
            case "help": case "?":
                return new[] { "commands: help feed pet dance sleep wake stats wear <item>", "  honk joke sorry meds mood juggle whistle read sneeze", "  yawn coffee wave room bugcatch copycat ls whoami clear exit", "(and a few secrets… 👀)" };
            case "boo": Wake(0); Buddy.Surprise(); Synth.Scream(); _progress.Achieve("boo", "ghost"); return new[] { "AAAAH! 👻", "…oh. it's you." };
            case "dance": Wake(0); Buddy.Dance(); Synth.Done(0.8); return new[] { "💃 *dances*" };
            case "feed": case "cookie": FeedCookie(); return new[] { "🍪" };
            case "pet": Poke(0); return new[] { "*happy noises*" };
            case "sleep": Buddy.Asleep = true; _sleepPokes = 0; Synth.Yawn(); return new[] { "goodnight 🌙" };
            case "wake": case "wake up": Wake(0); return new[] { "☀️ good morning!" };
            case "stats":
                var d = _progress.Data;
                return new[] { $"level {_progress.Level} · {d.Xp} XP", $"sessions {d.Sessions} · cookies {d.Cookies} · pets {d.Pets}", $"best bug catch {d.BestScore} · best copycat {d.BestCopycat}", $"trophies {d.Achievements.Count}/{Catalog.Achievements.Length}" };
            case "sorry": case "i'm sorry": case "im sorry": Calm(45); Buddy.Love(); return new[] { "…okay. I forgive you 🥺" };
            case "honk": Honk(); return new[] { "📯 HONK" };
            case "meds": case "medicine": GiveMedicine(); return new[] { "💊 here you go" };
            case "mood":
                return new[] { Buddy.Sick ? "feeling sick 🤢" : Buddy.Devil ? "😈😈😈" : Buddy.Anger > 70 ? $"FURIOUS 😡 ({(int)Buddy.Anger}/100)" : Buddy.Anger > 35 ? $"annoyed 😠 ({(int)Buddy.Anger}/100)" : $"happy 😊 ({(int)Buddy.Anger}/100 grumpy)" };
            case "joke":
                After(1.2, () => { Buddy.Celebrate(); Synth.Giggle(); });
                return Pick("Why do programmers prefer dark mode?|Because light attracts bugs. 🐛", "I told my code a joke…|it didn't get it. no sense of humour, just syntax.",
                            "Why did the developer go broke?|He used up all his cache. 💸", "A SQL query walks into a bar, sees two tables and asks…|\"Can I JOIN you?\"").Split('|');
            case "rage": case "tantrum": case "devil": return new[] { "nice try. you have to EARN it 😈" };
            case "juggle": case "whistle": case "read": case "sneeze": case "yawn": case "coffee": case "wave":
                var anim = Enum.Parse<IdleAnim>(char.ToUpper(cmd[0]) + cmd[1..]);
                Buddy.StartIdle(anim);
                if (cmd == "whistle") Synth.Whistle();
                if (cmd == "sneeze") After(1.1, Synth.Sneeze);
                return new[] { $"*{cmd}s*" };
            case "copycat": OpenCopycat(); return new[] { "🎵 let's play!" };
            case "room": OpenRoom(); return new[] { "🏠 welcome in!" };
            case "bugcatch": case "bug catch": case "play": OpenRoom(RoomForm.Overlay.Game); return new[] { "🐞 catch 'em all!" };
            case "version": return new[] { $"Claude Buddy v{Updater.CurrentText}" };
            case "update": Updater.Check(this, manual: true); return new[] { "checking for updates…" };
            case "xyzzy": _progress.Achieve("xyzzy"); return new[] { "Nothing happens.", "…or does it? ✨" };
            case "ls": return new[] { "desk.txt  bed.txt  cookie_jar\\" };
            case "dir": return new[] { "desk.txt  bed.txt  cookie_jar\\  (.secrets hidden)" };
            case "cd .secrets": case "type .secrets": return new[] { "Access is denied. (have you tried being root?)" };
            case "whoami": return new[] { "my favourite human" };
            case "42": return new[] { "the answer to everything ✨" };
            case "hi": case "hello": case "hey": Buddy.Pet(); Synth.Giggle(); return new[] { "hi!! 👋" };
            default: return new[] { $"'{raw}' is not recognized as an internal or external buddy command." };
        }
    }

    // MARK: Menu

    ContextMenuStrip MakeMenu()
    {
        var menu = new ContextMenuStrip();
        void Add(string text, Action a, bool enabled = true, bool check = false)
        {
            var it = new ToolStripMenuItem(text) { Enabled = enabled, Checked = check };
            it.Click += (_, _) => a();
            menu.Items.Add(it);
        }
        Add($"Level {_progress.Level} · {_progress.Data.Xp} XP", () => { }, false);
        double anger = Buddy.Anger;
        Add(Buddy.Sick ? "Mood: sick 🤢" : anger > 70 ? "Mood: FURIOUS 😡" : anger > 35 ? "Mood: annoyed 😠" : "Mood: happy 😊", () => { }, false);
        menu.Items.Add(new ToolStripSeparator());
        Add("Visit buddy's room 🏠", OpenRoom);
        Add("Feed a cookie 🍪", FeedCookie);
        menu.Items.Add(Wardrobe("Skins", ItemKind.Skin));
        menu.Items.Add(Wardrobe("Hats", ItemKind.Hat));
        menu.Items.Add(Wardrobe("Rooms", ItemKind.Room));
        Add("Play Bug Catch 🐞", () => OpenRoom(RoomForm.Overlay.Game));
        Add("Play Copycat 🎵", OpenCopycat);
        Add("Console…", OpenConsole);
        Add("Trophies…", ShowTrophies);
        menu.Items.Add(new ToolStripSeparator());
        Add("Honk at buddy 📯", Honk);
        Add("Tease with a cookie 😈", TeaseCookie);
        Add("Give medicine 💊", GiveMedicine);
        menu.Items.Add(new ToolStripSeparator());
        Add("Test celebration", () => { Wake(0); Buddy.Celebrate(); Synth.Done(); Corner.Say(0, "All done! ✨", "test-project", 8, false); });
        Add("Test with friend", () =>
        {
            _testFriendUntil = Clock.Now + 12; UpdateMoods();
            Corner.Say(1, "Hi, I'm the second session!", "other-project", 4);
            After(2.5, () => { Corner.Slots[1].Anim.Celebrate(); Synth.Done(1.25); Corner.Say(1, "All done! ✨", "other-project", 8, false); });
        });
        Add("Test: tantrum 😈", Rage);
        Add("Sounds", () => Synth.Muted = !Synth.Muted, check: !Synth.Muted);
        Add("Start with Windows", ToggleStartup, check: StartsWithWindows);
        Add("Move back to corner", ComeBack);
        Add(Updater.Available is string v ? $"Update to v{v} ✨" : $"Check for updates (v{Updater.CurrentText})", () => Updater.Check(this, manual: true));
        menu.Items.Add(new ToolStripSeparator());
        Add("Quit Claude Buddy", () => Application.Exit());
        return menu;
    }

    ToolStripMenuItem Wardrobe(string title, ItemKind kind)
    {
        var root = new ToolStripMenuItem(title);
        foreach (var it in Catalog.Items.Where(x => x.Kind == kind))
        {
            bool unlocked = _progress.IsUnlocked(it);
            bool wearing = (kind switch { ItemKind.Skin => _progress.Data.Skin, ItemKind.Hat => _progress.Data.Hat, _ => _progress.Data.Room }) == it.Id;
            string label = unlocked ? it.Name : it.Level is int l ? $"🔒 {it.Name} (Lv {l})" : $"🔒 Secret — {it.Hint}";
            var mi = new ToolStripMenuItem(label) { Checked = wearing, Enabled = unlocked };
            mi.Click += (_, _) => { _progress.Equip(it); Buddy.Celebrate(); Synth.Unlock(); };
            root.DropDownItems.Add(mi);
        }
        return root;
    }

    void ShowTrophies()
    {
        var lines = Catalog.Achievements.Select(a => _progress.HasAchievement(a.Id) ? $"🏆 {a.Name} — {a.Hint}" : $"🔒 ??? — {a.Hint}");
        var d = _progress.Data;
        MessageBox.Show(string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine +
                        $"Sessions: {d.Sessions}   Cookies: {d.Cookies}   Pets: {d.Pets}   Best Bug Catch: {d.BestScore}   Best Copycat: {d.BestCopycat}",
                        "Buddy's trophies", MessageBoxButtons.OK, MessageBoxIcon.None);
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool StartsWithWindows
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("ClaudeBuddy") != null; }
    }

    static void ToggleStartup()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (k.GetValue("ClaudeBuddy") != null) k.DeleteValue("ClaudeBuddy");
        else k.SetValue("ClaudeBuddy", $"\"{Application.ExecutablePath}\"");
    }
}
