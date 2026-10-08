# Modellprüfung (2026-10-08): Sind die Vorschläge die besten? Reicht die Datengrundlage?

Zwei Fragen, gemessen statt behauptet. Grundlage: der gespeicherte Gold-Snapshot (Patch 16.18,
173 Champions, 273 Lane-Zeilen, 1.482 Duelle, 2.909 Duos) und Live-Abrufe bei OP.GG am 08.10.2026
(Patch 16.20). Alle Zahlen sind mit den Werkzeugen in `src\DraftPilot.Tools` reproduzierbar.

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
| Aktueller Patch | Patchwechsel wird angezeigt | Snapshot muss per Knopf aktualisiert werden |

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

## Was offen bleibt

- **Drei Gewichte sind weiter Schätzungen:** übrige Gegner (0,35), Duo-Dämpfung (0,6) und
  Team-Zusammensetzung (0,16). Kalibrieren ginge nur an Spielausgängen. Bei 1.000 eigenen Spielen
  läge der Fehler eines Gewichts bei etwa 0,6, man könnte also 0 nicht von 1 unterscheiden.
- **Jungle-Duelle** bleiben auf den Auswahl-Listen, weil OP.GG für den Jungle keine vollständige
  Liste liefert. Sie sind jetzt aber entsprechend stark geglättet.
- **Die Erwartung eines Duells** rechnet die Lane-Stärken mit Steigung 1. Gemessen an den
  vollständigen Listen wären es 0,79. Der Unterschied kann auch von der Rang-Mischung kommen
  (Standard-Bracket gegen Gold) und ist ohne Gold-Vollständigkeitslisten nicht zu trennen. Er
  bleibt dokumentiert, nicht umgesetzt.
- **Der Datenstand altert.** Der hier gemessene Snapshot ist zwei Patches alt. Das Fenster meldet
  den Patchwechsel. Aktualisieren ist ein Knopfdruck.
