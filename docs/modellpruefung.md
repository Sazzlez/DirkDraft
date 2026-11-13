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

### Endgültiger Lauf, Phase 2 (13.11.2026)

Daten: Artefakt `games` des Sammler-Laufs 43355006541 und die Dateien des Branches `data` vom
13.11.2026, 07:22 UTC: 158.087 gezählte Spiele, davon **145.741 Spielzeilen** (Patch 16.22, Platin,
EUW). 116.598 zum Anpassen, 29.143 zurückgehalten. Berichte: `build/measure-model.md`,
`build/measure-comp.md`. Nur ein Patch: Seit dem 07.11. gab es keinen Patchwechsel, alles „über drei
Patches“ ist erst mit 16.23 messbar.

| Modell | Log-Loss | Treffer |
|---|---|---|
| Münzwurf | 0,6931 | 50,5 % |
| A: Draft-Chance wie heute | 0,6912 | 54,0 % |
| A kalibriert | 0,6877 | 54,1 % |
| Lane-Stärke allein | 0,6882 | 53,9 % |
| B ohne Comp: Stärke, Duelle je Lane, Synergie und Gegner-Paare je Art | 0,6875 | 54,0 % |
| **B: dazu die Comp-Merkmale** | **0,6871** | **54,1 %** |
| C ohne Paare: Stärken und Comp gemeinsam gelernt | 0,6883 | 53,8 % |
| C: dazu Synergie und Counter aus gelernten Merkmalen | 0,6883 | 53,8 % |

Ob die Unterschiede mehr als Zufall sind, Spiel für Spiel verglichen (gepaart, 95 %):

| Vergleich | Gewinn im Log-Loss | Abstand |
|---|---|---|
| Münzwurf → Lane-Stärke allein | +0,0050 ± 0,0010 | 9,6 σ |
| Lane-Stärke allein → B ohne Comp | +0,0006 ± 0,0004 | 2,7 σ |
| B ohne Comp → B | +0,0004 ± 0,0004 | 2,0 σ |
| A wie heute → B | +0,0041 ± 0,0010 | 8,3 σ |
| A kalibriert → B | +0,0006 ± 0,0005 | 2,4 σ |
| C → B | +0,0012 ± 0,0008 | 2,8 σ |

**Ergebnis: Stufe B wird ausgeliefert** — die heutigen Teile mit gemessenen Gewichten und Paar-Arten.

- **Der Draft sagt in Platin 54 % der Sieger richtig voraus** und erklärt 0,9 % der Unsicherheit. Mehr
  ist mit den zehn Champions allein nicht zu holen; die 55–60 % aus dem Konzept erreicht Platin nicht.
- **Der größte Gewinn ist die Draft-Chance selbst.** Sie liegt um den Faktor 4,64 ± 0,29 zu nah an
  50 %, weil sie Stärke und Duelle über die Lanes mittelt; gemessen zählt die Summe (Lane-Stärke
  0,905 ± 0,068 je Einheit). Das allein bringt 0,0035 der 0,0041.
- **Paare sind jetzt messbar**, alle fünf Duelle und drei weitere Arten:

  | Teil | Gewicht (1 = zählt, wie die geglättete Rate sagt) |
  |---|---|
  | Duell Top | +2,11 ± 0,64 |
  | Duell Jungle | +1,27 ± 0,61 |
  | Duell Mid | +1,38 ± 0,53 |
  | Duell Bot | +0,59 ± 0,38 |
  | Duell Support | +0,83 ± 0,55 |
  | Synergie Bot-Duo (ADC + Support) | +0,41 ± 0,27 |
  | Synergie Jungle + Mid / Jungle + Top / Jungle + Botlane / Mid + Support | −0,06 ± 0,33 / +0,21 ± 0,36 / −0,01 ± 0,21 / +0,16 ± 0,31 |
  | Synergie übrige Paare | +0,09 ± 0,15 |
  | Gegner Botlane-Kreuz (ADC ↔ Support) | +0,55 ± 0,27 |
  | Gegner Jungle ↔ Laner | +0,17 ± 0,18 |
  | übrige Gegner-Paare | +0,20 ± 0,15 |

  Das Duell auf Top zählt doppelt so viel, wie die Glättung von heute durchlässt, das auf Bot gut
  halb so viel: dieselbe Reihenfolge wie die Glättung je Lane in Messung 14. Von den Synergien trägt
  nur das Botlane-Duo; Jungle + Mid, Jungle + Top und die übrigen Paare sind von null nicht zu
  unterscheiden. Die App zählt heute alle Paare gleich (0,6, gemittelt).
- **Comp, gemessen** (je Befund im Sieg, Punkte bei 50 %; `build/measure-comp.md`):

  | Befund | Spiele mit Unterschied | Wirkung | in B (log-odds) |
  |---|---:|---|---|
  | AP-Schaden fehlt | 3,4 % | −7,0 ± 1,4 Pkt | −0,263 ± 0,064 |
  | AD-Schaden fehlt | 0,9 % | −7,2 ± 2,8 Pkt | −0,287 ± 0,123 |
  | kein Engage | 0,7 % | −5,5 ± 3,0 Pkt | −0,198 ± 0,135 |
  | wenig Waveclear | 26,5 % | −1,2 ± 0,5 Pkt | −0,067 ± 0,030 |
  | kein Frontline | 17,0 % | −0,5 ± 0,6 Pkt | −0,039 ± 0,029 |
  | kein Peel / nur Melee | je 0,7 % | +0,4 ± 3,2 / −1,8 ± 3,2 Pkt | nicht messbar |
  | wenig CC | 5 Spiele | zu selten | — |
  | Waveclear-Champions, Ranged, Poke, Tank-Shred, Early, Late (je einer bzw. Punkt) | 49–78 % | alle unter ±0,35 Pkt | null |
  | gemischtes Timing | 21,4 % | +0,2 ± 0,6 Pkt | null |

  Fehlender AP- oder AD-Schaden kostet rund 7 Punkte, fast das Doppelte des Deckels der App
  (±4 Punkte). „Wenig Waveclear“ aus 2.1 ist bestätigt (−1,2 ± 0,5). Tank-Shred, Timing, Poke und die
  Zahl der Ranged- oder Waveclear-Champions verschieben den Sieg nicht messbar, und das recht genau.
  Die Archetyp-Tabelle (Plan gegen Plan) zeigt kein Schere-Stein-Papier: Kein Feld liegt außerhalb
  seines Fehlerbalkens, außer Teamfight gegen „offen“ (+0,6 ± 0,5).
- **Früh oder spät** bestätigt die Einordnung, taugt aber nicht als Vorhersage: Die später skalierende
  Seite liegt in Spielen unter 25 Minuten 3,9 ± 0,7 Punkte unter der Erwartung, ab 35 Minuten
  1,6 ± 0,7 darüber. Vor dem Spiel hebt sich das auf (Late-Game-Champions je einer: −0,1 ± 0,2).
- **Kalibrierung von B gut:** In jedem Bereich höchstens 1,8 Punkte zwischen vorhergesagt und
  tatsächlich (60 %+: 62,2 % vorhergesagt, 64,0 % gewonnen, 822 Spiele).
- **Modell C verliert gegen B** (2,8 σ). Die gelernten Merkmale für Synergie und Counter bewegen die
  Vorhersage gar nicht mehr (Streuung 0,0 Punkte); was C lernt, sind Stärken und Comp. Das Gitter der
  Regularisierung wurde nach unten erweitert, das Optimum liegt innen (L2 0,0003 auf den Stärken).
  Die Faktorisierung aus Konzept §4 bringt bei dieser Datenmenge nichts.
- **Der beste Pick bringt** gegenüber dem nach Pickrate gewichteten Durchschnitt **+3,4 Punkte** (Top
  +3,9, Jungle +3,8, Mid +3,4, Bot +3,0, Support +2,8), nachträglich mit allen zehn Champions bekannt.
- **Lane-Gold nach 10 Minuten** (78.329 Trainingsspiele mit Gold). Ein Vorsprung von 1.000 Gold ist
  auf Top 12,8 ± 0,4, Jungle 13,6 ± 0,5, Mid 14,8 ± 0,5, Bot 14,6 ± 0,5 und Support 5,7 ± 0,8 Punkte
  wert. Das Matchup bestimmt den Gold-Unterschied auf Top (±127 Gold, 14,6 σ), Mid (±97, 12,9 σ) und
  Support (±54, 10,4 σ); auf Bot kaum (±33, 1,8 σ), im Jungle nicht. Auf zurückgehaltenen Spielen
  sagen die Matchups über das Gold — ohne jedes angepasste Gewicht — so gut voraus wie die gezählten
  Duelle mit angepassten Gewichten (beide 0,6876 gegen 0,6882 für die Stärke allein); die Gewichte
  je Lane passen zu 1 (Top 0,91 ± 0,41, Mid 1,10 ± 0,53). Das Gold nach 10 Minuten ist damit ein
  zweiter, unabhängiger Beleg für Matchups auf Top und Mid, aber kein besserer als die Siege.

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

### Nachmessung Phase 2 (13.11.2026, 158.087 Spiele)

Dasselbe Werkzeug auf `meta.json` vom 13.11.2026 (Ausgabe in `build/phase2/priors.log`).

| Lane | Paare | Median Spiele | τ (Momente) | κ (Momente) | bestes κ, linear | große Abweichungen: kommen wieder | heute behalten |
|---|---:|---:|---:|---:|---:|---:|---:|
| Top | 1.988 | 40 | 1,86 Pkt | 719 | 600 | 7 % | 7 % |
| Jungle | 1.975 | 42 | 1,76 Pkt | 804 | 800 | 6 % | 7 % |
| Mid | 1.741 | 36 | 1,51 Pkt | 1.090 | 1.000 | 5 % | 8 % |
| Bot | 819 | 51 | 1,21 Pkt | 1.698 | 2.000 | 9 % | 9 % |
| Support | 1.402 | 42 | 0,86 Pkt | 3.348 | 5.000 | 3 % | 8 % |
| alle | 7.925 | 41 | 1,48 Pkt | 1.131 | 1.200 | 6 % | 8 % |

- **Duelle streuen jetzt messbar,** auf jeder Lane (am 09.11. standen Mid und Support noch bei null).
  Über alle Lanes bestätigt sich das Gewicht von heute: Momente 1.131, bestes 1.200, heute 1.000.
- **Die Lanes unterscheiden sich:** Auf Top streuen Duelle doppelt so weit wie auf Support (1,86
  gegen 0,86 Punkte). Zwei Verfahren, die sich nichts teilen, ordnen die Lanes gleich: die Momente
  hier und die Gewichte des Modellvergleichs (Messung 13: Top 2,11, Mid 1,38, Jungle 1,27, Support
  0,83, Bot 0,59 — ein Gewicht über 1 heißt, die Glättung 1.000 ist dort zu stark).
- **Große Abweichungen bleiben Zufall:** Von mindestens 8 Punkten bei rund 70 Spielen in einer Hälfte
  zeigt die andere 3–9 % wieder. Die Glättung, die starke Belege durchlässt, sagt weiter nicht besser
  voraus (höchstens 0,005 % im Log-Loss).
- **Duos:** Um die Erwartung aus beiden Lane-Stärken streuen sie nicht messbar (τ = 0, κ = ∞ über
  12.385 Paare). Das passt zum Modellvergleich: Nur das Botlane-Duo trägt.
- **Lane-Stärke:** κ nach Momenten 862, bestes 600, flach zwischen 400 und 1.000; bei im Median
  3.284 Spielen je Zeile ändert das Gewicht nichts mehr. 800 bleibt.

Folge für Phase 3: die Duell-Glättung je Lane statt eines Gewichts für alle (siehe Messung 21).

## 15. Mechanik-Counter (09.11.2026, vorläufig)

Werkzeug: `npm run measure:model`, Abschnitt „Mechanik-Regeln“ (`app/tools/measure/mechanics.ts`). Die Listen
sind Einschätzung, nur eindeutige Fälle, und wurden vom Nutzer geprüft, bevor darauf gemessen wurde. Sie
liegen bewusst im Messwerkzeug, nicht in `champion_traits.json`. Gewicht je Begegnung im Sieg über die
Lane-Stärke hinaus, auf den Trainingsspielen angepasst; 46.277 Spiele.

| Regel | Begegnungen | je Begegnung im Sieg |
|---|---:|---|
| Dash-Stopper (Poppy, Cassiopeia, Singed, Taliyah) gegen Dash-Champ | 1.703 | +2,1 ± 2,4 Pkt |
| Spell Shield (Morgana, Sivir, Nocturne, Malzahar) gegen Hook | 843 | +1,7 ± 3,5 Pkt |
| Hook gegen ADC ohne Dash | 5.146 | −0,2 ± 1,5 Pkt |
| Autoattack-Blocker (Jax, Shen, Teemo, Quinn, Rammus, Malphite, Nasus) gegen Autoattack-Champ | 2.641 | +2,3 ± 1,9 Pkt |

- **Autoattack-Blocker gegen Autoattack-Champ** liegt knapp über dem Fehlerbalken. Bei vier geprüften
  Regeln ist das nur ein Hinweis. Im Lane-Gold nach 10 Minuten zeigt sich nichts (−16 ± 132 Gold, 154
  Spiele mit Gold).
- **Hook gegen ADC ohne Dash** bringt nichts, und das schon recht genau (±1,5 Punkte).
- Dash-Stopper und Spell Shield zeigen in die erwartete Richtung, liegen aber im Fehlerbalken.
- Auf den zurückgehaltenen Spielen bringen die vier Regeln zusammen 0,0001 im Log-Loss. Keine Regel
  bekommt ein Gewicht; mit dem endgültigen Lauf nachmessen.

### Nachmessung Phase 2 (13.11.2026, 145.741 Spielzeilen)

| Regel | Begegnungen | je Begegnung im Sieg | Gold nach 10 Min. |
|---|---:|---|---|
| Dash-Stopper gegen Dash-Champ | 5.475 | +1,1 ± 1,3 Pkt | — |
| Spell Shield gegen Hook | 2.809 | +3,6 ± 1,9 Pkt | — |
| Hook gegen ADC ohne Dash | 16.527 | +0,9 ± 0,8 Pkt | — |
| Autoattack-Blocker gegen Autoattack-Champ | 8.495 | +1,9 ± 1,1 Pkt | +61 ± 23 Gold (5.641 Spiele) |

- **Spell Shield gegen Hook** (3,7 σ) und **Autoattack-Blocker** (3,4 σ, dazu im Gold) sind jetzt klar
  zu sehen, Hook gegen ADC ohne Dash knapp (2,0 σ, unter einem Punkt).
- Für die Vorhersage bringen die vier Regeln trotzdem nichts: 0,6881 gegen 0,6882 im Log-Loss auf den
  zurückgehaltenen Spielen, die Treffer sinken von 53,9 auf 53,7 %. Was sie beschreiben, steckt zum
  großen Teil schon in den gezählten Duellen und Gegner-Paaren.
- **Keine Regel bekommt ein Gewicht.** Die Listen bleiben im Messwerkzeug.

## 16. Build-Wahl: Zufallstreffer statt „beste Winrate“ (11.11.2026)

Daten: `builds.json` und `situational.json` vom 11.11.2026 (108.362 Spiele, Patch 16.22), 308
Champion-Lanes ab 300 Spielen. Rechnung in `app/src/core/riotBuilds.ts`, Tests in
`app/tests/model/ownData.test.ts`.

**Befund.** Die Regel aus Messung 11 (Rate je Option zur Rate ihres Slots geglättet, beste zuerst)
wählte dünne Optionen, sobald κ endlich wurde:

| | Schuhe | Kern | Item 4 |
|---|---:|---:|---:|
| Plan ≠ meistgespielte Option (von 308) | 190 | 232 | 227 |
| gewählte Option unter 5 % der Käufe des Slots | 54 | 11 | 21 |
| Anteil der gewählten Option an den Spielen des Slots, Median | 25 % | 14 % | 16 % |

Jinx: Boots of Swiftness (35 Spiele, 60 %) statt Berserker's Greaves (11.994, 51 %). Xin Zhao: Ionian
Boots aus 6 von 2.693. Mejai's stand bei 55 Champion-Lanes im Plan, bei Ahri als viertes Item.

**Ursache.** Die meistgespielte Option *ist* die Rate des Slots, zu der geglättet wird. Jede Option
knapp darüber stand deshalb vor ihr, egal wie wenige Spiele sie hatte; die Glättung zieht sie nur
näher heran, nie darunter.

**Neue Regel.**
1. Die meistgespielte Option führt.
2. Eine andere übernimmt nur, wenn sie klar besser ist: zwei Standardfehler auf den geglätteten
   Raten (`LEAD_SIGMA`), also rund 98 % Wahrscheinlichkeit, dass sie wirklich besser ist. Drei
   Standardfehler auf den rohen Raten reichen nicht: Bei rund 20.000 Optionen nimmt der Zufall diese
   Hürde etwa 27-mal, und es kamen genau solche Fälle durch (Cassiopeia: Blitz und Barriere aus 13
   Spielen mit 12 Siegen).
3. Schuhe, Kern und Items 4–6 werden im laufenden Spiel gekauft; wer vorn liegt, kauft anders und
   gewinnt ohnehin öfter. Dort muss der Vorsprung auch im selben Spielstand halten (ausgeglichen,
   vorn, hinten; aus `situational.json`), gezählt an den Items, in denen sich zwei Optionen
   unterscheiden. Ohne diese Zahlen bleibt die meistgespielte Option. Zwei Kerne aus denselben Items
   in anderer Reihenfolge lassen sich so nicht vergleichen und bleiben beim meistgespielten.
4. Ein Item, das vor allem aus der Führung gekauft wird, ist nie „klar besser“. Über alle Champions
   wird Mejai's 4,6-mal so oft vorn wie hinten gekauft, das nächste Item (Endless Hunger) 1,5-mal;
   die Grenze liegt bei 2 (`WIN_MORE_RATIO`). Der Spielstand zählt das Gold des Teams und trennt
   deshalb nicht, wer im Team vorn liegt: Mejai's gewinnt bei Twisted Fate auch innerhalb von „vorn“
   89 % gegen 71 % mit Zhonya's.

**Schätzung von κ.** Die Momentschätzung zog je Option das Rauschen μ(1−μ)/g ab. Die Rate des Slots
ist aber aus denselben Spielen gezählt, das Rauschen um sie ist μ(1−μ)·(1/g − 1/G). In einer
Simulation ohne jeden Unterschied zwischen den Optionen (gleiche Spielzahlen wie in der Datei, 20
Ziehungen) ergab die alte Formel eine Streuung von −1,1² bis −1,7² Punkten statt 0 (Item 6, der
dünnste Slot: −3,6²), die neue höchstens +0,5² (Item 6: +0,9²). Folge auf der Datei:

| Slot | κ alt | κ neu | wahre Streuung (neu) |
|---|---:|---:|---:|
| Runen | ∞ | 2.051 | 1,1 Pkt |
| Zauber | ∞ | 5.862 | 0,7 Pkt |
| Start | ∞ | 3.724 | 0,8 Pkt |
| Schuhe | 14.863 | 1.714 | 1,2 Pkt |
| Kern | 828 | 459 | 2,3 Pkt |
| Item 4 | 512 | 314 | 2,8 Pkt |
| Item 5 | ∞ | 481 | 2,3 Pkt |
| Item 6 | ∞ | 392 | 2,5 Pkt |
| Support-Item | 59.670 | 2.016 | 1,1 Pkt |
| Skill-Reihenfolge | ∞ | 2.098 | 1,1 Pkt |
| Skill-Priorität | 3.741 | 1.330 | 1,4 Pkt |

„Kein Slot trägt einen messbaren Unterschied“ (Messung 11) war zum Teil dieses Artefakt.

**Ergebnis mit der neuen Regel** (dieselben 308 Champion-Lanes): Plan ≠ meistgespielt bei Runen 0,
Zaubern 0, Start 1, Schuhen 2, Kern 2, Items 4–6 2, Support-Item 1, Skill-Reihenfolge 1,
Skill-Priorität 4. Jede dieser Abweichungen stützt sich auf mindestens 270 Spiele (Katarina: Kraken
Slayer, Terminus, Death's Dance aus 270 mit 63 % gegen 1.086 mit 52 %; Kai'Sa: Gluttonous Greaves
2.926 mit 53 % gegen Berserker's 19.725 mit 49 %; Blitzcrank: Solstice Sleigh 2.514 mit 54 % gegen
3.412 mit 50 %). Keine gewählten Schuhe unter 5 % der Käufe; der gewählte Kern hat im Median 41 % der
Spiele des Slots. Mejai's steht noch bei 8 Champion-Lanes, dort ist es die meistgespielte freie
Option.

**Offen.** Innerhalb eines Slots gewinnt die 1.000 Gold teurere Option seltener: Kern −3,0, Item 4
−2,8, Schuhe −3,9 Punkte je 1.000 Gold. Das ist so groß wie die gemessene Streuung dieser Slots und
spricht für einen Einfluss des Zeitpunkts (wer drei billige Items fertig hat, ist früher fertig).
Der Vergleich im selben Spielstand fängt einen Teil ab; den Preis als eigene Größe zu messen steht aus.

### Nachmessung Phase 2 (13.11.2026, 158.087 Spiele)

Werkzeug: `npm run measure:builds -- <builds.json> <situational.json> <meta.json> <item.json>
--before-builds … --before-situational …` (`app/tools/measure/builds.ts`; `item.json` ist die von Data
Dragon, für Namen und Preise). Bericht in `build/measure-builds.md`. Vergleichsstand: die Dateien vom
11.11.2026, 16:43 UTC (114.585 Spiele). 337 Champion-Lanes ab 300 Spielen.

**κ je Slot** ist stabil; die Regel braucht keine Anpassung:

| Slot | κ bei 114.585 | κ bei 158.087 | wahre Streuung | Plan ≠ meistgespielt |
|---|---:|---:|---:|---:|
| Runen | 2.080 | 2.538 | 1,0 Pkt | 0 |
| Zauber | 5.453 | 5.397 | 0,7 Pkt | 1 |
| Start | 3.482 | 3.727 | 0,8 Pkt | 0 |
| Schuhe | 1.781 | 1.873 | 1,2 Pkt | 3 |
| Kern | 430 | 464 | 2,3 Pkt | 4 |
| Item 4 | 318 | 339 | 2,7 Pkt | 3 |
| Item 5 | 483 | 661 | 1,9 Pkt | 0 |
| Item 6 | 392 | 487 | 2,3 Pkt | 0 |
| Support-Item | 1.906 | 1.791 | 1,2 Pkt | 1 |
| Skill-Reihenfolge | 2.244 | 2.566 | 1,0 Pkt | 1 |
| Skill-Priorität | 1.313 | 1.334 | 1,4 Pkt | 7 |

20 Abweichungen vom Meistgespielten (vorher 13 auf 308 Champion-Lanes); die dünnste stützt sich auf
397 Spiele (Katarina Mid: Kraken Slayer, Terminus, Death's Dance, 62 % gegen 52 % aus 1.569).

**Halten die Abweichungen auf neuen Spielen?** Die Spielzeilen tragen keine Builds, damit geht es
nicht. Stattdessen: Die Regel wählt auf dem Stand vom 11.11. (14 Abweichungen auf 312 Champion-Lanes),
geprüft wird auf den 43.502 Spielen, die seither dazukamen (Zählung jetzt minus Zählung damals, je
Option). Diese Spiele hat die Wahl nie gesehen.

- 12 von 14 liegen auf den neuen Spielen vorn.
- Zusammen, nach Genauigkeit gewichtet: **+2,8 ± 1,3 Punkte** auf den neuen Spielen. Roh waren es auf
  dem alten Stand +5,6: Etwa die Hälfte des Vorsprungs bleibt, wie die Glättung es erwarten lässt.
- Vier der 14 stehen heute nicht mehr im Plan (Shaco Runen +1,3, Kayn Start −0,4, Kai'Sa Schuhe +2,4,
  eine Kern-Wahl −0,1 auf den neuen Spielen): Die Regel nimmt sie selbst zurück, sobald der Vorsprung
  schrumpft.

Die Abweichungen halten. Die Regel bleibt.

**Der Preis als eigene Größe.** Winrate einer Option gegen ihren Preis innerhalb des Slots, in Punkten
je 1.000 Gold (95 %):

| | alle Käufe | im selben Spielstand |
|---|---:|---:|
| Schuhe (builds.json) | −3,2 ± 1,1 | |
| Kern, ganze Option (builds.json) | −3,0 ± 0,5 | |
| Item 4 (builds.json) | −2,7 ± 0,5 | |
| Schuhe, einzeln (situational.json) | −3,2 ± 1,1 | +1,7 ± 1,0 |
| Kern, einzelne Items 1–3 | −0,8 ± 0,2 | +0,1 ± 0,2 |
| Items 4–6, einzeln | −1,9 ± 0,4 | −0,6 ± 0,3 |

Der Preis ist kein eigener Effekt: Zwischen Käufen im selben Spielstand verschwindet er (Kern), dreht
(Schuhe) oder schrumpft auf ein Fünftel der Streuung des Slots (Items 4–6). Wer hinten liegt, kauft
teurer und verliert ohnehin öfter. Genau diesen Vergleich verlangt Regel 3 schon für Schuhe, Kern und
Items 4–6. **Nichts geändert.**

## 17. Situative Items: Klasse gegen Champion (11.11.2026)

Rechnung in `app/src/core/situational.ts`, Tests in `app/tests/model/situational.test.ts`.

**Befund.** Hat ein Champion in einer Lage wenige Spiele, lehnt sich sein Kaufanteil dort an seine
Klasse an (50 Pseudospiele). Der Anteil „sonst“ kam dagegen fast nur vom Champion selbst, weil dort
Tausende Spiele gegen dieselben 50 stehen. Verglichen wurde also die Klasse in der Lage mit dem
Champion außerhalb. Talon bekam Rabadon's Deathcap („125-mal so oft“), weil andere Assassinen es
gegen Magieresistenz kaufen und er selbst nie; Irelia Zhonya's, Kog'Maw Infinity Edge. In manchen
Fällen kauft sogar die Klasse das Item in der Lage seltener als sonst (Annie: Rylai's 8,4 % gegen
12,7 %).

Gezählt je Champion-Lane und Lage, bis zu drei Vorschläge:

| | ab 100 Spielen | ab 2.000 Spielen |
|---|---:|---:|
| Vorschläge vorher | 1.970 | 666 |
| davon vom Champion in der Lage nie gekauft | 864 | 25 |
| Vorschläge nachher | 899 | 535 |
| davon vom Champion in der Lage nie gekauft | 1 | 0 |

**Neue Regel.** Die Klasse zählt auf beiden Seiten mit demselben Gewicht, nämlich dem, das die
Spiele des Champions in der Lage ihr lassen. Ohne eigene Spiele in der Lage steht dann das Verhältnis
der Klasse da, mit vielen das eigene. Und ein Item braucht mindestens fünf eigene Käufe des Champions
auf dieser Lane in diesem Slot (`MINIMUM_OWN_BUYS`): Die Klasse weiß, wann ein Item gekauft wird,
nicht, ob der Champion es überhaupt baut. Der eine verbleibende Fall ist Katarina Top ohne ein Spiel
in der Lage, mit 13 eigenen Käufen von Zhonya's.

## 18. Offene Counter bei dieser Datenmenge (11.11.2026)

Die Hinweise aus 2.1 („N offene Counter“, „sicherer Blind Pick“, Reihenfolge im Team-Plan; „Früh
dran — Pick-Reihenfolge tauschen?“ ist seit 11.11.2026 auf Nutzerwunsch entfernt) hängen an `openCounters`: ein freier Champion zählt als Counter ab
+0,08 Log-Odds (2 Punkte) über der Erwartung des Paars, nach Glättung.

**Befund.** Auf `meta.json` vom 11.11.2026 bewegt ein Lane-Matchup nach Glättung im Median 0,2
Punkte; 95 % liegen unter 0,9 (Bot 1,1), der größte Wert ist 3,1 (Kayle gegen Garen). Von 329
Champion-Lanes haben 4 einen offenen Counter und keine zwei. Die Hinweise erscheinen deshalb nicht.

**Lässt sich die Schwelle senken?** Geprüft: ein Counter nach Beweislage statt nach geglätteter
Größe (gemessene Rate über der Erwartung, in Standardfehlern), über alle 12.862 Duell-Richtungen:

| Schwelle | markiert | durch Zufall erwartet |
|---|---:|---:|
| 2 Standardfehler | 312 | 293 |
| 2,5 | 91 | 80 |
| 3 | 16 | 17 |

Auf jeder Stufe sind es so viele, wie der Zufall allein liefert. Über die Lane-Stärke hinaus trägt
die Datei also noch keine erkennbaren Counter, wie schon Messung 14 zeigt. Eine niedrigere Schwelle
würde Rauschen als Counter ausgeben. **Nichts geändert.** Die Hinweise melden sich von selbst, sobald
Paare genug Spiele haben (drei Patches zusammen, Phase 2) oder Messung 14 einer Lane weniger Glättung
zugesteht.

### Nachmessung Phase 2 (13.11.2026, 158.087 Spiele)

Werkzeug: `npm run measure:counters -- <meta.json> --before <meta-alt.json>`
(`app/tools/measure/counters.ts`), Bericht in `build/measure-counters.md`. Nur Patch 16.22: Der Sammler
hat seit dem 07.11. keinen Patchwechsel gesehen, die Messung über drei Patches ist noch nicht möglich.

**Was die Regel der App findet.** Ein Lane-Matchup bewegt nach Glättung weiter im Median 0,2 Punkte,
95 % liegen unter 0,9 (Bot 1,1), der größte Wert ist 2,9 (Twitch gegen Jinx, 58,0 % aus 641 Spielen).
Von 328 Champion-Lanes haben 14 einen offenen Counter (vorher 4), keine zwei.

**Zufallsvergleich**, 15.850 Duell-Richtungen, Zufallserwartung exakt aus der Binomialverteilung:

| Schwelle | markiert | durch Zufall erwartet | nur Paare ab 200 Spielen: markiert | erwartet |
|---|---:|---:|---:|---:|
| 2 Standardfehler | 432 | 355 ± 37 | 76 | 43 ± 13 |
| 2,5 | 126 | 92 ± 19 | 28 | 12 ± 7 |
| 3 | 24 | 18 ± 8 | 9 | 3 ± 3 |

Zum ersten Mal liegen die markierten über dem Zufall — aber nur knapp: Bei 2 Standardfehlern sind
rund vier von fünf Markierungen weiter Zufallstreffer, bei gut gezählten Paaren etwa die Hälfte.

**Auf neuen Spielen** (markiert auf dem Stand vom 11.11., geprüft auf den 43.502 Spielen seither):

| markiert ab | Richtungen | Vorsprung damals | auf neuen Spielen | davon wieder | die Glättung (1.000) behält |
|---|---:|---:|---:|---:|---:|
| 2 Standardfehlern | 354 | +9,9 Pkt | +2,3 ± 0,9 Pkt | 23 % | +1,4 Pkt |
| 2,5 | 91 | +12,0 Pkt | +2,7 ± 1,7 Pkt | 22 % | +1,6 Pkt |
| 3 | 11 | +12,6 Pkt | +4,0 ± 4,3 Pkt | 32 % | +2,3 Pkt |

Counter sind jetzt nachweisbar: Rund ein Viertel des gemessenen Vorsprungs kommt auf neuen Spielen
wieder. Die Glättung behält etwas weniger (1,4 gegen 2,3 ± 0,9), der Unterschied liegt im Fehlerbalken.

**Folge.** „Deutlich über der Zufallserwartung“ ist das noch nicht: Eine niedrigere Schwelle würde
vier Zufallstreffer je echtem Counter benennen. `COUNTER_MINIMUM_EDGE` bleibt bei 0,08. Die Glättung
je Lane misst Messung 14 neu (unten); mit ihr verschiebt sich, welche Paare die Schwelle erreichen. Die
Hinweise beginnen von selbst zu erscheinen (14 Champion-Lanes) und werden mit dem nächsten Patch mehr,
wenn Matchups über drei Patches zählen.

## 19. Kaufzeitpunkt der Schuhe (11.11.2026, noch ohne Zahlen)

Die Einkaufsliste im Spiel (`core/game.ts`, `basketOf`) braucht zwei Angaben, die `builds.json` bisher
nicht hatte: an welcher Stelle der Kaufreihenfolge das Paar Schuhe steht und ab wann die einfachen
Stiefel dazukommen. Der Sammler zählt beides seit dem 11.11.2026 (`bootsAfter`, `bootsWorth`,
Format in `docs/redesign/daten.md`).

Bisher nur an der einen Partie der Tests nachgezählt (`Fixtures/Riot`, zehn Spieler, neun mit Schuhen):

- Paar nach einem fertigen Item: 8 von 9, nach zwei: 1.
- Erste Schuhe beim Stand „nur Startitems“ (Stufe 0): 4 von 9; zwei Spieler erst ab 4.000 Gold Itemwert.
- Im selben Shop-Besuch kamen die Stiefel nach den Teilen: Blasting Wand, zwei Sekunden später
  Stiefel; Lost Chapter, dann Stiefel. Daher die Regel für Dateien ohne Zählung: Stiefel mit dem
  Gold, das nach den Teilen bleibt.

Eine Partie ist kein Beleg. **Nachzutragen, sobald die Zählung einige Tage läuft:** Verteilung von
`bootsAfter` und `bootsWorth` je Lane, Anteil der Champion-Lanes mit einer anderen Stelle als „nach
dem ersten Item“, und ob die Regel „Stiefel zuerst, sobald der Itemwert die Stufe der Hälfte
erreicht“ zu dem passt, was Spieler mit knappem Gold tatsächlich kaufen (Teil oder Stiefel).

### Nachmessung Phase 2 (13.11.2026)

Der Sammler zählt seit dem Abend des 11.11.: 352.978 Spieler-Spiele mit Schuhen, 423 Champion-Lanes ab
30. Werkzeug: `npm run measure:builds` (wie Messung 16), Abschnitt „Kaufzeitpunkt der Schuhe“.

**Fertige Items vor dem Paar (`bootsAfter`).**

| Lane | 0 | 1 | 2 | 3+ | Champion-Lanes | meistgezählt 0 | meistgezählt 1 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Top | 35 % | 54 % | 9 % | 1 % | 111 | 13 | 98 |
| Jungle | 9 % | 65 % | 18 % | 8 % | 82 | 3 | 79 |
| Mid | 23 % | 72 % | 5 % | 0 % | 96 | 8 | 88 |
| Bot | 20 % | 67 % | 11 % | 3 % | 54 | 4 | 50 |
| Support | 42 % | 52 % | 5 % | 1 % | 80 | 19 | 61 |
| alle | 26 % | 62 % | 10 % | 3 % | 423 | 47 | 376 |

„Nach dem ersten Item“ stimmt für 376 von 423 Champion-Lanes (89 %). Bei 47 (11 %) kommt das Paar
zuerst, vor allem bei Supports (19 von 80) und auf Top (13 von 111); nach zwei oder drei Items bei
keiner. Die Annahme für Dateien ohne Zählung (Paar nach dem ersten Item) ist damit die richtige.

**Itemwert beim Kauf der ersten Schuhe (`bootsWorth`).**

| Lane | nur Startitems | bis 1.000 | bis 2.000 | bis 3.000 | darüber | Viertel / Hälfte / drei Viertel der Spieler |
|---|---:|---:|---:|---:|---:|---|
| Top | 31 % | 22 % | 18 % | 16 % | 13 % | 0 / 1.000 / 2.500 |
| Jungle | 21 % | 11 % | 17 % | 23 % | 28 % | 1.000 / 2.500 / 3.500 |
| Mid | 25 % | 21 % | 24 % | 22 % | 8 % | 500 / 1.500 / 2.500 |
| Bot | 19 % | 17 % | 17 % | 25 % | 23 % | 1.000 / 2.000 / 3.000 |
| Support | 51 % | 22 % | 16 % | 8 % | 2 % | 0 / 0 / 1.500 |
| alle | 29 % | 18 % | 18 % | 19 % | 15 % | 0 / 1.500 / 3.000 |

- Die Lanes unterscheiden sich klar: Supports kaufen zur Hälfte beim ersten Rückruf, Jungler zur
  Hälfte erst ab 2.500 Gold Itemwert.
- **Eine scharfe Gewohnheit gibt es nicht.** Bei 347 von 423 Champion-Lanes liegen zwischen dem Viertel
  und den drei Vierteln der Spieler mindestens 1.500 Gold Itemwert. Die „Stufe der Hälfte“ ist die
  Mitte einer breiten Verteilung, kein Zeitpunkt, an dem die meisten kaufen. 29 % kaufen die ersten
  Schuhe schon mit nichts als den Startitems.
- Die Hälfte liegt bei 59 Champion-Lanes bei 0 (die App setzt dort Stiefel auf die erste
  Einkaufsliste), bei 125 bei 500–1.000, bei 169 bei 1.500–2.000, bei 70 ab 2.500.

**Was offen bleibt.** Ob Spieler mit knappem Gold das Teil oder die Stiefel nehmen, zeigen diese
Zählungen nicht: Sie sagen, bei welchem Itemwert gekauft wurde, nicht, was im selben Shop-Besuch sonst
möglich war. Dafür müsste der Sammler je Besuch mit dem ersten Stiefelkauf mitzählen, ob das Gold auch
für das größte Teil des nächsten Items gereicht hätte und was davon gekauft wurde. Die Frage des
Nutzers (Stiefel oder größtes Teil zuerst, `basketOf`, `bootsDue`) ist damit nicht entschieden;
**nichts geändert.**

### Nachtrag 13.11.2026: der Sammler zählt Shop-Besuche (Entscheidung des Nutzers)

Der Nutzer hat entschieden, die Frage den Daten zu überlassen. Der Sammler zählt deshalb jetzt je
Champion und Lane jeden Shop-Besuch nach dem Start, bei dem der Spieler noch keine Stiefel hatte und
mindestens den Preis der Stiefel ausgegeben hat — Stiefel waren also eine Wahl:

- `bootsShop`: alle solchen Besuche, nach Itemwert beim Betreten des Shops (Stufen wie `bootsWorth`)
  und nach ausgegebenem Gold: `300` (bis 649: Stiefel oder ein kleines Teil, nicht beides), `650`
  (bis 1.299), `1300` (und mehr). Schlüssel etwa `1000/650`.
- `bootsBought`: davon die Besuche, in denen Stiefel gekauft wurden.

Ein Besuch ist eine Folge von Käufen mit höchstens 30 Sekunden Abstand. Was er gekostet hat, ist, was
das Inventar an Ladenpreis gewonnen hat (ein fertiges Item zählt nur den Aufpreis auf seine Teile).
Gezählt wird bis einschließlich des Besuchs mit den ersten Stiefeln. Auswertung: `npm run
measure:builds`, Abschnitt „Was Spieler ohne Stiefel mit ihrem Gold kaufen“ — die Spalte „300–649
Gold“ ist die Antwort auf „Teil oder Stiefel bei knappem Gold“, und die Itemwert-Stufe, ab der mehr als
die Hälfte Stiefel kauft, ist die, ab der „Stiefel zuerst“ dem entspricht, was Spieler tun. Die
Zählung beginnt mit dem ersten Lauf des Sammlers auf diesem Stand und braucht einige Tage.

**Ein Zählfehler, dabei gefunden und behoben: Magisches Schuhwerk.** Wer die Rune nimmt, bekommt
Stiefel geschenkt und kann vorher keine kaufen. Solche Spieler zählten bisher mit: als Käufer „erst
ab 4.000 Gold Itemwert“ (wenn sie das geschenkte Paar aufwerten) oder gar nicht. Die Rune steht in
18,4 % der gezählten Runenseiten — **Jungle 39,9 %, Bot 30,5 %**, Top 7,8 %, Mid 7,0 %, Support 5,4 %.
Die Tabellen oben sind deshalb für Jungle und Bot verzerrt: Dass Jungler „zur Hälfte erst ab 2.500
Gold“ kaufen, liegt zu einem guten Teil an der Rune, nicht an der Gewohnheit. Seit dem 13.11.2026
lassen `bootsAfter`, `bootsWorth` und die neue Zählung Spieler mit der Rune aus
(`ParticipantBuild.FreeBoots`). Der Bestand mischt bis zum nächsten Patch alte und neue Zählung; die
Verteilungen sind danach neu zu lesen. Für die App heißt das: Bei Champions, deren Spieler die Rune
oft nehmen, kommen die Stiefel bisher zu spät auf die Einkaufsliste.

## 20. Situative Items im Build (11.11.2026, Nutzerwunsch)

Bis 2.0 galt: situative Items stehen neben dem Build, nie an seiner Stelle. Seit dem 11.11.2026
übernimmt ein situatives Item von selbst den Platz eines Plan-Items, markiert als situativ
(`core/situational.ts`: `rateSuggestions`, `situationalBuild`, `SwapTracker`).

**Regel.** Ein Vorschlag steht gegen das Plan-Item seines Platzes (Schuhe, Kern, Items 4–6), das
Spieler des Champions in dieser Lage am seltensten kaufen, unter den noch offenen. Er übernimmt den
Platz nur, wenn er dort selbst klar öfter gekauft wird (zwei Standardfehler). „1,6-mal so oft wie
sonst“ bei 6 % des Platzes ist eine Alternative; Mercury's Treads mit 55 % gegen Plated Steelcaps
mit 39 % ist das, was Spieler in der Lage tun.

**Zwei Sicherungen**, beide wegen desselben Effekts: Lagen aus den Items der Gegner (viel Rüstung,
viel Magieresistenz, Heilung) gibt es erst spät im Spiel, früh gekaufte Items haben dort kaum Käufe.

- Ersetzt wird nur ein Plan-Item mit eigenem Anteil in der Lage (mindestens 5 %). Ohne das verlor
  Garens erstes Item (Stridebreaker, 0 % „gegen Magieresistenz“) seinen Platz an Infinity Edge.
- Das erste Item wird wegen einer Lage aus Gegner-Items nie ersetzt. Ohne das verlor Vaynes Blade of
  the Ruined King (8 % der Kern-Käufe, sobald die Gegner sich heilen) seinen Platz an Infinity Edge.

**Wie oft.** 171 Champion-Lanes ab 2.000 Spielen, jede Lage einzeln, alle Plan-Items offen (also die
Obergrenze): 545 Vorschläge, 72 übernehmen einen Platz.

| Platz | Vorschläge | übernommen |
|---|---:|---:|
| Schuhe | 180 | 45 |
| Kern | 242 | 18 |
| Items 4–6 | 123 | 9 |

Ohne die Sicherungen und ohne die zwei Standardfehler waren es 227. Beispiele, die bleiben: Sion,
Ornn und Malphite gegen viel magischen Schaden Hollow Radiance statt Thornmail oder Frozen Heart;
Viego gegen viel Rüstung Lord Dominik's statt The Collector; Tahm Kench gegen Heilung Thornmail statt
Riftmaker.

**Schwachstelle.** Die Lagen aus Gegner-Items messen zum Teil nur „später im Spiel“: Wukongs Death's
Dance übernimmt bei Rüstung, bei Magieresistenz und bei Heilung denselben Platz. Bei Champions mit
zwei Bauweisen mischt es sich (Shaco: Rabadon's statt Guardian Angel). Die Lagen aus der Aufstellung
(viel AD, viel AP, Tanks, Kontrolle) sind davon frei. Wenn das stört: Übernahme auf diese Lagen und
die Schuhe begrenzen (`ITEM_SITUATIONS`).

### Nachmessung Phase 2 (13.11.2026, 158.087 Spiele)

Werkzeug: `npm run measure:builds` (wie Messung 16), Abschnitt „Situative Items im Build“. 198
Champion-Lanes ab 2.000 Spielen, jede Lage einzeln, alle Plan-Items offen: 614 Vorschläge, 88
übernehmen einen Platz (Schuhe 51, Kern 22, Items 4–6 15). 71 davon kommen aus Lagen der Aufstellung
(viel AP 55, viel AD 15, Tanks 1), 17 aus Lagen der Gegner-Items (Rüstung 8, Magieresistenz 7,
Heilung 2).

**Lage oder nur „später im Spiel“?** Die Spielzeilen tragen keine Käufe, und die Spielzeit eines Kaufs
steht in keiner Datei; der Vergleich „zur selben Spielzeit ohne die Lage“ geht nur mittelbar. Zwei
Proben, beide aus `situational.json`:

1. *Heben die drei Lagen dieselben Items?* Über 3.317 Items (Kern und Items 4–6, ab 100 Käufen des
   Champions): Wie viel öfter ein Item in der einen Lage gekauft wird, hängt eng damit zusammen, wie
   viel öfter in der anderen — Rüstung/Magieresistenz **+0,88**, Rüstung/Heilung **+0,92**,
   Magieresistenz/Heilung **+0,87**. Zum Vergleich die Aufstellung: viel AD gegen viel AP **−0,95**
   (wie es sein muss), viel AD gegen Rüstung aus Items −0,01. Rüstung und Magieresistenz verlangen
   entgegengesetzte Items; dass beide Lagen dieselben heben, heißt: Sie messen vor allem dasselbe
   Dritte, die Spielzeit.
2. *Die anderen beiden Lagen als Vergleich zur selben Spielzeit.* Ein Item, das zur Lage gehört,
   müsste in ihr deutlich öfter gekauft werden als in den beiden anderen, die genauso spät entstehen
   (mindestens 1,5-mal deren Mittel, die Grenze für einen Vorschlag). Das erfüllen **5 der 17**
   Übernahmen; bei 5 von 11 Items übernimmt dasselbe Item in zwei oder drei dieser Lagen (Wukong:
   Death's Dance bei Rüstung, Magieresistenz und Heilung; Viego: Lord Dominik's bei Rüstung und bei
   Magieresistenz). Unter den fünf, die bestehen, sind solche, die zur Lage nicht passen (Shaco:
   Rabadon's gegen Magieresistenz, Nilah: Bloodthirster gegen Magieresistenz).

**Folge.** Die Lagen aus Gegner-Items tragen keine Übernahme. Seit dem 13.11.2026 übernimmt ein
situatives Item einen Platz im Build nur noch aus einer Lage der Aufstellung (viel AD, viel AP, Tanks,
harte Kontrolle); aus den Lagen der Gegner-Items bleibt es die Alternative neben dem Build, wie bis
2.0 (`rateSuggestions`, `ITEM_SITUATIONS`). Die Regel des Nutzers vom 11.11. (situative Items
übernehmen von selbst) gilt weiter, für die 71 von 88 Fälle, in denen die Lage gemessen ist. Für die
genaue Probe müsste der Sammler die Käufe je Lage zusätzlich nach Spielminute zählen.

## 21. Das gemessene Draft-Modell in der App (13.11.2026, Phase 3)

Stufe B aus Messung 13 steht in der App: `app/src/core/model/draftModel.ts`, Tests in
`app/tests/model/draftModel.test.ts`. Es gilt nur für eigene Dateien (`source: "riot"`); OP.GG-Dateien
rechnen wie 1.5, die Golden-Referenz ist unverändert.

**Die Rechnung.** Die Chance des eigenen Teams ist die Summe (in log-odds) aus:

| Teil | Gewicht | gemessen |
|---|---|---|
| Lane-Stärke jedes Champions | 1 | 0,913 ± 0,066 im gemeinsamen Fit; mit den festen Gewichten ist 1 kalibriert (unten) |
| Duell auf jeder Lane | 1, geglättet je Lane | siehe „Glättung je Lane“ |
| Botlane-Duo (ADC + Support), beide Seiten | 0,41 | 0,415 ± 0,273 |
| Botlane-Kreuz (ADC gegen Support) | 0,55 | 0,547 ± 0,271 |
| übrige Gegner-Paare ohne Jungler | 0,20 | 0,195 ± 0,148 |
| AP-Schaden fehlt | −0,26 | −0,260 ± 0,064 |
| AD-Schaden fehlt | −0,295 | −0,295 ± 0,123 |
| kein Engage | −0,22 | −0,221 ± 0,134 |
| wenig Waveclear | −0,05 | −0,050 ± 0,024 |
| kein Frontline | −0,035 | −0,035 ± 0,028 |

Die Gewichte stammen aus „B schlank“: B, neu angepasst nur mit den Teilen, deren Gewicht im vollen
Fit mindestens zwei Standardfehler von null liegt (auf den Trainingsspielen gewählt). B schlank sagt
so gut voraus wie B (0,6872 gegen 0,6871, Unterschied 0,0000 ± 0,0001). **Kein Gewicht bekommen:**
alle Duos außer dem der Botlane, Jungler gegen Laner (+0,17 ± 0,18, knapp unter zwei
Standardfehlern), die Comp-Merkmale ohne Wirkung (Timing, Tank-Shred, Zahl der Ranged-, Waveclear-,
Early- und Late-Champions) und Poke: Dessen Gewicht steht im Fit bei −0,009 ± 0,007 je Punkt, in der
Comp-Messung allein bei −0,02 ± 0,14 Punkten — ein Treffer unter 29 geprüften Teilen, kein Befund.

**Abnahme.** `npm run measure:model` rechnet die Draft-Chance mit genau diesem Code (feste Gewichte,
nichts angepasst) auf den 29.143 zurückgehaltenen Spielen:

| Vergleich | Gewinn im Log-Loss | Abstand |
|---|---|---|
| B → App | +0,0000 ± 0,0002 | 0,2 σ |
| Draft-Chance wie in 2.0 → App | +0,0041 ± 0,0010 | 8,1 σ |
| Draft-Chance wie in 2.0, kalibriert → App | +0,0005 ± 0,0005 | 2,3 σ |

Kalibrierung: Steigung 1,04 ± 0,11, Achsenabschnitt +0,02 ± 0,02; in jedem Bereich höchstens 1,7
Punkte zwischen vorhergesagt und tatsächlich („unter 40 %“: 37,9 % vorhergesagt, 36,5 % gewonnen;
„ab 60 %“: 62,3 % und 64,0 %). Mit 0,9 auf der Lane-Stärke läge die Chance etwas zu nah an 50 %
(Steigung 1,11 ± 0,12), deshalb zählt sie voll.

**Was sich in der App ändert** (eigene Daten):

| | bis 2.0 | 2.1 |
|---|---|---|
| Draft-Chance | Stärke und Duelle über die Lanes gemittelt; lag 4,6-mal zu nah an 50 % | die Summe oben; ein offener Platz zählt als durchschnittlicher Champion |
| Pick: Matchup | Duell, Glättung 1.000 | Duell, Glättung je Lane |
| Pick: Enemy-Team | Duelle von irgendeiner Lane geborgt, gemittelt, × 0,35 (geschätzt) | gezählte Paare über Lanes hinweg (`versus` in `meta.json`), je Art gewichtet |
| Pick: Synergie | alle Duos gemittelt, × 0,6 (geschätzt) | nur das Botlane-Duo, × 0,41 |
| Pick: Teamcomp | Regeln × 0,16, höchstens ±4 Punkte | die fünf gemessenen Befunde mit ihrem Gewicht; Peel, nur Melee, CC, Disengage, Assassinen-Regeln, Early gegen Scaling und der gewählte Plan weiter als Regeln unter dem Deckel; Timing und Tank-Shred nur noch Hinweis |

Die Terme eines Picks sind damit genau das, was der Champion zur Chance des Teams beiträgt. Die Kachel
„Draft-Chance“ zeigt die Teile in Punkten und, solange der beratene Platz leer ist, die Chance mit
dem ersten Vorschlag („mit Ornn 46,1 %“).

**Glättung der Duelle je Lane** (`OWN_MATCHUP_PRIOR`: Top 700, Jungle 800, Mid 1.100, Bot 1.700,
Support 3.300 — die Momentenschätzung aus Messung 14, gerundet). Was dafür spricht und was nicht:

- Mit einem Gewicht von 1.000 für alle verlangt der Fit auf Top den Faktor 2,11 ± 0,64 und auf Bot
  0,59 ± 0,38. Mit der Glättung je Lane liegen alle fünf näher an 1: Top 1,57 ± 0,47, Jungle
  1,05 ± 0,51, Mid 1,49 ± 0,58, Bot 0,83 ± 0,53, Support 2,21 ± 1,52.
- Für die Vorhersage des Siegers bringt es **nichts Messbares**: Die App-Chance steht mit beiden
  Glättungen bei 0,6872 (Unterschied höchstens 0,0001 ± 0,0002). Duelle bewegen die Chance insgesamt
  zu wenig, als dass ihre Gewichtung auf 29.143 Spielen sichtbar würde.
- Geändert ist es trotzdem, weil die Streuung je Lane selbst gemessen ist (zwei Verfahren, gleiche
  Reihenfolge) und ein Gewicht für alle das Top-Duell nachweislich zu schwach zählt. Es ist eine
  Konstante; wer sie nicht will, setzt alle fünf auf 1.000 und verliert in der Vorhersage nichts.
  **Vom Nutzer am 13.11.2026 bestätigt: so lassen.**
- Folge für die offenen Counter (Messung 18): 10 Champion-Lanes haben jetzt einen, 2 zwei oder mehr
  (vorher 14 und 0); auf Support keiner mehr, auf Top fünf. Größte Abweichung: Kayle gegen Garen,
  57,4 % aus 509 Spielen, geglättet +3,6 Punkte (vorher +2,9 für Twitch gegen Jinx).

**Comp bei offenen Plätzen.** Gemessen sind die Befunde an vollständigen Teams. Solange Plätze offen
sind, zählt ein Befund mit dem Anteil des Teams, der feststeht (vier von fünf: 80 %); ein Pick
bekommt, was das Team mit ihm mehr wert ist als ohne ihn. Als letzter Pick ist der Unterschied
zwischen „schließt die Lücke“ und „lässt sie offen“ genau der gemessene Wert, davor weniger. Das
Anteilige ist eine Annahme zwischen null und dem Messwert, nie mehr als gemessen.

**Was nicht gemessen ist und so bleibt wie es war:** die Bann-Rechnung (`BAN_LANE_FOCUS`,
`BAN_ALLY_SHARE`, Pick- und Bannrate als Tor), die Regeln unter dem Comp-Deckel, der Hinweis auf
offene Counter. Das Modell kennt weiter weder die Pick-Reihenfolge noch den Spieler.

**Sammler und C#-Kern.** Der Export braucht nichts Neues: `versus` steht seit dem 07.11. in
`meta.json`, die Gewichte sind Konstanten der App und werden mit `npm run measure:model` nachgemessen
(`--duel-prior 1000` rechnet zum Vergleich mit einem Duell-Gewicht für alle Lanes, `--skip-c 1` lässt
das Gitter von Modell C weg). Der C#-Kern (`Tools -- recommend`) rechnet weiter das Modell von 2.0;
die App ist seit 2.0 der TypeScript-Kern.

## 22. Erfolgskontrolle (13.11.2026, Konzept §5 Punkt 7)

Werkzeug für die Messungen 22 bis 26: `npm run measure:phase4 -- <games.json.gz> <meta.json>
<builds.json> <matchups.json.gz> [--before-matchups alt.json.gz]` (`app/tools/measure/phase4.ts`),
Bericht in `build/measure-phase4.md`.

Gewinnen Spieler öfter, deren Champion das Modell in ihrem Draft weit oben gesehen hätte? 2.000
zurückgehaltene Spiele, Raten nur aus den Trainingsspielen: jeder Platz mit jedem freien Champion
seiner Lane besetzt, der Rest des Drafts bleibt, gerechnet mit `draftModel.ts`.

| Platz des gespielten Champions in der Liste | Spieler | gewonnen |
|---|---:|---|
| oberstes Zehntel | 2.955 | 52,3 ± 1,8 % |
| bis zum obersten Viertel | 3.210 | 50,9 ± 1,7 % |
| obere Hälfte darunter | 4.975 | 51,8 ± 1,4 % |
| untere Hälfte, oberes Viertel | 4.241 | 48,8 ± 1,5 % |
| unterstes Viertel | 4.039 | 46,7 ± 1,5 % |

**Ja.** Wer aus dem obersten Viertel der Liste gespielt hat, gewann 51,6 %, wer aus dem untersten,
46,7 %: rund 5 Punkte Unterschied. Am deutlichsten im Jungle (+8,9 ± 4,4) und auf Top (+7,5 ± 4,4),
auf Mid nicht zu sehen (+1,0 ± 4,7). Das ist kein Beweis, dass der Pick den Unterschied macht — wer
starke Champions spielt, sitzt im Mittel auch in einem stärkeren Draft —, aber die Liste ordnet in
die richtige Richtung, auf Spielen, die sie nie gesehen hat.

## 23. Lane-Zuordnung der Gegner (13.11.2026, Konzept §5 Punkt 5)

291.482 Teams aus den Spielzeilen, mit den Lanes, die Riot aufgezeichnet hat. Dieselbe Rechnung wie in
der App für Gegner ohne Lane-Angabe (`LanePredictor`, Rollenraten aus `meta.json`), erst mit einem
aufgedeckten Champion, dann mit mehr. Die Platz-Priors der App zählen hier nicht mit, weil Riot die
Pick-Reihenfolge nicht herausgibt.

| aufgedeckt | Lane richtig | als sicher gezeigt | davon richtig | als unsicher gezeigt | davon richtig |
|---:|---:|---:|---:|---:|---:|
| 1 | 84,4 % | 89,8 % | 87,7 % | 10,2 % | 55,3 % |
| 2 | 85,0 % | 90,2 % | 88,6 % | 9,8 % | 51,7 % |
| 3 | 86,5 % | 91,4 % | 89,9 % | 8,6 % | 50,1 % |
| 4 | 88,7 % | 92,4 % | 92,0 % | 7,6 % | 47,4 % |
| 5 | 91,9 % | 93,8 % | 94,8 % | 6,2 % | 48,0 % |

- Im ganzen Team sind 91,9 % der Plätze richtig, alle fünf bei 82,3 % der Teams. Bot (95,1 %) und
  Support (94,6 %) am sichersten, Mid (88,9 %) und Top (89,0 %) am wenigsten.
- **Die Markierung „unsicher“ trifft die Richtigen:** Was die App als sicher zeigt, stimmt zu 88–95 %,
  was sie als unsicher zeigt, nur zur Hälfte.
- Die Fehler sind die Flex-Champions: Yone, Yasuo und Akali zwischen Mid und Top, Ekko, Sylas und
  Qiyana zwischen Mid und Jungle, Darius im Jungle, Lux als Support. Das ist die Mehrdeutigkeit des
  Champions, kein Fehler der Rechnung: Aus den Champions allein ist nicht mehr zu holen.

**Nichts geändert.** Die Zuordnung braucht keine Nachstellung; das Menü zum Umstellen der Lane bleibt
der Weg für die Flex-Fälle.

## 24. Erfahrung auf dem Champion (13.11.2026, Konzept §5 Punkt 1)

28.226 Stichproben: in jedem vierten Spiel die Mastery-Punkte eines Spielers auf seinem Champion, des
Spielers mit dem seltensten Champion des Spiels. Median 78.148 Punkte.

| Mastery-Punkte | Spieler | gewonnen | nach Lane-Stärke erwartet | Unterschied |
|---|---:|---:|---:|---:|
| unter 10.000 | 3.656 | 42,0 % | 49,5 % | −7,4 ± 1,6 Pkt |
| 10.000–30.000 | 4.729 | 49,0 % | 49,6 % | −0,6 ± 1,4 Pkt |
| 30.000–100.000 | 7.202 | 49,3 % | 49,6 % | −0,3 ± 1,2 Pkt |
| 100.000–300.000 | 5.850 | 51,7 % | 49,8 % | +1,9 ± 1,3 Pkt |
| ab 300.000 | 6.789 | 52,7 % | 49,9 % | +2,9 ± 1,2 Pkt |

- **Erfahrung zählt, und zwar viel:** je Verzehnfachung der Mastery-Punkte +4,1 ± 0,8 Punkte Winrate.
  Zwischen einem Spieler unter 10.000 Punkten und einem ab 300.000 liegen gut 10 Punkte — mehr als
  zwischen dem besten und dem durchschnittlichen Pick (+3,4, Messung 13). Der große Sprung liegt ganz
  unten: Wer einen Champion kaum kennt, verliert 7 Punkte; ab 10.000 Punkten ist der Rest gering.
- **Was davon in der Winrate eines Champions steckt:** Für die 151 Champion-Lanes mit mindestens 100
  Stichproben, gegenüber dem Median aller Stichproben, zwischen −2,7 und +2,8 Punkten, die mittlere
  Hälfte zwischen −0,8 und +1,0. Oben die Champions der Mains (Shaco Support 361.936 Punkte im
  Median, +2,8; Riven Top 265.157, +2,2; Zoe Mid, Gangplank Mid, je +2,1), unten die neuen (Locke
  Jungle 17.773, −2,7; Zaahen −2,2): Deren Winrate liegt unter dem, was sie mit eingespielten
  Spielern hätten.

**Noch nicht in der App.** Die Messung trägt eine Korrektur, aber zwei Dinge fehlen dafür:

1. Die App kennt die Stichproben nicht. Der Sammler müsste je Champion-Lane Anzahl und Median der
   Mastery-Punkte in `meta.json` schreiben (aus den Spielzeilen, ohne neue Riot-Anfrage).
2. Gemessen sind nur die seltenen Champions; populäre trifft die Stichprobe fast nie (177 von 328
   Champion-Lanes haben weniger als 100). Eine Korrektur nur für die gemessenen verschöbe sie gegen
   die ungemessenen. Dafür müsste die Stichprobe breiter werden (etwa abwechselnd der seltenste und
   ein zufälliger Spieler, ohne zusätzliche Anfragen).

Offen ist außerdem, wo die Korrektur gelten soll: Sie beschreibt einen durchschnittlichen Spieler auf
dem Champion, also die Vorschläge für den eigenen Platz — nicht die Gegner und Mitspieler, die ihren
Champion gewählt haben und im Mittel so erfahren sind wie die gezählten Spieler.

**Entscheidung des Nutzers (13.11.2026): nicht gewünscht** — weder die Korrektur der Zahl noch ein
Hinweis. Die Messung bleibt hier stehen, gebaut wird nichts. Die Mastery-Stichprobe hat der Nutzer
am selben Tag abbestellt: Der Sammler fragt niemanden mehr (rund ein Zehntel mehr Spiele pro Stunde);
die Zeilen bis dahin behalten ihre Werte, `npm run measure:phase4` liest sie weiter.

## 25. Runen, Zauber und Start passend zum Matchup (13.11.2026, Konzept §5 Punkt 3)

`matchups.json.gz` aus dem Artefakt des Sammlers: je Champion, Lane und Lane-Gegner, wie oft welcher
Keystone, Nebenbaum, Zauber neben Blitz und welches Startitem genommen wurde. „Deutlich öfter gegen
diesen Gegner“: Matchup ab 50 Spielen, Anteil gegen ihn mindestens 15 % und mindestens 1,5-mal so
groß wie gegen alle anderen, 3 Standardfehler Abstand.

70.997 Wahlen geprüft, **537 gefunden** in 440 Matchups (Keystone 119, Nebenbaum 79, Zauber 119,
Startitem 220); der Zufall allein ließe bei 3 Standardfehlern rund 96 erwarten. Auf neuen Spielen
(gefunden auf dem Stand vom 11.11., 22:15 UTC, geprüft auf den Spielen seither):

| Art | Wahlen | Anteil gegen den Gegner minus Anteil sonst: damals | auf neuen Spielen | davon wieder |
|---|---:|---:|---:|---:|
| Keystone | 86 | +14,6 Pkt | +12,3 Pkt | 84 % |
| Nebenbaum | 72 | +12,6 Pkt | +7,5 Pkt | 59 % |
| Zauber neben Blitz | 103 | +12,3 Pkt | +8,3 Pkt | 67 % |
| Startitem | 183 | +20,7 Pkt | +16,9 Pkt | 82 % |
| alle | 444 | +15,9 Pkt | +12,0 Pkt | 76 % |

**Das Signal ist echt** — anders als bei Countern (Messung 18: 23 %) kommen hier drei Viertel wieder.
Die deutlichsten Fälle sind die, die jeder Spieler kennt: Doran's Shield gegen Teemo (Darius 72 %
gegen sonst 20 %, Sett 68 % gegen 19 %, Renekton 70 % gegen 18 %), gegen Vayne und Heimerdinger auf
Top; Cleanse gegen Ashe (Jinx 22 % gegen 3 %, Yunara 24 % gegen 5 %, Kai'Sa 17 % gegen 3 %).

**Noch nicht in der App.** Die Datei liegt nur im Artefakt eines Laufs, nicht im Branch `data`; der
Sammler müsste sie veröffentlichen, die App sie laden, und die Build-Ansicht braucht einen Platz dafür
(„gegen Teemo nehmen 72 % Doran's Shield statt Doran's Blade“, als Hinweis wie die situativen Items).
Das ist ein Umbau der Anzeige und geht nach der Arbeitsweise des Nutzers zuerst in eine Vorschau.

## 26. Vorausschau im Draft (13.11.2026, Konzept §5 Punkt 2)

Prüfen lässt sich die Vorausschau nicht, weil die Spieldaten weder Pick-Reihenfolge noch Banns kennen.
Messbar ist, wie weit sie die Zahl bewegen könnte: für 310 Champion-Lanes das erwartete Duell gegen
alle Lane-Gegner, gewichtet nach Pickrate, und dasselbe ohne die drei schlimmsten Counter (gebannt
oder vergeben) — der günstigste Fall.

- Das erwartete Duell gegen das ganze Feld liegt 0,05 Punkte neben null: Es steckt in der Lane-Stärke.
- Ohne die drei schlimmsten Counter steigt die Zahl im Median um 0,07 Punkte, bei 95 % der
  Champion-Lanes um höchstens 0,17, im größten Fall um 0,33 (Ezreal Bot).

**Kein Term.** Ein Drittelpunkt im besten Fall, nicht prüfbar, ist kleiner als der Fehlerbalken jeder
Zeile der Liste. Was die Vorausschau als Hinweis leisten kann, steht seit 2.1 schon da: offene Counter,
sicherer Blind Pick, Flex-Picks für offene Lanes, die Reihenfolge im Team-Plan.

**Patchtag (Punkt 6)** ist nicht messbar, solange der Sammler keinen Patchwechsel gesehen hat.

## 27. Die Stärke-Skala im Spiel (13.11.2026, vorläufig)

Die Prüfung aus `docs/lane-vergleich.md` („Prüfung, bevor die Skala erscheint“), mit
`npm run measure:duels -- <games.json.gz> <meta.json>` (`app/tools/measure/duels.ts`). Bestand:
16.302 Spielzeilen mit Duellen, 236.844 Lane-Duelle (14,5 je Spiel), Patch 16.22. Vorhergesagt wird,
welche Seite den Kill macht; geprüft auf den Duellen zurückgehaltener Spiele (2 von 10).

### Ein Fehler im Sammler: das Level des Siegers

Der Sammler las zu jedem Duell Level und Items „bis zum Kill“. Der Level-Aufstieg, den der Kill selbst
gibt, trägt in Riots Zeitleiste aber **dieselbe Millisekunde wie der Kill** und steht direkt dahinter —
in vier nachgelesenen Zeitleisten bei 54 von 335 Kills für den Sieger und bei 50 für einen Helfer. Er
zählte also als Level vor dem Duell.

Gemessen an den Duellen direkt nach 10:00 (die Zeile trägt die Erfahrung nach genau zehn Minuten, also
das wahre Level unmittelbar davor): In den ersten drei Sekunden steht beim Sieger in 22,1 % der Duelle
ein Level zu viel, beim Verlierer in 1,0 % (837 Duelle).

Weil der Fehler nur den Sieger trifft, verfälscht er die Gewichte weit stärker als „jedes fünfte
Duell“. Dieselben 13.351 Duelle zwischen 10:00 und 11:00, einmal mit dem Level der Zeile, einmal mit
dem Level laut Erfahrung nach zehn Minuten (Log-Odds, ± 95 %):

| Lane | Duelle | Level, wie gezählt | Level, wahr | 1.000 Gold, wie gezählt | 1.000 Gold, wahr |
|---|---:|---:|---:|---:|---:|
| Top | 3.484 | +1,92 ± 0,14 | +0,33 ± 0,10 | −0,21 ± 0,13 | +0,86 ± 0,11 |
| Jungle | 455 | +1,73 ± 0,35 | +0,82 ± 0,28 | +0,46 ± 0,21 | +0,58 ± 0,19 |
| Mid | 2.390 | +1,98 ± 0,18 | +0,53 ± 0,12 | +0,29 ± 0,15 | +0,92 ± 0,13 |
| Bot (2 gegen 2, Summen) | 7.022 | +0,85 ± 0,06 | +0,17 ± 0,05 | +0,12 ± 0,05 | +0,42 ± 0,05 |
| alle | 13.351 | +1,21 ± 0,05 | +0,30 ± 0,04 | +0,09 ± 0,04 | +0,52 ± 0,04 |

Wie gezählt sähe ein Level Vorsprung auf Top nach 87 % aus und Itemwert nach nichts. Wahr ist rund:
**ein Level 57 %, 1.000 Gold Itemwert 63 %** (alle Lanes; auf Top und Mid wiegt das Gold mehr, rund
70 %). Der Itemwert ist nicht betroffen (niemand kauft in der Millisekunde des Kills).

**Behoben am 13.11.2026:** Der Sammler liest den Stand eine Millisekunde vor dem Kill
(`RiotMatchReader.LaneKills`). Zeilen seitdem enden auf −2 (`RiotGames.LevelsBeforeDuel`,
Spiel-Liste Version 4); die alten bleiben, wie sie sind, und sind daran zu erkennen, dass die −2 fehlt.
Das Werkzeug misst an den markierten Zeilen, sobald es 40.000 markierte Duelle gibt (rund ein Tag
Sammeln), vorher an allen und sagt das dazu.

### Was sich trotz des Fehlers schon sagen lässt

Auf allen 236.844 Duellen (Level mit Fehler — die Log-Loss-Werte selbst sind deshalb zu gut und
gelten nicht):

| Variante | Log-Loss | Gewinn gegen die Zeile davor | in Standardfehlern |
|---|---:|---:|---:|
| Level + Itemwert | 0,5176 | | |
| + Resistenzen gegen die Schadensart des Gegners | 0,5159 | +0,0017 | +6,9 |
| Level + Itemwert, Gewichte je Abschnitt (Level 1–5, 6–10, 11+) | 0,5118 | +0,0058 gegen die erste | +11,3 |
| + Champion-Stärke je Lane | 0,5065 | +0,0053 | +11,9 |
| + Champion-Stärke je Lane und Abschnitt | 0,5037 | +0,0082 gegen „je Abschnitt“ | +14,4 |
| + das Paar (Champion gegen Champion) | 0,5037 | −0,0000 | −0,4 |

- **Die Champion-Stärke je Abschnitt besteht die Prüfung**, auf jeder Lane einzeln (Top +7,2,
  Jungle +7,2, Mid +9,1, Bot +7,0 Standardfehler): 2.060 Effekte, zur 0 gezogen mit Streuung 0,30
  Log-Odds (auf einem inneren Viertel gewählt). Der Fehler im Level hängt nicht am Champion, das
  Ergebnis sollte halten; es wird an sauberen Zeilen wiederholt. Die größten Effekte passen zum
  Spiel: spät stark Nasus, Akali, Katarina, Urgot; spät schwach im Duell Xerath, Twisted Fate,
  Orianna; früh schwach Veigar.
- **Das Paar bringt nichts darüber hinaus** (9.445 Paare, jede Stärke über 0,06 sagt schlechter
  vorher). „Champion X gegen genau Champion Y“ erscheint nicht; die Skala kennt nur, wie stark jeder
  der beiden im Abschnitt allgemein im Duell ist.
- Resistenzen gegen die passende Schadensart und Gewichte je Abschnitt sind beide messbar; ihre
  Größe ist erst mit sauberen Leveln zu lesen.

**Nichts in der App geändert.** Offen bis zu den sauberen Zeilen: die Gewichte selbst, die Kalibrierung
(„+2“ gewinnt so oft, wie es sagt) und die Übersetzung in die sieben Stufen. Die Vorschau-Kachel
rechnet bis dahin weiter mit Beispielzahlen.

## Was offen bleibt (2.0)

- **Seit 2.1 erledigt** (Messung 13 und 21): Die drei geschätzten Gewichte (übrige Gegner 0,35,
  Duo-Dämpfung 0,6, Comp 0,16) sind für eigene Daten durch gemessene ersetzt, die Comp-Merkmale aus
  2.1 sind gemessen. Die beiden Punkte unten dazu gelten nur noch für OP.GG-Dateien.
- **Über drei Patches** ist nichts gemessen: Der Sammler kennt bisher nur Patch 16.22. Mit 16.23
  nachmessen: Duelle und Gegner-Paare über die Patches, Patch-Vorwissen für die Lane-Stärke, breitere
  Fehlerbalken am Patchtag.
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
