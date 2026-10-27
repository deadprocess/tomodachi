tomodachi – a Noctalia-style shell for Windows
==============================================
(Deutsch: siehe LIES-MICH.txt)

tomodachi replaces the Start menu and the taskbar with a slim bar at the top of
the screen: open windows as tabs on the left, a "notch" in the middle.
Tap the Win key to unfold the notch:

  Apps        app launcher: type, Enter. Right-click for "Run as administrator".
  Widgets     clock, calendar, media, CPU/GPU/RAM, drives, network,
              quick settings (volume, output device, Wi-Fi, Bluetooth)
  Windows     all open windows (instead of the taskbar)
  ⚙           settings: bar or notch only, profile picture, monitor,
              language, autostart, …
  ⏻           lock, sleep, sign out, restart, shut down

Also: Win+W opens the widgets, Win+Ctrl+W a wallpaper picker (images from the
"Wallpaper" folder in your user folder, changeable in settings).


GETTING STARTED
---------------
1. Put this folder somewhere permanent, e.g. C:\Tools\tomodachi
   (not in Downloads, autostart remembers the path).
2. Double-click tomodachi.exe.
   Windows may say "Windows protected your PC": the exe isn't signed.
   "More info" → "Run anyway".
3. The taskbar disappears, the bar shows up. Tap the Win key.
4. Recommended: in settings (⚙) turn on "Start with Windows". It asks for admin
   rights once. Only then does the Win key also work while an admin window or a
   game with anti-cheat is focused, and only then do you get SSD temperatures.


EMERGENCY EXIT
--------------
  Ctrl+Alt+Shift+Q        quits tomodachi, the taskbar comes back
  Settings → Quit         same thing with the mouse

tomodachi always restores everything when it quits. If it crashes, a small
watchdog process cleans up. If something still looks wrong:
Win+R → path\to\tomodachi.exe --restore → Enter.


UNINSTALL
---------
1. Settings → turn off "Start with Windows"
   (or Win+R → path\to\tomodachi.exe --uninstall).
2. Settings → Quit.
3. Delete this folder. Settings and logs live in %APPDATA%\tomodachi, the icon
   cache in %LOCALAPPDATA%\tomodachi. Both can go.


GOOD TO KNOW
------------
- Windows 10/11, 64-bit only. English or German UI (follows Windows).
- Over games, videos and maximized windows bar and notch hide. A Win tap still
  brings both up.
- CPU temperatures need the optional PawnIO driver (https://pawnio.eu).
- Problems? Settings → Logs, send the newest file.

Built entirely by an AI (Claude) for personal use, shared as is.
Third-party libraries and their licenses: THIRD-PARTY-NOTICES.txt.
Source: https://github.com/deadprocess/tomodachi
