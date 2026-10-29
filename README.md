# Claude Buddy for Windows

A tiny desktop companion for **Claude Code** on Windows 10 and 11. It sits in the corner of your
screen and reacts to your Claude Code sessions. It thinks while Claude works, celebrates when a
task is done, and waves when Claude needs your permission. It also has XP, unlockables, a mini-game,
secrets, and quite a temper.

![Buddy's moods](docs/moods.png)

> Unofficial fan project. Not affiliated with or endorsed by Anthropic. "Claude" is a trademark of Anthropic.
> Looking for the Mac version? → [claude-buddy](https://github.com/ilyaanpoint-bot/claude-buddy)

## Install

**Option 1: one line in PowerShell** (downloads the latest release):

```powershell
irm https://raw.githubusercontent.com/ilyaanpoint-bot/claude-buddy-windows/main/install.ps1 | iex
```

**Option 2: from source** (uses the .NET 8 SDK if you have it, otherwise the release download):

```powershell
git clone https://github.com/ilyaanpoint-bot/claude-buddy-windows.git
cd claude-buddy-windows
powershell -ExecutionPolicy Bypass -File install.ps1          # add -Login to start with Windows
```

The installer:

- puts `ClaudeBuddy.exe` in `%USERPROFILE%\.claude\claude-buddy\`
- adds **Claude Buddy** to the Start menu and your desktop
- adds four hooks to `%USERPROFILE%\.claude\settings.json`: `UserPromptSubmit`, `Stop`,
  `Notification`, `SessionEnd`. It backs up the file first and keeps your existing hooks.

Claude Code sessions you start after installing will talk to buddy.

**Lost buddy, or quit him?** Open **Claude Buddy** from the Start menu and he comes straight
back to the corner and waves. If he ever ends up off-screen (say, you unplug a monitor), he walks
back by himself.

To remove it: `powershell -ExecutionPolicy Bypass -File uninstall.ps1` (your XP and unlocks are kept).

## Updating

Buddy checks GitHub for a new version once a day. When one is out, he tells you in a speech bubble.
Right-click him → **Update to vX.Y.Z** and say yes. A PowerShell window downloads the new version and
buddy restarts. Your XP, unlocks and hooks are kept. You can also check any time from the menu
(**Check for updates**) or by typing `update` in his console.

On **v0.1.0**, which had no updater, run the install line above once more to update.

## Features

- **Knows what Claude Code is doing:**
  - Thinking animation while a session runs.
  - Hops, sparkles and a chime when it finishes.
  - An alert when Claude needs permission.
- **Two sessions at once?** A blue friend appears, and each buddy follows its own session.
- **XP and levels** from finished sessions, petting, cookies and games.
- **Unlockable skins and hats** (right-click → Skins / Hats), including secret ones.
- **His room** (double-click buddy): he wanders around, works at his desk while Claude runs and
  sleeps in bed. Unlock furniture as you level up (a plant, lamp, bookshelf, record player, fish
  tank, arcade cabinet, neon sign, and Byte the cat) and new rooms: Greenhouse, Space Station and
  a secret Beach Hut.
- **Games:** Bug Catch (catch falling bugs, dodge the errors) and Copycat (a memory game). Both are under
  **Play** in the room.
- **Personality:**
  - Eyes that follow your cursor.
  - Idle routines: juggling, whistling, sneezing, coffee.
  - Random thoughts, and sounds generated live.
  - He falls asleep when you're away.
- **A temper.** Poke him too much, fling him across the screen, squish him or honk at him, and he
  gets angry. At the end he turns into a little devil and throws bombs across your screen. Click
  them to defuse them. 😈
- **Cookies:** too many make him sick, so keep some medicine handy.
- **Secrets.** Right-click → Console… and try `sudo buddy`. There are more to find.

## Controls

| Do this | And buddy… |
|---|---|
| Click | giggles (and earns XP) |
| Double-click | opens his room |
| Click 5 times fast | gets dizzy |
| Drag | moves; flick hard to fling him |
| Click and hold | squishes |
| Wiggle the cursor on him | is tickled |
| Right-click | opens the menu: room, feed, skins, hats, rooms, games, console, trophies, honk, medicine, sounds, start with Windows, updates |

## How it works

```
Claude Code ──hooks──▶ hook.ps1 ──appends a JSON line──▶ %USERPROFILE%\.claude\claude-buddy\events.jsonl
                                                                          │
                                                ClaudeBuddy.exe tails it ◀┘
```

`hook.ps1` only writes the event name, session ID, working folder and notification message
to a local file. None of that leaves your PC. The only network request buddy makes is the daily
update check, which asks GitHub's public API for the latest release number.

## Develop

```powershell
dotnet run --project src
```

It's plain WinForms + GDI+ with no dependencies. Every push is built on GitHub Actions. The build
also runs a self-test (`ClaudeBuddy.exe --self-test <folder>`) that opens every screen of the room
and saves a screenshot of each. Tagged versions (`v*`) publish `ClaudeBuddy.exe` to Releases, and
the updater picks them up from there.

PRs with new hats, games and secrets are very welcome.

## License

MIT
