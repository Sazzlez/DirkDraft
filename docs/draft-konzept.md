# Draft-Konzept: Was ein Pick wert ist (Entwurf, 07.11.2026)

Ziel: Jeder Vorschlag im Draft ist eine Zahl, die geschätzte Winrate deines Teams mit diesem Pick,
gerechnet aus allem, was im Draft schon feststeht. Dazu gehören Meta-Stärke, Counter, Lane- und
Team-Synergien und die Team-Comp. Jeder Teil ist **gemessen**, aus unseren eigenen Spielen, mit
Fehlerbalken. Kein Teil hat ein geschätztes Gewicht.

## 1. Wie 2.0.0 heute rechnet

Jeder Teil verschiebt die Winrate (auf der Log-Odds-Skala), die Teile werden addiert. Jeder Teil zählt
nur das, was er **über die anderen hinaus** erklärt. Ein Duell zählt also nur, soweit es mehr ist als
die Lane-Stärke der beiden Champions.

| Teil | Was gezählt wird | Gewicht | Grundlage |
|---|---|---|---|
| Meta-Stärke | Winrate des Champions auf deiner Lane, geglättet (800 Spiele) | 1 | gemessen |
| Lane-Counter | Duell gegen den wahrscheinlichen Lane-Gegner, geglättet (1.000) | 1 | gemessen |
| Gegnerteam | Duelle gegen die übrigen Gegner, gemittelt | 0,35 | **geschätzt** |
| Synergie | alle Paare mit Mitspielern, gemittelt, geglättet (750) | 0,6 | Glättung gemessen, Gewicht **geschätzt** |
| Team-Comp | 7 Regeln (AP/AD, Frontline, Engage, Peel, CC, nur Melee) | höchstens ±4 Punkte | **keine Daten dahinter** |
| Offene Counter | Champions, die dich auf deiner Lane noch kontern können | nur als Text | — |

### Schwachstellen

1. **Drei Gewichte sind geschätzt** (0,35 · 0,6 · Comp). Im Code steht selbst: Abgleich mit echten
   Spielen ist offen.
2. **Die Botlane wird wie zwei getrennte 1-gegen-1-Lanes gerechnet.** ADC gegen gegnerischen Support
   und Support gegen gegnerischen ADC zählt der Sammler gar nicht. Für einen Support wie Blitzcrank
   gegen einen ADC ohne Flucht fehlt damit genau das Wichtigste.
3. **Der Lane-Partner ist einer von vier Mitspielern.** Das Botlane-Duo zählt so viel wie Top + Support.
   Und weil gemittelt wird, verwässert jeder weitere Mitspieler ein starkes Duo.
4. **Gegner außerhalb der Lane** werden aus Lane-Duellen geschätzt: Gegen den gegnerischen ADC sucht
   das Modell ein Duell auf irgendeiner Lane. Für einen Support findet es da meist nichts.
5. **Comp-Regeln haben keine Zahlen.** Ob „kein Engage“ in Platin wirklich Spiele kostet, ist nicht
   gemessen.
6. **Die Pick-Reihenfolge fehlt in der Zahl.** Ein früher Pick, der leicht zu kontern ist, steht genauso
   da wie ein sicherer. Das Risiko steht nur als Text daneben.

## 2. Das Konzept

### Grundsatz: Gewichte aus echten Spielen messen

Der Sammler legt zusätzlich **je gezähltem Spiel die zehn Champions mit Lane und den Sieger** ab, ohne
Namen und ohne IDs, etwa 20 Bytes pro Spiel, im nicht öffentlichen Bestand, je Patch. Daraus lässt sich
für jeden Teil prüfen, wie gut er den Sieger vorhersagt:

- Die Spiele werden geteilt: An 80 % werden die Tabellen gezählt (Lanes, Duelle, Duos …), an den
  übrigen 20 % wird geprüft. So schaut kein Spiel seine eigene Antwort ab.
- Die Gewichte der Teile werden per logistischer Regression an den 80 % bestimmt und an den 20 %
  bestätigt (Log-Loss gegenüber „nur Meta-Stärke“).
- **Ein Teil, der an den Prüfspielen nichts verbessert, bekommt Gewicht 0.** Er darf dann als
  Hinweis stehen, aber die Zahl nicht verschieben.

Neue Riot-Abfragen kostet das nicht: Alles kommt aus denselben Spieldaten, die der Sammler schon liest.

### Die Teile

**1. Meta-Stärke** bleibt die Basis: Winrate auf der Lane, geglättet. Die Glättung wird mit mehr Spielen
neu gemessen. Pickrate macht einen Champion nicht stärker; sie zählt nur für die Sicherheit
(Fehlerbalken), nicht als Bonus.

**2. Lane-Counter**
- Top, Jungle, Mid: Duell gegen den Lane-Gegner wie heute.
- **Botlane als 2 gegen 2:** vier Duelle — ADC↔ADC, Support↔Support, ADC↔gegnerischer Support,
  Support↔gegnerischer ADC. Der Sammler zählt die beiden Kreuz-Duelle neu dazu. Jedes der vier
  bekommt ein gemessenes Gewicht. Zu erwarten: Duelle gleicher Rolle wiegen am meisten, die
  Kreuz-Duelle weniger, aber spürbar.

**3. Gegnerteam:** Statt Lane-Duelle zu borgen, zählt der Sammler „gegen wen im selben Spiel“ für alle
25 Gegner-Paare, mit Lanes, wie er es bei Duos schon tut. Das Gewicht wird gemessen und ersetzt die 0,35.

**4. Lane-Synergie:** Paare bekommen einen Typ mit eigenem Gewicht: **Botlane-Duo** (ADC + Support),
**Jungle + Mid**, **Jungle + Top**, **Jungle + Botlane**. Der Sammler zählt alle Paare schon mit Lanes,
hier ändert sich nur die Rechnung in der App. Begründung im Vorschlag: „starkes Botlane-Duo mit Nami“.

**5. Team-Synergie:** alle übrigen Paare, **addiert statt gemittelt**, mit eigenem, gemessenem (kleinem)
Gewicht. Dann verwässert ein dritter Mitspieler kein starkes Duo mehr.

**6. Team-Comp:** Jede Regel wird an den Spielen gemessen: Wie oft gewinnen Teams **mit** der Lücke
(zum Beispiel ohne AP-Schaden, ohne Frontline, ohne Engage) gegenüber Teams ohne sie? Regeln mit
messbarem Effekt zählen mit der gemessenen Größe. Die anderen bleiben als Hinweis („kein Engage“),
ohne die Zahl zu verschieben.

**7. Pick-Reihenfolge:** Ist dein Lane-Gegner noch offen, zählt das **erwartete Duell gegen die
Champions, die der Gegner auf deiner Lane noch nehmen kann**, gewichtet danach, wie oft sie dort
gespielt werden. So gewinnen früh sichere Picks mit wenigen harten Countern, und spät der direkte
Counter. Dass der Gegner bewusst kontert, zeigen die Spieldaten nicht (Riot gibt die Pick-Reihenfolge
nicht heraus). Der schlimmste Fall bleibt deshalb Text: „2 offene Counter: …“.

**8. Banns** nutzen dieselben Teile umgekehrt, wie heute.

### Anzeige

Unter jedem Vorschlag die Teile in Punkten, damit klar ist, warum er oben steht. Zum Beispiel
„Meta +1,2 · Counter +2,0 · Botlane-Duo +0,8 · Comp −0,5“. Die Begründungszeilen bleiben wie
gewohnt eine Zeile je Grund.

## 3. Fahrplan

| Phase | Was | Wann |
|---|---|---|
| 1 | Sammler: Spiel-Liste je Patch, Gegner-Paare auf anderen Lanes (darin die Botlane-Kreuz-Duelle). Die App rechnet noch wie heute; 2.0.0 übergeht das neue Feld. | **erledigt 07.11.2026** — zählt ab dem ersten Lauf mit diesem Stand |
| 2 | Messen: Glättungen, Gewichte aller Teile, Comp-Regeln — Ergebnis in `docs/modellpruefung.md` | nach 5–7 Tagen Sammeln (~150.000 Spiele) |
| 3 | Modell mit den gemessenen Gewichten, Anzeige der Teile, Tests, Release 2.1 | nach Phase 2, Release nur mit deinem OK |

Bleibt wie es ist: Daten nur aus eigener Zählung, keine Namen anderer Spieler, kein automatisches
Picken oder Bannen. Pool und Verlauf bleiben draußen.

### Was ich erwarte (Vermutung, nicht gemessen)

- Das **Botlane-Duo** und die **Botlane-Kreuz-Duelle** werden für Supports und ADCs der größte Gewinn
  sein: Heute fehlt dort die halbe Lane.
- **Team-Synergien** außerhalb der Lanes sind in Solo/Duo vermutlich klein. 1.5 maß über alle Paare
  ±1,8 Punkte um die Erwartung.
- Von den **Comp-Regeln** bleiben wohl nur wenige mit Gewicht (zum Beispiel einseitiger Schaden).
  Das ist gut so: Eine Regel, die nichts vorhersagt, verdrängt heute bessere Vorschläge.
