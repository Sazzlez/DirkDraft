# DirkDraft

Draft- und Spielhilfe für League of Legends. Liest den Champ Select live mit, schätzt die Lanes des
Gegners und schlägt Picks und Banns vor — für dich und für jeden Teammate, der am Zug ist. Im Spiel
zeigt es den nächsten Kauf gegen dein Gold, den nächsten Skillpunkt und was die Gegner bauen.

Ab Version 2.0 rechnet DirkDraft mit **eigenen Zahlen** aus Ranked-Spielen in Platin (Solo/Duo,
EUW), die ein Sammler über die offizielle Riot-API zählt — nicht mehr mit OP.GG.

## Installieren und aktualisieren

`DirkDraft-win-Setup.exe` von der Releases-Seite (https://github.com/Sazzlez/DirkDraft/releases)
laden und doppelklicken. Es installiert sich ins eigene Benutzerprofil, legt eine Verknüpfung an und
startet. Windows warnt beim ersten Start, weil der Installer nicht signiert ist („Weitere
Informationen" → „Trotzdem ausführen"). Fehlt unter Windows 10 die WebView2-Laufzeit von Microsoft,
holt der Installer sie vorher.

Neue Versionen: Beim Start fragt DirkDraft einmal bei GitHub nach. Gibt es eine neuere Version,
steht oben „Version X ist da · Jetzt aktualisieren" — erst der Klick lädt, installiert und startet
neu. Wer 1.x installiert hat, bekommt 2.0 genauso als Update; Einstellungen und Fensterplatz bleiben.
Was sich geändert hat, steht in `CHANGELOG.md`.

## Was es zeigt

Oben drei Bereiche, die von selbst wechseln (ein Klick hält bis zum nächsten Moment):

- **Draft** — das Board mit beiden Teams, geschätzten Lanes und der Uhr; darunter die Vorschläge
  für den Platz, der gerade dran ist, mit Begründung und Fehlerbalken („Vorne gleichauf"), deinem
  Hover, Banns, Draft-Chance und Comp-Vergleich. Nach dem Lock: dein Matchup mit Build und dem Knopf
  **„Runen und Zauber übernehmen"**. In ARAM und Mayhem: „Behalten oder tauschen?" für deine Bank.
- **Build** — Runen, Zauber, Start, Schuhe, Kern, spätere Items und die Skill-Reihenfolge bis 18.
- **Spiel** — nächster Kauf gegen dein Gold, Build-Pfad, nächster Skillpunkt, Gegner-Items mit
  Schadensart und „Für dieses Spiel": bis zu drei Items, die gegen genau diese Gegner deutlich öfter
  gekauft werden, als Alternative neben dem Build.

Das Fenster passt sich der Breite an: breit ab 1.100 Pixeln, mittel ab 720, schmal ab 340 — schmal
passt es neben den Client. X legt es in den Infobereich (Rechtsklick auf das Symbol: Öffnen ·
Beenden); bei Champ Select und Spielstart kommt es von selbst, ohne den Fokus zu nehmen.

**Einstellungen** (Zahnrad): Ein- und Ausblenden, immer im Vordergrund, Blitz auf D oder F (Standard
F), auch nicht gekaufte Champions vorschlagen. Gespeichert in `%APPDATA%\DraftPilot\settings.json`.

## Woher die Zahlen kommen

Ein GitHub-Actions-Workflow (`.github/workflows/collect-builds.yml`) sammelt alle sechs Stunden
fünfeinhalb Stunden lang mit einem persönlichen Riot-Schlüssel (Secret `RIOT_API_KEY`) und
veröffentlicht vier Dateien im Branch `data`: `meta.json` (Lane-Winrates, Duelle, Duos, Banns,
Schadensarten), `builds.json`, `situational.json` und `aram.json`. DirkDraft lädt sie alle 30
Minuten — nie während Draft oder Spiel — und hält eine Kopie unter
`%LOCALAPPDATA%\DraftPilot\data\2.0`. In keiner Datei stehen Spielernamen, PUUIDs oder Match-IDs.
Mayhem-Spiele gibt die Riot-API nicht heraus; für Mayhem stehen die Zahlen aus normalem ARAM. Mehr
in `docs/redesign/daten.md`.

## Was DirkDraft nie tut

Keine Namen anderer Spieler in der Anzeige, keine Cooldown- oder Ult-Timer, kein Lesen aus dem
Spielspeicher, nichts injiziert, kein automatisches Hovern, Picken, Bannen oder Annehmen. Geschrieben
wird in den Client nur, was du mit „Runen und Zauber übernehmen" anklickst; fremde Runenseiten
werden nur nach Rückfrage mit Namen ersetzt. Keine Telemetrie; Netz nur für die eigenen Daten,
Data Dragon (Bilder, Namen) und die Update-Prüfung.

## Entwickeln

Die App liegt in `app/` (Tauri 2, Svelte 5, TypeScript): der Kern in `app/src/core`, die Anbindung
an Client und Spiel in Rust unter `app/src-tauri/src`. Voraussetzungen: Node, Rust (MSVC) und die
Visual Studio Build Tools.

```powershell
cd app
npm install
npx tauri dev        # Fenster mit echtem Client
npm run dev          # nur die Oberfläche im Browser, mit aufgezeichneten Drafts (?scene=…)
npx vitest run       # TS-Tests (inkl. Golden-Vergleich mit 1.5)
cd src-tauri; cargo test
```

Der Sammler und die Werkzeuge sind C# (`src/DraftPilot.Tools`, `riot collect|aram|export|…`),
siehe `docs/redesign/daten.md`. `src/DraftPilot.App` ist das alte 1.x-Fenster und dient noch als
Vergleich.

Eine Version veröffentlichen (aus dem Haupt-Checkout auf `master`):

```powershell
.\app\scripts\release.ps1 -Version 2.0.0 -PackId DirkDraft-Beta   # Probe, eigene App, kein Upload
.\app\scripts\release.ps1 -Version 2.0.0                           # echtes Release
```

Das Skript trägt die Version ein, testet, baut (`tauri build`), packt mit `vpk` unter der Paket-ID
`DirkDraft` (dieselbe wie 1.x), checkt „Release X.Y.Z" ein, setzt den Tag, schiebt und lädt hoch.
Voraussetzungen: `dotnet tool install -g vpk`, GitHub CLI mit `gh auth login`.
