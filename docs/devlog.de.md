# tomodachi – Roadmap

Eine Shell im Stil von Noctalia, die über Windows gelegt wird: Notch mit rundem Reze-Profilbild, App-Launcher, Widget-Center und keine Taskleiste.
Explorer läuft im Hintergrund weiter. tomodachi ersetzt nur das, was man sieht.

## Stack (Vorschlag)

**C# / .NET 10 + WPF** (`net10.0-windows`, SDK 10.0.403 ist installiert) und Win32 per P/Invoke.

- **LibreHardwareMonitorLib** liefert CPU, GPU, RAM, Temperaturen und NVMe-SMART. Mit Web-Stacks bräuchte man dafür einen Sidecar.
- **ManagedShell** (von Cairo Desktop) liefert fertig: AppBar, Tray-Hosting und Fensterliste.
- **Media-Steuerung:** WinRT `GlobalSystemMediaTransportControlsSessionManager`, nativ ansprechbar.
- **Fenster:** transparent, randlos und topmost, mit flüssigen Animationen und wenig RAM (anders als WebView2).

Alternativen:
- **Tauri** (Rust + Svelte): mehr CSS-Freiheit, aber die Sensoren werden schwierig.
- **Qt/QML:** am nächsten an Quickshell und Noctalia, aber schlechtere Windows-Integration.

---

## Phase 0 – Fundament

| #    | Paket                 | Inhalt | Fertig wenn |
|------|-----------------------|--------|-------------|
| 0.1 ✅ | Projekt-Skelett       | Solution, WPF-App, Single-Instance, Logging (`%APPDATA%\tomodachi\logs`), JSON-Config | App startet und loggt |
| 0.2 ✅ | Overlay-Fensterbasis  | Randlos, transparent, topmost, `WS_EX_TOOLWINDOW` (kein Alt+Tab), `WS_EX_NOACTIVATE`, Per-Monitor-DPI v2, klickdurchlässige Bereiche | Leeres Overlay schwebt über allem, klaut keinen Fokus |
| 0.3  | Theme-System          | Farb-Tokens (dunkel, Noctalia-artig), Fonts, Radien, Animationsdauern als Ressourcen | Ein Token ändern → ganze UI ändert sich |
| 0.4 ✅ | **Notausstieg**       | Hotkey `Ctrl+Alt+Shift+Q` beendet und stellt alles wieder her. Crash-Handler gibt Taskleiste und Win-Taste frei. Startflag `--safe` | Absturz oder Kill → Windows ist wieder normal |

> 0.4 kommt **vor** Phase 1. Ohne Taskleiste und mit geschluckter Win-Taste sperrt man sich sonst selbst aus.
>
> **So funktioniert 0.4:** Jede Systemänderung wird *vorher* ins Restore-Journal (`%APPDATA%\tomodachi\restore-journal.json`) eingetragen.
> Ein Watchdog-Prozess (`tomodachi --watchdog <pid>`) wartet auf das Ende der Shell und räumt auf, falls noch etwas im Journal steht.
> Jede Restore-Aktion hat ein Timeout von 5 s. Getestet: Hart-Kill, Hotkey, `--quit`, `--restore`, Watchdog-Neustart, `--safe`.
> Neue Systemeingriffe (Hook, Taskleiste …) bekommen einen Eintrag in `SystemRestore.Actions`.


## Phase 1 – Windows zähmen

| #    | Paket                     | Inhalt | Fertig wenn |
|------|---------------------------|--------|-------------|
| 1.1 ✅ | Win-Taste abfangen        | `WH_KEYBOARD_LL`-Hook auf eigenem Thread. Win **allein getippt** schaltet die Shell um. Das Startmenü wird per „Mask-Key“-Trick unterdrückt (unbelegte VK `0xE8` vor dem Loslassen injizieren). Win+Kombis (Win+E, Win+D, Win+Shift+S) laufen weiter | Win-Tipp öffnet tomodachi, das Startmenü erscheint nie |
| 1.2  | Hotkey-Registry           | Konfigurierbare Kombos, z. B. Win+Space → Launcher, Win+A → Widget-Center (überschreibt Win11 Quick Settings) | Kombos lösen die richtigen Panels aus |
| 1.3 ✅ | Taskleiste entfernen      | `Shell_TrayWnd` und alle `Shell_SecondaryTrayWnd` verstecken, Arbeitsbereich per `SPI_SETWORKAREA` auf Vollbild setzen. Auf `TaskbarCreated` reagieren (Explorer-Neustart), Watchdog gegen Win11-Wiedererscheinen. Beim Beenden wiederherstellen | Maximierte Fenster nutzen den ganzen Bildschirm, keine Taskleiste auf allen Monitoren |
| 1.4 ✅ | Erhöhte Rechte / Autostart | Start per Aufgabenplanung beim Login mit höchsten Rechten, ohne UAC-Prompt. Nötig für Sensoren und damit der Hook auch bei Admin-Fenstern im Fokus greift (UIPI) | Startet nach Login automatisch elevated |

**Bekannte Grenzen:** Win+L und Ctrl+Alt+Del lassen sich nicht abfangen. Der Hook muss schneller als `LowLevelHooksTimeout` antworten, sonst entfernt Windows ihn still.

> **Stand 1.1 ✅ (von dir mit echten Tastendrücken bestätigt am 26.10.2026):** Win-Tipp **und Ctrl+Esc** öffnen und schließen
> den Launcher zuverlässig, ohne Startmenü. Das endgültige Design nach vielen Irrwegen:
> 1. **Win-Down zurückhalten** (`WinKeyStateMachine`): Windows sieht einen einzelnen Win-Tipp nie. Kommt eine zweite Taste,
>    wird Win-Down davor nachgespielt (Win+E, Win+Shift+S … gehen). Win+L sperrt per `LockWorkStation`, weil Windows ein simuliertes Win+L ignoriert.
> 2. **Fokus nur per Hotkey-Recht:** Der Tipp schickt F24, die als `RegisterHotKey` registriert ist. Wer `WM_HOTKEY` bekommt,
>    darf sich nach vorn holen. Alt-Injektion, `SwitchToThisWindow` und `AttachThreadInput` öffneten das Startmenü und sind raus.
>    Der Fokus geht nie an die geparkte Windows-Suche zurück (`ShellWindows`).
> 3. **Hook blind, wenn tomodachi vorn ist:** Solange ein tomodachi-Fenster im Vordergrund ist, ruft Windows den eigenen
>    LL-Hook nicht auf (reproduziert, unabhängig vom Tastaturfokus). Dann übernimmt Raw Input: Dummy-Taste VK `0xFF` beim
>    Win-Down (kein Startmenü), Win-Up ohne andere Taste = Tipp.
> 4. **Hook am Leben halten:** Aufwärmen gegen JIT-Timeouts, Raw-Input-Gegenprobe hängt sofort neu ein, zusätzlich alle 30 s.
>
> Abschaltbar per `"InterceptWinKey": false`, im `--safe`-Modus aus. `"DiagnoseWinTapNoAction": true` erkennt Tipps nur, ohne Aktion (Fehlersuche).
> Logik per Unit-Tests abgedeckt (`tests/`). Live-Tests mit der Win-Taste macht der User selbst (siehe Lehre: injizierte Tasten mischen sich mit echtem Tippen).

> **Stand 1.3:** Auto-Hide ist **nicht** das Ziel, sondern das Mittel. Mit Auto-Hide gibt Explorer selbst den Arbeitsbereich
> auf allen Monitoren frei, statt ihn wie bei `SPI_SETWORKAREA` zurückzusetzen. Das Fenster wird zusätzlich versteckt,
> damit die Leiste auch beim Hovern nicht hereinfährt. `TaskbarHider` hält sie weg: ein WinEvent-Hook auf `EVENT_OBJECT_SHOW`
> (sofort) und 1-s-Polling (Explorer-Neustart, neue Monitore). Der Originalzustand steht im Journal.
> Getestet: 3 Taskleisten-Fenster weg, Arbeitsbereich 3440×1440, Explorer-Einblenden nach < 150 ms wieder weg, `--quit` und Hart-Kill stellen alles zurück.
> Abschaltbar per `"HideTaskbar": false`.

> **Stand 1.4 ✅ (eingerichtet am 26.10.2026):** Getestet mit echtem Start ohne Adminrechte über explorer.exe:
> Der Neustart über die Aufgabe läuft ohne UAC-Abfrage und mit `--from-task`, elevated. `--quit` aus einem Prozess mit Medium-Integrität beendet die elevated Shell sauber.
> Aufbau: `tomodachi --install` legt (mit UAC-Abfrage) die Aufgabe
> „tomodachi“ an: Start bei Anmeldung mit höchsten Rechten, Priorität normal. Ein normaler Start erkennt die Aufgabe und
> startet ohne UAC-Abfrage über `schtasks /Run` elevated neu. `--uninstall` entfernt sie. Single-Instance und `--quit` funktionieren
> auch zwischen elevated und normalem Prozess.
> **Folge für Phase 3:** Ein elevated Launcher würde Apps elevated starten. Apps müssen deshalb ohne Adminrechte gestartet werden (z. B. über Explorer).

## Phase 2 – Notch und Profilbild

| #    | Paket               | Inhalt | Fertig wenn |
|------|---------------------|--------|-------------|
| 2.1 ✅ | Profilbild         | Rundes Reze-Profilbild als „Icon“ in der Notch (ersetzt das ursprünglich geplante Chibi-Maskottchen) | Profilbild liegt in `assets/avatar/` |
| 2.2 ✅ | Notch (zugeklappt)  | Dunkle Pille, oben mittig an der Kante, unten abgerundet, Maskottchen darin | Notch sitzt pixelgenau auf dem Hauptmonitor |
| ~~2.3~~ | ~~Maskottchen-Animation~~ | Entfällt: Profilbild statt animiertem Maskottchen | – |
| 2.4  | Ausklapp-Mechanik   | State-Machine `collapsed → peek → expanded(launcher \| widgets \| …)`, Spring-Animation im Dynamic-Island-Stil, die die Größe morpht | Weiche Übergänge zwischen allen Zuständen |
| 2.5  | Peek-Inhalte        | Uhrzeit, aktueller Song, Lautstärke- und Helligkeits-OSD (ersetzt das Windows-OSD) | Lautstärke ändern → Notch zeigt kurz den Balken |

> **Stand 2.1 (geändert am 26.10.2026):** Kein animiertes Chibi-Maskottchen mehr, stattdessen ein **rundes Profilbild** in
> der Notch: `assets/avatar/reze.png` (256 px, außerhalb des Kreises transparent), 30 px groß mit dezentem Ring.
> Geschnitten aus `assets/avatar/source/reze-sketch.png` mit `tools/make-avatar.ps1` (Ausschnitt x 290, y 165, 310 px:
> eng ums Gesicht, weil bei 30 px sonst das Haar dominiert). Die Notch-Fläche ist voll deckend, bei 95 % schien Text durch.
> Live auf dem Alienware geprüft: zugeklappt und mit offenem Launcher.
> Fan-Art (Signatur „NL“), nur privat. Die vorherigen Chibi-Versionen (Vektor und Cursor-Pack-Nachzeichnung) sind entfernt.

> **Stand 2.2:** `NotchShape` zeichnet den Umriss mit konkaven „Ohren“ an der Bildschirmkante, ohne Kontur an der Oberkante.
> Das Fenster hat immer die ausgeklappte Maximalgröße und ist sonst transparent (klickdurchlässig), deshalb animiert nur die Form.
> Größen und Farben kommen aus `Theme/Theme.xaml` (erster Teil von 0.3). Hover vergrößert die Notch leicht, Ausklappen
> animiert in 250 ms (Vorgriff auf 2.4). `FullscreenWatcher` blendet sie aus, solange ein Vollbild-Programm auf dem Hauptmonitor
> im Vordergrund ist. Maximierte Fenster zählen nicht als Vollbild. Getestet: Position, Kanten, Ausklappen, Vollbild vs. maximiert.
> Hover ist nur gebaut, nicht getestet (dafür müsste ich deine Maus bewegen).

## Phase 3 – App-Launcher

| #    | Paket           | Inhalt | Fertig wenn |
|------|-----------------|--------|-------------|
| 3.1 ✅ | App-Index       | Startmenü-Verknüpfungen (User und ProgramData), Store-Apps über `shell:AppsFolder`, Icons extrahieren und cachen | Alle installierten Apps mit Icon gelistet |
| 3.2 ✅ | Suche & Ranking | Fuzzy-Match, Frecency (häufig/zuletzt benutzt) wird gespeichert | „vsc“ findet VS Code an erster Stelle |
| 3.3 🟡 | Launcher-UI     | In der ausgeklappten Notch: Suchfeld, Ergebnisliste, ↑↓/Enter/Esc, Ctrl+Shift+Enter startet als Admin. Rechtsklick → Anpinnen: bis zu 5 Kacheln über der Liste (`pinned-apps.json`), Ctrl+1…5 startet sie | Komplett per Tastatur bedienbar |
| 3.4  | Extras          | Rechner, Websuche, `ms-settings:`-Seiten, Power-Befehle (Herunterfahren, Neustart, Sperren, Energiesparen) | „shutdown“ und „2*21“ funktionieren |

> **Stand 3.1:** `AppIndex` liest `shell:AppsFolder` (= „Alle Apps“ des Startmenüs, klassisch + Store) auf einem eigenen STA-Thread:
> 90 Apps in ~200–280 ms, Deinstaller werden ausgefiltert. Neu eingelesen wird beim Öffnen, höchstens alle 30 s.
> `IconCache`: Speicher → PNG in `%LOCALAPPDATA%\tomodachi\icons` → Shell (`IShellItemImageFactory`, Alphakanal korrekt).
>
> **Stand 3.2:** `FuzzyMatcher` in Stufen: exakt > Prefix > Wortanfang > Akronym („vsc“) > Teilstring > Teilfolge. Ohne Diakritika.
> Zusätzlich ein Alias aus Exe- oder Paketname: „notepad“ → „Editor“, „calc“ → „Rechner“. `LaunchHistory` (Frecency) mit Halbwertszeit 14 Tage,
> Boost ≤ 120. 29 Unit-Tests.
>
> **Stand 3.3 (🟡):** Win-Tipp/Ctrl+Esc öffnet den Launcher in der Notch (vorerst fest, 1.2 macht es konfigurierbar).
> Getestet: Fokus im Suchfeld, Tippen filtert, ↑↓, Enter startet, Esc schließt und gibt den Fokus ans vorherige Fenster zurück.
> Eine aus der elevated Shell gestartete App läuft **ohne Adminrechte** (Start über explorer.exe, Elternprozess explorer).
> Win-Tipp zum Öffnen und Schließen vom User bestätigt. Noch nicht getestet: Mausklick auf einen Treffer, Ctrl+Shift+Enter.
> Bugs unterwegs: Fokus landete aufs Fenster statt aufs Suchfeld (behoben). Windows hat den Hook nach einem langsamen Start still entfernt
> (behoben: Aufwärmen + Raw-Input-Gegenprobe hängt sofort neu ein + Neu-Einhängen alle 30 s).

## Phase 4 – Widget-Center

| #    | Paket                | Inhalt | Fertig wenn |
|------|----------------------|--------|-------------|
| 4.1 ✅ | Panel-Rahmen         | Widgets-Reiter in der ausgeklappten Notch mit Karten-Grid. Schließt per Esc oder Klick daneben | Leere Karten animieren rein und raus |
| 4.2 ✅ | Sensor-Service       | LibreHardwareMonitor im 1-s-Takt, Ringpuffer für Verlaufskurven, ein zentraler Service für alle Widgets | Werte ohne UI im Log sichtbar |
| 4.3 ✅ | CPU / GPU / RAM      | Last, Temperatur, Takt, VRAM, Sparklines | Stimmt mit dem Task-Manager überein |
| 4.4 ✅ | Laufwerke            | Belegung pro Laufwerk, NVMe/SSD-Temperatur und -Gesundheit (SMART), Lese- und Schreibrate | Alle SSDs und NVMes mit Temperatur |
| 4.5 ✅ | Netzwerk             | WLAN (SSID, Signal) oder Ethernet, Up/Down-Rate, IP. Später: WLAN-Liste und Verbinden | Live-Traffic sichtbar |
| 4.6 ✅ | Media-Player         | GSMTC: Cover, Titel, Interpret, Play/Pause/Vor/Zurück, Fortschritt, mehrere Quellen (Spotify, Browser) | Spotify lässt sich aus der Shell steuern |
| 4.7  | Audio                | Lautstärke, Ausgabegerät wechseln (CoreAudio) | Ersetzt das Lautstärke-Icon der Taskleiste |
| 4.8  | Quick-Toggles        | Bluetooth, Nicht stören, Dark Mode, Energieplan, Akku (Laptop) | Toggles wirken sofort |

> **Stand Phase 4 + 5.1 + 5.3 (26.10.2026, live auf dem Alienware geprüft):** Alles **in der Notch als Reiter**
> (Apps · Widgets · Fenster), kein eigenes Panel. Widgets-Reiter 960 px breit, Apps und Fenster 640 px; die Notch morpht beim Wechsel.
> Öffnen: Win-Tipp → Apps, **Win+W** / Klick aufs Profilbild / `--widgets` → Widgets, `--windows` → Fenster.
> Nur der Apps-Reiter holt sich den Fokus. Widgets und Fenster bleiben **ohne Fokus**, sonst wäre der Hook blind (Win+W, Esc);
> beim Wechsel Apps → Widgets geht der Fokus ans vorherige Fenster zurück. Klick woanders hin klappt zu (Vordergrund-WinEvent).
> Widgets-Karten: Uhr + Monatskalender, Media (Cover, Titel, Quelle, ⏮⏯⏭, Fortschritt), System (CPU/GPU/RAM mit 60-s-Kurven,
> GPU-Temperatur + VRAM), Laufwerke (Belegung, NVMe-Temperatur, Lese-/Schreibrate), Netzwerk (Typ, Speed, IP, ↓/↑ mit Kurven).
> Fenster-Reiter: Alt+Tab-Liste mit Icons, Klick holt nach vorn, ✕ schließt.
> Sensoren: LibreHardwareMonitorLib **0.9.6** (MPL-2.0), geprüft: **kein WinRing0**, nur PawnIO, falls installiert.
> Ohne PawnIO fehlen CPU-Temperaturen (werden ausgeblendet). Aktualisiert wird nur bei offenem Widgets- oder Fenster-Reiter:
> 0,14 % CPU offen, 0 % zu. RAM-Bedarf gesamt ~255 MB (→ 6.3). Mediendaten über WinRT, deshalb TFM `net10.0-windows10.0.19041.0`.
> Noch nicht von dir getestet: Reiter per Klick, Win+W, Esc, Klick aufs Profilbild, Fenster anklicken und schließen, Media-Buttons.

## Phase 5 – Was mit der Taskleiste verschwindet

| #    | Paket                  | Inhalt | Fertig wenn |
|------|------------------------|--------|-------------|
| 5.1 ✅ | Uhr & Kalender         | Datum und Kalender im Widget-Center | – |
| 5.2  | **Tray-Icons**         | Discord, Steam usw. haben sonst kein Icon mehr. tomodachi wird per ManagedShell selbst Tray-Host. Das ist der schwierigste Teil | Tray-Icons mit Rechtsklick-Menü in der Shell |
| 5.3 ✅ | Laufende Fenster       | Liste offener und minimierter Fenster (sonst nicht mehr erreichbar), als Fenster-Reiter in der Notch | Minimiertes Fenster wiederherstellbar |
| 5.4  | Benachrichtigungen     | Windows-Toasts laufen weiter. Optional in der Notch spiegeln (`UserNotificationListener`) | – |
| 5.6 ✅ | Powermenü              | Power-Reiter (⏻ in der Notch-Kopfzeile, `--power`): Sperren, Energie sparen, Abmelden, Neustart, Herunterfahren, Laufzeit. Sitzungsende nur mit zweitem Klick (3 s) | Ersetzt den Ein/Aus-Knopf des Startmenüs |
| 5.5 ✅ | Wallpaper-Picker       | Eigenständigen `C:\WallpaperPicker` 1:1 übernommen (Overlay, Bedienung, Win+Strg+W), ohne eigenen Prozess und Tray-Icon | Win+Strg+W öffnet das Kartenband, Enter setzt das Bild |

> **Stand 5.5 (26.10.2026):** Code in `Wallpaper/`, nur Namensraum und Klassenname (`WallpaperOverlay`) angepasst.
> Tray-App ersetzt durch `WallpaperPicker` (Hotkey über tomodachis `GlobalHotkey`, Monitorgrößen per `EnumDisplayMonitors`
> statt WinForms), `--wallpaper` als CLI-Befehl, Ordner per `WallpaperFolder`. Öffnen klappt die Notch zu.
> Nebenbei behoben: Strg→Win→W wurde als Win+W (Widgets) abgefangen. Win-Kombis gelten jetzt nur ohne Strg/Shift/Alt.
> Live geprüft: Hotkey registriert, Overlay per `--wallpaper` auf und zu, Abdunkeln wie im Original. RAM tomodachi ~280 MB
> (die Vorschaubilder bleiben wie bei der Tray-App im Speicher). Noch nicht von dir getestet: Win+Strg+W, Bild setzen, Shift für alle Monitore.

## Phase 6 – Betrieb

| #    | Paket          | Inhalt |
|------|----------------|--------|
| 6.1 🟡 | Settings-UI (Einstellungs-Reiter ⚙: Profilbild, Monitor, Ausblenden, Win-Taste, Taskleiste, Autostart, Wallpaper-Ordner, Neustart/Beenden) | Offen: Hotkeys, Theme, Widgets konfigurieren |
| 6.2 🟡 | Multi-Monitor (Notch-Monitor per `NotchMonitor` wählbar, z. B. `DELA0BA`) | Notch pro Monitor oder nur auf dem Hauptmonitor, Taskleiste auf allen entfernen |
| 6.3  | Performance    | Budget: Leerlauf unter 1 % CPU, unter 150 MB RAM |
| 6.4 🟡 | Installer (`tools\publish.ps1` → ZIP mit Einzel-Exe, LIES-MICH, Lizenzen; Autostart per Einstellungen) | Offen: signierte Exe, damit SmartScreen nicht warnt |

---

## MVP-Reihenfolge

1. 0.1 → 0.2 → **0.4**
2. 1.1 (Win-Taste)
3. 2.2 + Profilbild
4. 1.3 (Taskleiste weg)
5. 3.1 → 3.3 (Launcher)
6. 4.1 + 4.2 + 4.3 + 4.6 (erste Widgets)
7. 5.3 (Fensterliste, sonst ist man ohne Taskleiste aufgeschmissen)

Danach der Rest in beliebiger Reihenfolge, 5.2 (Tray) möglichst früh.

## Offene Entscheidungen

- [x] Stack: .NET 10 + WPF
- [ ] Hotkeys: Win-Tipp → Widget-Center oder Launcher? Welche Kombo für das andere?
- [x] Notch-Icon: rundes Reze-Profilbild statt Chibi-Maskottchen. Reze ist eine geschützte Figur (Chainsaw Man), das Bild ist Fan-Art, also nur privat nutzen.
- [ ] Explorer als Shell behalten (Vorschlag) oder komplett ersetzen? Ersetzen bricht Toasts, Tray und Teile der Einstellungen.
