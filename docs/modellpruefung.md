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
