# tomodachi

A [Noctalia](https://github.com/noctalia-dev/noctalia-shell)-style shell for Windows. It replaces the Start menu and the taskbar
with a single notch at the top of the screen: tap the Win key and it unfolds into an app launcher, widgets, a window list,
settings and a power menu.

*[Deutsche Kurzfassung](README.de.md)*

> **About this project.** tomodachi was built entirely by an AI coding assistant (Claude, by Anthropic) in a long
> conversation with me. I decided what it should do and how it should feel, tested every build on my own PC and reported
> what broke. I have not read the code. It's my personal shell, shared as is, in case it's useful to someone else.

## What it does

| | |
|---|---|
| **Win key** | A tap opens the notch instead of the Start menu. Win+E, Win+L, Win+Shift+S and every other combo keep working. |
| **Apps** | Fuzzy search over everything in "All apps", plus Steam games and program shortcuts on the desktop. Right-click → *Run as administrator*. |
| **Widgets** | Clock and calendar, media controls, CPU / GPU / RAM with history, drives with NVMe temperatures, network. Win+W or click the avatar. |
| **Windows** | Open and minimized windows, since there is no taskbar anymore. |
| **Power** | Lock, sleep, sign out, restart, shut down. Anything that ends the session needs a second click. |
| **Settings** | Profile picture, notch monitor, language, Win key, taskbar, autostart, wallpaper folder. No config files needed. |
| **Wallpapers** | Win+Ctrl+W opens a full-screen strip of slanted wallpaper cards; Enter applies the picture. |
| **Taskbar** | Gone while tomodachi runs, back the moment it quits. |
| **Fullscreen** | The notch hides over games, videos and maximized windows. A Win tap still brings it up. |

The UI is available in English and German (follows Windows, or pick one in Settings).

## Install

1. Download `tomodachi-<version>.zip` from [Releases](../../releases) and unzip it somewhere permanent
   (not *Downloads*; autostart remembers the path).
2. Run `tomodachi.exe`. Windows SmartScreen will warn because the exe isn't signed: *More info → Run anyway*.
3. Open Settings (⚙) and turn on **Start with Windows**. It asks for admin rights once. Only then does the Win key also
   work while an admin window or a game with anti-cheat is focused.

Requirements: Windows 10/11 x64. The release is self-contained, no .NET install needed.
CPU temperatures need the optional [PawnIO](https://pawnio.eu) driver; everything else works without it.

## If something goes wrong

tomodachi touches the taskbar and the Win key, so it comes with a safety net:

| | |
|---|---|
| `Ctrl+Alt+Shift+Q` | Quit and restore everything. |
| Settings → **Quit** | Same thing with the mouse. |
| `tomodachi.exe --restore` | Undo all system changes, even if tomodachi isn't running. |
| Crash or Task Manager kill | A watchdog process restores the taskbar automatically. |
| `tomodachi.exe --safe` | Start with UI only: no hook, taskbar untouched. |

To uninstall: Settings → turn off **Start with Windows** → **Quit**, then delete the folder and `%APPDATA%\tomodachi`.

## Build

Needs the .NET 10 SDK.

```powershell
dotnet build Tomodachi.slnx
dotnet test Tomodachi.slnx
.\tools\publish.ps1          # dist\tomodachi-<version>.zip: single self-contained exe + readme + licenses
.\tools\update-install.ps1   # replace the installed exe (the one autostart points to) with the newest build
```

Only the exe that autostart points to runs elevated. A second copy (e.g. `bin\Debug`) runs without admin rights, and then
the Win key can't see keystrokes in elevated windows: the Start menu wins.

## Command line

| | |
|---|---|
| `--widgets`, `--windows`, `--power`, `--settings` | Toggle the notch on that tab (handy for macro keys). |
| `--wallpaper` | Toggle the wallpaper picker. |
| `--quit` / `--restore` / `--safe` | See above. |
| `--install` / `--uninstall` | Add / remove the autostart task (same as the switch in Settings). |

## Files

| | |
|---|---|
| `%APPDATA%\tomodachi\config.json` | Settings (everything is also in the Settings tab; `LogLevel` only here). |
| `%APPDATA%\tomodachi\avatar.png` | Your profile picture. Without it the notch shows the default 友 avatar. |
| `%APPDATA%\tomodachi\logs\` | Logs, kept for 14 days. |
| `%LOCALAPPDATA%\tomodachi\icons\` | Icon cache, safe to delete. |

## How it works

tomodachi doesn't replace Explorer; it sits on top of it.

- **Win key:** a low-level keyboard hook holds back Win-down and replays it the moment another key follows, so combos
  still reach Windows. A lone tap becomes an injected F24 hotkey, because only a hotkey grants the right to take focus.
  Raw input covers the moments when Windows doesn't call the hook (while tomodachi itself is focused).
  The logic is a small state machine with unit tests ([`WinKeyStateMachine`](src/Tomodachi/Input/WinKeyStateMachine.cs)).
- **Taskbar:** auto-hide plus hidden taskbar windows. Every change is written to a restore journal first; quitting,
  crashing, signing out or `--restore` all replay it.
- **Sensors:** [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (no WinRing0 driver),
  media via the Windows media session API, everything polled only while the Widgets tab is open.
- **Fullscreen:** looks at the topmost real window under the notch (skipping overlays), not just the focused one.

```
src/Tomodachi/
  Core/          config, logging, single instance, autostart, elevation
  Input/         Win key hook + state machine, global hotkeys
  Notch/         the notch window, its tabs, settings, power menu, avatar
  Launcher/      app index (Start menu, Steam, desktop), fuzzy search, launch history
  Widgets/       sensors, network, media, window list and their views
  Wallpaper/     the wallpaper picker
  Shell/         taskbar, fullscreen detection, power actions
  Safety/        restore journal, watchdog, emergency hotkey
  Localization/  UI texts (Strings.resx = English, Strings.de.resx = German)
tests/           unit tests (key handling, search, widget math, power menu, localization)
tools/           publish and update scripts
packaging/       readme and license notes that ship with a release
docs/            roadmap and the original German development log
```

## Roadmap

See [docs/roadmap.md](docs/roadmap.md). Biggest gap: tray icons (Discord, Steam, …) have nowhere to go without the taskbar.

## License

[MIT](LICENSE). Third-party components and their licenses: [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt).
