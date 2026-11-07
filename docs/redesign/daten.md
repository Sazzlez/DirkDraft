# Eigene Daten (M4)

DirkDraft 2.0 liest keine OP.GG-Daten mehr. Alle Zahlen kommen aus Spielen, die der Sammler mit
einem Riot-Schlüssel selbst zählt, und liegen als drei Dateien im Branch `data`:

| Datei | Inhalt | Quelle |
|---|---|---|
| `meta.json` | Lane-Winrate, Pick-, Bann- und Rollenrate je Champion und Lane; jedes Duell (beide Richtungen); jedes Duo; Schadensart; Grundlinien und Duell-Linie | Solo/Duo, Platin, EUW |
| `builds.json` | Runen, Spells, Start, Schuhe, Kern, Items 4–6, Support-Item, Skill-Reihenfolge je Champion und Lane | dieselben Spiele |
| `situational.json` | Stiefel und Items 4–6 je Champion, Lane und Gegner-Lage (viel AD, viel AP, 2+ Tanks, viel harte Kontrolle), mit dem Gesamtwert des Platzes zum Vergleich; dazu dieselben Zahlen je Klasse | dieselben Spiele |
| `mayhem.json` | Winrate je Champion (Bank), Builds, Augments je Champion | ARAM Mayhem |

`meta.json` hat das Format, das das Modell schon immer gelesen hat (`MetaSnapshot`), mit drei neuen
Feldern: `source: "riot"`, `platform`, `games`. Daran erkennt die App, dass die Duelle vollständig
sind: Sie werden zur auf derselben Datei gemessenen Duell-Linie geglättet statt mit dem
Listen-Versatz, den OP.GGs Auswahl der auffälligsten Gegner brauchte, und kein Text nennt mehr
OP.GG. Tiers gibt es keine — das war OP.GGs eigene Einstufung.

**Situative Items** (Spielansicht, Kachel „Für dieses Spiel“): nur gemessen, keine Faustregeln.

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
- Angezeigt als Alternative neben dem Plan, nie als Ersatz; Items aus dem Plan oder im Inventar
  werden nicht vorgeschlagen; höchstens drei.

Flex nutzt die Solo/Duo-Zahlen. Beim Patchwechsel bleibt der alte Bestand als Rückfall
veröffentlicht, bis der neue Patch 2.000 Spiele hat (Mayhem: 500); die App sieht am Patch, dass die
Zahlen hinterherhinken.

In keiner Datei stehen Spielernamen, PUUIDs oder Match-IDs. Die Spielerlisten, über die der Sammler
neue Spiele findet, leben nur im Speicher eines Laufs.

## Was am Ende einzutragen ist

Im Repository unter *Settings → Secrets and variables → Actions*:

- `RIOT_API_KEY` — der Personal Key. Ohne ihn tut der Workflow nichts.
- `RIOT_ID` — eine Riot-ID im Format `Name#TAG`, die ARAM Mayhem spielt (deine). Von ihr aus folgt
  der Mayhem-Sammler den Mitspielern. Ohne sie gibt es keine `mayhem.json`, der Rest läuft.

Stand 07.11.2026 läuft Mayhem unter **2400** (`gameMode` `KIWI`, „ARAM: Chaos“), gelesen aus dem
Client während einer Runde. Falls Riot das ändert:

```
dotnet run --project src/DraftPilot.Tools -- riot queues --riot-id "Name#TAG" --key-file build/riot-key.txt
```

zeigt die Queues der letzten Spiele; die passende Zahl gehört dann als `--queues` in den Workflow.

## Lokal

```
dotnet run --project src/DraftPilot.Tools -c Release -- riot collect --minutes 60 --state build/riot-state-platinum.json.gz --key-file build/riot-key.txt
dotnet run --project src/DraftPilot.Tools -- riot export --state build/riot-state-platinum.json.gz --out build/data/builds.json --meta-out build/data/meta.json
dotnet run --project src/DraftPilot.Tools -- priors 200 --snapshot build/data/meta.json
```

`priors` misst auf der eigenen Datei, wie stark Lane-, Duell- und Duo-Raten geglättet werden
sollten (Halbierungstest und Momentenschätzung).
