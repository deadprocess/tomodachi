# Roadmap

Where tomodachi stands and what could come next. The full history, with every dead end around the Win key,
is in the original German development log: [devlog.de.md](devlog.de.md).

## Done

- **Foundation:** overlay windows (topmost, click-through, no focus stealing, per-monitor DPI), logging, single
  instance, JSON config, emergency exit, restore journal, watchdog, elevated autostart via Task Scheduler.
- **Win key:** tap opens the notch, combos still reach Windows, no Start menu, also while tomodachi or an elevated
  window has focus (when tomodachi runs elevated).
- **Taskbar:** removed on all monitors, restored on quit, crash or sign-out.
- **Notch:** round profile picture, hover, morphing tabs (Apps · Widgets · Windows · Settings · Power), hides over
  fullscreen and maximized windows.
- **Launcher:** Start menu apps, Store apps, Steam library, desktop shortcuts; fuzzy search with acronyms and exe
  aliases; frecency; run as administrator.
- **Widgets:** clock and calendar, media controls (any app using the Windows media session), CPU/GPU/RAM with
  60-second history, drives with NVMe temperature and throughput, network.
- **Window list, power menu, wallpaper picker, settings tab, English/German UI.**
- **Packaging:** self-contained single exe, update script for the installed copy.

## Next

| | Idea | Notes |
|---|---|---|
| **Tray icons** | Host the notification area so Discord, Steam etc. get their icons back | Biggest gap. Apps send icons to a window called `Shell_TrayWnd`; tomodachi would have to provide one (like Cairo Desktop / ManagedShell). |
| Volume & brightness | Show the OSD in the notch, switch audio output (CoreAudio) | Replaces the taskbar's volume icon. |
| Quick toggles | Bluetooth, do not disturb, dark mode, power plan | |
| Launcher extras | Calculator, web search, `ms-settings:` pages | "2*21" and "bluetooth" should just work. |
| Notifications | Mirror Windows toasts in the notch | Needs research: `UserNotificationListener` wants package identity. |
| Hotkeys & theme | Configurable shortcuts and colors in Settings | |
| Performance | ~280 MB RAM today (sensors, thumbnails, WPF); goal under 150 MB | |
| Signed exe | So SmartScreen stops warning | Needs a code-signing certificate. |
| Screenshots | For this README | With the default avatar and a clean desktop. |

## Bigger ideas

- **Tiling:** automatic window layouts with keyboard control, maybe a Niri-style scrolling layout for ultrawide
  monitors. Could also work alongside [komorebi](https://github.com/LGUG2Z/komorebi).
- **Real shell replacement:** run tomodachi instead of Explorer (`Winlogon\Shell`), splitting Explorer's jobs into
  small processes (tray, desktop, autostart, notifications) under a session manager. Only on a test account first.
