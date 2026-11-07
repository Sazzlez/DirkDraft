# DirkDraft 2.0 „Board“ — Bestandsaufnahme und Spezifikation (M0)

Stand 06.11.2026, Branch `redesign/board`. Grundlage: README, CHANGELOG, `docs/modellpruefung.md`,
`docs/opgg-schnittstelle.md`, `docs/augments.md`, `docs/optimierungspotenzial.md`, die
Memory-Einträge des Projekts und eine Code-Inventur von `src/` (nicht vom README abgeschrieben).
Was 1.5 kann, steht hier vollständig; was 2.0 daraus macht, steht jeweils als **2.0:** dahinter.

Jede Konstante übernimmt 2.0 mit ihrer Begründung aus dem C#-Code; die Golden-Tests
(`app/tests/golden/`, siehe dortiges README) prüfen, dass das Modell dasselbe rechnet.

---

## 0. Vorgaben, die sich seit dem Auftrag geändert haben

Der Nutzer hat nach dem Auftrag entschieden (06.11.2026):

| Auftrag | Neue Vorgabe | Folge für 2.0 |
|---|---|---|
| M4: OP.GG-Abrufe, Snapshot-Update, Draft-Abrufe | **Keine OP.GG-Daten mehr.** Alles aus dem eigenen Riot-Sammler („Nimm direkt die neue Schnittstelle statt OP.GG“) | Die App lädt eine fertige Datei vom Branch `data` (GitHub). Im Draft wird nichts mehr geholt. |
| „Draft-Abrufe automatisch … höchstens 16 pro Draft“ | entfällt mangels Abrufen | Nur noch: Datei laden, Data-Dragon-Bilder, Update-Prüfung. |
| „Matchup-Build nach meistgespielt; ohne Gegner nach Winrate“ | **Unser Build, je Slot die beste Winrate** (Wilson-Untergrenze), auch bei bekanntem Gegner | Matchup-genaue Builds aus eigenen Daten, sobald je Paarung genug Spiele vorliegen; sonst Lane-Build mit Hinweis (wie 1.5 seit `6f85945`). |
| „Solo/Duo, Flex und ARAM getrennte Bestände“ | Sammler: **Solo/Duo in Platin, EUW**; Flex nutzt diese Zahlen; **ARAM Mayhem** eigene Zählung | Flex trägt einen Hinweis „Zahlen aus Solo/Duo“. Mayhem braucht einen Startpunkt (offene Frage 3). |
| Stärke-Kurve aus OP.GG `game_lengths` | entfällt als Quelle | Aus eigenen Spielen (`gameDuration` je Champion/Lane, mit Spielzahl je Abschnitt) — die Messbedingung „erst prüfen, ob stabil“ bleibt. |

Unverändert gilt das gemessene **Modell** (Glättung, Gleichauf, Fehlerbalken, Comp-Regeln). Mit
eigenen, vollständigen Daten fallen zwei OP.GG-Korrekturen von selbst auf neutral: der
Auswahl-Versatz der Counter-Listen (`MatchupBaseline` → 0, es gibt keine Auswahl mehr) und die
Duell-Linie fürs Standard-Bracket (`CompleteDuelSlope` → 1, alles kommt aus einem Bracket). Die
Glättungsgewichte (1.000 Duelle, 750 Duos, 300 Lane) werden auf den neuen Daten nachgemessen
(`Tools -- priors`-Verfahren), bevor sie gelten.

---

## 1. Fenster und Rahmen

**1.5:** WPF, randlos, 1120 × 900 Startgröße, Minimum 1000 × 640, ziehbar und gespeichert
(`windowLeft/Top/Width/Height`, NaN = nicht platziert), Monitorwechsel wird abgefangen
(`EnsureOnScreen`), Maximieren unterbunden. Titelleiste: Logo, Name, Verbindungspunkt mit
Status-Text, Update-Pill, Pin (immer oben, `keepOnTop`, Standard aus), Minimieren in die
Taskleiste (`MinimisedByHand`), X in den Infobereich (einmaliger Hinweis-Ballon, `trayHintShown`).
Infobereich: Öffnen · Daten aktualisieren · Beenden, Doppelklick öffnet, Tooltip ≤ 127 Zeichen.
Einzelinstanz über Mutex `Local\DirkDraft.SingleInstance` und Event `Local\DirkDraft.ShowWindow`
(zweiter Start holt das erste Fenster nach vorn).

Automatik: bei Champ Select einblenden (`autoShowOnChampSelect`, ohne Fokusraub), nach dem Draft
optional ausblenden (`autoHideAfterChampSelect`), bei Spielstart einblenden ohne Aktivieren
(`autoShowOnGameStart`); ein vom Spiel minimiertes Fenster wird 60 s lang wiederhergestellt, ein
von Hand minimiertes nicht. Kein Autostart.

**2.0:** Tauri 2, eigene Titelleiste nach Auftrag (Logo · Name · Status | Draft · Build · Spiel |
Datenstand-Pill · Pin · Minimieren · Schließen), Mica/Acrylic/deckend #1c1c1e, Mindestgröße
**340 × 560**, responsiv (Abschnitt 7). Alle Automatik-Regeln oben bleiben.

## 2. Momente (was das Fenster wann zeigt)

| Moment | 1.5 | 2.0 (Board) |
|---|---|---|
| Lobby / kein Client | Startkarte: Client-Status, Datenstand, Update-Zeile, Version | Leerer Zustand, der sagt, was kommt; Datenstand in der Pill |
| Planung & Hover | Liste + Zeile „DEIN HOVER“ (Winrate, Platz k von n, Urteil), offene Counter je Zeile solange Lane-Gegner unbekannt | Hauptkachel: Hover-Zeile fest, offene Counter |
| Bannen | Ban-Liste mit Punkten (stark > 3,5, sinnvoll > 2,5), Kriterien aufklappbar | Drei Banns mit Grund und Punkten, „Weitere Banns“, Nebenkachel „Dem Gegner fehlt noch“ |
| Dein Pick | 8 Zeilen (Einstellung), Gleichauf-Gruppe mit Akzentkante, Urteil in Worten, Kriterien mit Balken | Drei vorne, Gruppe „Vorne gleichauf · N“, Rest auf Abruf, Balken der Beiträge, Segment „Alle · Mein Pool“, Nebenkacheln Draft-Chance (Halbkreis) und Comp-Vergleich |
| Teammate am Zug | dieselbe Liste für seinen Slot, Klick auf Slot wechselt das Ziel bis zum nächsten Zug | gleich |
| Gelockt / Finalisierung / Laden | Matchup-Panel (Winrate, Spielzahl), Build-Karte, Comp-Tabelle | Hauptkachel „Dein Matchup“ + Runen + Spells + Start + Skills + Kern; Draft-Urteil in drei Sätzen |
| ARAM / Mayhem | „Behalten oder tauschen?“ — eigener Champion + Bank nach ARAM-Winrate, Rundung im Fehlerbalken, Rerolls in der Kopfzeile; ARAM-Build; Mayhem-Stern | gleich, Daten aus eigener Zählung |
| Arena, rotierende Modi | keine Lanes, kein Build | gleich |
| Im Spiel | Build groß (Runen, Items, Skills 1–18 mit abgeleiteten 16–18, Augmente bei Mayhem), Lane-Übersicht mit Balken (±10 Punkte Vollausschlag), „Draft insgesamt“ | Bereich Spiel: nächster Kauf gegen Gold, Build-Pfad mit Häkchen, Skill-Leiste mit nächstem Punkt, Gegner-Items mit Schadensart, Hinweise (aus Live-Client-API Port 2999) |
| Nachspiel | — (zurück zur Startkarte) | neu: aus Top 3 gepickt? Ausgang? Build-Abweichung? Verlauf, lokal |

Moment-Logik 1.5 (`UpdateBuildSection`, `OnGameflowPhase`): GameStart/InProgress/Reconnect = Spiel
läuft; Spielende verwirft Build und Lanes; Client weg im Spiel = Phase None. 2.0: eine explizite
Zustandsmaschine (Lobby, Planung, Bannen, Pick, Teammate, Gelockt, Laden, ARAM-Bank, Spiel,
Nachspiel) im TS-Kern; der Moment schaltet den Bereich, Handwahl hält bis zum nächsten Moment.

Texte und Schwellen, die 2.0 übernimmt: Urteile „bester Pick der Liste / gleichauf mit Platz 1 /
knapp dahinter / deutlich dahinter / Datenlage zu dünn“; Kriterien-Urteile ≥ 2 „stark dafür“,
≥ 0,6 „dafür“, > −0,6 „neutral“, > −2 „dagegen“, sonst „stark dagegen“; Ban-Tor ≥ 0,6 „wird oft
genommen“, ≥ 0,25 „gelegentlich“, sonst „selten“; Build-Alternativen ab 20 Spielen, Prozentzahl erst
ab 50 Spielen („dünne Datenlage“); Kauf-Hinweise AD-Anteil ≥ 0,65 → Armor, AP ≥ 0,65 → MR,
CC ≥ 6 → Tenacity, nur ab drei aufgedeckten Gegnern, nie umsortierend.

## 3. Client-Anbindung (wird Rust-I/O + TS-Kern)

- **Installation finden:** `product_settings.yaml` (`product_install_full_path`), dann
  `RiotClientInstalls.json`, dann Standardpfade auf C:/D:/E:. `lockfilePath` überschreibt.
- **Lockfile beobachten:** Dateisystem-Ereignisse, 1,5 s Schonfrist in 125-ms-Schritten,
  Format `LeagueClient:pid:port:password:https`.
- **Zertifikat:** eingebettetes `riotgames.pem` als einzige Wurzel, keine Sperrprüfung,
  Host-Name-Abweichung toleriert, CA selbst präsentiert = gültig. Für HTTP und WebSocket.
- **HTTP:** `https://127.0.0.1:port`, Basic `riot:<password>`, kein Proxy, 5 s; 404 = „beantwortet,
  keine Daten“, Fehler = „nicht beantwortet“ (der Unterschied verhindert, dass ein Aussetzer den
  Draft schließt).
- **WebSocket:** WAMP, Abos `OnJsonApiEvent_lol-champ-select_v1_session` und
  `…_lol-gameflow_v1_gameflow-phase`; Wiederverbindung 500 ms verdoppelnd bis 5 s; jeder Neuaufbau
  sät per HTTP (erst Phase, dann Session), schließt das Panel nur bei echtem 404.
- **Gelesen:** Session, Gameflow-Phase, `pickable-champion-ids` (Besitzfilter, bis nicht leer
  wiederholt), `lol-patch/v1/game-version` (Fallback `system/v1/builds`), Runenseiten und Inventar.
  **2.0 neu:** Mastery und eigene Spielhistorie für „Mein Pool“ (nur Filter), Live-Client-API.
- **Geschrieben (nur auf Klick, ein Knopf):** Runen-Import — `POST/DELETE /lol-perks/v1/pages`,
  Name „DirkRunen · {Überschrift}“ ≤ 30 Zeichen, eigene Seiten (Präfix DirkRunen oder alt
  DraftPilot) werden ersetzt, eine fremde nur nach Nachfrage mit Namen (älteste löschbare), Abbruch
  ohne Löschen, wenn mehr als ein Platz fehlt; 4+2+3 Perks.
  **2.0 neu (Nutzerwunsch 07.11.2026):** derselbe Klick setzt die Beschwörerzauber —
  `PATCH /lol-champ-select/v1/session/my-selection` (`spell1Id`, `spell2Id`). **Flash immer auf F**
  (Nutzervorgabe 07.11.2026; umstellbar auf D erst mit der Einstellungsseite), auch wenn es vorher
  auf D lag (`core/loadout.ts`); ein anderer schon gewählter Zauber bleibt auf seiner Taste, erst dann
  gilt die Reihenfolge der Daten; schon so gesetzt = nichts schreiben. Knopf „Runen und Zauber
  übernehmen“.
- **Queues:** 420 Solo/Duo, 440 Flex, 400/430/490/480 Normal/Swiftplay, 700/720 Clash, 830–890
  Bots, 450 ARAM, 2400/2410/2450/3270/3280 Mayhem, 1700/1710/1750 Arena, 900/1900/1020/1300/1400
  rotierend; Custom-Kennung nur bei unbekannter ID. Keine Lanes in ARAM, Mayhem, Arena, rotierend.
- **Datenschutz:** `SessionScrubber` entfernt `chatDetails`, `gameId` und je Spieler gameName,
  tagLine, puuid, obfuscatedPuuid, summonerId, obfuscatedSummonerId, playerAlias, internalName,
  nameVisibilityType. `LiveGameScrubber` ersetzt summonerName, riotId(GameName/TagLine),
  KillerName, VictimName, Assisters, Acer, Recipient durch „Spieler N“ (auch nackte Strings).
- **Aufzeichnen/Abspielen:** JSONL `{offsetMs, session}` oder `{offsetMs, phase}`, gescrubbt.
  2.0: Replay auch im Browser (ersetzt `--demo`/`--screenshot`).

## 4. Modell (TS-Kern, Golden-geprüft)

Alle Terme Log-Odds, summiert, per Sigmoid zurück. `Logit` klemmt auf [0,01; 0,99], NaN → 0;
Punkte = Log-Odds × 25.

- **Lane-Vorhersage:** alle Zuordnungen (≤ 120), Gewicht = max(Rollenanteil, 0,01) × Sitz-Prior;
  manuell/zugewiesen = harte 1; verdeckt = nur Sitz-Prior (flach 0,2, bewusst); widersprüchliche
  Handwahl → ohne Handwahl rechnen. Unsicher unter **0,6**.
- **Pick-Terme:** Lane-Stärke (geglättet, Drift × Patch-Rückstand **3,7e-4** im Fehler);
  Matchup Σ P(Gegner auf Lane) × (Logit(Duell) − Erwartung), P < 0,05 übersprungen, zentriert
  (Steigung 1,23 hätte Stärke 2,2-fach gezählt); übrige Gegner **0,35** × gewichtetes Mittel;
  Synergie **0,6** × Mittel(Logit(Duo) − Logit(Erwartung)); Comp **0,16** × Fit (±1). Ohne Lane:
  Hauptlane nach Spielen. Offene Counter (Kante ≥ 0,08) nur als Chip.
- **Banns:** Ban-Lane nach Rollenanteil × Platz; Tor = clamp(Pick·8 + Ban·4, 0, 1) × Platz;
  Stärke, Lane-Gefahr **0,5**, Team-Gefahr **0,5**; Wert = Tor × (Sigmoid(Σ) − 0,5) × 100.
- **Comp:** Schwellen DamageImbalance 0,2, Frontline Defense ≥ 6 oder Tank, Engage/Peel/CC ≥ 2,
  Ranged ≥ 300; Befunde und Fit-Boni/-Abzüge wie in `CompAnalyzer` (alle in den Golden-Daten).
- **Draft-Chance:** Sigmoid(Mittel eigene Lane-Logits − Mittel Gegner + Mittel zentrierte Duelle);
  „—“ solange eine Seite verdeckt.
- **Fehler:** `ErrorBudget` (Varianzsumme), Gleichauf unter **1 σ**, deutlich ab **3 σ**,
  Nachkommastelle 0 ab 0,5 Punkten Median-Fehler.
- **Glättung:** Lane 300, Duell 1.000, Duo 750 (Duo zur Paar-Linie, Steigung ≈ 0,4, ab 100 Paaren);
  Spiegelkanten exaktes Komplement. **2.0:** auf eigenen Daten nachmessen (Abschnitt 0).
- **ARAM-Bank:** Prior 400 zu 0,5, Rundungsvarianz 0,01²/12, leer ohne Zahl für den eigenen.
- **Augmente:** Grobkörnigkeits-Filter 170 × k/n mit n ≤ 40, Median-Beliebtheit — **2.0:** mit
  eigenen Mayhem-Daten echte Winrate mit Spielzahl statt OP.GG-Wert; Regeln für dünne Zeilen wie
  überall (unter 50 Spielen keine Prozentzahl).
- **Build-Auswahl:** Wilson-Untergrenze z = 1,96; Skill-Plan auf 18 erweitert, abgeleitete Stufen
  markiert.

## 5. Daten

**1.5:** OP.GG (MCP, `mcp-api.op.gg`), großes Update ≈ 320 Abrufe auf Knopfdruck, im Draft
gesperrt; Draft-Abrufe (Counter, vollständige Duell-Listen, Build, Augmente, ARAM) mit Budget 24,
10 s, 3 Fehlversuche, Backoff 3–30 s; Data Dragon für Patch, Champion-Daten, Bilder, Namen;
Build-Cache je Patch/Champion/Lane/Gegner/Variante, 14 Tage.

**2.0:** eine Datei vom Riot-Sammler (Branch `data`, `builds.json` heute; wird erweitert um
Lane-Zahlen, Banns, Duelle, Duos, Schadensart, Mayhem) — Format so geschnitten, dass der TS-Kern
dieselben Strukturen wie `MetaSnapshot` bekommt. Laden beim Start und auf Knopfdruck
(„Daten aktualisieren“ = Datei holen, Sekunden statt Minuten). Data Dragon bleibt für Bilder und
Namen, Update-Prüfung bei GitHub bleibt. Vorhandene `snapshot.json` (OP.GG) wird gelesen, bis die
eigene Datei da ist, und als „OP.GG-Bestand von 1.5“ beschriftet.

Pfade bleiben: `%LOCALAPPDATA%\DraftPilot\data` (Snapshot, Bilder, Cache, Logs) und
`%APPDATA%\DraftPilot\settings.json` (Schlüssel siehe 1.5: Fenster, showUnowned, autoShow…,
dataLanguage, autoHide…, keepOnTop, trayHintShown, recommendationCount, softwareRendering,
lockfilePath, gameMode, tier). Logs: crash.log, startup.log (≤ 400 Zeilen), gekappt bei 256 KB.

## 6. Feste Regeln (unverändert)

Keine fremden Spielernamen, keine Cooldown-/Ult-Timer, keine Injection, kein Speicherlesen, kein
Auto-Hover/-Pick/-Bann/-Accept, Runen und Beschwörerzauber nur auf Klick als einziger Schreibweg
(Zauber seit 07.11.2026 auf Nutzerwunsch), keine Telemetrie, Netz nur für
die eigene Datendatei, Data Dragon und die Update-Prüfung. „Mein Pool“ blendet aus, gewichtet nie.

## 7. Responsiv (2.0)

Container-Abfragen auf der Fensterbreite: breit ≥ 1.100 px (Board dreispaltig, Uhr Mitte,
Kachelraster 1,7 fr / 1 fr), mittel 720–1.099 (Phasenzeile über zweispaltigem Board, Avatare 36,
Lane-Prozente nur unter 60 %), schmal 340–719 (Leiste aus zehn Avataren, Segment-Schalter eigene
Zeile, einspaltig, Pill ins Menü); Hauptkachel < 470 px → Balken und Legende weichen.

## 8. Werkzeuge

1.5-Befehle, die bleiben (C#): `record`, `replay`, `probe`, `events`, `ingame`, `scrub`, `inspect`,
`recommend`, `noise`, `priors`, `patchdrift`, `riot …`, `golden` (neu). OP.GG-Befehle (`update`,
`opgg`, `guidecheck`, `coverage`, `matchupfit`) werden mit 2.0 überflüssig. 2.0 bringt
Browser-Replay und Playwright-Screenshots.

## 9. Tests → Vitest

Übernommen werden die Fälle aus: AramAdvisor, AugmentAdvisor, BuildChoice, CompAnalyzer,
CounterRisk, DraftBalance, DraftState, DraftTracker, HostileInput, LaneMatchups, LanePredictor,
LiveGameScrubber, LiveOverlay (soweit ohne OP.GG sinnvoll), MainLane, PatchVersion, QueueKinds,
RecommendationTarget, Recommender, Replay, RuneImporter, ScoreError, SeatPriors, SessionScrubber,
ShrinkageTarget, SynergyLine, TraitFile, TurnTracker, AppSettings, SnapshotStore. Rust-seitig:
RiotCertificate, LcuRead. Entfallen mit OP.GG: OpGgParser, MatchupGuideParser,
AnalysisBuildParser, CompleteDuel, SnapshotBuilder, LiveDraftFetcher (ihre Golden-Pläne bleiben als
Formatbeispiel).

## 10. Bekannte Kleinigkeiten aus 1.5 (nicht übernehmen)

`BuildHint` „gegen #0“ bei unbekanntem Gegner; ungenutztes `overrides.json`; ungebundenes
`ShowIdleCard`; veraltete Kommentare (OpGgMcpClient „nur vom Update“, AutoHide „schrumpft“).
