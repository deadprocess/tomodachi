# tomodachi (Deutsch)

Eine Shell für Windows im Stil von [Noctalia](https://github.com/noctalia-dev/noctalia-shell): Startmenü und Taskleiste
weichen einer Notch oben am Bildschirm. Ein Tipp auf die Win-Taste klappt sie auf: App-Starter, Widgets, Fensterliste,
Einstellungen und Powermenü. Die ausführliche Beschreibung steht in der [englischen README](README.md).

> **Zum Projekt.** tomodachi wurde komplett von einer KI (Claude von Anthropic) im Gespräch mit mir programmiert.
> Ich habe entschieden, was es können und wie es sich anfühlen soll, jede Version auf meinem PC getestet und Fehler
> gemeldet. Den Code habe ich nicht gelesen. Es ist meine persönliche Shell, geteilt, wie sie ist.

## Loslegen

1. `tomodachi-<Version>.zip` unter [Releases](../../releases) laden und an einen festen Ort entpacken.
2. `tomodachi.exe` starten. SmartScreen warnt, weil die Exe nicht signiert ist: *Weitere Informationen → Trotzdem ausführen*.
3. In den Einstellungen (⚙) **Mit Windows starten** einschalten (fragt einmal nach Adminrechten).

Die Oberfläche ist auf Deutsch oder Englisch, je nach Windows-Sprache oder fest in den Einstellungen.

## Notausstieg

- `Strg+Alt+Shift+Q` oder Einstellungen → **Beenden**: tomodachi beendet sich, die Taskleiste kommt zurück.
- `tomodachi.exe --restore`: nimmt alle Änderungen am System zurück, auch ohne laufendes tomodachi.
- Bei einem Absturz räumt ein Wächter-Prozess automatisch auf.

## Sicherheit & Privatsphäre

- **Kein Netzwerk:** keine Verbindungen, keine Telemetrie, keine Update-Prüfung.
- **Nur die Win-Taste:** Alle anderen Tasten gehen unverändert durch und werden nie gespeichert, auch nicht im Log.
- **Adminrechte sind optional:** Sie dienen nur dazu, dass die Win-Taste auch über Admin-Fenstern und Spielen mit
  Anti-Cheat funktioniert. Apps aus dem Launcher starten ohne Adminrechte, außer du wählst „Als Administrator starten“.
- **Alles umkehrbar:** Änderungen an der Taskleiste stehen vorher im Wiederherstellungs-Journal und werden beim
  Beenden, Absturz oder `--restore` zurückgenommen. In die Registry schreibt tomodachi nichts.
- **Deine Daten bleiben lokal:** in `%APPDATA%\tomodachi` und `%LOCALAPPDATA%\tomodachi`.
- **Die Exe unter Releases baut GitHub selbst** aus genau diesem Quellcode.

Details in der [englischen README](README.md#security--privacy).

## Lizenz

[MIT](LICENSE). Fremde Bibliotheken: [packaging/THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt).
