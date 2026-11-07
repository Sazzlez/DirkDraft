# DirkDraft 2.0 — Prüfbericht (07.11.2026)

Auftrag: `docs/redesign/auftrag-pruefung.md`. Gearbeitet im Worktree `redesign-board`, Branch
`redesign/board`. Nichts ausgeliefert, gepusht, gemergt oder installiert; `master` und der
Haupt-Checkout sind unverändert (`1d933e7`).

## Kurzfassung

2.0 war nicht auslieferbar — vor allem, weil vier Quelldateien und alle Testaufzeichnungen nie
eingecheckt waren (ein frischer Checkout hätte nicht gebaut) und weil die App die echte
`situational.json` verwarf. Beides ist behoben, dazu eine Reihe von Fehlern in Oberfläche, Builds,
situativen Items und Client-Anbindung. Die Build-Auswahl ist auf eine gemessene Regel umgestellt.
Offen bleiben der Live-Durchlauf mit dir, der Update-Test an einer Probe-Installation (braucht dein
OK) und Windows 11 (Mica) bei einem Freund.

## Gefunden und behoben

### Repository
- `.gitignore` mit `build/` und `recordings/` erwischte jeden Ordner dieses Namens: `app/src/ui/build`
  (Build-Bereich, Übernehmen-Knopf, Runen, Skill-Raster), `app/tests/recordings` und
  `app/tests/golden/recordings` waren nie eingecheckt. Jetzt nur noch in der Wurzel; ein Export des
  Branches testet und baut.

### Oberfläche (alle Szenen, 3 Breiten × 2 Themen, dazu Release-Fenster)
- Schrittleiste fest auf Turnier-Draft („Bann 2“ während gepickt wurde) → liest die echte Zugfolge
  (Turnier, Ranked/„Abwechselnde Wahl“, Blind).
- „Du bannst“ in der Planungsphase → „Planung“.
- Tote Knöpfe: „Runen übernehmen“ im Build-Bereich, „ändern“ an der Gegner-Lane → echter
  Übernehmen-Knopf (nur im Champ Select), Lane-Menü mit „automatisch“ (auch für die eigene Lane im
  Blind Pick).
- Klick auf einen Teammate (1.5-Funktion) war nicht verdrahtet → wieder da, auch nach dem eigenen Lock.
- Patch-Rückstand wurde nie gesetzt → Client-Patch wird gelesen, Hinweis + breitere Fehlerbalken.
- Titelleiste mittel (Daten-Pill „…vo…“), leere dritte Spalte, Comp-Vergleich über den Kachelrand,
  doppelter Strich im Matchup, Runen-Symbole im hellen Thema, Uhr-Ring nur für 30 s, Build-Kopf
  „Lane· Kein Matchup …“ doppelt, unterschiedliche Knopftexte, Versionszeile fehlte in der echten
  Lobby, Browser-Dialog für die fremde Runenseite → im Fenster gefragt.
- Queues: Blind, Quickplay, Clash, Co-op, Arena hießen „Ranked Solo/Duo“ → eigene Namen; Flex sagt
  „Zahlen aus Solo/Duo“. Arena und ARAM ohne eigene Zahl erklären sich statt leer zu bleiben.

### Vorschläge (Draft)
- Lane-Glättung auf eigenen Daten gemessen: 800 statt 300 (Halbierung 800, Momente 851, Rangtest
  50,27 % statt 50,18 %). Golden-Parität mit 1.5 bleibt (OP.GG-Datei behält 300).
- Duell- und Duo-Gewichte bestätigt (kein messbares Signal bei 14 bzw. 36 Spielen im Median).

### Builds
- Wilson-Auswahl wählte Zufall (Caitlyn: Runenseite mit 8 Spielen) → Glättung je Slot mit gemessenem
  Gewicht; heute (τ = 0 in jedem Slot) entscheidet die Spielzahl.
- Gleiche Zauberpaare/Runenseiten getrennt gezählt → zusammengefasst.
- Items 4–6 mit Dubletten (34 Zeilen) und Kern-Wiederholungen (75) → keine mehr.
- 374 von 683 Zeilen ohne Kern → Hauptlane mit Hinweis.
- Support-Item wurde nie angezeigt → im Build-Bereich.
- Übernehmen: ein abgelehnter Zauber-Wechsel kostete die Runenseite → beides unabhängig, einzeln
  gemeldet.

### Spiel und situative Items
- `situational.json` (Version 2) wurde als „unbekannte Version“ verworfen → „Für dieses Spiel“ wäre nie
  erschienen. Alle echten Dateien laden jetzt ohne Fehler, auch ohne `aram.json`.
- Build-Items wurden als „Alternative“ vorgeschlagen (Kai'Sa: Terminus) → ausgeschlossen.
- Stiefel bei Item-Lagen (0,2 %/0,0 %/2,9 % der Käufe) → nur Lagen der Aufstellung, keine Stiefel
  nach dem Kauf.
- Stufe-3-Stiefel und verwandelte Items (Muramana, Seraph) zählten nicht als vorhanden → „Nächster
  Kauf“ schlug Gekauftes erneut vor. Skill-Raster und Karte widersprachen sich. Schadens-Hinweis ab
  60 % statt 65 % (1.5).

### Client, Fenster, Update
- Ohne gefundenes League nie wieder gesucht → alle 10 s; nach Client-Ende alte Phase im Snapshot.
- 1.5-Fensterplatz wurde nicht übernommen (README/CHANGELOG versprachen es) → beim ersten Start
  übernommen; Platz wird auch beim Schließen in den Infobereich gesichert.
- Erster Datenabruf offline: nächster Versuch nach 30 min → nach 1 min, mit Hinweis.
- Glas-Effekt (Mica) abschaltbar, da auf Windows 11 nie gelaufen.
- Velopack prüft beim Update keine Paket-ID → Beta-Pakete auf eigenem Kanal „beta“; Testquelle
  `DIRKDRAFT_UPDATE_SOURCE`.

### Fehlersuche
- 2.0 schrieb kein Log → `%LOCALAPPDATA%\DraftPilot\data\2.0\dirkdraft.log` (≤ 256 KB, ohne Namen,
  Tokens, Passwörter): Start, League/Client/Socket, Übernehmen, Update, Datenprobleme, Skriptfehler,
  Abstürze.

## Gemessen

Alle Zahlen in `docs/modellpruefung.md`, Messungen 10–12 (Daten vom 07.11.2026, 6.185 Spiele):
Lane-Glättung 800 (Halbierung, Momente, Rangtest), Duell/Duo bestätigt, Streuung der Build-Optionen
je Slot τ = 0, Beliebtheit vs. Winrate ohne einheitliches Muster, Stiefelkäufe je Lage.

## Abnahme-Stand

| Punkt | Stand |
|---|---|
| `npx vitest run` | grün, 518 Tests (23 neu), 7 übersprungen (OP.GG-Fälle) |
| `npm run check` | 0 Fehler, 0 Warnungen |
| `cargo test` | grün, 24 Tests (3 neu) |
| `dotnet test` | grün, 636 Tests |
| Screenshots | 13 Szenen × 3 Breiten × 2 Themen = 78, gesichtet, ohne Darstellungsfehler |
| Release-Build | gebaut (9,5 MB), gestartet: Daten unter CSP geladen (raw.githubusercontent.com, Data Dragon), Client verbunden, eine Instanz, X → Infobereich, Fensterplatz gesichert, 1.5-Platz übernommen |
| `tauri dev` | echtes Fenster gegen den laufenden Client (Leerlauf), Log geprüft |
| Beta-Paket | `release.ps1 -PackId DirkDraft-Beta` für 2.0.0 und 2.0.1: Installer 10,1 MB (1.5: 78 MB), Vollpaket 6,1 MB, Delta 2.0.0 → 2.0.1 0,96 MB, WebView2-Bootstrap drin, Kanal `beta`, kein Upload; je ca. 5 Minuten |
| Update-Test | **offen, braucht dein OK** — sicherer Weg in `docs/redesign/migration.md` |
| Live-Durchlauf (Custom Game) | **offen, braucht dich** |
| Windows 11 / Mica | **offen** — hier nicht testbar (Windows 10); Schalter zum Abschalten eingebaut |
| Sammler | Lauf 42367242351 sammelt noch (Ranked bis ca. 15:45 UTC, dann ARAM, Veröffentlichung mit `aram.json` gegen 16:45 UTC). Die geplanten Läufe (06:23, 12:23 UTC) sind **beide nicht angesprungen**, obwohl der Workflow aktiv, das Repo öffentlich und kein Fork ist. Die App läuft mit und ohne `aram.json` (geprüft gegen die echten Dateien). |

## Offen und Empfehlung

1. **Live-Durchlauf mit dir** (Custom Game, „Freie Wahl“ mit Bots; dazu einmal „Abwechselnde Wahl“
   für die Schrittleiste): Board, Vorschläge, Lane-Menü, Teammate-Klick, Übernehmen (Runen + Zauber,
   Blitz auf F), Ladebildschirm, Spielansicht mit „Für dieses Spiel“, Einblend-Automatik.
2. **Update-Test** an einer Probe-Installation (DirkDraft-Beta, Kanal beta) 2.0.0 → 2.0.1, und der
   Schritt 1.5 → 2.0 unter einer Test-ID — beides installiert nichts an deinem DirkDraft, braucht aber
   dein OK (siehe migration.md).
3. **Sammler-Zeitplan:** Wenn auch der 18:23-Termin ausbleibt, schlage ich vor, dass jeder Lauf am Ende
   den nächsten selbst startet (`workflow_dispatch` über `GITHUB_TOKEN`, Recht `actions: write`) —
   das braucht eine Änderung an `master` und damit dein OK.
4. **Ein Freund mit Windows 11** sollte die Beta einmal starten (Mica).

**Empfehlung:** 2.0 ist nach dieser Prüfung technisch bereit für die Probe, aber noch **nicht** für die
Auslieferung. Ausliefern, sobald Punkt 1 und 2 ohne Befund durch sind; Punkt 4 kann parallel als Beta
laufen. Der Weg dann: `redesign/board` nach `master` mergen, aus dem Haupt-Checkout
`release.ps1 -Version 2.0.0` (Kanal `win`, Delta gegen 1.5 aus `build\Releases`).
