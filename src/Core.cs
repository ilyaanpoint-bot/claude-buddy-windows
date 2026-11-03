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
enum ItemKind { Skin, Hat, Accessory, Room, Decor, Wallpaper, Floor }

record Item(string Id, ItemKind Kind, string Name, int? Level, string Hint, int? Price = null);
record Achievement(string Id, string Name, string Hint, int Xp);
record QuestDef(string Id, string Text, int Goal, int Coins, bool Best);
record GameReward(int Xp, int Coins, bool NewBest);

class Quest { public string Id { get; set; } = ""; public int Progress { get; set; } public bool Done { get; set; } }

static class Catalog
{
    static Item S(string id, string name, int? lv, string hint = "", int? price = null) => new(id, ItemKind.Skin, name, lv, hint, price);
    static Item Ht(string id, string name, int? lv, string hint = "", int? price = null) => new(id, ItemKind.Hat, name, lv, hint, price);
    static Item A(string id, string name, int? lv, string hint = "", int? price = null) => new(id, ItemKind.Accessory, name, lv, hint, price);
    static Item Rm(string id, string name, int? lv, string hint = "", int? price = null) => new(id, ItemKind.Room, name, lv, hint, price);
    static Item D(string id, string name, int? lv, int? price = null) => new(id, ItemKind.Decor, name, lv, "", price);
    static Item Wp(string id, string name, int? lv, int? price = null) => new(id, ItemKind.Wallpaper, name, lv, "", price);
    static Item Fl(string id, string name, int? lv, int? price = null) => new(id, ItemKind.Floor, name, lv, "", price);

    public static readonly Item[] Items =
    {
        S("classic", "Classic", 1), S("sky", "Sky", 1), S("bubblegum", "Bubblegum", 2), S("mint", "Mint", 3), S("grape", "Grape", 5),
        S("ocean", "Ocean", 6), S("midnight", "Midnight", 8), S("strawberry", "Strawberry", 10), S("gold", "Golden", 12),
        S("panda", "Panda", 16), S("lava", "Lava", 20), S("robot", "Robot", null, price: 180), S("galaxy", "Galaxy", null, price: 260),
        S("rainbow", "Rainbow", null, "Try being root."), S("ghost", "Ghost", null, "Say something spooky in the console."),
        S("devil", "Little Devil", null, "Make buddy REALLY angry."), S("slime", "Slime", null, "Too much of a good thing…"),
        S("cookie", "Cookie", null, "Feed buddy 100 cookies."), S("shadow", "Shadow", null, "Visit the room at 3 in the morning."),

        Ht("none", "No hat", 1), Ht("party", "Party hat", 2), Ht("bow", "Bow", 3), Ht("beanie", "Beanie", 4), Ht("headphones", "Headphones", 6),
        Ht("cowboy", "Cowboy hat", 7), Ht("wizard", "Wizard hat", 9), Ht("grad", "Grad cap", 11), Ht("flower", "Flower crown", 13),
        Ht("crown", "Crown", 15), Ht("santa", "Santa hat", null, price: 90), Ht("pirate", "Pirate hat", null, price: 120),
        Ht("viking", "Viking helmet", null, price: 150), Ht("halo", "Halo", null, price: 200),
        Ht("propeller", "Propeller cap", null, "Make buddy dizzy."), Ht("chef", "Chef hat", null, "Feed buddy 10 cookies."),
        Ht("nightcap", "Nightcap", null, "Keep poking a sleeping buddy."), Ht("horns", "Devil horns", null, "Defuse 5 bombs in one tantrum."),
        Ht("headband", "Headband", null, "Reach round 8 in Copycat."),

        A("none-acc", "Nothing", 1), A("sunglasses", "Sunglasses", 2), A("glasses", "Nerd glasses", 4), A("bowtie", "Bow tie", 6),
        A("scarf", "Scarf", 9), A("mustache", "Mustache", null, price: 60), A("monocle", "Monocle", null, price: 140),
        A("medal", "Gold medal", null, "Complete 10 daily quests."),

        Rm("studio", "Cozy Studio", 1), Rm("greenhouse", "Greenhouse", 7), Rm("cabin", "Snowy Cabin", 9), Rm("space", "Space Station", 11),
        Rm("synthwave", "Synthwave", null, price: 300), Rm("reef", "Coral Reef", null, price: 300), Rm("beach", "Beach Hut", null, "Score 20 in Bug Catch."),

        D("plant", "Plant", 2), D("poster", "Poster", 3), D("lamp", "Desk lamp", 4), D("shelf", "Bookshelf", 5), D("rug", "Rug", 6),
        D("fishtank", "Fish tank", 8), D("records", "Record player", 10), D("arcade", "Arcade cabinet", 13), D("neon", "Neon sign", 14),
        D("cat", "Byte the cat", 18), D("cactus", "Cactus", null, 40), D("clock", "Wall clock", null, 50), D("beanbag", "Beanbag", null, 70),
        D("lavalamp", "Lava lamp", null, 80), D("lights", "Fairy lights", null, 90), D("disco", "Disco ball", null, 150), D("trophycase", "Trophy case", null, 120),

        Wp("wp-room", "Room default", 1), Wp("wp-cream", "Cream stripes", 1), Wp("wp-sage", "Sage dots", null, 40), Wp("wp-blush", "Blush stripes", null, 40),
        Wp("wp-sun", "Sunny dots", null, 50), Wp("wp-bricks", "Red brick", null, 80), Wp("wp-navy", "Navy stars", null, 90), Wp("wp-cosmos", "Cosmos", null, 120),
        Fl("fl-room", "Room default", 1), Fl("fl-oak", "Light oak", 1), Fl("fl-tiles", "White tiles", null, 40), Fl("fl-carpet", "Plum carpet", null, 50),
        Fl("fl-checker", "Checkerboard", null, 70), Fl("fl-marble", "Marble", null, 90),
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
        new("squasher", "Bug Squasher", "Score 20 in Bug Catch.", 50),
        new("xyzzy", "Adventurer", "An old magic word.", 20),
        new("rage", "Unleashed", "Push buddy over the edge.", 60),
        new("defuse", "Bomb Squad", "Defuse 5 bombs in one tantrum.", 60),
        new("sick", "Tummy Ache", "Too many cookies.", 30),
        new("yeet", "Yeet!", "Fling buddy across the screen.", 30),
        new("squish", "Stress Ball", "Squish buddy.", 20),
        new("tickle", "Tickle Monster", "Wiggle the cursor on buddy.", 20),
        new("tease", "Rude!", "Take back a cookie.", 20),
        new("copycat", "Copycat Champ", "Reach round 8 in Copycat.", 60),
        new("whacker", "Whack Master", "Score 25 in Whack-a-Bug.", 50),
        new("flyer", "Frequent Flyer", "Score 10 in Buddy Hop.", 50),
        new("typist", "Speed Typist", "Score 20 in Type Rush.", 50),
        new("shopper", "Shopaholic", "Buy 5 things in the shop.", 50),
        new("decorator", "Interior Designer", "Rearrange the furniture.", 30),
        new("focus", "Deep Focus", "Finish a focus timer.", 40),
        new("streak", "On Fire", "Visit buddy 7 days in a row.", 80),
        new("quester", "Quest Champion", "Complete 10 daily quests.", 80),
        new("cookiejar", "Cookie Jar", "Feed 100 cookies.", 80),
        new("witching", "Witching Hour", "Visit the room at 3am.", 50),
        new("named", "Hello, My Name Is", "Give buddy a name.", 20),
    };

    public static readonly QuestDef[] Quests =
    {
        new("pet", "Pet buddy 5 times", 5, 15, false), new("feed", "Feed buddy 3 cookies", 3, 15, false),
        new("session", "Finish 3 Claude sessions", 3, 30, false), new("games", "Play 2 arcade games", 2, 20, false),
        new("bugcatch", "Score 15 in Bug Catch", 15, 25, true), new("copycat", "Reach round 6 in Copycat", 5, 25, true),
        new("whack", "Score 20 in Whack-a-Bug", 20, 25, true), new("hop", "Score 5 in Buddy Hop", 5, 25, true),
        new("type", "Score 15 in Type Rush", 15, 25, true), new("focus", "Finish a focus timer", 1, 30, false),
        new("console", "Type 3 console commands", 3, 10, false), new("outfit", "Try on a new outfit", 1, 10, false),
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
    public string Room { get; set; } = "studio";
    public int Cookies { get; set; }
    public int Sessions { get; set; }
    public int Pets { get; set; }
    public int BestCopycat { get; set; }
    public int BestScore { get; set; }
    public string Day { get; set; } = "";
    public int ToolXpToday { get; set; }
    public int CookiesToday { get; set; }
    public int SessionsToday { get; set; }
    public bool Muted { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string LastUpdateCheck { get; set; } = "";
    // 2.0
    public int Coins { get; set; }
    public string Accessory { get; set; } = "none-acc";
    public List<string> Owned { get; set; } = new();
    public List<string> Hidden { get; set; } = new();
    public Dictionary<string, double> DecorX { get; set; } = new();
    public string Wallpaper { get; set; } = "wp-room";
    public string Floor { get; set; } = "fl-room";
    public Dictionary<string, int> Best { get; set; } = new();
    public string QuestDay { get; set; } = "";
    public List<Quest> Quests { get; set; } = new();
    public int QuestsDone { get; set; }
    public int Streak { get; set; }
    public string StreakDay { get; set; } = "";
    public string Name { get; set; } = "Buddy";
    public string FriendSkin { get; set; } = "sky";
    public int FocusMinutes { get; set; }
    public int FocusToday { get; set; }
    public int GamesPlayed { get; set; }
    public int Bought { get; set; }
    public bool V2 { get; set; }
    public double Scale { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public bool Quiet { get; set; }
    public bool NoThoughts { get; set; }
}

class Progress
{
    // Declared before Shared: static fields initialise in order, and the constructor may save.
    static readonly object SaveLock = new();
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static readonly Progress Shared = new();

    public SaveData Data { get; private set; }
    public Action<int, List<Item>> OnLevelUp;
    public Action<Achievement, Item> OnAchievement;

    static string Backup => Paths.Save + ".bak";
    static string Temp => Paths.Save + ".tmp";

    Progress()
    {
        // The save is never thrown away: try save.json, then the last good backup, then a
        // half-finished write. A file that exists but can't be read is set aside, not overwritten.
        string from = null;
        foreach (var path in new[] { Paths.Save, Backup, Temp })
            if ((Data = TryLoad(path)) != null) { from = path; break; }
        if (from != Paths.Save && File.Exists(Paths.Save))
        {
            // Set the broken file aside so the next save can't rotate it over the good backup.
            try
            {
                if (new FileInfo(Paths.Save).Length > 0)
                    File.Copy(Paths.Save, Path.Combine(Paths.Dir, $"save.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
                if (from != null) File.Delete(Paths.Save);
            }
            catch { }
        }
        if (from != null && from != Paths.Save) Save();
        Data ??= new SaveData();
    }

    static SaveData TryLoad(string path)
    {
        // Antivirus, OneDrive or a second copy of buddy can hold the file for a moment, so retry.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{') return null;
                return JsonSerializer.Deserialize<SaveData>(text, Json);
            }
            catch (IOException) { System.Threading.Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(100); }
            catch (Exception e) { Program.Log(e); return null; }
        }
        return null;
    }

    public static int XpNeeded(int level) => 25 * (level - 1) * level;

    public int Level
    {
        get { int l = 1; while (Data.Xp >= XpNeeded(l + 1)) l++; return l; }
    }

    public double LevelFraction
    {
        get { int lo = XpNeeded(Level), hi = XpNeeded(Level + 1); return (double)(Data.Xp - lo) / (hi - lo); }
    }

    public Action<QuestDef, bool> OnQuest;
    public Action<int, int> OnStreak;
    public int WelcomeGift { get; private set; }

    /// One-time upgrade from 1.x saves.
    public void Migrate()
    {
        if (Data.V2) return;
        Data.V2 = true;
        WelcomeGift = 100 + 10 * Level;
        Data.Coins += WelcomeGift;
        if (Data.BestScore > 0) Data.Best["bugcatch"] = Data.BestScore;
        if (Data.BestCopycat > 0) Data.Best["copycat"] = Data.BestCopycat;
        Save();
    }

    public bool IsUnlocked(Item it) => it.Level is int l ? Level >= l : Data.Unlocked.Contains(it.Id) || Data.Owned.Contains(it.Id);
    public bool Has(string id) => Catalog.Find(id) is Item it && IsUnlocked(it);
    public bool Placed(string id) => Has(id) && !Data.Hidden.Contains(id);
    public bool HasAchievement(string id) => Data.Achievements.Contains(id);

    public bool IsEquipped(Item it) => it.Kind switch
    {
        ItemKind.Skin => Data.Skin == it.Id, ItemKind.Hat => Data.Hat == it.Id, ItemKind.Accessory => Data.Accessory == it.Id,
        ItemKind.Room => Data.Room == it.Id, ItemKind.Wallpaper => Data.Wallpaper == it.Id, ItemKind.Floor => Data.Floor == it.Id,
        _ => Placed(it.Id),
    };

    public void Award(int xp)
    {
        if (xp <= 0) return;
        int before = Level;
        Data.Xp += xp;
        int after = Level;
        if (after > before) Data.Coins += 20 * (after - before);
        Save();
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

    /// Wears / applies an item; for furniture, puts it out or away.
    public void Equip(Item it)
    {
        if (!IsUnlocked(it)) return;
        switch (it.Kind)
        {
            case ItemKind.Skin: Data.Skin = it.Id; break;
            case ItemKind.Hat: Data.Hat = it.Id; break;
            case ItemKind.Accessory: Data.Accessory = it.Id; break;
            case ItemKind.Room: Data.Room = it.Id; break;
            case ItemKind.Wallpaper: Data.Wallpaper = it.Id; break;
            case ItemKind.Floor: Data.Floor = it.Id; break;
            case ItemKind.Decor: if (!Data.Hidden.Remove(it.Id)) Data.Hidden.Add(it.Id); break;
        }
        if (it.Kind is ItemKind.Skin or ItemKind.Hat or ItemKind.Accessory) Track("outfit");
        Save();
    }

    public void SetName(string name)
    {
        var n = (name ?? "").Trim();
        if (n.Length > 16) n = n[..16];
        if (n.Length == 0 || n == Data.Name) return;
        Data.Name = n;
        Save();
        Achieve("named");
    }

    // Shop

    static ulong Hash(string s) { ulong h = 1469598103934665603; foreach (var b in System.Text.Encoding.UTF8.GetBytes(s)) h = (h ^ b) * 1099511628211; return h; }
    static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public Item DailyDeal
    {
        get
        {
            var forSale = Catalog.Items.Where(i => i.Price != null && !IsUnlocked(i)).ToList();
            return forSale.Count == 0 ? null : forSale[(int)(Hash(Today) % (ulong)forSale.Count)];
        }
    }

    public int? Price(Item it) => it.Price is int p ? (DailyDeal?.Id == it.Id ? p * 7 / 10 : p) : null;

    public bool Buy(Item it)
    {
        if (Price(it) is not int p || IsUnlocked(it) || Data.Coins < p) return false;
        Data.Coins -= p;
        Data.Owned.Add(it.Id);
        Data.Bought++;
        Save();
        if (Data.Bought >= 5) Achieve("shopper");
        return true;
    }

    // Days, streaks and quests

    public void RollDay()
    {
        var d = Today;
        if (d != Data.Day) { Data.Day = d; Data.ToolXpToday = 0; Data.CookiesToday = 0; Data.SessionsToday = 0; Data.FocusToday = 0; }
        if (Data.StreakDay != d)
        {
            Data.Streak = Data.StreakDay == DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") ? Data.Streak + 1 : 1;
            Data.StreakDay = d;
            int bonus = 5 * Math.Min(7, Data.Streak);
            Data.Coins += bonus;
            Save();
            OnStreak?.Invoke(Data.Streak, bonus);
            if (Data.Streak >= 7) Achieve("streak");
        }
        if (Data.QuestDay != d)
        {
            Data.QuestDay = d;
            var rng = new Random((int)(Hash(d) & 0x7fffffff));
            Data.Quests = Catalog.Quests.OrderBy(_ => rng.Next()).Take(3).Select(q => new Quest { Id = q.Id }).ToList();
            Save();
        }
    }

    /// Moves a daily quest along. For score quests pass the score as `amount`.
    public void Track(string id, int amount = 1)
    {
        RollDay();
        var q = Data.Quests.FirstOrDefault(x => x.Id == id && !x.Done);
        var def = Catalog.Quests.FirstOrDefault(x => x.Id == id);
        if (q == null || def == null) return;
        q.Progress = def.Best ? Math.Max(q.Progress, amount) : q.Progress + amount;
        if (q.Progress < def.Goal) { Save(); return; }
        q.Progress = def.Goal;
        q.Done = true;
        Data.QuestsDone++;
        Data.Coins += def.Coins;
        bool sweep = Data.Quests.All(x => x.Done);
        if (sweep) Data.Coins += 25;
        Save();
        OnQuest?.Invoke(def, sweep);
        Award(15);
        if (Data.QuestsDone >= 10) Achieve("quester", "medal");
    }

    // Things that happen

    public void ToolUsed()
    {
        RollDay();
        if (Data.ToolXpToday >= 80) return;
        Data.ToolXpToday++;
        Award(1);
    }

    public (int xp, int coins) SessionFinished()
    {
        RollDay();
        Data.Sessions++;
        Data.SessionsToday++;
        bool first = Data.SessionsToday == 1;
        int xp = first ? 45 : 25, coins = first ? 15 : 5;
        Data.Coins += coins;
        Award(xp);
        Track("session");
        if (DateTime.Now.Hour < 5) Achieve("night");
        if (Data.SessionsToday >= 10) Achieve("marathon");
        return (xp, coins);
    }

    /// Returns false when buddy is too full for more XP from cookies today.
    public bool FedCookie()
    {
        RollDay();
        Data.Cookies++;
        if (Data.Cookies >= 10) Achieve("cookies", "chef");
        if (Data.Cookies >= 100) Achieve("cookiejar", "cookie");
        Track("feed");
        if (Data.CookiesToday >= 12) { Save(); return false; }
        Data.CookiesToday++;
        Award(2);
        return true;
    }

    public void Petted() { Data.Pets++; Award(1); Track("pet"); }

    public (int xp, int coins) FocusFinished(int minutes)
    {
        RollDay();
        Data.FocusMinutes += minutes;
        Data.FocusToday += minutes;
        int xp = Math.Min(60, minutes), coins = Math.Max(5, minutes / 2);
        Data.Coins += coins;
        Award(xp);
        Track("focus");
        Achieve("focus");
        return (xp, coins);
    }

    /// Records a finished arcade game and pays out.
    public GameReward FinishGame(string id, int score)
    {
        RollDay();
        int s = Math.Max(0, score);
        bool newBest = s > (Data.Best.TryGetValue(id, out var b) ? b : 0);
        if (newBest) Data.Best[id] = s;
        if (id == "bugcatch") Data.BestScore = Math.Max(Data.BestScore, s);
        if (id == "copycat") Data.BestCopycat = Math.Max(Data.BestCopycat, s);
        Data.GamesPlayed++;
        var (xpMul, coinDiv) = id switch { "copycat" => (3, 1), "hop" => (5, 1), "whack" => (2, 2), "type" => (2, 2), _ => (1, 2) };
        int xp = Math.Min(60, s * xpMul);
        int coins = Math.Min(30, id is "copycat" or "hop" ? s * 2 : s / coinDiv);
        Data.Coins += coins;
        Save();
        Award(xp);
        Track("games");
        Track(id, s);
        switch (id)
        {
            case "bugcatch": if (s >= 20) Achieve("squasher", "beach"); break;
            case "copycat": if (s >= 7) Achieve("copycat", "headband"); break;
            case "whack": if (s >= 25) Achieve("whacker"); break;
            case "hop": if (s >= 10) Achieve("flyer"); break;
            case "type": if (s >= 20) Achieve("typist"); break;
        }
        return new GameReward(xp, coins, newBest);
    }

    /// Writes save.tmp, flushes it to disk, then swaps it in (keeping the old one as save.json.bak),
    /// so quitting, End Task, an update or a shutdown can never leave a half-written save.
    public void Save()
    {
        lock (SaveLock)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Paths.Dir);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(Data, Json);
                    using (var f = new FileStream(Temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        f.Write(bytes, 0, bytes.Length);
                        f.Flush(true);
                    }
                    if (File.Exists(Paths.Save)) File.Replace(Temp, Paths.Save, Backup, true);
                    else File.Move(Temp, Paths.Save);
                    return;
                }
                catch (Exception e)
                {
                    if (attempt < 4) { System.Threading.Thread.Sleep(50); continue; }
                    // Some drives don't support Replace: fall back to a plain copy of the finished file.
                    try { if (File.Exists(Temp)) File.Copy(Temp, Paths.Save, true); } catch (Exception e2) { Program.Log(e2); }
                    Program.Log(e);
                }
            }
        }
    }
}
