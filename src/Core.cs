// Shared basics: clock, paths, colours, the unlock catalogue, and XP / save-file handling.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClaudeBuddy;

static class Clock
{
    static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Now => Sw.Elapsed.TotalSeconds;
}

static class Paths
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "claude-buddy");
    public static string Events => Path.Combine(Dir, "events.jsonl");
    public static string Save => Path.Combine(Dir, "save.json");
}

static class Palette
{
    public static Color Rgb(uint hex, int a = 255) => Color.FromArgb(a, (int)((hex >> 16) & 0xff), (int)((hex >> 8) & 0xff), (int)(hex & 0xff));
    public static readonly Color Orange = Rgb(0xD97757);
    public static readonly Color Blue = Rgb(0x548FEB);
    public static readonly Color Spark = Rgb(0xFAC75C);
    public static readonly Color Face = Rgb(0x2E1A14);

    public static Color Blend(Color a, Color b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
    }

    public static Color Alpha(Color c, double a) => Color.FromArgb((int)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);
}

enum Mood { Idle, Working, Attention }
enum ItemKind { Skin, Hat }

record Item(string Id, ItemKind Kind, string Name, int? Level, string Hint);
record Achievement(string Id, string Name, string Hint, int Xp);

static class Catalog
{
    public static readonly Item[] Items =
    {
        new("classic", ItemKind.Skin, "Classic", 1, ""),
        new("sky", ItemKind.Skin, "Sky", 1, ""),
        new("mint", ItemKind.Skin, "Mint", 3, ""),
        new("grape", ItemKind.Skin, "Grape", 5, ""),
        new("midnight", ItemKind.Skin, "Midnight", 8, ""),
        new("gold", ItemKind.Skin, "Golden", 12, ""),
        new("rainbow", ItemKind.Skin, "Rainbow", null, "Try being root."),
        new("ghost", ItemKind.Skin, "Ghost", null, "Say something spooky in the console."),
        new("devil", ItemKind.Skin, "Little Devil", null, "Make buddy REALLY angry."),
        new("slime", ItemKind.Skin, "Slime", null, "Too much of a good thing…"),

        new("none", ItemKind.Hat, "No hat", 1, ""),
        new("party", ItemKind.Hat, "Party hat", 2, ""),
        new("beanie", ItemKind.Hat, "Beanie", 4, ""),
        new("headphones", ItemKind.Hat, "Headphones", 6, ""),
        new("wizard", ItemKind.Hat, "Wizard hat", 9, ""),
        new("crown", ItemKind.Hat, "Crown", 15, ""),
        new("propeller", ItemKind.Hat, "Propeller cap", null, "Make buddy dizzy."),
        new("chef", ItemKind.Hat, "Chef hat", null, "Feed buddy 10 cookies."),
        new("nightcap", ItemKind.Hat, "Nightcap", null, "Keep poking a sleeping buddy."),
        new("horns", ItemKind.Hat, "Devil horns", null, "Defuse 5 bombs in one tantrum."),
        new("headband", ItemKind.Hat, "Headband", null, "Reach round 8 in Copycat."),
    };

    public static readonly Achievement[] Achievements =
    {
        new("dizzy", "Dizzy!", "Click buddy five times, fast.", 30),
        new("whee", "Wheee!", "Shake buddy around.", 30),
        new("cookies", "Cookie Monster", "Feed 10 cookies.", 40),
        new("root", "Root Access", "Type a very powerful command.", 50),
        new("boo", "Spooky", "Scare buddy.", 30),
        new("grumpy", "Five More Minutes", "Bother buddy while it sleeps.", 30),
        new("night", "Night Shift", "Finish a session between midnight and 5am.", 50),
        new("marathon", "Marathon", "Finish 10 sessions in one day.", 100),
        new("xyzzy", "Adventurer", "An old magic word.", 20),
        new("rage", "Unleashed", "Push buddy over the edge.", 60),
        new("defuse", "Bomb Squad", "Defuse 5 bombs in one tantrum.", 60),
        new("sick", "Tummy Ache", "Too many cookies.", 30),
        new("yeet", "Yeet!", "Fling buddy across the screen.", 30),
        new("squish", "Stress Ball", "Squish buddy.", 20),
        new("tickle", "Tickle Monster", "Wiggle the cursor on buddy.", 20),
        new("tease", "Rude!", "Take back a cookie.", 20),
        new("copycat", "Copycat Champ", "Reach round 8 in Copycat.", 60),
    };

    public static Item Find(string id) => Items.FirstOrDefault(i => i.Id == id);
}

class SaveData
{
    public int Xp { get; set; }
    public List<string> Unlocked { get; set; } = new();
    public List<string> Achievements { get; set; } = new();
    public string Skin { get; set; } = "classic";
    public string Hat { get; set; } = "none";
    public int Cookies { get; set; }
    public int Sessions { get; set; }
    public int Pets { get; set; }
    public int BestCopycat { get; set; }
    public string Day { get; set; } = "";
    public int ToolXpToday { get; set; }
    public int CookiesToday { get; set; }
    public int SessionsToday { get; set; }
    public bool Muted { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
}

class Progress
{
    public static readonly Progress Shared = new();
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SaveData Data { get; private set; }
    public Action<int, List<Item>> OnLevelUp;
    public Action<Achievement, Item> OnAchievement;

    Progress()
    {
        try { Data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(Paths.Save), Json); } catch { }
        Data ??= new SaveData();
    }

    public static int XpNeeded(int level) => 25 * (level - 1) * level;

    public int Level
    {
        get { int l = 1; while (Data.Xp >= XpNeeded(l + 1)) l++; return l; }
    }

    public bool IsUnlocked(Item it) => it.Level is int l ? Level >= l : Data.Unlocked.Contains(it.Id);
    public bool HasAchievement(string id) => Data.Achievements.Contains(id);

    public void Award(int xp)
    {
        if (xp <= 0) return;
        int before = Level;
        Data.Xp += xp;
        Save();
        int after = Level;
        if (after > before)
            OnLevelUp?.Invoke(after, Catalog.Items.Where(i => i.Level is int l && l > before && l <= after).ToList());
    }

    /// Grants an achievement (once) and the secret item tied to it, if any.
    public void Achieve(string id, string unlocking = null)
    {
        var a = Catalog.Achievements.FirstOrDefault(x => x.Id == id);
        if (a == null || Data.Achievements.Contains(id)) return;
        Data.Achievements.Add(id);
        Item it = null;
        if (unlocking != null && !Data.Unlocked.Contains(unlocking)) { Data.Unlocked.Add(unlocking); it = Catalog.Find(unlocking); }
        Save();
        OnAchievement?.Invoke(a, it);
        Award(a.Xp);
    }

    public void Equip(Item it)
    {
        if (!IsUnlocked(it)) return;
        if (it.Kind == ItemKind.Skin) Data.Skin = it.Id; else Data.Hat = it.Id;
        Save();
    }

    void RollDay()
    {
        var d = DateTime.Now.ToString("yyyy-MM-dd");
        if (d != Data.Day) { Data.Day = d; Data.ToolXpToday = 0; Data.CookiesToday = 0; Data.SessionsToday = 0; }
    }

    public void ToolUsed()
    {
        RollDay();
        if (Data.ToolXpToday >= 80) return;
        Data.ToolXpToday++;
        Award(1);
    }

    public int SessionFinished()
    {
        RollDay();
        Data.Sessions++;
        Data.SessionsToday++;
        int xp = Data.SessionsToday == 1 ? 45 : 25;
        Award(xp);
        if (DateTime.Now.Hour < 5) Achieve("night");
        if (Data.SessionsToday >= 10) Achieve("marathon");
        return xp;
    }

    /// Returns false when buddy is too full for more XP from cookies today.
    public bool FedCookie()
    {
        RollDay();
        Data.Cookies++;
        if (Data.Cookies >= 10) Achieve("cookies", "chef");
        if (Data.CookiesToday >= 12) { Save(); return false; }
        Data.CookiesToday++;
        Award(2);
        return true;
    }

    public void Petted() { Data.Pets++; Award(1); }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Dir);
            File.WriteAllText(Paths.Save, JsonSerializer.Serialize(Data, Json));
        }
        catch { }
    }
}
