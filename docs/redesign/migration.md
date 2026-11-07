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
4. 2.0 liest `settings.json` am alten Ort (Format kompatibel, unbekannte Schlüssel bleiben stehen).
   Den Fensterplatz (`windowLeft/Top/Width/Height`) übernimmt der erste 2.0-Start, danach hält ihn
   das window-state-Plugin (`%APPDATA%\com.sazzlez.dirkdraft\.window-state.json`). Die Zahlen
   kommen nicht mehr aus `snapshot.json` (OP.GG), sondern aus dem eigenen Sammler; ihre Kopie liegt
   unter `%LOCALAPPDATA%\DraftPilot\data\2.0`. Die 1.x-Dateien daneben (`snapshot.json`, Bilder,
   Build-Cache) stören nicht und werden nicht gelöscht.
5. Neue Nutzer laden wie bisher `DirkDraft-win-Setup.exe` — dann rund 10–15 MB statt 78 MB.

**Vor dem Release getestet**, nicht angenommen (Spike gleich in M1, Abnahme in M6):
- Update einer installierten 1.5 auf ein Tauri-Paket gleicher ID in einer frischen Windows-10-VM.
- Ob der Rust-Crate GitHub Releases direkt kann; sonst `HttpSource` auf
  `https://github.com/Sazzlez/DirkDraft/releases/latest/download/` (dort liegt `releases.win.json`).
- WebView2-Bootstrap ohne vorinstallierte Laufzeit.

**Ausprobieren ohne die Freunde zu treffen:** Vorab-Versionen unter eigener Paket-ID
(`DirkDraft-Beta`, eigener Ordner, eigene Verknüpfung) oder als Velopack-Kanal `beta`; 1.x-Kopien
prüfen nur den Standardkanal und sehen davon nichts.

## Stand der Prüfung (07.11.2026)

- **Werkzeuge:** 1.5 nutzt Velopack 1.2.0 (C#), `vpk` ist 1.2.0, der Rust-Crate 1.2.x — eine Linie.
  1.x ist auf dem Standardkanal `win` gepackt; `release.ps1` packt das echte 2.0 dort auch.
- **Paket-ID wird beim Update nicht geprüft:** Velopacks Update-Prüfung liest nur den Feed des Kanals
  und vergleicht Versionen. Eine Probe unter `DirkDraft-Beta` auf `win` nähme deshalb ein späteres
  echtes Release als eigenes Update. Die Beta packt `release.ps1` daher zusätzlich auf dem Kanal
  `beta`, den nie jemand hochlädt.
- **Probe-Quelle:** `DIRKDRAFT_UPDATE_SOURCE` (Ordner oder http(s)-Adresse) lässt die Update-Prüfung
  von 2.0 einen lokalen Feed lesen statt GitHub — nur für Probe-Installationen; ohne die Variable
  bleibt es GitHub.
- **Sicherer Testweg (nur mit OK, installiert nichts am echten DirkDraft):**
  1. `release.ps1 -Version 2.0.0 -PackId DirkDraft-Beta`, dann `-Version 2.0.1` → `build\Releases-beta`.
  2. `DirkDraft-Beta-beta-Setup.exe` installieren (eigener Ordner `%LOCALAPPDATA%\DirkDraft-Beta`,
     eigene Verknüpfung „DirkDraft Beta“), mit `DIRKDRAFT_UPDATE_SOURCE=…\build\Releases-beta`
     starten: Pill „Version 2.0.1 ist da“ → Klick → Neustart in 2.0.1, Einstellungen und Fensterplatz
     bleiben. Danach über „Apps & Features“ wieder entfernen.
  3. 1.5 → 2.0 ohne die echte Installation: die 1.5-Dateien (`build\Releases\DirkDraft-1.5.0-full.nupkg`)
     unter einer Test-ID als 1.5.0 neu packen, installieren, dann mit dem `Update.exe` dieser
     Test-Installation das 2.0.0-Paket derselben Test-ID anwenden. Das prüft genau den Schritt, den
     „Jetzt aktualisieren“ in 1.5 auslöst (Dateien ersetzen, Haken, Neustart); die Anzeige „Version
     2.0.0 ist da“ in 1.5 ist Velopacks Standard-Feed und unverändert.

## Ergebnis des Update-Tests (07.11.2026)

Bestanden, an Probe-IDs in der Claude-Shell-Umgebung (eigene AppData-Sicht), danach deinstalliert:

- **2.0 → 2.0:** `DirkDraft-Beta` 2.0.1 (Setup `--silent` startet die App nicht von selbst), gestartet
  mit `DIRKDRAFT_UPDATE_SOURCE=…uildReleases-beta` → Pill „Update 2.0.2“ → Klick → Neustart als
  2.0.2; die Prüfung danach meldet „neu keine“. Velopack las den Kanal `beta` aus dem Manifest.
- **1.5 → 2.0:** die 1.5-Dateien unverändert als `DirkDraft-UpdTest` 1.5.0 gepackt (Kanal `updtest`,
  ohne Desktop-Verknüpfung), installiert, gestartet; `Update.exe apply --package …2.0.0-full.nupkg
  --waitPid <1.5>` und 1.5 beendet → `current` enthält nur `DirkDraft.exe` und `sq.version` (2.0.0),
  2.0 startet von selbst, `settings.json` mit 1.5-Werten erhalten, Diagnose dank `diagnostics.on` an.
  Delta 1.5.0 → 2.0.0: 4,0 MB (Vollpaket 6,2 MB); scheitert ein Delta, lädt Velopack das Vollpaket.
- **Befund:** ein 1.5-Fenster ohne gespeicherte Größe verlor seinen Platz → behoben (Position wird
  auch allein übernommen), am Release-Build nachgeprüft.

## Alternative (nur falls der Spike scheitert)

Tauri-eigener NSIS-Installer mit `tauri-plugin-updater` und eine **Brückenversion 1.6** über
Velopack, die den 2.0-Installer herunterlädt, startet und sich danach selbst deinstalliert.
Nachteile: zwei Installationsorte im Übergang, zwei Update-Mechanismen, mehr Fehlerquellen.

## Was 1.x-Nutzer merken

- Ein Klick wie bei jedem Update. Danach neues Fenster, gleiche Einstellungen.
- Datenquelle wechselt auf den eigenen Sammler: der alte OP.GG-Snapshot bleibt nutzbar, bis die
  erste eigene Datei geladen ist; die Datenstand-Pill sagt, welcher Bestand gerade gilt.
