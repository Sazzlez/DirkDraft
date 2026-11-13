# Draft-Konzept: Was ein Pick wert ist (Entwurf 2, 07.11.2026)

**Ziel:** Auf jeder Lane steht oben der Champion, mit dem dein Team das Spiel am wahrscheinlichsten
gewinnt — als Einzelspieler auf der Lane und als Team. Die Zahl dahinter ist die geschätzte Winrate
nach allem, was im Draft feststeht. Jeder Teil davon ist an unseren eigenen Spielen gemessen, keiner
ist geschätzt.

## 1. Was der Draft kann — und was nicht

Ehrlich vorweg, damit die Erwartung stimmt:

- **Der Draft entscheidet einen Teil des Spiels, nicht das Spiel.** Erfahrungsgemäß sagen die zehn
  Champions allein den Sieger in Solo/Duo nur zu etwa 55–60 % richtig voraus. Den Rest machen Spieler,
  Form und Spielverlauf aus. Wie viel es bei uns in Platin genau ist, misst Phase 2.
- **Der beste Pick gegenüber einem durchschnittlichen** bringt typischerweise ein paar Prozentpunkte,
  bei einem echten Counter mehr. Das klingt wenig, ist über viele Spiele aber viel: +3 Punkte Winrate
  sind auf Dauer ein deutlicher Aufstieg.
- **Wie gut jemand einen Champion beherrscht, sieht das Modell nicht.** Es rechnet für einen
  durchschnittlichen Platin-Spieler, der den Champion spielt. Ein Champion, den man kaum kennt,
  verliert erfahrungsgemäß spürbar häufiger, als die Zahl sagt.

Gerade deshalb muss jeder Teil gemessen sein. Bei Effekten von wenigen Punkten verdrängt ein
geschätztes Gewicht schnell einen besseren Vorschlag.

## 2. Wie 2.0.0 heute rechnet

*Stand 13.11.2026: Phase 2 ist gemessen, Stufe B hat gewonnen und steht für eigene Daten in der App
(`docs/modellpruefung.md`, Messung 13 und 21). Die Tabelle beschreibt 2.0.0; was 2.1 daraus macht,
steht dort.*

Fünf Teile verschieben die Winrate und werden addiert. Jeder zählt nur, was er über die anderen
hinaus erklärt:

| Teil | Was | Gewicht | Grundlage |
|---|---|---|---|
| Meta-Stärke | Winrate auf der Lane, geglättet (800) | 1 | gemessen |
| Lane-Counter | Duell gegen den Lane-Gegner, geglättet (1.000) | 1 | gemessen |
| Gegnerteam | Duelle gegen die übrigen Gegner, gemittelt | 0,35 | geschätzt |
| Synergie | alle Paare mit Mitspielern, gemittelt (Glättung 750) | 0,6 | geschätzt |
| Team-Comp | 7 Regeln (AP/AD, Frontline, Engage, Peel, CC, nur Melee); seit 2.1 dazu Waveclear, Timing im eigenen Team, Tank-Shred (aus den Build-Daten), AD/AP nach Schadensmenge — der Plan beider Teams nur als Hinweis | ≤ ±4 Punkte | keine Daten — Messwerkzeug steht (unten) |

Schwachstellen:
- Drei Gewichte sind geschätzt.
- Gegner außerhalb der eigenen Lane werden aus Lane-Duellen geborgt und fehlen meist ganz.
- Die Botlane wird als zwei getrennte 1-gegen-1-Lanes gerechnet.
- Jungle-Einfluss auf die Lanes gibt es nicht.
- Synergien werden gemittelt, statt zu zählen.
- Die Comp-Regeln haben keine Zahlen dahinter.
- Die Pick-Reihenfolge fehlt in der Zahl.

## 3. Was auf welcher Lane zählt

Die Spieldaten zeigen nur, wer gegen wen und mit wem gewonnen hat. Daraus lässt sich messen, welche
Beziehungen auf welcher Lane wie viel wiegen. Was ich erwarte (Vermutung, Phase 2 misst es):

| Lane | Counter | Synergie | Team-Comp |
|---|---|---|---|
| **Top** | Lane-Duell wiegt am meisten (Insel-Lane, harte Counter); dazu: wie gefährdet gegen den gegnerischen Jungler | Jungle + Top (Ganks, Dive) | Frontline oder Splitpush, je nach Team |
| **Jungle** | Jungle-Duell (Invade, Clear); wie gut die eigenen Ganks gegen die gegnerischen Laner gehen | Jungle + Laner mit Kontrolle und Lane-Druck (Mid, Bot, Top) | Engage, früh oder spät |
| **Mid** | Lane-Duell (starke Counter); dazu der gegnerische Jungler | Jungle + Mid (2 gegen 2 in der Mitte), Mid + Support (Roams) | oft die AP-Quelle des Teams |
| **ADC** | Botlane 2 gegen 2: ADC-Duell **und** gegnerischer Support | ADC + Support, das stärkste Paar im Spiel | braucht Schutz (Peel, Frontline) |
| **Support** | Botlane 2 gegen 2: Support-Duell **und** gegnerischer ADC (Engage gegen ADC ohne Flucht) | Support + ADC; Support + Jungle/Mid (Roams) | Engage oder Peel, je nach Team |

Für alle Lanes gleich: Meta-Stärke im aktuellen Patch, Team-Comp beider Seiten und ob das Team früh
oder spät gewinnt.

## 4. Das Konzept: ein gemeinsames Modell für alle Lanes

Statt fünf einzeln gerechneter Teile ein **einziges Modell, das an allen gezählten Spielen gelernt
wird**:

> Winrate von Blau = Meta-Stärke aller zehn Champions auf ihren Lanes
> + Synergie jedes Paars im selben Team (nach Paar-Art: Botlane-Duo, Jungle + Mid, …)
> + Counter jedes Paars über die Teams hinweg (nach Paar-Art: Lane-Duell, Botlane-Kreuz, Jungle gegen Laner, …)
> + Team-Comp beider Seiten (gemessene Merkmale)

**Warum das besser ist:**
1. **Keine Doppelzählung, keine geschätzten Gewichte.** Alles wird gemeinsam an echten Ergebnissen
   bestimmt. Jede Paar-Art bekommt die Stärke, die sie in Platin tatsächlich hat.
2. **Alle Lanes nach denselben Regeln,** aber jede mit ihren eigenen Beziehungen (Tabelle oben).
3. **Auch seltene Paare bekommen eine sinnvolle Zahl.** Jeder Champion bekommt wenige Merkmale, die
   das Modell selbst lernt, etwa „Engage“, „ohne Flucht“ oder „spät stark“. Ein Paar mit wenigen
   Spielen erbt dann, was ähnliche Paare zeigen. Blitzcrank gegen einen seltenen ADC ohne Flucht sieht
   aus wie Blitzcrank gegen andere ADCs ohne Flucht. Heute fällt so ein Paar auf „neutral“ zurück.
   Fachlich ist das eine Faktorisierung: dasselbe Prinzip, mit dem Empfehlungssysteme seltene
   Kombinationen schätzen.
4. **Erklärbar bleibt es:** Jede Zahl zerfällt in Beiträge einzelner Champions und Paare, die
   Begründungszeilen bleiben („+1,5 gegen Jinx“, „starkes Botlane-Duo mit Nami“).

**Absicherung:** Gelernt wird an 80 % der Spiele, geprüft an den übrigen 20 %. Das gemeinsame Modell
ersetzt das heutige nur, wenn es an den Prüfspielen den Sieger messbar besser vorhersagt **und** gut
kalibriert ist („55 %“ gewinnt wirklich etwa 55 %). Phase 2 vergleicht drei Stufen:
**A** heute · **B** heutige Teile mit gemessenen Gewichten und Paar-Arten · **C** gemeinsames Modell.
Ausgeliefert wird die beste.

### Mehr aus denselben Spielen holen

- **Meta-Stärke ohne Neustart je Patch:** Stärke ändert sich mit Patches, aber gemessen nur um etwa
  einen halben Punkt pro Patch, außer bei Buffs und Nerfs. Der alte Patch dient als Vorwissen, der
  neue korrigiert. Dann sind die Zahlen schon am Patchtag brauchbar, statt einen Tag lang dünn.
- **Synergien und Counter über mehrere Patches:** Ob Blitzcrank ADCs ohne Flucht bestraft, hängt am
  Kit, nicht am Patch. Diese Beziehungen werden über die letzten drei Patches gelernt, mit mehr
  Gewicht auf dem neuesten. Das verdreifacht die Daten genau dort, wo sie am dünnsten sind. Dafür muss
  der Sammler die Spiel-Zeilen der letzten drei Patches behalten (Phase 1b).
- **Früh oder spät:** Die Spiel-Zeilen tragen Spieldauer und Gold-Vorsprung nach 15 Minuten. Daraus
  lässt sich messen, welche Champions früh und welche spät gewinnen, und ob ein Team mit zu wenig
  Spätphase gegen ein spät starkes Team verliert.

### Team-Comp, gemessen

Jedes Merkmal wird an den Spielen geprüft, gegeben die Champions: Anteil AP-Schaden, Frontline, harte
Kontrolle, Engage, Peel, Reichweite, früh oder spät stark. Dazu das Zusammenspiel mit dem gegnerischen
Team, soweit es sich zeigt. Merkmale ohne messbaren Effekt verschieben die Zahl nicht und bleiben
höchstens Hinweis.

Seit 2.1 stehen auf der Liste auch die Regeln, die 2.1 ungemessen eingeführt hat (alle unter dem
Comp-Deckel oder ganz ohne Gewicht):

| Merkmal | Wie es je Team gezählt wird | Was die Messung zeigen muss |
|---|---|---|
| Waveclear | Champions mit Waveclear 2; Befund „wenig Waveclear“ ab vier Champions ohne einen | Team ohne Waveclear verliert öfter, als die Stärke sagt |
| Reichweite / Poke | Ranged-Champions; Summe des Traits `poke` | Effekt je Champion bzw. Punkt, Vorzeichen offen |
| Tank-Shred | Champions, die auf ihrer Lane meistens Anti-Tank-Items bauen (builds.json), gegen zwei oder mehr Frontliner | positiver Effekt nur gegen Frontliner |
| Timing | Early- und Late-Game-Champions; „gemischtes Timing“ | Effekt je Champion; dazu beschreibend: gewinnt die später skalierende Seite die langen Spiele? |
| Archetyp-Duelle | Plan je Team (Teamfight, Pick, Poke, Protect, Splitpush, offen) | Tabelle Plan gegen Plan: tatsächliche minus erwartete Winrate — gibt es ein Schere-Stein-Papier? |

**Werkzeug:** `npm run measure -- <games.json.gz> <meta.json> <builds.json>` (in `app/`,
`tools/measure/comp.ts`). Es rechnet beide Comps mit genau den Regeln der App (CompAnalyzer, Traits,
TankShred) und misst jedes Merkmal per logistischer Regression über die Stärke der zehn Champions
hinaus — einzeln und gemeinsam, in Punkten mit 95-%-Fehlerbalken —, prüft den Gewinn an
zurückgehaltenen Spielen (80/20) und schreibt Archetyp-Tabelle und Timing nach Spieldauer in einen
Bericht (`build/measure-comp.md`). Es ändert kein Gewicht. Merkmale, die in weniger als 200 Spielen
die Seiten unterscheiden, bleiben draußen.

### Pick-Reihenfolge

- **Lane-Gegner noch offen:** Es zählt das erwartete Duell gegen die Champions, die der Gegner auf
  deiner Lane noch nehmen kann, gewichtet nach Platin-Pickrate. Über alle Gegner gemittelt steckt das
  schon in der Meta-Stärke. Neu ist, was wegfällt: Sind die schlimmsten Counter eines Champions schon
  gebannt oder vergeben, steigt er. Ist der Lane-Gegner bekannt, zählt das direkte Duell, also der
  gezielte Counter.
- **Bewusstes Kontern** zeigen die Spieldaten nicht, weil Riot die Pick-Reihenfolge nicht herausgibt.
  Wie vorsichtig man früh picken sollte, lässt sich deshalb nicht messen. Der schlimmste Fall bleibt als
  Text stehen („2 offene Counter: …“).
- **Banns** aus demselben Modell: gebannt wird, was die Winrate deines Teams am meisten drücken würde,
  wenn der Gegner es nimmt, gewichtet nach Pick-Wahrscheinlichkeit.

### Anzeige

Unter jedem Vorschlag die Teile in Punkten, damit klar ist, warum er oben steht. Zum Beispiel
„Meta +1,2 · Lane +1,8 · Botlane 2v2 +0,9 · Team +0,4 · Comp −0,3“. Oben im Draft steht die Chance des
ganzen Teams („Dein Draft: 52 % → mit Nami 54 %“). Die Begründungszeilen bleiben eine Zeile je Grund.

## 5. Über das Fundament hinaus (Phase 4, vom Nutzer gewünscht am 07.11.2026)

1. **Winrates gehören auch den Spielern.** Seltene, schwere Champions haben hohe Winrates, weil sie
   fast nur ihre Mains spielen. Der Sammler fragt deshalb in jedem vierten Spiel einen Spieler nach
   seiner Mastery auf seinem Champion, und zwar den mit dem seltensten Champion. Das kostet etwa ein
   Zehntel der Spiele pro Stunde. Daraus wird gemessen, wie viel der Winrate auf Erfahrung zurückgeht,
   und die Zahl wird für einen durchschnittlichen Spieler korrigiert.
2. **Vorausschau im Draft:** Bewertet wird auch, was nach deinem Pick wahrscheinlich noch kommt: ob
   dein Lane-Partner noch einen guten Partner findet, Picks für mehrere Lanes, verbleibende Counter.
   Gerechnet wird mit Pickraten und den gemessenen Teilen. Prüfen lässt es sich nur an der Logik,
   weil die Spieldaten keine Pick-Reihenfolge enthalten.
3. **Runen, Zauber und Start passend zum Matchup:** Der Sammler zählt je Champion, Lane und
   Lane-Gegner Keystone, Nebenbaum, den Zauber neben Blitz und die Startitems. Gezeigt wird, was gegen
   genau diesen Gegner deutlich öfter genommen wird, wie bei den situativen Items.
4. *(Benachbarte Ränge als Vorwissen — nicht gewünscht.)*
5. **Lane-Zuordnung der Gegner** an den echten Lanes der Spiel-Zeilen prüfen und nachstellen.
6. **Patchtag:** Champions, deren Werte sich laut Data Dragon geändert haben, bekommen am Patchtag
   breitere Fehlerbalken. Wie viel breiter, wird beim nächsten Patchwechsel gemessen.
7. **Erfolgskontrolle:** In den Spiel-Zeilen wird geprüft, ob Spieler, deren Champion das Modell im
   jeweiligen Draft weit oben gesehen hätte, wirklich öfter gewinnen.

Die Daten für 1 und 3 sammelt der Sammler seit dem 07.11.2026 (Phase 1c). 5, 6 und 7 brauchen nur die
Spiel-Zeilen. Alles zusammen kommt mit 2.1.

**Stand 13.11.2026** (`docs/modellpruefung.md`, Messung 22 bis 26): 7 gemessen und bestätigt; 5 gemessen,
nichts zu ändern; 2 gemessen, kein Term (höchstens 0,33 Punkte, nicht prüfbar); 1 und 3 gemessen, beide
mit klarem Signal, aber nicht in der App — 1 hat der Nutzer am 13.11.2026 abgelehnt (weder Korrektur
noch Hinweis), 3 braucht die Datei im Branch `data` und einen Platz in der Build-Ansicht; 6 wartet auf
den ersten Patchwechsel.

## 6. Fahrplan

| Phase | Was | Wann |
|---|---|---|
| 1 | Sammler: Gegner-Paare über alle Lanes (`versus`, darin Botlane-Kreuz und Jungle gegen Laner), je Spiel eine Zeile (zehn Champions mit Lane, Sieger, Dauer, Gold nach 15 Minuten). Die App rechnet noch wie heute. | **erledigt 07.11.2026** — zählt ab dem Lauf, der in der Nacht auf den 08.11. startet |
| 1b | Sammler behält die Spiel-Zeilen der letzten drei Patches (für Synergien und Counter über Patches) | **erledigt 07.11.2026** |
| 1c | Sammler: Mastery-Stichprobe in der Spiel-Zeile (Punkt 1), Runen, Zauber und Start je Lane-Gegner (Punkt 3) | **erledigt 07.11.2026** |
| 2 | **erledigt 13.11.2026** (145.741 Spielzeilen, ein Patch; Ergebnis: Stufe B, `docs/modellpruefung.md` Messung 13–20). Messen: A, B und C an Prüfspielen vergleichen; Glättungen, Paar-Arten je Lane, Comp-Merkmale (Werkzeuge stehen: `npm run measure:model` für A/B, `npm run measure` für die Comp; Zwischenstand in `docs/modellpruefung.md` §13), früh/spät; dazu, wie viel der beste Pick gegenüber dem Durchschnitt bringt. Ergebnis in `docs/modellpruefung.md` | nach 5–7 Tagen Sammeln (~150.000 Spiele) |
| 3 | Bestes Modell in Sammler-Export und App, Anzeige der Teile, Team-Chance, dazu Phase 4 (Punkte 1–3, 5–7), Tests, Release 2.1 | Modell, Anzeige der Teile und Team-Chance **erledigt 13.11.2026** (Messung 21; der Export braucht nichts Neues); Phase 4 siehe `docs/modellpruefung.md` ab Messung 22; Release nur mit deinem OK |

Bleibt wie es ist: Daten nur aus eigener Zählung, keine Namen anderer Spieler, kein automatisches
Picken oder Bannen, Pool und Verlauf bleiben draußen. Zusätzliche Riot-Abfragen nur für die
Mastery-Stichprobe (eine in jedem vierten Spiel); alles andere kommt aus den Spielen, die der Sammler
ohnehin liest.
