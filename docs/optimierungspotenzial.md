# DirkDraft — Optimierungspotenzial (Analyse vom 2026-09-01)

Ergebnis einer Prüfung mit vier unabhängigen Blickwinkeln (Empfehlungsmodell, Datengrundlage,
Effizienz/Betrieb, Draft-Ablauf) plus adversarischer Priorisierung. 45 Einzelbefunde, hier auf das
Handlungsrelevante verdichtet.

## Stand der Umsetzung (2026-09-01)

**Umgesetzt:** Maßnahme 1 (exakte Winrates), 2 (Build zuerst, Gegner parallel), 4 (Bootstrap und
Gleichrangigkeits-Schwelle), 5 (Ausfälle sichtbar, Fehler pro Aufruf, Live-Kanten nach Lane) sowie
die Quick Wins Gleichheits-Wächter, Test-Tor, vergessene Datenfelder, echter Datenstand und
Feld-Diagnose. Gemessene Wirkung von Maßnahme 1: von 12 auf 274 verschiedene Lane-Winraten, Zeilen
auf exakt 0,50 von 68 auf 2, Zeilen ohne Spielzahl von 9 auf 0.

**Offen:** Maßnahme 3 (Duell-Term zentrieren), 6 (Mittelspalte nach dem eigenen Lock) und die
restlichen Quick Wins (`queueId` lesen, Ban-Gate und Ban-Stärke aus derselben Lane, Jungle-Duos
streichen, doppeltes Laden beim Start).

**Zusätzlich, nicht aus dieser Analyse:** Blind-Pick-Rückfall. Der Live-Test zeigte, dass
Warteschlangen ohne Gegner-Reveal strukturell *keinen* Build und *keine* Runen bekommen — die
Bedingung dafür verlangt einen aufgedeckten Lane-Gegner, und OP.GG hat nachweislich keinen Build
ohne Gegner (29 Werkzeuge, keins davon generisch). Gelöst über einen benannten Ersatzgegner: den
meistgespielten Champion der eigenen Lane, sichtbar als solcher gekennzeichnet. Greift nur, wenn
gar kein Gegner aufgedeckt ist — ein Draft, der sie zeigt, ist die paar Sekunden Wartezeit wert.
Das Scoring bleibt unangetastet: einen Gegner in die Bewertung zu erfinden würde jeden Kandidaten
unterschiedlich verzerren.

### Was Maßnahme 4 ergeben hat

Der Score trägt jetzt seinen eigenen Standardfehler, aus den Stichprobengrößen hinter seinen
Termen. `Tools -- noise` zieht denselben Draft wiederholt aus den Stichproben und prüft die
Formel gegen echtes Resampling — am Support-Draft des Nutzers stimmten beide auf zwei Stellen
überein (Rell 1,51 analytisch gegen 1,48 gezogen, über alle acht Kandidaten).

Das gemessene Ergebnis dieses Drafts: **Platz 1 bleibt nur in 48 % der Ziehungen derselbe
Champion** (Leona 34 %, Alistar 14 %, Blitzcrank 4 %). Die neue Regel markiert genau diese vier
als gleichrangig. Die frühere Schwelle von 0,003 markierte nichts. Der Fehler stammt fast
vollständig aus einer einzigen Synergie über 111 Spiele.

Damit sind zwei Falschaussagen der Oberfläche weg: „starke Wahl" ab festen 53 % (jetzt in
Standardfehlern gemessen) und die Nachkommastelle, die eine Genauigkeit behauptete, die keine
111-Spiele-Statistik hergibt. Die Rauschzahlen der Analyse selbst (1 SE median 0,68 pp,
Rundungs-sd 0,29 pp) sind überholt: sie wurden am gerundeten Snapshot vor Maßnahme 1 gemessen.

Nachträglich beim ersten echten Lauf gelernt und bereits berücksichtigt: OP.GG lässt den
Synergie-Zweig für die **eigene Lane** eines Champions weg. Eine Feld-Warnung darf deshalb nur
anschlagen, wenn *jede* Antwort ein Feld ablehnt — sonst meldet sie bei jedem Update 16
Fehlalarme.

Volltext aller Befunde mit Belegen (Datei:Zeile und Messungen):
`~/.claude/projects/D--Claude-Code-LoL/67de650d-.../subagents/workflows/wf_a260ce8b-f7d/journal.jsonl`

---

## Die zwei wichtigsten Einsichten

**1. Die Rangfolge liegt innerhalb ihres eigenen Rauschens.** Bootstrap über den echten Recommender
(Rate je Champion neu gezogen aus Binomial(play, rate), 400 Ziehungen): Der Top-1-Pick bleibt
Nr. 1 in nur **21,3 %** der Ziehungen (leeres Mid), 35,0 % (Top gegen Darius), 54,5 % (Mid gegen
Syndra), 96,3 % (Bot gegen Caitlyn). Die Liste ist also bei dünner Draft-Information kaum mehr als
eine Zufallsauswahl aus der Spitzengruppe — sie behauptet aber mit „53,9 % WR" und „starke Wahl"
eine Genauigkeit, die die Daten nicht tragen. `CountLeadingTies` (MainViewModel.cs:1636) hat den
Mechanismus schon, aber mit 0,3 pp eine zu enge Schwelle.

**2. Gültigkeit vs. Präzision (die Grenze des Ansatzes).** OP.GGs Lane-Winrate ist die Siegquote
von Spielen, in denen jemand diesen Champion *freiwillig gewählt* hat — Selektion auf Spielerwahl
und Spielerkönnen, nicht der kausale Effekt des Picks. Ein 54-%-Champion ist teilweise eine
54-%-Spielerpopulation. Alle Verbesserungen unten machen die Rechnung **präziser, nicht richtiger**.
Diese Grenze gehört in einen Satz in der UI, nicht in eine weitere Konstante.

---

## Priorisierte Maßnahmen

### 1. Lane-Winrate aus `win/play` rechnen statt das gerundete Feld lesen — klein
`SnapshotBuilder.cs:736` fragt `win_rate` an (2 Dezimalstellen), nicht den exakten Zähler `win`.
Folge, am echten Snapshot gemessen: **274 Lane-Zeilen haben nur 12 verschiedene Winrate-Werte**,
68 davon (24,8 %) liegen auf exakt 0,50 — dort ist der ganze Lane-Term nur noch `TierNudge`.
Die Mid-Top-4 haben alle rawWR 0,52 und Tier 1; ihr Endabstand von 0,048 pp entsteht allein aus
der Spielzahl. 130 Zeilen darüber macht es der gleiche Builder richtig (`:465` rechnet
`(double)wins / play` aus `counters[].win`).
**Voraussetzung für fast alles andere** — Priors, Bootstrap-Baseline und der Ban-Befund stecken
heute voller Rundungsartefakte.
Erster Schritt: einen Probe-Aufruf mit `win` in `desired_output_fields`; liefert der Endpunkt den
Zähler, dann in `BuildLaneMetaFields` ergänzen. Risiko: mehrere Fixture-Tests brechen.

### 2. Build zuerst holen, Gegner-Abrufe parallel — klein
`MainViewModel.cs:1104` läuft seriell über alle Gegner (~3 s pro Aufruf), **erst danach**
`:1134-1154` der Build. Der Build ist das einzige zeitkritische, klickbare Ergebnis
(Runen-Import) und kommt so bis zu 15 s später als möglich. Derselbe Endpunkt verträgt beim
Update 10 gleichzeitige Aufrufe.
Erster Schritt: den Build-Block vor die Schleife ziehen (reine Verschiebung), dann
`SemaphoreSlim(3)` + `Task.WhenAll` wie in `IconDownloader.cs:44-68`. Nebeneffekt: die Liste
sortiert sich einmal statt fünfmal um.

### 3. Duell-Term pro Champion und Lane zentrieren — mittel
`Recommender.cs:419` addiert `Logit(matchup.WinRate)` unzentriert, während `:330` im selben Score
schon `Logit(stat.WinRate)` addiert — und die Lane-Winrate *ist* der Mittelwert über alle Gegner.
Doppelzählung, gemessene Korrelation der beiden Terme r = 0,469 (pro Champion 0,683). Zugleich
fehlen **83 % der Matchup-Kanten**, und eine fehlende Kante zählt heute als „ausgeglichen".
Nach Zentrierung heißt „keine Kante" automatisch „durchschnittliches Matchup dieses Champions".
Löst zwei Probleme gratis, für die sonst zusätzliche Netzlast fällig wäre.
Erster Schritt: **offline validieren** — 20 % der 1506 Kanten zurücklegen, Log-Loss gegen den
pauschalen 0,5-Wert messen. Läuft als Test gegen den vorhandenen Snapshot, ohne Spielausgänge.
Risiko: Die Kanten sind eine selektierte Stichprobe (Mittelwert 0,469, weak_counters überwiegen);
der Versatz muss nach Stichprobenzahl zur 0 geschrumpft werden.

### 4. Bootstrap als Test, Gleichrangigkeits-Schwelle auf das gemessene Rauschen — mittel
Messinstrument für 1 und 3, und Ende der größten inhaltlichen Falschaussage der Oberfläche
(`RecommendationViewModel.cs:142` vergibt ab 53 % „starke Wahl" — auf leerem Mid erreichen das
vier Champions ohne jede Gegner-Information). Rauschgrenze: 1 SE median 0,68 pp, dazu
Rundungs-sd 0,29 pp, gegen heute 0,003 in `CountLeadingTies`.

### 5. OP.GG-Ausfälle sichtbar machen, Fehler pro Gegner, Live-Kanten nach (Champion, Lane) — klein
Drei Befunde, eine Stelle. `LiveDraftFetcher.cs:309/366` schluckt jede `OpGgApiException` und gibt
`[]` zurück — der ganze vorhandene Ausfall-Apparat wird nie erreicht. Ein einzelner Fehlschlag
sperrt 30 s **alles** (auch den Build). Und `_liveFetched` ist ein `HashSet<int>`
(`MainViewModel.cs:59`): kippt die Lane-Vorhersage, liegen die teuer geholten Kanten dauerhaft
unter der falschen Lane — `_buildFetched` macht es schon richtig (dreiteiliger Schlüssel).

### 6. Nach dem eigenen Lock zeigt die breiteste Spalte unmögliche Picks — mittel
Die letzten ~60 s des Drafts füllt die Mittelspalte eine Liste, die niemand mehr klicken kann
(`TurnTracker.cs:68`, Header „Picks für dich · Mid (Ausblick)"), während Build und Runen-Knopf in
der 280-px-Spalte stecken. Sinnvoller: eigene Matchup-Karte plus Build groß.

---

## Quick Wins (klein, sofort spürbar)

- **Gleichheits-Wächter für die Reason-Chips** (`RecommendationViewModel.cs:159-161`): unbedingte
  Zuweisung in die `ObservableCollection` → bis ~30 überflüssige Replace-Meldungen je Render,
  teurer als der gesamte Rechenkern (0,35 ms). Derselbe Fehler ist an zwei anderen Stellen
  bewusst behoben, mit Kommentar. Zwei Zeilen.
- **`dotnet test` als Tor in `publish.ps1`** (heute nur `dotnet publish`, kein CI im Repo).
- **`queueId` und `isCustomGame` in `ChampSelectSession`** — steht im ersten Frame jeder Session
  (`queueId: 3110`), null Zusatzaufrufe. Heute empfiehlt das Tool in ARAM/Custom Lane-Picks mit
  derselben Bestimmtheit wie in Ranked Solo.
- **`positions[].stats.play` mitanfragen und datenlose Lanes aus `BestLaneLogOdds` ausschließen**:
  `AddLaneFallback` setzt `Play = 0` → Shrinkage zieht auf exakt 0,5 → 9 von 274 Zeilen heben
  einen Champion über seine echte Lane. Null Zusatzaufrufe.
- **Ban-Gate und Ban-Stärke aus derselben Lane** und `enemyLanes` an `ScoreBans` durchreichen
  (heute weiß die zweite Ban-Runde nichts von sechs offenen Picks; bei 28 von 173 Champions
  begründet ein Mid-Ban sich mit „stark auf Bot").
- **Jungle→{Top,Mid,Support}-Duo-Ausweitung streichen**: 120 der ~354 Update-Aufrufe für eine
  Lane-Information, die nie gelesen wird (`Deduplicate` gruppiert lane-frei). Update 1:38 → ~1:05.
- **`CLAUDE.md` korrigieren**: Snapshot ist 441 KB (nicht 294), Pick-Kandidaten 42–63 je Lane
  (173 nur für Bans), Render-Kern 0,35 ms.
- **Startzeit**: `StartupReport.Write()` (App.xaml.cs:82) lädt Snapshot, Traits und Settings
  komplett selbst, `MainViewModel` (:113) danach erneut — ~180 ms doppelte UI-Thread-Arbeit vor
  dem ersten Frame.

## Bewusst nicht tun

- **Spielausgänge mitschreiben, um die Score-Konstanten zu kalibrieren.** Rechnung:
  SE(β) ≈ 20/√n, also 0,63 bei 1000 eigenen Spielen — man könnte nicht unterscheiden, ob ein
  Gewicht 0 oder 1 sein soll. Bräuchte zusätzlich einen neuen LCU-Lesezugriff nach dem Spiel.
  Ehrliche Konsequenz: Kalibrierung aufgeben und die Grenze dokumentieren.
- **66 zusätzliche Aufrufe für Zweitlane-Matchups** — Maßnahme 3 löst dasselbe Problem gratis.
- **Ban-Wert als Ersatzwert-Differenz** — der Beleg (0,005 pp Abstand) *ist* das Rundungsartefakt
  aus Maßnahme 1. Nach 1 neu stellen.
- **Live-Counter auf Platte cachen** — trägt auf einer ungemessenen Vermutung; Maßnahme 2 nimmt
  den Großteil der Wartezeit.
- **Comp-Term ausbauen** — gemessen 2,1 % Rangvarianz, |Punkte| median 0,00. Wenn überhaupt:
  `CompScale = 0` und die Chips als Erklärung behalten.
- **Eigenes App-Testprojekt** — `MainViewModel` ist wegen `Application.Current.Dispatcher`
  (Zeile 46) ohne Umbau nicht instanziierbar; das Test-Tor in `publish.ps1` holt den Großteil.
- **Summoner-Spell-Import per Knopf** — wäre ein zweiter Schreibpfad in den Client.
  Entscheidung des Nutzers, nicht Umsetzungsaufgabe.
- **Sitz-Priors anfassen** — echte Sitz-Statistiken wären nur für die *gegnerische* Reihenfolge
  nützlich, die der Client nie liefert. `pick_order_priors.json` bleibt flach.

## Offene Fragen (blinde Flecken der Analyse)

- ~~**Anzeige-Präzision**~~ — erledigt mit Maßnahme 4: die Nachkommastelle folgt jetzt dem
  gemessenen Fehlerbalken, einmal für die ganze Spalte entschieden. Ursprünglicher Befund: Die UI schreibt `P1` („53,9 %") auf Daten mit 1-pp-Auflösung. Ganze
  Prozent oder eine Spanne wäre sofort ehrlicher, ohne jede Modelländerung.
- **Nutzt der Nutzer die Liste überhaupt?** Ob aus den Top 3 gepickt wurde, ist ohne jeden neuen
  Zugriff feststellbar (eigener Lock steht in der Session, Empfehlung liegt im Speicher). Die
  einzige Kennzahl, die die eigentliche Frage beantwortet.
- **Aufmerksamkeitsbudget**: `RecommendationCount = 8`, je Zeile bis zu fünf Chips. In 30 Sekunden
  könnten 3–4 gruppierte Zeilen die Entscheidungsqualität mehr heben als jede Modelländerung.
- **Reihenfolge zählt**: Die Maßnahmen entwerten sich teils gegenseitig (Priors, Ban-Befund und
  Bootstrap-Baseline hängen alle an Maßnahme 1). Ohne festgelegte Reihenfolge messen die Tests
  hinterher etwas anderes als vorher.
- **Bestehende Tests als Risiko**: Ungeprüft, welche der 220 Tests das *falsche* Verhalten
  festschreiben (z. B. „fehlendes Matchup ergibt Score X") und damit eine Korrektur blockieren.
