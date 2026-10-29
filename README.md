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

## Features

- **Knows what Claude Code is doing:**
  - Thinking animation while a session runs.
  - Hops, sparkles and a chime when it finishes.
  - An alert when Claude needs permission.
- **Two sessions at once?** A blue friend appears, and each buddy follows its own session.
- **XP and levels** from finished sessions, petting, cookies and games.
- **Unlockable skins and hats** (right-click → Skins / Hats), including secret ones.
- **Copycat:** a memory game (double-click buddy).
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
| Double-click | plays Copycat |
| Click 5 times fast | gets dizzy |
| Drag | moves; flick hard to fling him |
| Click and hold | squishes |
| Wiggle the cursor on him | is tickled |
| Right-click | opens the menu: feed, skins, hats, console, trophies, honk, medicine, sounds, start with Windows |

## How it works

```
Claude Code ──hooks──▶ hook.ps1 ──appends a JSON line──▶ %USERPROFILE%\.claude\claude-buddy\events.jsonl
                                                                          │
                                                ClaudeBuddy.exe tails it ◀┘
```

`hook.ps1` only writes the event name, session ID, working folder and notification message
to a local file. Nothing leaves your PC.

## Develop

```powershell
dotnet run --project src
```

It's plain WinForms + GDI+ with no dependencies. Every push is built on GitHub Actions, and tagged
versions (`v*`) publish `ClaudeBuddy.exe` to Releases.

PRs with new hats, games and secrets are very welcome. The Mac version also has a room to visit and
Bug Catch, and help porting those is wanted.

## License

MIT
