# Eigene Daten (M4)

DirkDraft 2.0 liest keine OP.GG-Daten mehr. Alle Zahlen kommen aus Spielen, die der Sammler mit
einem Riot-Schlüssel selbst zählt, und liegen als vier Dateien im Branch `data`:

| Datei | Inhalt | Quelle |
|---|---|---|
| `meta.json` | Lane-Winrate, Pick-, Bann- und Rollenrate je Champion und Lane; jedes Duell (beide Richtungen); jedes Duo; Gegner auf anderen Lanes (`versus`, ab 30 Spielen, eine Zeile je Paar); Schadensart; Grundlinien und Duell-Linie | Solo/Duo, Platin, EUW |
| `builds.json` | Runen, Spells, Start, Schuhe, Kern, Items 4–6, Support-Item, Skill-Reihenfolge je Champion und Lane; seit 11.11.2026 dazu der Kaufzeitpunkt der Schuhe (unten) | dieselben Spiele |
| `situational.json` | Stiefel, Kern (Items 1–3) und Items 4–6 je Champion, Lane und Gegner-Lage (viel AD, viel AP, 2+ Tanks, viel harte Kontrolle), mit dem Gesamtwert des Platzes zum Vergleich; dazu dieselben Zahlen je Klasse | dieselben Spiele |
| `aram.json` | Winrate je Champion (Bank), ARAM-Builds — auch für Mayhem | normales ARAM (Queue 450), Spieler aus den Platin-Ranglisten |

`meta.json` hat das Format, das das Modell schon immer gelesen hat (`MetaSnapshot`), mit drei neuen
Feldern: `source: "riot"`, `platform`, `games`. Daran erkennt die App, dass die Duelle vollständig
sind: Sie werden zur auf derselben Datei gemessenen Duell-Linie geglättet statt mit dem
Listen-Versatz, den OP.GGs Auswahl der auffälligsten Gegner brauchte, und kein Text nennt mehr
OP.GG. Tiers gibt es keine — das war OP.GGs eigene Einstufung.

**Für die Messung des Draft-Modells** (`docs/draft-konzept.md`, seit 07.11.2026): `versus` zählt je Spiel
die 20 Gegner-Paare auf verschiedenen Lanes, darunter die Botlane-Kreuzpaare ADC ↔ gegnerischer Support.
Die App liest das Feld noch nicht (2.0.0 übergeht es). Außerdem legt der Sammler je gezähltem Spiel eine
Zeile ab: zehn Champions mit Lane, den Sieger, die Spieldauer und den Gold-Vorsprung nach 15 Minuten, ohne Spieler, ohne Match-ID. In jedem vierten Spiel trägt die
Zeile zusätzlich die Mastery-Punkte eines Spielers auf seinem Champion (des Spielers mit dem seltensten
Champion; eine Abfrage, ohne den Spieler zu speichern). Dazu zählt der Sammler je Champion, Lane und
Lane-Gegner Keystone, Nebenbaum, Zauber neben Blitz und Startitems (`matchups.json.gz`, nur laufender
Patch).

**Für die Stärke-Skala im Spiel** (`docs/lane-vergleich.md`, seit 08.11.2026, Spiel-Liste Version 2): jede
achte Zeile, höchstens 40.000 je Patch, trägt zusätzlich einen Lane-Teil — Gold und Erfahrung jedes Platzes
nach 10 Minuten und jedes Lane-Duell: ein Kill zwischen Lane-Gegnern ohne fremde Hilfe (Bot: unter den vier
Bot-Spielern, nur der Partner des Siegers hilft), mit Sekunde, Sieger, Verlierer und für jeden Spieler der
Lane Level, Itemwert (Ladenpreis der gehaltenen Items ohne Tränke, Wards, Schmuck und Support-Quest),
Rüstung und Magieresistenz aus Items. Level und Inventar werden aus der Zeitleiste nachgespielt, die der
Sammler ohnehin holt — keine zusätzliche Anfrage. Gemessen am 08.11.: ein Platin-Spiel hat rund 15 solche
Duelle (280 in 19 Spielen), daher nur eine Stichprobe.

Seit Version 3 (09.11.2026) trägt **jede** Zeile Gold und Erfahrung nach 10 Minuten — für die Messung,
was ein Matchup in der Lane-Phase tut (`docs/modellpruefung.md` §13); das sind 20 Zahlen je Spiel, die
Duelle wären rund 300. Die Duelle bleiben bei jeder achten Zeile; eine Zeile ohne ihre Duelle schließt mit
−1, damit sie nie als Spiel ohne Duelle gelesen wird. Ohne gelesene Minute 10 endet eine Zeile nach 15 Zahlen.

Die Zeilen liegen im
Bestand (der laufende Patch und die zwei davor; ein Wechsel der Region verwirft sie) und als Artefakt `games` jedes Laufs (`games.json.gz`, eine Woche
aufbewahrt, nie veröffentlicht). Herunterladen: `gh run download <Lauf> -n games`.

**Situative Items** (Spielansicht, gleichnamige Kachel): nur gemessen, keine Faustregeln.

- *Lagen:* viel physischer bzw. magischer Schaden, 2+ Tanks, 3+ mit harter Kontrolle (aus den
  gegnerischen Champions) und — zum Zeitpunkt jedes Kaufs — Gegner mit ≥ 150 Rüstung bzw. ≥ 120
  Magieresistenz aus Items oder ≥ 2 Lebensraub-/Omnivampir-Items. Im Spiel bleibt eine erreichte Lage
  bis zum Ende stehen.
- *Signal:* was Spieler des Champions in der Lage deutlich häufiger kaufen als sonst (mindestens
  1,5-mal so oft, mindestens 5 % des Platzes, 2 Standardfehler, 30 Käufe). Zur Klasse hin geglättet
  (50 Pseudospiele).
- *Sicherung:* Die Winrate mit dem Item gegen die anderen Wahlen des Platzes, verglichen nur innerhalb
  desselben Spielstands beim Kauf (vorne/ausgeglichen/hinten, ±1.500 Gold Team-Vorsprung aus der
  Timeline). Messbar schlechter (1,5 Standardfehler darunter) heißt: kein Vorschlag.
- *Kern* (Items 1–3, seit 07.11.2026 gezählt): Hier passieren die meisten Reaktionen auf die Gegner
  (Mikaels Segen gegen viel harte Kontrolle), bei Supports fast alle — sie kommen selten über drei Items
  hinaus. Vorgeschlagen, solange noch keine drei Items fertig sind, als Alternative zum nächsten Kern-Item.
  Der Sammler speichert nur fertige Zählungen; die Kern-Zahlen wachsen deshalb erst mit den Spielen ab
  dieser Änderung.
- *Stiefel* nur für die Lagen der Aufstellung (AD, AP, Tanks, harte Kontrolle): Die Item-Lagen
  entstehen erst, wenn Stiefel längst gekauft sind (0,2 % / 0,0 % / 2,9 % der Stiefelkäufe, gemessen
  07.11.2026). Mit gekauften Stiefeln keine Stiefel-Vorschläge mehr.
- Angezeigt als Alternative neben dem Plan, nie als Ersatz; Items aus dem Plan (Stiefel, Kern,
  Items 4–6) oder im Inventar werden nicht vorgeschlagen; höchstens drei.
- Dateiformat Version 2 (`RiotSituationalFile.CurrentVersion`): Zeilen `[Champion, Lane, Slot, Lage,
  Spielstand, Item, Spiele, Siege]`, Item 0 = Summe des Platzes.

Flex nutzt die Solo/Duo-Zahlen. Beim Patchwechsel bleibt der alte Bestand als Rückfall
veröffentlicht, bis der neue Patch 2.000 Spiele hat (ARAM: 500); die App fragt den Client nach seinem
Patch, nennt im Draft beide Patches und verbreitert die Lane-Fehlerbalken je Patch Rückstand.

**Builds** aus `builds.json`: je Slot die Option mit der besten Winrate, die ihre Spiele hergeben —
zur Rate des Slots geglättet, mit einem Gewicht, das die Datei selbst misst (wahre Streuung der
Optionen über alle Zeilen). Auf den ersten 6.185 Spielen war diese Streuung in jedem Slot null; dann
entscheidet die Spielzahl. Gleiche Zauberpaare (beide Tastenfolgen) und Runenseiten (Nebenrunen in
beliebiger Reihenfolge) zählen als eine Option. Details: `docs/modellpruefung.md`, Messungen 10–12.

**Kaufzeitpunkt der Schuhe** (`builds.json`, seit 11.11.2026, aus der Zeitleiste, ohne zusätzliche
Riot-Anfrage). Zwei Zählungen je Champion und Lane, als Optionen mit Text wie die Skill-Reihenfolge:

- `bootsAfter`: Wie viele fertige Items vor dem Paar Schuhe gekauft wurden — `"0"` bis `"3"` (drei oder
  mehr). Die App stellt das Paar in der Kaufreihenfolge an die meistgezählte Stelle.
- `bootsWorth`: Was die Items wert waren, als die ersten Schuhe gekauft wurden (einfache Stiefel oder
  gleich ein Paar) — der Itemwert 30 Sekunden davor, also beim Betreten des Shops, in Stufen von 500
  Gold: `"0"` (nur Startitems), `"500"`, … `"4000"` (und mehr). Itemwert nach derselben Regel wie beim
  Lane-Vergleich (`ItemCatalog.CountsForWorth`; in der App `core/game.ts` `itemWorth`). Die App nimmt
  die Stufe, bis zu der die Hälfte der Spieler gekauft hat: Ist das eigene Inventar so viel wert,
  kommen die Stiefel auf die Einkaufsliste.

Seit dem 13.11.2026 dazu, für die Frage „Stiefel oder das größte Teil zuerst“ (`docs/modellpruefung.md`,
Messung 19; die App liest sie noch nicht):

- `bootsShop`: Shop-Besuche nach dem Start, bei denen der Spieler noch keine Stiefel hatte und
  mindestens den Preis der Stiefel ausgegeben hat. Text `Itemwert/Ausgabe`, der Itemwert beim Betreten
  in den Stufen von `bootsWorth`, die Ausgabe als `300` (bis 649 Gold), `650` (bis 1.299) oder `1300`
  — etwa `"1000/650"`. `games` zählt hier Besuche, nicht Spiele.
- `bootsBought`: davon die Besuche, in denen Stiefel gekauft wurden; dieselben Schlüssel.

Spieler mit der Rune „Magisches Schuhwerk“ (Stiefel geschenkt, vorher keine kaufbar) zählen seit dem
13.11.2026 in keiner der vier Zählungen mit; davor standen sie als späte Käufer in `bootsAfter` und
`bootsWorth`.

Alle vier Zählungen werden beim Export weder auf die meistgespielten Optionen noch nach Mindestspielen
gekürzt, sonst stimmten die Hälfte und die Anteile nicht mehr. Die Siegzahlen stehen wie überall dabei, werden aber nicht gelesen: Wer früh Schuhe
kauft, liegt oft hinten oder vorn, das sagt nichts über den Zeitpunkt. Ältere Dateien haben die Felder
nicht; dann gilt in der App: Paar nach dem ersten Item, Stiefel mit dem Gold, das nach den Teilen
übrig bleibt. Unter 30 gezählten Spielen ebenso.

In keiner Datei stehen Spielernamen, PUUIDs oder Match-IDs. Die Spielerlisten, über die der Sammler
neue Spiele findet, leben nur im Speicher eines Laufs.

## Was am Ende einzutragen ist

Im Repository unter *Settings → Secrets and variables → Actions*:

- `RIOT_API_KEY` — der Personal Key. Ohne ihn tut der Workflow nichts.
- `RIOT_ID` wird nicht mehr gebraucht (darf stehen bleiben).

**Warum normales ARAM statt Mayhem:** Mayhem läuft unter Queue **2400** (`gameMode` `KIWI`,
„ARAM: Chaos“), aber die Riot-API gibt diese Spiele nicht heraus — sie stehen in keiner Spielliste,
und ein direkter Abruf eines bekannten Mayhem-Spiels antwortet **403** (gemessen 07.11.2026, gleicher
Schlüssel, Ranked-Spiel daneben abrufbar). Normale ARAM-Spiele (450) sind abrufbar. Der Sammler zählt
deshalb normales ARAM als Stellvertreter (Nutzerentscheid); die App sagt das an der Bank und am Build.
Augments gibt es so keine. Die Queues der eigenen letzten Spiele zeigt:

```
dotnet run --project src/DraftPilot.Tools -- riot queues --riot-id "Name#TAG" --key-file build/riot-key.txt
```

(Die Riot-API listet dabei nur, was sie herausgibt.)

## Lokal

```
dotnet run --project src/DraftPilot.Tools -c Release -- riot collect --minutes 60 --state build/riot-state-platinum.json.gz --key-file build/riot-key.txt
dotnet run --project src/DraftPilot.Tools -- riot export --state build/riot-state-platinum.json.gz --out build/data/builds.json --meta-out build/data/meta.json
dotnet run --project src/DraftPilot.Tools -- priors 200 --snapshot build/data/meta.json
```

`priors` misst auf der eigenen Datei, wie stark Lane-, Duell- und Duo-Raten geglättet werden
sollten (Halbierungstest und Momentenschätzung).
