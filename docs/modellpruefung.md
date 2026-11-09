# Modellprüfung (2026-10-08): Sind die Vorschläge die besten? Reicht die Datengrundlage?

Zwei Fragen, gemessen statt behauptet. Grundlage: der gespeicherte Gold-Snapshot (Patch 16.18,
173 Champions, 273 Lane-Zeilen, 1.482 Duelle, 2.909 Duos) und Live-Abrufe bei OP.GG am 08.10.2026
(Patch 16.20); die Messungen 7 bis 9 zusätzlich auf einem am selben Abend frisch gebauten
Gold-Snapshot von 16.20 und auf der Datei des Nutzers (alle Ränge, 16.20). Alle Zahlen sind mit den
Werkzeugen in `src\DraftPilot.Tools` reproduzierbar.

## Kurzantwort

**Vor dieser Prüfung: nein, nicht verlässlich.** Zwei Teile des Scores haben systematisch zu viel
gezählt. Duo-Winrates enthielten die Einzelstärke der Champions ein zweites Mal und waren sechsfach
zu schwach geglättet. Die Lane-Duelle stammten zum großen Teil aus OP.GGs Listen der „auffälligsten
Gegner" — einer Auswahl der Extreme, deren Werte sich bei unabhängiger Messung kaum wiederfinden. So
kamen Schätzungen wie „Singed 60 % gegen Jax" zustande. Mit denselben Daten, richtig gewichtet, und
der vollständigen Duell-Liste sind es 53 %, gleichauf mit vier anderen.

**Danach: so gut, wie die verfügbaren Daten es hergeben — und das ist weniger, als eine
Statistikseite suggeriert.** Die Lane-Stärke ist solide gemessen. Matchups und Duos bewegen die
Gewinnchance real nur um wenige Punkte. Die Spitze der Liste liegt deshalb oft innerhalb des
Rauschens, und das Fenster sagt das.

**Reicht die Datengrundlage?** Für die Lane-Stärke ja: zehntausende Spiele je Zeile. Für Duos und
Matchups nur bedingt: Ein Einzelwert über ein paar hundert Spiele ist zum großen Teil Zufall. Für
den Lane-Gegner reicht sie jetzt, weil der Draft dessen vollständige Duell-Liste holt; vorher hatten
nur 14 bis 23 % der Kandidaten überhaupt eine Zahl gegen ihn.

## Was einen Draft gewinnen lässt — und was davon messbar ist

| Faktor | Im Score? | Datengrundlage |
|---|---|---|
| Lane-Stärke des Champions | ja, voll | sehr gut: Median 29.000 Spiele je Zeile, wahre Streuung 1,6 Punkte |
| Lane-Duell gegen den Gegner | ja, geglättet | gespeichert dünn (Auswahl-Listen), live für den aufgedeckten Gegner vollständig |
| Übrige Gegner | ja, 0,35-fach | dünn, Gewicht unkalibriert |
| Duos mit Mitspielern | ja, nur der Anteil über die Einzelstärke hinaus | mittel: Median 382 Spiele, wahre Streuung 1,8 Punkte |
| Team-Zusammensetzung (Engage, Peel, Schaden …) | ja, gedeckelt auf ±4 Punkte | Regeln ohne Winrate-Grundlage, kuratierte Traits |
| Konter-Risiko eines frühen Picks | nur als Hinweis | ob der Gegner kontert, ist nicht messbar |
| Können und Champion-Erfahrung des Spielers | **nein** | bewusst ausgeschlossen (keine Pool-Gewichtung) |
| Aktueller Patch | ja: Fehlerbalken wächst je Patch Rückstand, Chip nennt beide Patches | Snapshot muss per Knopf aktualisiert werden |

Eine Grenze bleibt grundsätzlich: OP.GGs Winrates messen Spiele, in denen jemand diesen Champion
*freiwillig* gewählt hat. Das ist Auswahl nach Spielerwunsch und -können, nicht die kausale Wirkung
des Picks. Alle Verbesserungen hier machen die Rechnung **genauer**, nicht kausal richtiger.

## Die Messungen

### 1. Glättungsgewichte (`Tools -- priors`)

Jede Zeile ist „Siege aus Spielen". Halbiert man ihre Spiele zufällig, lässt sich mit der einen
Hälfte die andere vorhersagen. Das Gewicht, das das am besten tut, ist das, was die Daten tragen.
200 Teilungen je Zeile, gegengeprüft mit einer Momentenschätzung der wahren Streuung:

| | vorher | Halbierung | Momente | jetzt |
|---|---:|---:|---:|---:|
| Lane | 300 | 600 (flach) | 932 | 300, Wirkung null bei diesen Spielzahlen |
| Duo (zur Paar-Erwartung) | 100 | 600–1.000 | 753 | **750** |
| Duell | 150 | 100 (*irreführend, siehe 4*) | 168 | **1.000** |

### 2. Duos und Einzelstärke

Regression der Duo-Logits auf die Lane-Logits beider Partner (2.670 Paare): Steigung **0,41** für
den eigenen und **0,38** für den Partner. Eine Duo-Rate enthält also etwa 0,4 der Einzelstärke.
Gegen einen globalen Mittelwert zentriert, blieb dieser Anteil im Synergie-Term. Ein Kandidat mit
gelistetem Duo bekam so seine Lane-Stärke ein zweites Mal, einer ohne nicht. Die Halbierungsprobe
entscheidet zwischen drei Erwartungen: global, volle Summe (Steigung 1) und Regressionsgerade. Die
Gerade sagt am besten vorher. Die volle Summe ist schlechter als global.

### 3. Abdeckung (`Tools -- coverage`)

Gegen die zehn meistgespielten Gegner einer Lane hat der gespeicherte Vorrat nur für folgende
Anteile der Kandidaten eine Duell-Zahl:

- Top 17 %
- Jungle 16 %
- Mid 14 %
- Bot 23 %
- Support 16 %

Der Grund: Die Analyse-Schnittstelle nennt pro Liste nur die drei auffälligsten Gegner.

### 4. Vollständige Duell-Listen und der Fluch des Gewinners (`Tools -- guidecheck`)

OP.GGs Matchup-Guide enthält in `data.counters` die **vollständige** Duell-Liste eines Champions
auf seiner Lane: 39 bis 57 Gegner, 30 bis 558 Spiele je Duell. Einen Rang-Filter kennt er nicht (ein
`tier` wird angenommen und ignoriert). Gemessen an 16 Champions über vier Lanes; Jungle liefert
keine Liste:

- **Population:** 788 Duelle liegen im Mittel 0,36 Punkte neben der Gold-Erwartung. Das ist
  vernachlässigbar.
- **Auswahl-Effekt:** Am selben Tag und für dieselben 123 Paare weichen die Counter-Listen im Mittel
  **5,0 Punkte** von der Erwartung ab, die vollständigen Listen **2,3**. Die vollständige Abweichung
  folgt der Counter-Abweichung mit einer Steigung von nur **0,13**. Die Counter-Listen nennen die
  Extremwerte aus etwa fünfzig verrauschten Duellen, und diese Werte fallen bei neuer Messung zur
  Mitte zurück.
- **Kalibrierung:** Das Gewicht, unter dem ein Counter-Listen-Duell die unabhängige Messung am
  besten vorhersagt, ist 1.000 (Fehler 12,4 statt 20,5 bei 150). Der Halbierungstest auf den
  vollständigen Listen, bei dem keine Auswahl geteilt wird, landet ebenfalls bei 1.000. Die
  Halbierung der *gespeicherten* Duelle aus Messung 1 kann den Auswahl-Effekt nicht sehen, weil
  beide Hälften einer ausgewählten Zeile dieselbe Auswahl teilen.
- **Was das über das Spiel sagt:** Jenseits der Lane-Stärke beider Champions verschiebt ein Duell
  die Winrate um etwa 1,6 Punkte (Standardabweichung). Matchups sind real, aber klein. Mit ein paar
  hundert Spielen lassen sie sich kaum auflösen.

### 5. Wirkung auf Drafts (`Tools -- recommend … --live`)

| Draft | vorher Platz 1 | jetzt Spitze |
|---|---|---|
| Top gegen Jax | Singed 60 % ±1,8 | Singed, Garen, Sett, Warwick, Kled 52–53 %, gleichauf |
| Bot gegen Ezreal/Karma | Kog'Maw 59 % ±2,2 | Kog'Maw, Veigar, Brand 54–55 %, gleichauf |
| Mid gegen Syndra | Katarina 58 % ±1,3 | Fizz, Gwen, Heimerdinger 53–54 % |
| Support gegen Cait/Lux | Taric 57 % ±2,1 | Braum, Thresh, Taric, Leona 54–55 %, gleichauf |
| Jungle mit Yasuo | Platz 1 nur in 24 % der Ziehungen stabil | 97 % |

Vorher landeten Champions ohne eigene starke Lane-Rate oben, getragen von einem einzigen
Auswahl-Duell oder einem dünnen Duo: Katarina und Irelia gegen Syndra, Karthus mit Yasuo. Jetzt
führen Champions mit nachweisbarer Stärke, und die vollständigen Listen liefern Duelle für fast
alle Kandidaten.

### 6. Stimmen die Fehlerbalken noch? (`Tools -- noise`)

Ja. Der analytische Fehlerbalken und echtes Neuziehen stimmen weiter überein, zum Beispiel:

| Champion | analytisch | Neuziehen |
|---|---:|---:|
| Leona | 0,33 | 0,33 |
| Braum | 0,59 | 0,58 |
| Gwen | 0,81 | 0,80 |
| Taric | 0,82 | 0,90 |

### 7. Wie schnell altern die Daten? (`Tools -- patchdrift`)

Derselbe Gold-Bracket, Patch 16.18 gegen 16.20 (frisch geholt am 08.10.2026). Was sich zwischen den
beiden Dateien bewegt, ist Stichprobenrauschen plus echte Verschiebung; das Rauschen ist aus den
Spielzahlen bekannt und wird abgezogen:

- Echte Verschiebung der Lane-Raten: **0,68 Punkte** Standardabweichung über zwei Patches.
- Von den fünf stärksten Champions einer Lane standen nach zwei Patches im Schnitt nur **zwei**
  noch dort.
- Als Irrfahrt gelesen: 3,7·10⁻⁴ Logit² je Patch. Um so viel wächst die Varianz jeder Lane-Zahl
  pro Patch, den die Daten hinter dem Client liegen (`ScoreModel.LaneDriftPerPatch`). Der Score
  selbst bleibt unverändert — älter heißt unsicherer, nicht in eine bekannte Richtung falsch.

Dazu, weil es am selben Tag auffiel: In den ersten Tagen eines Patches sind die Zahlen dünn. Die
Datei des Nutzers (alle Ränge, 16.20, Stand 07.10.) hat im Median 6.176 Games je Lane-Zeile; die
Gold-Datei von 16.18 nach zwei Wochen Laufzeit 29.000. Das Aktualisieren holt trotzdem immer den
neuesten Patch — geprüft an Data-Dragon-Version, OP.GG-Datenpatch und Datenstand der Datei.

### 8. Aus welchem Rang stammen die vollständigen Listen? (`Tools -- guidecheck <datei>`)

Der Matchup-Guide nimmt keinen Rang-Parameter an und beschreibt OP.GGs Standard-Bracket. Seine
Duelle gegen die Lane-Raten derselben Woche gelesen:

| Lane-Raten der Datei | gepoolt | innerhalb der Listen |
|---|---:|---:|
| alle Ränge (Datei des Nutzers) | 0,996 | **0,984 ± 0,068** |
| Gold | 0,757 | **0,670 ± 0,076** |

16 Listen, 789 Duelle. Die 0,79, die eine erste Runde auf der älteren Gold-Datei maß, war also
kein Modellfehler, sondern der Rang-Unterschied: Ein in Gold starker Champion ist in Emerald+
weniger stark, und sein Duell wird dort gemessen. Mit Steigung 1 las sich jeder in Gold starke
Champion gegen jeden aufgedeckten Gegner als schwächer, als er ist.

**Gepoolt oder innerhalb der Listen?** Der Draft liest einen aufgedeckten Gegner und jeden
Kandidaten aus dessen Liste. Gefragt ist also, wie sich die Duelle **einer** Liste mit der Stärke
des Kandidaten ändern. Gepoolt geht zusätzlich ein, wie weit der Listen-Besitzer selbst im
Standard-Bracket neben seiner Gold-Rate liegt — je Liste eine Konstante, bei einem Dutzend
Besitzern vor allem Rauschen. Deshalb wird innerhalb der Listen gefittet.

Ein erster Versuch nahm die Steigung aus den Lane-Raten beider Brackets statt aus Duellen. Er
ergab 0,55 und hätte die Erwartung in die andere Richtung verkippt; verworfen. Jetzt holt jedes
Aktualisieren vollständige Listen und fittet die Linie direkt
(`SnapshotBuilder.MeasureCompleteDuelLine`). Die Stichprobengröße folgt dem Fehlerbalken: Zwölf
Listen (drei je Lane) ergaben 0,589 bei rund ±0,09, also nicht genau genug; mit acht je Lane,
32 Abrufen (rund 8 % mehr beim Aktualisieren):

| Datei | Steigung | Versatz | Duelle |
|---|---:|---:|---:|
| Gold 16.20, 32 Listen | **0,663 ± 0,056** | −0,0074 | 1.521 |

Das stimmt mit der unabhängigen Messung an 16 anderen Listen überein (0,670). Ein Fehler von
±0,056 verschiebt einen Kandidaten am Rand seiner Lane (zwei Standardabweichungen Lane-Stärke) um
etwa 0,2 Punkte gegen die Mitte, weit innerhalb der Fehlerbalken der Liste.

### 9. Gespiegelte Duelle

Fast jedes Duell gegen den Lane-Gegner liest der Kandidat aus der Liste des **Gegners**, also von
der anderen Seite. Beide Versätze — der Auswahl-Versatz der Counter-Listen und der der
Duell-Linie — gehören zu der Richtung, in der OP.GG die Liste aufgeschrieben hat. Die Erwartung der
Gegenrichtung trug sie mit demselben Vorzeichen statt mit dem umgekehrten. Folge: Ein dünnes Duell,
das exakt der Erwartung entsprach, war gespiegelt das Doppelte des Versatzes wert — +0,026 Logit
(0,7 Punkte) auf der aktuellen Gold-Datei, +0,059 (1,5 Punkte) auf der älteren, für jeden Champion,
den die Auswahl-Liste eines Gegners nannte. Jetzt ist die Erwartung der Gegenrichtung das exakte
Komplement; ein Test hält fest, dass ein Duell an seiner Erwartung in beiden Richtungen nichts
beiträgt.

## Was geändert wurde

1. **Duo-Erwartung je Paar** (`SynergyLine`): Duos werden zur Regressionsgeraden ihrer beiden
   Lane-Raten gezogen, und der Synergie-Term zählt nur die Abweichung davon. Die Gerade wird beim
   Laden aus den Zeilen der Datei gefittet. Bei weniger als 100 Paaren gilt die alte globale
   Erwartung.
2. **Glättungsgewicht für Duos 750, für Duelle 1.000** (`Shrinkage`), beide gemessen.
3. **Vollständige Duell-Liste für jeden aufgedeckten Gegner** (`FetchCompleteDuelsAsync`), ein
   Abruf mehr je Gegner. Das Budget je Draft steigt von 16 auf 24 Abrufe.
4. **Vorrang der vollständigen Liste** vor der Auswahl-Liste beim selben Paar, unabhängig von der
   Stichprobe. Die vollständige Liste ist keine Auswahl und bekommt deshalb auch keinen
   Auswahl-Versatz in ihrer Erwartung.
5. **Ehrliche Texte:** Wo „aus N Games" steht, steht jetzt der gemessene Wert. Die Prozentzahl
   daneben ist die Schätzung, und der Tooltip sagt, warum beide sich unterscheiden.
6. **Patch-Rückstand im Fehlerbalken** (`LaneDriftPerPatch`, Messung 7) und als Chip im Draft.
7. **Duell-Linie je Datei** (`CompleteDuelSlope`/`CompleteDuelOffset`, Messung 8): 32 Abrufe
   mehr beim Aktualisieren, rund 8 %. Kommen weniger als 150 Duelle zurück, gilt die einfache
   Erwartung, und die Datei vermerkt das. `Tools -- inspect` zeigt Steigung, Fehler und Stichprobe.
8. **Gespiegelte Duelle** lesen ihre Erwartung als exaktes Komplement (Messung 9).
9. **Der Auswahl-Versatz wurde mit 0 gemessen**, seit das Duell-Gewicht auf 1.000 stand: Sein
   Filter („nur Kanten mit mindestens so vielen Games wie das Glättungsgewicht") ließ kaum noch eine
   Kante durch. Er hat jetzt eine eigene Schwelle von 150 Games. Nie ausgeliefert.

## Was offen bleibt

- **Drei Gewichte sind weiter Schätzungen:** übrige Gegner (0,35), Duo-Dämpfung (0,6) und
  Team-Zusammensetzung (0,16). Kalibrieren ginge nur an Spielausgängen. Bei 1.000 eigenen Spielen
  läge der Fehler eines Gewichts bei etwa 0,6, man könnte also 0 nicht von 1 unterscheiden.
- **Jungle-Duelle** bleiben auf den Auswahl-Listen, weil OP.GG für den Jungle keine vollständige
  Liste liefert. Sie sind jetzt aber entsprechend stark geglättet.
- **Gold-eigene Duelle gibt es nur als Auswahl.** Die vollständigen Listen kommen aus dem
  Standard-Bracket; die Linie aus Messung 8 rechnet sie auf die Lane-Stärken der Datei um, aber
  ein Duell, das in Gold anders läuft als in Emerald+, bleibt unsichtbar.
- **Der Datenstand altert.** Das Fenster meldet den Patchwechsel, und der Fehlerbalken wächst mit
  ihm. Aktualisieren ist ein Knopfdruck. Am ersten Tag eines Patches sind die Zahlen dünner, aber
  beschreiben das richtige Spiel.

---

# Nachtrag 2.0 (2026-11-07): dieselben Fragen auf den eigenen Daten

Seit 2.0 kommen alle Zahlen aus dem eigenen Sammler (Riot-API, Solo/Duo, Platin, EUW). Gemessen auf
den Dateien des Branches `data` vom 07.11.2026 (Stand 05:20 UTC): `meta.json` und `builds.json` aus
**6.185 Spielen**, Patch 16.22. Die Experimente liegen nicht im Repository; `priors` ist das
bekannte Werkzeug, die übrigen Rechnungen sind kurze Skripte gegen dieselben Dateien.

## 10. Glättung auf eigenen Daten (`Tools -- priors 400 --snapshot meta.json`)

| | Zeilen | Median Spiele | Halbierung | Momente | vorher | jetzt |
|---|---:|---:|---:|---:|---:|---:|
| Lane | 294 | 149 | **800** (−0,018 % Log-Loss ggü. 300) | 851 (τ = 1,71 Pkt.) | 300 | **800** |
| Duell | 767 Paare | 14 | ∞ (−0,002 %) | ∞ (τ = 0) | 1.000 | 1.000 |
| Duo, zur Paar-Erwartung | 251 Paare | 36 | ∞ (−0,405 % ggü. −0,402 % bei 800) | ∞ (τ = 0) | 750 | 750 |

- **Lane:** Beide Verfahren finden rund 800 — und schon 1.5 maß auf OP.GGs Gold-Zeilen 600 bis 932.
  κ beschreibt die wahre Streuung der Champions, nicht die Stichprobe, und gilt deshalb auch, wenn die
  Datei wächst. Wirkung, gemessen als Rangtest (200 zufällige Teilungen: Top 5 je Lane auf der einen
  Hälfte gewählt, Winrate in der anderen): κ 0 → 49,86 %, 300 → 50,18 %, **800 → 50,27 %**. Mit 300
  standen Champions mit 28 bis 44 Spielen in den Top 5 (Neeko und Gragas Mid, Vel'Koz Platz 1 Bot),
  mit 800 führen Champions mit Hunderten Spielen. Das Gewicht 800 gilt nur für eigene Dateien
  (`source: "riot"`, `shrinkage.ts`); OP.GG-Dateien behalten 300 — dort ist die Golden-Parität mit
  1.5 gemessen, und bei 29.000 Spielen je Zeile ist die Wirkung null.
- **Duelle und Duos:** Bei 14 bzw. 36 Spielen im Median ist über die Erwartung hinaus kein Signal
  messbar. Die Gewichte halten beide klein, wie es sein soll; sie bleiben.
- Pick- und Bann-Raten haben dieselbe Skala wie bei OP.GG (Pick je Lane summiert ≈ 1,9, Bann über
  alle Champions ≈ 9,3) — das Bann-Tor gilt unverändert.

## 11. Build-Auswahl (`builds.json`, 683 Zeilen)

Die Regel aus 1.5, „je Slot die beste Wilson-Untergrenze“, wählte auf den eigenen Daten Zufall:
Caitlyn bekam eine Runenseite mit **8 Spielen und 8 Siegen** (Untergrenze 68 %) statt der Seite mit
469 Spielen, Jinx einen Start mit 5 Spielen, Lee Sin Runen mit 9.

- **Wahre Streuung zwischen Optionen eines Slots** (Momentenschätzung über alle Zeilen ab 30
  Spielen, Optionen ab 5): **τ = 0,00 in jedem Slot** — Runen (1.047 Optionen), Zauber (594), Start
  (689), Stiefel (790), Kern (920), viertes Item (716), Skill-Reihenfolge (760), Skill-Maximierung
  (514), Support-Item (131). Jeder Winrate-Unterschied zwischen den Optionen ist hier Stichproben-
  rauschen.
- **Beliebtheit gegen Winrate** (gepoolt, relativ zur Rate des Slots, ohne Auswahl): Kein
  einheitliches Muster. Seltene Optionen (< 3 % des Slots) liegen bei Runen +3,2, Start +2,8,
  Skill-Reihenfolge +3,1, aber bei Zaubern −3,3 und Skill-Maximierung −3,9 Punkte, jeweils über 600
  bis 900 Spiele (≈ 1,8 σ). Eine feste Strafe für seltene Builds ließe sich damit nicht begründen.
- **Verworfen:** ein Halbierungstest für Builds. Beide Hälften einer Stichprobe teilen deren Glück —
  eine 8/8-Option ist in beiden Hälften 4/4 —, der Test belohnt also genau die Zufallstreffer, die er
  finden soll.

**Geändert (`riotBuilds.ts`):** Jede Option wird zur Rate ihres Slots gezogen, mit κ = μ(1−μ)/τ² − 1
aus der Datei selbst (ab 100 Optionen je Slot; darunter oder ohne messbare Streuung κ = ∞). Gleiche
Schätzungen entscheidet die Spielzahl. Heute heißt das: die meistgespielte Option; sobald die Daten
echte Unterschiede tragen, folgt dieselbe Regel ihnen. Dazu:

- Gleiche Builds zählen als einer: ein Zauberpaar in beiden Tastenfolgen, eine Runenseite unabhängig
  von der Reihenfolge der Nebenrunen (z. B. bei Ahri getrennt gezählt).
- Items 4 bis 6 wiederholen weder den Kern noch einander (vorher 34 Zeilen mit Dubletten, 75 mit
  einem Kern-Item).
- Lane-Zeilen unter 30 Spielen oder ohne Kern nehmen die Hauptlane mit Hinweis (vorher hatten 374
  von 683 Zeilen gar keinen Kern im Plan; ihr Median: 3 Spiele).

## 12. Situative Items (`situational.json`)

- Stichproben sind plausibel: Stahlkappen gegen viel AD, Merkurs gegen viel AP, Randuins bei Darius
  gegen AD, Naturgewalt bei Garen gegen AP, Schutzengel und Blutdürster bei Schützen gegen viel
  Rüstung.
- **Stiefel nur für Lagen der Aufstellung:** Von 57.533 gezählten Stiefelkäufen fielen 0,2 % in
  „Rüstung“, 0,0 % in „Magieresistenz“ und 2,9 % in „Heilung“ — die Gegner bauen diese Werte erst,
  wenn Stiefel längst gekauft sind. Was dort zählt, sind späte Wechsel (Jinx: „Stiefel der
  Wendigkeit 13,5-mal so oft gegen Heilung“). Bei Items 4–6 fallen 40,6 % der Käufe in „Rüstung“ —
  dort ist das Signal echt.
- Items aus dem eigenen Build sind keine Alternative zu ihm (vorher: Kai'Sa „Terminus“, Jinx
  „Klinge der Unendlichkeit“, beide im Kern).

## 13. Modellvergleich A/B/C — Zwischenstand (09.11.2026, vorläufig)

Werkzeug: `npm run measure:model -- <games.json.gz> <meta.json> [builds.json]` (`app/tools/measure/model.ts`).
Alle Raten nur aus Trainingsspielen gezählt (für diese selbst ohne das eigene Fünftel), durch die
MetaLookup der App gelesen; geprüft an 9.248 zurückgehaltenen von 46.277 Spielen (Patch 16.22, Platin,
Artefakt vom 09.11. morgens). Kein Gewicht geändert — das kommt mit dem endgültigen Lauf (~150.000 Spiele).

| Modell | Log-Loss | Treffer |
|---|---|---|
| Münzwurf | 0,6931 | 50,2 % |
| A: Draft-Chance wie heute | 0,6915 | 53,7 % |
| A kalibriert | 0,6887 | 53,7 % |
| Lane-Stärke allein | 0,6890 | 53,9 % |
| B: Stärke, Duelle je Lane, Synergie und Gegner-Paare je Art, Comp | 0,6890 | 53,4 % |
| C ohne Paare: Stärken je Champion und Lane gemeinsam gelernt, Comp | 0,6883 | 54,0 % |
| C: dazu Synergie und Counter aus 2 gelernten Merkmalen je Champion und Lane | 0,6882 | 54,1 % |

- **Der Draft allein sagt in Platin wenig voraus:** rund 54 % Treffer, 0,6 % der Unsicherheit erklärt.
  Weniger als die 55–60 % aus dem Konzept.
- **Die Draft-Chance ist zu flach:** Sie passt erst mit Steigung 4,2 ± 0,6, liegt also viel zu nah an
  50 %. Sie mittelt Stärke und Duelle über die Lanes, gemessen zählt die Summe (Lane-Stärke
  0,85 ± 0,13 je Einheit). Betrifft C2, nach der Messung.
- **Lane-Stärke trägt fast alles.** Duelle, Synergien und Gegner-Paare sind mit diesen Spielen nicht
  messbar (Fehlerbalken ±0,5 bis ±2); dafür braucht es die volle Menge und die drei Patches.
- **Comp:** „AP-Schaden fehlt“ (−0,32 ± 0,19) und „kein Engage“ (−0,36 ± 0,24) sind selten, aber
  deutlich; die Merkmale aus 2.1 liegen bei null.
- **Kalibrierung von B gut:** in jedem Bereich weniger als 2,5 Punkte zwischen vorhergesagt und
  tatsächlich.
- **Modell C** (`tools/measure/factor.ts`, Konzept §4) ist knapp das beste, aber im Rauschen: 0,0009 vor B.
  Sein Vorsprung kommt aus den gemeinsam gelernten Stärken. Synergie und Counter aus gelernten Merkmalen
  bewegen die Vorhersage kaum: Streuung 0,0 und 0,4 Punkte, gegenüber 4,5 für die Stärke und 1,4 für die
  Comp. Regularisierung auf einem Validierungsteil gewählt, Optimum innen im Gitter (L2 0,003 auf den
  Stärken).
- **Der beste Pick bringt** gegenüber dem nach Pickrate gewichteten Durchschnitt +2,8 Punkte (Bot +3,1,
  Support +2,6; nach C +2,9). Nachträglich gerechnet mit allen zehn Champions bekannt, also eine
  Obergrenze.
- **Lane-Gold nach 10 Minuten, je Lane** (`app/tools/measure/laneGold.ts`; der Sammler behielt es ab dem
  08.11. für jedes achte Spiel, ab Spiel-Liste Version 3 für jedes; hier 3.114 Trainingsspiele). Ein Vorsprung von 1.000 Gold ist auf Top, Jungle, Mid und
  Bot 13–15 Punkte Winrate wert, auf Support 8. Den Gold-Unterschied bestimmt auf Top das Matchup fast
  so stark wie der Champion (±166 gegen ±186 Gold, 1,9 σ); auf Jungle, Mid, Bot und Support zeigt sich
  kein Matchup. Über das Gold zählt ein Top-Paar ab 31 Spielen halb, über den Sieg bei gleich starkem
  Matchup erst ab rund 530. Auf zurückgehaltenen Spielen bringen die Gold-Matchups noch nichts
  (0,0001 im Log-Loss, Gewicht Top 0,16 ± 0,90), weil die Paare erst ein bis fünf Spiele mit Gold
  haben. Mit dem endgültigen Lauf nachmessen.

## 14. Counter je Lane, Hälfte gegen Hälfte (09.11.2026, vorläufig)

Werkzeug: `dotnet run --project src/DraftPilot.Tools -- priors 200 --snapshot meta.json`, Abschnitt
„Counter je Lane“ (Rechnung in `PairEvidence`, nur für eigene Daten). Daten: 46.277 Spiele, Patch 16.22.

Die Frage: Wie viel der Abweichung eines Duells von beider Stärke zeigt sich in Spielen wieder, auf
denen es nicht gezählt wurde? Und lässt eine Glättung, die starke Belege durchlässt, echte Hard-Counter
besser durch als das eine Gewicht von heute (κ = 1.000)?

| Lane | Paare | Median Spiele | τ (Momente) | bestes κ, linear | große Abweichungen: kommen wieder | heute behalten |
|---|---:|---:|---:|---:|---:|---:|
| Top | 1.236 | 30 | 1,34 Pkt | 1.200 | 4 % | 5 % |
| Jungle | 1.332 | 26 | 1,77 Pkt | 3.000 | 1 % | 5 % |
| Mid | 1.046 | 30 | 0 | ∞ | 5 % | 6 % |
| Bot | 548 | 40 | 1,52 Pkt | 1.200 | 10 % | 7 % |
| Support | 900 | 36 | 0 | ∞ | −7 % | 6 % |
| alle | 5.062 | 30 | 0,80 Pkt | 3.000 | 2 % | 6 % |

- **Große Abweichungen sind mit dieser Datenmenge fast nur Zufall.** Gezählt sind Abweichungen von
  mindestens 8 Punkten bei mindestens 40 Spielen in einer Hälfte (im Mittel 55–70 Spiele). Die andere
  Hälfte zeigt davon 1–10 % wieder, die Glättung von heute behält 5–7 %. Die Glättung ist für diese
  Menge nicht zu stark; je Lane liegt das beste Gewicht bei 1.200 bis unendlich.
- **Die Glättung, die starke Belege durchlässt** (`PairEvidence.Mixture`: die meisten Paare ohne
  Effekt, ein Anteil π mit), sagt die andere Hälfte nicht besser voraus; der Unterschied liegt unter
  0,01 % im Log-Loss, wie bei jedem Gewicht ab 1.000.
- **Einzelne Hard-Counter** wie Kayle gegen Garen (62 % aus 140 Spielen, rund 3 Standardfehler) sind so
  nicht von den rund 30 zufälligen Ausreißern je Lane und Teilung zu trennen. Das liegt an den Spielen je
  Paar, nicht an der Glättung. Mit der vollen Menge (~150.000) und mit Matchups über drei Patches
  nachmessen; erst dann kann eine Lane oder ein gut gezähltes Paar weniger Glättung verdienen.

## Was offen bleibt (2.0)

- **ARAM:** Die erste `aram.json` kommt mit dem Lauf vom 07.11. nachmittags; eine lokale Probe hatte
  157 Spiele (Median 8 je Champion). Das ARAM-Gewicht (400) ist aus 1.5 übernommen und erst mit
  einigen Tausend Spielen nachzumessen. Bis dahin liegt die Bank meist gleichauf — das Fenster sagt es.
- **Builds:** Mit wachsender Datenmenge die Streuung je Slot erneut messen; die Regel stellt sich
  selbst ein, aber ob Runen- und Kern-Unterschiede je messbar werden, ist offen.
- **Drei Gewichte bleiben Schätzungen** (übrige Gegner 0,35, Duo-Dämpfung 0,6, Comp 0,16), wie in
  1.5 — kalibrierbar erst an Spielausgängen.
- **Comp-Merkmale aus 2.1** (Waveclear, Timing, Tank-Shred, AD/AP nach Schaden, Plan beider Teams)
  laufen ungemessen unter dem Comp-Deckel oder ohne Gewicht. Gemessen werden sie mit
  `npm run measure` (Liste und Methode: `docs/draft-konzept.md`, „Team-Comp, gemessen“); das Ergebnis
  kommt hierher, bevor eines davon ein Gewicht bekommt.
