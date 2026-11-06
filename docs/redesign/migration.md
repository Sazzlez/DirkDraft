# Von 1.x auf 2.0 — wie installierte Kopien umsteigen (Plan, M0)

## Ausgangslage 1.x

- Installiert mit Velopack (`vpk pack`, Paket-ID **`DirkDraft`**), nach `%LOCALAPPDATA%\DirkDraft`,
  Hauptprogramm `DirkDraft.exe`, Desktop- und Startmenü-Verknüpfung.
- Update-Quelle: GitHub Releases von `Sazzlez/DirkDraft` (`releases.win.json`). Beim Start eine
  Prüfung; „Version X ist da“ → **Jetzt aktualisieren** → herunterladen, ersetzen, neu starten.
  Nichts ohne Klick.
- Daten und Einstellungen liegen getrennt vom Programm: `%LOCALAPPDATA%\DraftPilot\data`,
  `%APPDATA%\DraftPilot\settings.json`.

## Empfehlung: 2.0 als nächstes Velopack-Paket derselben ID

Velopack kann jede Windows-Anwendung verpacken, nicht nur .NET, und hat ein offizielles Rust-Paket
(`velopack`-Crate, gelesen in docs.velopack.io „Getting started: Rust“). Damit:

1. Die Tauri-App wird mit `vpk pack --packId DirkDraft --mainExe DirkDraft.exe` gepackt — dieselbe
   ID, derselbe Programmname. Für Windows 10 ohne Edge-WebView2 kommt die Laufzeit über
   `--framework webview2` vor der Installation (laut Velopack-Doku „Bootstrapping“).
2. In `main()` der Rust-Seite läuft zuerst `VelopackApp::build().run()` (Installer-Haken), die
   Update-Prüfung nutzt den `UpdateManager` des Crates gegen die GitHub-Releases.
3. Eine installierte 1.5 sieht 2.0 als ganz normales Update: **„Version 2.0.0 ist da“ → Jetzt
   aktualisieren.** Kein zweiter Installer, kein zweiter Programmordner, die Verknüpfungen bleiben.
4. 2.0 liest `settings.json`, `snapshot.json` und den Build-Cache an den alten Orten (Formate
   kompatibel), Fensterposition und Einstellungen gehen also nicht verloren.
5. Neue Nutzer laden wie bisher `DirkDraft-win-Setup.exe` — dann rund 10–15 MB statt 78 MB.

**Vor dem Release getestet**, nicht angenommen (Spike gleich in M1, Abnahme in M6):
- Update einer installierten 1.5 auf ein Tauri-Paket gleicher ID in einer frischen Windows-10-VM.
- Ob der Rust-Crate GitHub Releases direkt kann; sonst `HttpSource` auf
  `https://github.com/Sazzlez/DirkDraft/releases/latest/download/` (dort liegt `releases.win.json`).
- WebView2-Bootstrap ohne vorinstallierte Laufzeit.

**Ausprobieren ohne die Freunde zu treffen:** Vorab-Versionen unter eigener Paket-ID
(`DirkDraft-Beta`, eigener Ordner, eigene Verknüpfung) oder als Velopack-Kanal `beta`; 1.x-Kopien
prüfen nur den Standardkanal und sehen davon nichts.

## Alternative (nur falls der Spike scheitert)

Tauri-eigener NSIS-Installer mit `tauri-plugin-updater` und eine **Brückenversion 1.6** über
Velopack, die den 2.0-Installer herunterlädt, startet und sich danach selbst deinstalliert.
Nachteile: zwei Installationsorte im Übergang, zwei Update-Mechanismen, mehr Fehlerquellen.

## Was 1.x-Nutzer merken

- Ein Klick wie bei jedem Update. Danach neues Fenster, gleiche Einstellungen.
- Datenquelle wechselt auf den eigenen Sammler: der alte OP.GG-Snapshot bleibt nutzbar, bis die
  erste eigene Datei geladen ist; die Datenstand-Pill sagt, welcher Bestand gerade gilt.
