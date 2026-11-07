# Auftrag: DirkDraft 2.0 komplett prüfen, korrigieren und auslieferbar machen

## Ziel

DirkDraft 2.0 (Tauri 2 + Svelte 5 + TypeScript, Rust für Client und Spiel) ist gebaut, aber noch
nicht ausgeliefert. Prüfe das **ganze Tool** gründlich und mach es fehlerfrei:

1. **Alle UI-Bugs und Fehler** finden und beheben — in allen Momenten, Breiten, Themen und im echten
   Fenster.
2. **Logik, Korrektheit und Fehler der Vorschläge** prüfen, verbessern und fehlerfrei machen:
   Champion-Vorschläge im Draft (Picks, Banns, Hover, ARAM-Bank), die **Builds** und die
   **situativen Items** im Spiel.
3. Sicherstellen, dass das Tool **bei meinen Freunden einwandfrei läuft** und dass 2.0 **einfach per
   Update** eingespielt werden kann — auch über eine installierte 1.5.

Überdenke dabei alles. Wenn du eine bessere Lösung siehst, setz sie um — aber begründe sie, und bei
Modelländerungen gilt: **messen statt vermuten** (siehe unten).

## Arbeitsort und Regeln

- Arbeite im Worktree `D:\Claude Code\LoL\.claude\worktrees\redesign-board`, Branch `redesign/board`.
  Dort ist alles committet. Committe in sinnvollen Schritten (deutsche Commit-Nachrichten wie im
  Verlauf, per `git commit -F <datei>` — siehe Werkzeug-Fallen).
- **Nichts ausliefern:** kein Push, kein Merge nach `master`, kein Tag, kein Release, kein Upload ohne
  mein ausdrückliches OK in dieser Session. `master` (Stand `1d933e7`, enthält nur den Sammler) und der
  Haupt-Checkout `D:\Claude Code\LoL` bleiben unangetastet, solange ich nichts anderes sage.
- **Nichts auf meinem Rechner installieren oder deinstallieren** (auch keine Probe-Installation) ohne
  vorherige Rückfrage. Meine installierte DirkDraft 1.5 darf nicht kaputtgehen.
- Keine Passwörter, Tokens oder Schlüssel in Felder tippen oder ausgeben. Der Riot-Schlüssel liegt in
  `D:\Claude Code\LoL\build\riot-key.txt` (nur lesen, nie anzeigen). Den LCU-Token liest man aus der
  Kommandozeile von `LeagueClientUx.exe`; nie ausgeben.
- Für Live-Tests mit dem League-Client frag mich; ich starte Client und Custom Games (Spielen →
  Benutzerdefiniert → „Freie Wahl" = Blind Pick mit Bots; Banns gibt es dort nicht).
- Antworte auf Deutsch, verständlich, ohne Fachjargon-Wüsten.

## Projektkarte

- `app/src/core/` — der Kern (rein, getestet): `model/` (MetaLookup mit Glättung, Recommender,
  LanePredictor, TurnTracker, CompAnalyzer, advisors inkl. ARAM-Bank, Fehlerbalken/Gleichauf,
  .NET-Zahlenformat), `engine.ts` (DraftEngine → DraftFrame, Besitz-Filter `setSelectable`),
  `moment.ts`, `riotBuilds.ts` (Build-Plan, Wilson-Untergrenze, Lane-Rückfall, `aramPlan`),
  `aram.ts`, `situational.ts`, `game.ts` (Live-Client → nächster Kauf/Skill/Gegner), `loadout.ts`
  (Zauber-Plätze, Blitz auf F), `refresh.ts` (Datenabruf alle 30 min, nie in Draft/Spiel).
- `app/src/live/` — `session.ts` (Ereignisse → ViewModel), `bridge.ts` (Tauri-Ereignisse,
  Datenabruf, Übernehmen-Knopf), `settings.svelte.ts`, `update.svelte.ts`.
- `app/src/data/` — `remote.ts` (meta/builds/situational/aram.json vom Branch `data`), `golden.ts`
  (Frame → Ansicht; Name historisch), `builds.ts`, `gameView.ts`, `assets.ts` (Data Dragon),
  `scenes.ts` (Aufzeichnungen für Browser und Screenshots).
- `app/src/ui/` — Svelte-Oberfläche (Shell, TitleBar, Settings, draft/, build/, game/).
- `app/src-tauri/src/` — Rust: `shell.rs` (Fenster, Infobereich, Einblend-Automatik, eine Instanz),
  `client.rs` (Befehle/Ereignisse), `lcu/` (Installation finden, lockfile, TLS gegen Riots v1/SHA-1-
  Zertifizierungsstelle von Hand geprüft, HTTP, WAMP-Socket, Scrubber, Live-Client-Poller, Import),
  `settings.rs` (liest/schreibt 1.5s `%APPDATA%\DraftPilot\settings.json`), `data.rs`
  (Datenkopie `%LOCALAPPDATA%\DraftPilot\data\2.0`), `updater.rs` (Velopack, GitHub Releases).
- `src/` (C#) — 1.5-App (`DraftPilot.App`, nur noch Vergleich), Kern-Referenz, **Sammler**
  (`DraftPilot.Meta/Riot`, `DraftPilot.Tools riot collect|aram|export|export-aram|queues`,
  `priors`). `tests/` = xUnit.
- `.github/workflows/collect-builds.yml` — Sammler auf GitHub (Platin Solo/Duo EUW + normales ARAM),
  veröffentlicht in Branch `data`.
- Doku: `docs/redesign/spec.md` (Verhalten 1.5 → 2.0, feste Regeln), `daten.md`, `migration.md`,
  `docs/modellpruefung.md` (gemessene Modellbefunde), `CHANGELOG.md`, `README.md`.

## Befehle und Umgebung (Windows 10 Home, PowerShell 5.1)

- Node: `$env:Path = "D:\Claude Code\devtools\node;$env:Path"`. Rust/MSVC sind eingerichtet
  (`RUSTUP_HOME`/`CARGO_HOME` unter `D:\Claude Code\devtools\rust`).
- In `app/`: `npx tauri dev` (echtes Fenster), `npm run dev` (Browser mit Szenen, `?scene=…`,
  `?data=own` für eigene Beispieldaten aus `app/public/dev-data`), `npx vitest run`,
  `npm run check` (svelte-check), `npm run shots` (Playwright-Screenshots aller Szenen).
- In `app/src-tauri/`: `cargo test`. Release-Build: `npx tauri build` (~9 min, `target\release\
  DirkDraft.exe`). Repo-Wurzel: `dotnet test`.
- Paketieren: `app/scripts/release.ps1 -Version 2.0.0 -PackId DirkDraft-Beta` baut eine getrennte
  Probe-App (eigener Ordner/eigene Verknüpfung) und lädt nie hoch. Ohne `-PackId` ist es das echte
  Release (nur mit meinem OK).
- Werkzeug-Fallen (bestätigt): PowerShell 5.1 schreibt mit `Set-Content -Encoding utf8` ein BOM;
  deutsche Anführungszeichen „…" brechen doppelt gequotete PS-Strings — Datei-Änderungen mit dem
  Edit-Werkzeug; `git push` hängt am Credential-Manager (siehe Memory, Helper-Reset mit gh);
  `gh` liegt unter `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GitHub.cli_…\bin\gh.exe`; Prozesse aus
  deiner Shell sehen ein eigenes AppData-Overlay (Settings/Daten der echten Nutzer-Welt sind andere
  Dateien — Memory „AppData: zwei Welten").
- Bildschirmfotos des echten Fensters: Win32 `GetWindowRect`/`CopyFromScreen` (das Fenster kann auf
  einem zweiten Monitor mit negativen Koordinaten liegen).

## Feste Regeln und meine Entscheidungen (nicht ändern, nicht erneut vorschlagen)

- Keine Namen anderer Spieler in der Anzeige; keine Cooldown-/Ult-Timer; kein Speicherlesen, keine
  Injection; **kein** automatisches Hovern/Picken/Bannen/Annehmen.
- Einziger Schreibweg in den Client: **Runen und Beschwörerzauber, nur auf Klick**. Eigene Seiten
  (Präfix DirkRunen/DraftPilot) werden ersetzt, fremde nur nach Rückfrage mit Namen.
  **Blitz immer auf F** (Einstellung D/F, Standard F), auch wenn er vorher auf D lag.
- Daten nur aus eigener Zählung (kein OP.GG mehr). Abruf automatisch alle 30 min, **nie während
  Draft oder Spiel**, verpasste Termine im Leerlauf nachholen; einzige Ausnahme: allererster Start
  ohne jede Kopie lädt auch im Draft. Kein Knopf.
- Mayhem: Riot gibt Mayhem-Spiele nicht heraus (403, gemessen) → **normales ARAM** als
  Stellvertreter, so beschriftet; keine Augment-Liste.
- Situative Items: nur gemessen (Kauf-Häufigkeit in der Lage als Signal, Winrate je Spielstand nur
  als Sicherung), höchstens drei, klar als **Alternative** neben dem Build, nie Ersatz, folgen live den
  Gegner-Items.
- Vorschläge nur für Champions, die man besitzt oder die gratis sind (abschaltbar).
- **Abgelehnt, nicht wieder einbauen:** Nachspiel/Ergebnis/Verlauf, „Mein Pool", Alters-Warnung oder
  harte Grenze für alte Daten.
- Design: Apple-Stil, responsiv (breit ≥ 1.100, mittel 720–1.099, schmal ≥ 340), Windows 10 deckend
  #1c1c1e, Windows 11 Mica.

## Aufgaben

### A. Bestandsaufnahme UI und Fehler
- Alle Szenen in drei Breiten (z. B. 1280, 900, 360) × hell/dunkel per Screenshot durchsehen
  (`npm run shots`, eigene Daten mit `?data=own`), dazu das echte Fenster (`tauri dev`) und den
  **Release-Build** (`tauri build`; im Release gilt eine strenge CSP — in Dev ist sie aus! Prüfen:
  Daten-Download von raw.githubusercontent.com, Data-Dragon-Bilder, Schrift, IPC, Ereignisse).
- Bekannte Auffälligkeiten von heute: Titelleiste bei mittlerer Breite (Daten-Pill/Umschalter/
  Update-Pill); Schrittleiste nur für Turnier-Draft/Blind gebaut (Draft-Modus „Abwechselnde Wahl"
  prüfen); eigene Lane im Blind Pick (zeigte Botlane, obwohl ein Teammate schon Botlane hatte);
  Szenen-Leiste darf im echten Fenster nie erscheinen; Einstellungsblatt, Infobereich-Hinweis,
  leere Zustände („Das Spiel lädt …"), Fehlermeldungen des Übernehmen-Knopfs.
- Konsole und Rust-Log auf Fehler prüfen; jede Ausnahme hat eine Ursache.

### B. Draft-Vorschläge: Logik und Korrektheit
- Recommender, Glättung, Fehlerbalken/Gleichauf, Banns (Gate), Lane-Vorhersage, TurnTracker (Klick
  auf Teammate), Besitz-Filter, Hover-Rang, Draft-Chance, Comp-Regeln, ARAM-Bank mit eigenen Zahlen.
- 2.0 ist auf den 1.5-Zahlen bis 1e-9 gleich (Golden-Tests, `app/tests/golden`). Verbesserungen
  sind erlaubt, aber **gemessen**: auf den eigenen Daten (Branch `data`, lokal
  `build/riot-state-platinum.json.gz` im Worktree, `Tools -- priors --snapshot`) mit Halbierungstest/
  Backtest belegen, in `docs/modellpruefung.md` festhalten, Golden-Erwartungen nur bewusst und mit
  Begründung anpassen. Die Glättungsgewichte (Lane 300, Duell 1.000, Duo 750) sollten auf den
  inzwischen größeren eigenen Daten nachgemessen werden.
- Randfälle: Flex (nutzt Solo/Duo-Zahlen), Custom/Blind ohne Positionen, Draft-Modus, Turnier, ARAM,
  Mayhem, Arena/rotierend (keine Lanes), Neustart der App mitten im Draft, Champion ohne Daten,
  neuer Champion, Patchwechsel (alter Bestand bis 2.000 Spiele).

### C. Builds
- `riotPlan`/`aramPlan`: Wilson-Auswahl je Slot, Alternativen ab 20 Spielen, Lane-Rückfall mit
  Hinweis, Runen (11 IDs → Seite 4+2+3 im richtigen Format für `/lol-perks/v1/pages`), Shards,
  Zauber, Start, Schuhe, Kern, spätere Items, Skill-Reihenfolge (auf 18 erweitert), Support-Item.
  Gegen echte `builds.json` prüfen, ob die Vorschläge plausibel sind (Stichproben bekannter Champions).
- Übernehmen-Knopf: Seiten-Logik (eigene ersetzen, fremde nur nach Rückfrage, Abbruch ohne Löschen),
  Zauber-Plätze, Verhalten außerhalb des Champ Select, Fehlermeldungen.

### D. Situative Items im Spiel
- Methode (`situational.ts`, C# `RiotSituational.cs`) auf echter `situational.json` prüfen: Lagen,
  Schwellen, Glättung, Sicherung je Spielstand, Ausschluss von Plan-/Inventar-Items, höchstens drei,
  live mit den Gegner-Items, Texte. Gerne optimieren — gemessen.
- Spielansicht insgesamt: nächster Kauf gegen Gold (Komponenten, Rezept-Kosten), Build-Pfad,
  Skillpunkt, Gegner-Schadensart, ARAM/Mayhem im Spiel (`gameMode` ARAM/KIWI).

### E. Robustheit mit dem Client
- Client startet nach/vor der App, Client-Neustart, Socket-Abbruch (darf den Draft nie schließen),
  App startet mitten im Draft oder Spiel, Reconnect, League in anderem Ordner (`lockfilePath`),
  kein League installiert, Live-Client-API im Ladebildschirm, mehrere Monitore/DPI, Ein-Instanz-
  Verhalten, Einblenden ohne Fokusraub, vom Spiel minimiertes Fenster.

### F. Bei meinen Freunden und das Update
- Freunde: Windows 10 **und 11** (Mica-Pfad mit transparentem Fenster ist noch nie gelaufen!),
  evtl. ohne WebView2, unsignierter Installer (SmartScreen), anderer Installationsort von League,
  erste Start ohne Daten.
- **Update 1.5 → 2.0:** gleiche Paket-ID `DirkDraft`, gleiche `DirkDraft.exe`, gleiche Release-Quelle;
  Delta-Pakete aus `D:\Claude Code\LoL\build\Releases` (dort liegen die 1.x-Pakete). Prüfen, dass
  eine installierte 1.5 „Version 2.0.0 ist da" zeigt und sauber aktualisiert, Einstellungen bleiben,
  1.5-Reste (alte Datenordner, Autostart, Verknüpfungen) nicht stören. Einen sicheren Testweg finden,
  der meine echte Installation nicht anfasst (z. B. Probe unter eigener Paket-ID, lokale Quelle) —
  vorher mit mir abstimmen.
- **Bekannte Lücke:** Der Fensterplatz aus 1.5 (`windowLeft/Top/Width/Height` in settings.json) wird
  von 2.0 nicht übernommen (2.0 nutzt das window-state-Plugin). README, CHANGELOG und migration.md
  versprechen aber, dass er bleibt — entweder beim ersten 2.0-Start übernehmen oder die Texte
  korrigieren.
- Die Update-Prüfung von 2.0 selbst (`updater.rs`, Pill in der Titelleiste) ist noch nie gelaufen —
  mit einem Probe-Paket testen (2.0.0 → 2.0.1 unter der Beta-ID).
- `release.ps1` einmal mit `-PackId DirkDraft-Beta` durchlaufen lassen (WebView2-Bootstrap
  `--framework webview2` prüfen), Installer-Größe und Ablauf festhalten.

### G. Sammler kurz prüfen
- Lauf 42367242351 (gestartet 07.11. 11:13 UTC) sollte zusätzlich `aram.json` in Branch `data`
  veröffentlichen. Die geplanten Läufe (cron alle 6 h) sind am 07.11. vormittags nicht angesprungen —
  klären, ob sie inzwischen laufen. Die App muss mit und ohne `aram.json` sauber funktionieren.

## Abnahme

- `npx vitest run`, `npm run check`, `cargo test`, `dotnet test`: alles grün; neue Logik hat Tests.
- Screenshots aller Szenen in drei Breiten × zwei Themen ohne Darstellungsfehler; Release-Build
  gestartet und geprüft.
- Live-Durchlauf mit mir (Custom Game): Board, Vorschläge, Übernehmen (Runen + Zauber, Blitz auf F),
  Ladebildschirm, Spielansicht mit situativen Items, Einblend-Automatik, Einstellungen.
- Beta-Paket gebaut; Update-Weg (1.5 → 2.0 und 2.0 → 2.0.x) mit meinem OK getestet oder ein
  klarer, sicherer Plan dafür.
- Doku (spec, daten, migration, README, CHANGELOG) stimmt mit dem Verhalten überein.
- Zum Schluss ein Bericht: was gefunden und behoben wurde, was gemessen wurde, was offen bleibt, und
  eine Empfehlung, ob 2.0 ausgeliefert werden kann. **Ausgeliefert wird erst nach meinem OK.**
