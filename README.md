# tomodachi

A [Noctalia](https://github.com/noctalia-dev/noctalia)-style shell for Windows. It replaces the Start menu and the taskbar
with a slim bar at the top of the screen, a notch flowing out of its middle: tap the Win key and the notch unfolds into an
app launcher, widgets, a window list, settings and a power menu.

*[Deutsche Kurzfassung](README.de.md)*

> **About this project.** tomodachi was built entirely by an AI coding assistant (Claude, by Anthropic) in a long
> conversation with me. I decided what it should do and how it should feel, tested every build on my own PC and reported
> what broke. I have not read the code. It's my personal shell, which I use daily and keep maintaining, shared in
> case it's useful to someone else.
>
> Keep in mind what that means: tomodachi hooks the keyboard and can run with admin rights, and nobody independent
> has reviewed the code. The [Security & privacy](#security--privacy) section explains what it does and why, but if
> you want to run it elevated, read [`src/`](src) yourself first, or don't.

## What it does

| | |
|---|---|
| **Win key** | A tap opens the notch instead of the Start menu. Win+E, Win+L, Win+Shift+S and the other combos keep working; only Win+W is taken (it opens tomodachi's widgets instead of Windows' widget board). |
| **Top bar** | One bar across the notch monitor with your open windows as tabs, left of the notch; click to switch or minimize, drag to reorder. Prefer just the notch? Settings → *Notch only*. |
| **Apps** | Fuzzy search over everything in "All apps", plus Steam games and program shortcuts on the desktop. Right-click → *Run as administrator*. |
| **Widgets** | Clock and calendar, media controls, CPU / GPU / RAM with history, drives (NVMe temperatures need admin rights), network. Quick settings: volume, mute, output device, Wi-Fi and Bluetooth on/off, Wi-Fi networks (Wi-Fi/Bluetooth only show up if the PC has them, and haven't been tested on real hardware yet). Win+W or click the avatar. |
| **Windows** | Open and minimized windows, since there is no taskbar anymore. Right-click → bring to front, close, or (opt-in) *End task*. |
| **Power** | Lock, sleep, sign out, restart, shut down. Anything that ends the session needs a second click. |
| **Settings** | Bar with notch or notch only, profile picture, notch monitor, language, Win key, taskbar, autostart, wallpaper folder. No config files needed. |
| **Wallpapers** | Win+Ctrl+W opens a full-screen strip of slanted wallpaper cards; Enter applies the picture. |
| **Taskbar** | Gone while tomodachi runs, back the moment it quits. |
| **Fullscreen** | Bar and notch hide over games, videos, maximized windows and Windows 11's snap bar; nothing reserves screen space. A Win tap, or resting the mouse at the top edge above the notch, still brings both up. |

The UI is available in English and German (follows Windows, or pick one in Settings).

## Install

1. Download `tomodachi-<version>.zip` from [Releases](../../releases) and unzip it straight into
   `C:\Program Files\tomodachi` (Windows asks for admin rights once). Autostart remembers the path and starts that exe
   with admin rights, so it belongs in a folder only admins can change. tomodachi warns when you turn on autostart from
   anywhere else. Unzip or copy, don't move: a move within the same drive keeps the file permissions of the source
   folder, and tomodachi will warn even though the path says Program Files. If that happened, tomodachi offers to reset
   the permissions when you turn on autostart. To do it by hand, from an admin PowerShell:

   ```powershell
   icacls "C:\Program Files\tomodachi" /setowner Administrators /T /C
   icacls "C:\Program Files\tomodachi" /reset /T /C
   ```
2. Run `tomodachi.exe`. Windows SmartScreen will warn because the exe isn't signed: *More info → Run anyway*.
3. Open Settings (⚙) and turn on **Start with Windows**. It asks for admin rights once. Then press **Restart** in Settings
   (or sign in again): from now on tomodachi runs with admin rights, and only then does the Win key also work while an
   admin window or a game with anti-cheat is focused.

Requirements: Windows 10 (2004 or later) or Windows 11, x64. Only tested on Windows 11.
The release is self-contained, no .NET install needed.
CPU temperatures need the optional [PawnIO](https://pawnio.eu) driver; everything else works without it.

## If something goes wrong

tomodachi touches the taskbar and the Win key, so it comes with a safety net:

| | |
|---|---|
| `Ctrl+Alt+Shift+Q` | Quit and restore everything. |
| Settings → **Quit** | Same thing with the mouse. |
| `tomodachi.exe --restore` | Bring the taskbar back, even if tomodachi isn't running (starts Explorer if needed). |
| Crash or Task Manager kill | A watchdog process restores the taskbar automatically. |
| `tomodachi.exe --safe` | Start with UI only: no hook, taskbar untouched. |

To uninstall: Settings → turn off **Start with Windows** (or `--uninstall`) → **Quit**, then delete the folder,
`%APPDATA%\tomodachi` and `%LOCALAPPDATA%\tomodachi`.

## Security & privacy

tomodachi reads every keystroke and can run with admin rights. That deserves a clear answer to "what does it do with
that?", so here it is. Short version first:

| | |
|---|---|
| **Network** | None. No telemetry, no update checks, no accounts. |
| **Keyboard** | Sees every key, reacts only to the Win key. Nothing is stored or sent. |
| **Admin rights** | Optional, off by default, removable any time. |
| **System changes** | Taskbar changes are undone when it quits. Everything else only happens when you click it. |
| **Your data** | Two local folders. Delete them and it's gone. |
| **The download** | Built by GitHub Actions from this source, not on my PC. |

### No network

tomodachi's own code never opens a network connection. The network widget only reads the traffic counters of your
adapter. The Wi-Fi list asks Windows to scan, and when you click a network, asks Windows to connect; Windows does
the connecting, not tomodachi. (Checked on a running instance: 0 TCP and 0 UDP connections.)

### Keyboard

A keyboard hook receives every keystroke; that's simply how hooks work. tomodachi acts only on the Win key and a
handful of combos (Win+W, Win+L, Ctrl+Esc, and Esc while the notch is open). While Win is held, other keys are
checked just to tell a combo from a tap. Every other key passes through unchanged and is never stored or sent
anywhere. Even the debug log only notes "other key", never which one.

tomodachi also sends a few keys of its own: F24 to open the notch, an unused key code to stop the Start menu from
appearing, and a replayed Win-down before a combo.

### Admin rights

Admin rights exist for one reason: so the Win key keeps working while an admin window or a game with anti-cheat has
focus. You get them by turning on **Start with Windows** in Settings, which creates a Task Scheduler entry. Turn it
off and the entry is gone. Apps you launch from tomodachi run *without* admin rights unless you choose
*Run as administrator*.

Because that task runs elevated, tomodachi closes the usual ways another program could ride along with it. These
protections assume the install folder is admin-only, which is why [Install](#install) says `C:\Program Files`.
If your account can write to the install folder, the protection is void; that is what the warning when turning on
autostart is about.

<details>
<summary>What exactly is hardened</summary>

- The exe and its DLLs load from the install folder only; nothing is unpacked to `%TEMP%`.
- The task starts tomodachi through a small `cmd` step that clears the `CORECLR_*`, `COR_*` and code-loading
  `DOTNET_*` environment variables, which Windows would otherwise pass into the elevated process.
- The task is registered directly, without a temporary file.
- Turning on autostart from a folder non-admins can change asks for confirmation first.

</details>

### System changes

Taskbar changes (auto-hide, hidden windows) are written to a restore journal before they happen and are undone on
quit, crash, sign-out, or with `--restore`.

Everything else changes only when you click it: the autostart task, your wallpaper, volume, default output device,
Wi-Fi and Bluetooth. These behave like Windows' own quick settings: they change exactly what you clicked and are
not undone on quit.

tomodachi reads two registry values (Steam's path and the fast-startup flag) and writes none. Windows itself stores
the taskbar's auto-hide setting and the scheduled task, as it always does.

### Your data stays local

Settings, logs, launch history (which apps you start, used for ranking), the restore journal and your profile
picture live in `%APPDATA%\tomodachi`; the icon cache lives in `%LOCALAPPDATA%\tomodachi`. Delete both folders
and nothing is left. See [Files](#files) for what each file holds.

### The download matches the source

The exe in [Releases](../../releases) is built by GitHub Actions from this repository
([workflow](.github/workflows/build.yml)), not on my PC. You can also [build it yourself](#build).

As said at the top, the code was written by an AI and I haven't reviewed it. Everything in this section was checked
against the code by the same AI that wrote it, not by me or anyone else. If you need certainty, read [`src/`](src)
or build from source.

## Build

Needs the .NET 10 SDK.

```powershell
dotnet build Tomodachi.slnx
dotnet test Tomodachi.slnx
.\tools\publish.ps1          # dist\tomodachi-<version>.zip: self-contained exe + native WPF DLLs + readme + licenses
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
| `%APPDATA%\tomodachi\config.json` | Settings (all in the Settings tab, except `LogLevel` and one diagnostic switch). |
| `%APPDATA%\tomodachi\restore-journal.json` | System changes currently in effect, with their original state. |
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
  media via the Windows media session API, everything polled only while the Widgets or Windows tab is open.
- **Fullscreen:** looks at the topmost real window under the notch (skipping overlays), not just the focused one,
  and at Windows 11's snap bar.

```
src/Tomodachi/
  Core/          config, logging, single instance, autostart, elevation
  Input/         Win key hook + state machine, global hotkeys
  Notch/         the notch window, its tabs, settings, power menu, avatar
  Bar/           the top bar with window tabs
  Launcher/      app index (Start menu, Steam, desktop), fuzzy search, launch history
  Widgets/       sensors, network, media, audio, radios, window list and their views
  Wallpaper/     the wallpaper picker
  Shell/         taskbar, fullscreen detection, power actions
  Safety/        restore journal, watchdog, emergency hotkey
  Localization/  UI texts (Strings.resx = English, Strings.de.resx = German)
tests/           unit tests (key handling, search, widget math, power menu, top bar, quick settings, localization)
tools/           publish and update scripts
packaging/       readme and license notes that ship with a release
docs/            roadmap and the original German development log
```

## Roadmap

See [docs/roadmap.md](docs/roadmap.md). Biggest gap: tray icons (Discord, Steam, …) have nowhere to go without the taskbar.

## License

[MIT](LICENSE). Third-party components and their licenses: [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt).
