# Eigene Daten (M4)

DirkDraft 2.0 liest keine OP.GG-Daten mehr. Alle Zahlen kommen aus Spielen, die der Sammler mit
einem Riot-Schlüssel selbst zählt, und liegen als vier Dateien im Branch `data`:

| Datei | Inhalt | Quelle |
|---|---|---|
| `meta.json` | Lane-Winrate, Pick-, Bann- und Rollenrate je Champion und Lane; jedes Duell (beide Richtungen); jedes Duo; Gegner auf anderen Lanes (`versus`, ab 30 Spielen, eine Zeile je Paar); Schadensart; Grundlinien und Duell-Linie | Solo/Duo, Platin, EUW |
| `builds.json` | Runen, Spells, Start, Schuhe, Kern, Items 4–6, Support-Item, Skill-Reihenfolge je Champion und Lane | dieselben Spiele |
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
Zeile ab: zehn Champions mit Lane, den Sieger, die Spieldauer und den Gold-Vorsprung nach 15 Minuten, ohne Spieler, ohne Match-ID. Die Zeilen liegen im
Bestand (nur der laufende Patch) und als Artefakt `games` jedes Laufs (`games.json.gz`, eine Woche
aufbewahrt, nie veröffentlicht). Herunterladen: `gh run download <Lauf> -n games`.

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
