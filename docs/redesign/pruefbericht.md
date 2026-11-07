# DirkDraft 2.0 — Prüfbericht (07.11.2026)

Auftrag: `docs/redesign/auftrag-pruefung.md`. Gearbeitet im Worktree `redesign-board`, Branch
`redesign/board`. Nichts ausgeliefert oder gemergt, kein Tag, kein Release. Mit deinem OK: zwei
Sammler-Commits auf `master` gepusht (`57faa83`, `0c31629`) und der Update-Test an
Probe-Installationen (eigene App-IDs, in der Claude-Shell-Umgebung, danach wieder deinstalliert); deine
installierte 1.5 ist unberührt.

## Kurzfassung

2.0 war nicht auslieferbar — vor allem, weil vier Quelldateien und alle Testaufzeichnungen nie
eingecheckt waren (ein frischer Checkout hätte nicht gebaut) und weil die App die echte
`situational.json` verwarf. Beides ist behoben, dazu eine Reihe von Fehlern in Oberfläche, Builds,
situativen Items und Client-Anbindung. Die Build-Auswahl ist auf eine gemessene Regel umgestellt.
Der Update-Test (2.0 → 2.0 und 1.5 → 2.0) ist bestanden; sein einziger Befund ist behoben. Offen bleiben
Reste des Live-Durchlaufs und Windows 11 (Mica) bei einem Freund.

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
- Update-Test: ein 1.5-Fenster, das verschoben, aber nie in der Größe geändert wurde (1.5 speichert
  die Größe dann als „NaN“), verlor seinen Platz → Position wird auch ohne Größe übernommen, mit
  2.0s eigener Größe, auf dem Bildschirm gehalten.
- Velopack schrieb jede Update-Prüfung ins Log, mit dem Windows-Profilpfad → von Velopack nur noch
  Warnungen und Fehler; die eigenen Update-Zeilen sagen, was geschah.
- Erster Datenabruf offline: nächster Versuch nach 30 min → nach 1 min, mit Hinweis.
- Glas-Effekt (Mica) abschaltbar, da auf Windows 11 nie gelaufen.
- Velopack prüft beim Update keine Paket-ID → Beta-Pakete auf eigenem Kanal „beta“; Testquelle
  `DIRKDRAFT_UPDATE_SOURCE`.

### Fehlersuche
- 2.0 schrieb kein Log → `%LOCALAPPDATA%\DraftPilot\data\2.0\dirkdraft.log` (≤ 256 KB, ohne Namen,
  Tokens, Passwörter): Start, League/Client/Socket, Übernehmen, Update, Datenprobleme, Skriptfehler,
  Abstürze — dazu je Spiel eine Zeile, welche Lagen erkannt und welche situativen Items vorgeschlagen
  wurden, und mit welchem Datenstand.
- Diagnose-Aufzeichnung (Einstellungen → Diagnose): die letzten 10 Drafts als abspielbare
  Sitzungsaufzeichnung und die letzten 3 Spiele als ein Live-Client-Schnappschuss je Minute, beides
  ohne Namen. Normalerweise aus; an, solange der Schalter nie benutzt wurde und neben settings.json eine
  leere `diagnostics.on` liegt (so nur auf dem Entwickler-PC, auch nach dem Update aus 1.5, das
  unbekannte Schlüssel beim Speichern verwirft). „Diagnose speichern“ legt Log und Aufzeichnungen als
  Ordner auf den Desktop.

## Gemessen

Alle Zahlen in `docs/modellpruefung.md`, Messungen 10–12 (Daten vom 07.11.2026, 6.185 Spiele):
Lane-Glättung 800 (Halbierung, Momente, Rangtest), Duell/Duo bestätigt, Streuung der Build-Optionen
je Slot τ = 0, Beliebtheit vs. Winrate ohne einheitliches Muster, Stiefelkäufe je Lage.

## Abnahme-Stand

| Punkt | Stand |
|---|---|
| `npx vitest run` | grün, 529 Tests (34 neu, davon einer gegen den echten Ranked-Draft), 7 übersprungen (OP.GG-Fälle) |
| `npm run check` | 0 Fehler, 0 Warnungen |
| `cargo test` | grün, 27 Tests (6 neu) |
| `dotnet test` | grün, 636 Tests |
| Screenshots | 13 Szenen × 3 Breiten × 2 Themen = 78, gesichtet, ohne Darstellungsfehler |
| Release-Build | gebaut (9,5 MB), gestartet: Daten unter CSP geladen (raw.githubusercontent.com, Data Dragon), Client verbunden, eine Instanz, X → Infobereich, Fensterplatz gesichert, 1.5-Platz übernommen |
| `tauri dev` | echtes Fenster gegen den laufenden Client (Leerlauf), Log geprüft |
| Beta-Paket | `release.ps1 -PackId DirkDraft-Beta` für 2.0.0, 2.0.1 und 2.0.2: Installer 10,1 MB (1.5: 78 MB), Vollpaket 6,1 MB, Delta 2.0.0 → 2.0.1 0,96 MB, WebView2-Bootstrap drin, Kanal `beta`, kein Upload; je ca. 5 Minuten |
| Update-Test | **bestanden** (07.11., mit deinem OK, Probe-IDs in der Claude-Shell-Umgebung, danach deinstalliert). **A** 2.0 → 2.0: Beta 2.0.1 installiert, mit `DIRKDRAFT_UPDATE_SOURCE` gestartet → Pill „Update 2.0.2“ → Klick → Neustart als 2.0.2, Prüfung danach „neu keine“. **B** 1.5 → 2.0: die echten 1.5-Dateien als `DirkDraft-UpdTest` 1.5.0 installiert und gestartet, dann `Update.exe apply` mit dem 2.0.0-Paket derselben ID (was „Jetzt aktualisieren“ in 1.5 auslöst) → 1.5 beendet, `current` enthält nur noch 2.0, 2.0 startet, Einstellungen erhalten, Diagnose dank `diagnostics.on` an. Delta 1.5 → 2.0: 4,0 MB. Befund: Fensterplatz ohne 1.5-Größe (behoben, am Release-Build nachgeprüft: Platz 120/60 übernommen). |
| Live-Durchlauf | **zwei echte Solo/Duo-Spiele** (07.11., Support; Release-Build lief mit, Fenster nur beobachtet, „Übernehmen“ hat der Spieler selbst geklickt): Planung → Banns → Picks → Finalisierung → Ladebildschirm → Spiel durchgängig richtig; „Runen und Zauber übernommen.“ beide Male; Teammate-Klick und „weitere“ funktionierten; Spielansicht mit nächstem Kauf, Skillpunkt und Gegner-Items stimmte. Behoben aus dem Test: Zeilengrund „ist Ranged“, „Teammate 3 bannt“ bei gleichzeitigen Banns, „Warten“ im Ladebildschirm, geratene Lanes auf leeren Gegnerplätzen, leere Hinweis-Kachel. Der Draft ist als geschwärzter Testfall `live-ranked.jsonl` im Repo. Nicht gesehen: Custom „Abwechselnde Wahl“, Blind Pick, ARAM, eine Lage mit situativem Vorschlag. |
| Windows 11 / Mica | **offen** — hier nicht testbar (Windows 10); Schalter zum Abschalten eingebaut |
| Sammler | Lauf 42367242351 sammelt noch (Veröffentlichung mit `aram.json` gegen 16:45 UTC). GitHubs Zeitplan ist unzuverlässig: 06:23 UTC fiel aus, 12:23 kam 53 Minuten zu spät. Mit deinem OK auf `master`: jeder Lauf startet seinen Nachfolger (`57faa83`), und situative Käufe werden auch für die ersten drei Items gezählt (`0c31629`, Wunsch nach Mikaels Segen); der nächste Lauf (42384099091) sammelt schon damit. Die App läuft mit und ohne `aram.json` und mit und ohne Kern-Zeilen. |

## Offen und Empfehlung

1. **Live-Durchlauf:** Ranked ist in zwei Spielen durch. Offen: ein kurzer Blick auf Blind Pick oder
   ARAM, das Lane-Menü, Blitz auf F bei einem Champion mit Blitz auf D vorher, und ein Spiel mit
   situativem Vorschlag in „Für dieses Spiel“.
2. ~~Update-Test~~ — bestanden, siehe Abnahme-Stand.
3. **Sammler:** Kette und Kern-Zählung sind auf `master`. Prüfen, ob der Lauf nach 42384099091 von selbst
   startet, und ob nach einigen Läufen Kern-Vorschläge (z. B. Mikaels Segen gegen viel Kontrolle) auftauchen.
4. **Ein Freund mit Windows 11** sollte die Beta einmal starten (Mica).

**Empfehlung:** 2.0 ist nach dieser Prüfung technisch bereit für die Probe und hat zwei echte Ranked-Spiele
ohne Ausfall begleitet; der Update-Test ist bestanden. Freigegeben ist es erst mit deinem „release“. Die Reste
aus Punkt 1 (Blind Pick/ARAM, Lane-Menü, ein situativer Vorschlag) sind durch Tests und Szenen abgedeckt,
nur nicht live gesehen — nach meiner Einschätzung kein Hindernis; Punkt 4 kann nach dem Release ein Freund
prüfen, der Glas-Schalter fängt einen Fehler dort ab. Der Weg dann: `redesign/board` nach `master` mergen, aus dem Haupt-Checkout
`release.ps1 -Version 2.0.0` (Kanal `win`, Delta gegen 1.5 aus `build\Releases`).
