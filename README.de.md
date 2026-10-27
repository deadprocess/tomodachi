# tomodachi (Deutsch)

Eine Shell für Windows im Stil von [Noctalia](https://github.com/noctalia-dev/noctalia): Startmenü und Taskleiste
weichen einer schmalen Leiste oben am Bildschirm, mit den offenen Fenstern als Tabs und einer Notch, die aus ihrer Mitte
fließt. Ein Tipp auf die Win-Taste klappt die Notch auf: App-Starter, Widgets mit Schnelleinstellungen (Lautstärke,
Ausgabegerät, WLAN, Bluetooth), Fensterliste, Einstellungen und Powermenü. Wer nur die Notch will: Einstellungen →
*Nur Notch*. Die ausführliche Beschreibung steht in der [englischen README](README.md).

> **Zum Projekt.** tomodachi wurde komplett von einer KI (Claude von Anthropic) im Gespräch mit mir programmiert.
> Ich habe entschieden, was es können und wie es sich anfühlen soll, jede Version auf meinem PC getestet und Fehler
> gemeldet. Den Code habe ich nicht gelesen. Es ist meine persönliche Shell, geteilt, wie sie ist.

## Loslegen

1. `tomodachi-<Version>.zip` unter [Releases](../../releases) laden und direkt nach `C:\Programme\tomodachi` entpacken
   (fragt einmal nach Adminrechten). Der Autostart startet genau diese Exe mit Adminrechten, deshalb gehört sie in einen
   Ordner, den nur Admins ändern dürfen. An anderen Orten warnt tomodachi beim Einschalten des Autostarts.
   Entpacken oder kopieren, nicht verschieben: Verschieben auf demselben Laufwerk behält die Dateirechte des alten
   Ordners, dann warnt tomodachi auch unter `C:\Programme`. In dem Fall bietet tomodachi beim Einschalten des Autostarts
   an, die Rechte zurückzusetzen. Von Hand geht es in einer Admin-PowerShell:

   ```powershell
   icacls "C:\Program Files\tomodachi" /setowner Administratoren /T /C
   icacls "C:\Program Files\tomodachi" /reset /T /C
   ```
2. `tomodachi.exe` starten. SmartScreen warnt, weil die Exe nicht signiert ist: *Weitere Informationen → Trotzdem ausführen*.
3. In den Einstellungen (⚙) **Mit Windows starten** einschalten (fragt einmal nach Adminrechten), danach dort
   **Neu starten**. Erst dann läuft tomodachi mit Adminrechten.

Die Oberfläche ist auf Deutsch oder Englisch, je nach Windows-Sprache oder fest in den Einstellungen.

## Notausstieg

- `Strg+Alt+Shift+Q` oder Einstellungen → **Beenden**: tomodachi beendet sich, die Taskleiste kommt zurück.
- `tomodachi.exe --restore`: nimmt alle Änderungen am System zurück, auch ohne laufendes tomodachi.
- Bei einem Absturz räumt ein Wächter-Prozess automatisch auf.

## Sicherheit & Privatsphäre

- **Kein Netzwerk:** keine Verbindungen, keine Telemetrie, keine Update-Prüfung.
- **Sieht jede Taste, nutzt nur die Win-Taste:** So funktionieren Tastatur-Hooks, sie bekommen jeden Tastendruck mit.
  tomodachi wertet nur die Win-Taste aus (dazu Win+W, Win+L, Strg+Esc, Esc bei offener Notch). Alle anderen Tasten
  gehen unverändert durch und werden nie gespeichert oder verschickt, auch nicht im Log.
- **Adminrechte sind optional:** Sie dienen nur dazu, dass die Win-Taste auch über Admin-Fenstern und Spielen mit
  Anti-Cheat funktioniert. Apps aus dem Launcher starten ohne Adminrechte, außer du wählst „Als Administrator starten“.
  Die Autostart-Aufgabe läuft mit Adminrechten, deshalb schließt tomodachi die Wege, auf denen fremder Code mitfahren
  könnte: Exe und DLLs nur aus dem Installationsordner (nichts in `%TEMP%`), ein kleiner `cmd`-Schritt leert vor dem
  Start die `CORECLR_*`-, `COR_*`- und codeladenden `DOTNET_*`-Umgebungsvariablen, die Aufgabe wird ohne Temp-Datei
  angelegt, und das Einschalten aus einem für Nicht-Admins beschreibbaren Ordner fragt nach. Das setzt voraus, dass der
  Installationsordner selbst nur für Admins beschreibbar ist (daher `C:\Programme`).
- **Alles umkehrbar:** Änderungen an der Taskleiste stehen vorher im Wiederherstellungs-Journal und werden beim
  Beenden, Absturz oder `--restore` zurückgenommen. tomodachi selbst schreibt nichts in die Registry
  (Windows speichert wie immer die Taskleisten-Einstellung und die Autostart-Aufgabe).
- **Deine Daten bleiben lokal:** in `%APPDATA%\tomodachi` und `%LOCALAPPDATA%\tomodachi`.
- **Die Exe unter Releases baut GitHub selbst** aus genau diesem Quellcode.

Details in der [englischen README](README.md#security--privacy).

## Lizenz

[MIT](LICENSE). Fremde Bibliotheken: [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt).
