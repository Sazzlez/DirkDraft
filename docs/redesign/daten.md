# Eigene Daten (M4)

DirkDraft 2.0 liest keine OP.GG-Daten mehr. Alle Zahlen kommen aus Spielen, die der Sammler mit
einem Riot-Schlüssel selbst zählt, und liegen als vier Dateien im Branch `data`, neben zwei von Hand
gepflegten:

| Datei | Inhalt | Quelle |
|---|---|---|
| `meta.json` | Lane-Winrate, Pick-, Bann- und Rollenrate je Champion und Lane; jedes Duell (beide Richtungen); jedes Duo; Gegner auf anderen Lanes (`versus`, ab 30 Spielen, eine Zeile je Paar); Schadensart; Grundlinien und Duell-Linie | Solo/Duo, Platin, EUW |
| `builds.json` | Runen, Spells, Start, Schuhe, Kern, Items 4–6, Support-Item, Skill-Reihenfolge je Champion und Lane; seit 11.11.2026 dazu der Kaufzeitpunkt der Schuhe (unten) | dieselben Spiele |
| `situational.json` | Stiefel, Kern (Items 1–3) und Items 4–6 je Champion, Lane und Gegner-Lage (viel AD, viel AP, 2+ Tanks, viel harte Kontrolle), mit dem Gesamtwert des Platzes zum Vergleich; dazu dieselben Zahlen je Klasse | dieselben Spiele |
| `aram.json` | Winrate je Champion (Bank), ARAM-Builds — auch für Mayhem | normales ARAM (Queue 450), Spieler aus den Platin-Ranglisten |
| `traits.json` | Champion-Merkmale nach Einschätzung: Engage, Peel, CC, Skalierung, Waveclear, Poke, Split, Pick | `data/champion_traits.json` im Repo, von Hand gepflegt |
| `combos.json` | Combo-Listen für die Hinweise am Pick (Wombo, Knock-up für Yasuo, Enchanter mit Hypercarry) | `data/combos.json` im Repo, von Hand gepflegt |

**Eine Quelle für die gepflegten Dateien (seit 17.11.2026).** Traits, Combos und Platz-Priors liegen
nur noch in `data/` im Hauptordner. Vorher lagen Traits und Priors doppelt vor (dort und in
`app/src/core/model/data/`), die Combo-Listen standen in `app/src/core/model/kits.ts`, und alles war
in die App eingebaut: Ein neuer Champion hatte bis zum nächsten Release keine Traits, ein reworkter
behielt seine alten. Jetzt legt der Export `traits.json` und `combos.json` bei jedem Lauf mit in den
Branch `data` (dieselben Dokumente ohne `$comment`, mit `version`), und die App lädt sie mit den
anderen Dateien. Die eingebaute Kopie (aus `data/` zur Bauzeit) gilt nur, bis der erste Download da
ist, und wenn eine geladene Datei nichts Lesbares enthält. Die Platz-Priors hängen an keinem Champion
und bleiben eingebaut. Fehlt einem Champion aus Data Dragon ein Eintrag in den Traits, meldet der
Export `::warning::Champion ohne Traits: …` und schreibt eine Zeile in die Zusammenfassung des Laufs.

`meta.json` hat das Format, das das Modell schon immer gelesen hat (`MetaSnapshot`), mit drei neuen
Feldern: `source: "riot"`, `platform`, `games`. Daran erkennt die App, dass die Duelle vollständig
sind: Sie werden zur auf derselben Datei gemessenen Duell-Linie geglättet statt mit dem
Listen-Versatz, den OP.GGs Auswahl der auffälligsten Gegner brauchte, und kein Text nennt mehr
OP.GG. Tiers gibt es keine — das war OP.GGs eigene Einstufung.

**Für die Messung des Draft-Modells** (`docs/draft-konzept.md`, seit 07.11.2026): `versus` zählt je Spiel
die 20 Gegner-Paare auf verschiedenen Lanes, darunter die Botlane-Kreuzpaare ADC ↔ gegnerischer Support.
Die App liest das Feld noch nicht (2.0.0 übergeht es). Außerdem legt der Sammler je gezähltem Spiel eine
Zeile ab: zehn Champions mit Lane, den Sieger, die Spieldauer und den Gold-Vorsprung nach 15 Minuten, ohne Spieler, ohne Match-ID. Bis zum 13.11.2026 trug jede vierte
Zeile zusätzlich die Mastery-Punkte eines Spielers auf seinem Champion (des Spielers mit dem seltensten
Champion; eine Abfrage, ohne den Spieler zu speichern). Der Nutzer hat die Abfrage am 13.11.2026
herausgenommen; die beiden Plätze in der Zeile bleiben und sind seitdem −1 und 0. Dazu zählt der Sammler je Champion, Lane und
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

Seit Version 4 (13.11.2026) endet eine Zeile mit Duellen auf **−2**: Level und Items jedes Duells sind
der Stand eine Millisekunde vor dem Kill. In den Zeilen davor (ohne −2) enthält das Level des Siegers
und seines Helfers den Aufstieg durch den Kill selbst, in rund jedem fünften Duell — wer Level misst,
nimmt nur markierte Zeilen (`RiotGames.HasLevelsBeforeDuel`; `docs/modellpruefung.md`, Messung 27).

Die Zeilen liegen im
Bestand (der laufende Patch und die zwei davor; ein Wechsel der Region verwirft sie) und als Artefakt `games` jedes Laufs (`games.json.gz`, eine Woche
aufbewahrt). Sie stehen nicht im Branch `data` und die App liest sie nicht; als Artefakt eines
öffentlichen Repos kann sie aber jeder angemeldete GitHub-Nutzer laden. Ihr Inhalt hat keine Namen,
keine PUUIDs und keine Match-IDs. Herunterladen: `gh run download <Lauf> -n games`.

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

## Wie die App die Dateien lädt (seit 17.11.2026)

Die Shell lädt, nicht die Seite (`app/src-tauri/src/data.rs`, `net.rs`): je Datei eine Anfrage mit dem
gespeicherten ETag (`If-None-Match`). Antwortet GitHub „unverändert“, wird nichts geschrieben, nichts
gelesen und nichts neu geparst. Eine neue Datei legt die Shell neben die behaltene Kopie
(`<Name>.new`); die Seite liest und prüft sie und meldet dann „übernehmen“ oder „verwerfen“. Eine
verworfene Version wird nicht noch einmal geladen (ihr ETag steht in `<Name>.rejected`), und eine
kaputte Datei ersetzt nie die letzte gute. Vorher las die Seite bei jeder Prüfung ohne neue Daten
40,6 MB und schrieb 20,3 MB über die IPC, alle 30 Minuten. Die Seite braucht dafür keinen Zugriff auf
`raw.githubusercontent.com` mehr (CSP). Geprüft wird weiter alle 30 Minuten und nie im Champ Select
oder im Spiel (`app/src/core/refresh.ts`).

## Namen, Preise und Bilder aus Data Dragon (seit 17.11.2026)

Die Tabellen (Champions, Items, Beschwörerzauber, Runen) lädt ebenfalls die Shell
(`app/src-tauri/src/ddragon.rs`, Befehl `assets_load`) und behält sie je Version auf der Platte
(`%LOCALAPPDATA%\DraftPilot\data\2.0\ddragon\<Version>\<Sprache>\`). Geladen wird für den Patch des
Clients, sobald der ihn nennt, sonst für den Patch der Daten: genau diese Version, sonst die neueste
desselben Patches, sonst die neueste überhaupt. Ohne Netz nimmt die Shell die behaltene Kopie, notfalls
die eines älteren Patches; die Seite fragt dann bei der nächsten Gelegenheit wieder (jede Datenprüfung,
neuer Client-Patch, Client beendet). Vorher gab es einen Ladeversuch je Programmlauf: Ein Start ohne
Netz ließ die Namen bis zum nächsten Start leer, und nach einem Client-Patch blieben die alten Tabellen.

Items, die der Katalog des Client-Patches nicht kennt, die nicht im Shop stehen oder die es auf der
Karte nicht gibt (Kluft 11, Heulende Schlucht 12), fallen aus Plan und Vorschlägen
(`buyable`, `buyablePlan` in `app/src/core/game.ts`). Ohne Katalog wird nichts gefiltert.

## Was ein Lauf aushält (seit 17.11.2026)

**Fehler bei einzelnen Spielen.** Gibt Riot ein Spiel nicht heraus (400, 403 auf diesem Spiel, sechsmal
5xx oder gar keine Antwort), zählt der Sammler es als übersprungen und macht weiter; der Bericht und
eine Warnung im Lauf nennen die Zahl und den letzten Grund. Der Lauf endet nur bei 401, bei 403 auf
einer Liga-Seite oder Spiel-Liste (beides heißt: der Schlüssel) oder nach 20 Fehlern in Folge (Ausfall).
Der Stand wird bei jedem Ende gesichert, auch beim Abbruch (`CollectorFailures.cs`).

**Export auch nach Fehlern.** „Exportieren“, „Spiel-Liste sichern“ und „Veroeffentlichen“ laufen auch,
wenn ein Schritt davor fehlgeschlagen ist; ein Fehler in „ARAM sammeln“ hält die Ranked-Dateien
desselben Laufs nicht mehr zurück. Ob eine Datei veröffentlicht werden darf, entscheidet allein der
Schutz unten.

**Schutz vor einem leeren Stand.** Der Stand liegt im Actions-Cache. Fehlt der einmal, beginnt der
Sammler bei null, und sein Export würde wenige Tausend Spiele an die Stelle von 270.000 setzen. Vor
dem Push vergleicht `riot guard` deshalb die `games` der neuen `meta.json` (und der `aram.json`) mit der
veröffentlichten aus dem Branch `data`:

| Fall | Folge |
|---|---|
| gleicher Patch, neue Datei unter 50 % der veröffentlichten Spiele | nicht veröffentlichen; `::error::` mit beiden Zahlen, der veröffentlichte Stand bleibt |
| neue Datei zählt keine Spiele | nicht veröffentlichen |
| neue Datei von einem älteren Patch als dem veröffentlichten | nicht veröffentlichen (ein alter Stand, etwa aus einem Backup) |
| anderer (neuerer) Patch, oder noch nichts veröffentlicht | normal weiter |

`meta.json`, `builds.json` und `situational.json` gehören zu einer Zählung und werden zusammen
veröffentlicht oder gar nicht; `aram.json` wird für sich geprüft. Was nicht ersetzt wird, bleibt auf
dem Branch stehen. Von Hand:

```
dotnet run --project src/DraftPilot.Tools -- riot guard --old alt/meta.json --new neu/meta.json   # Exitcode 3: nicht veröffentlichen
```

**Backup des Stands.** Einmal am Tag — der erste Lauf eines UTC-Tages, der so weit kommt — lädt der
Workflow beide Stände (`riot-state.json.gz`, `aram-state.json.gz`) als Artefakt `state-backup` hoch,
14 Tage aufbewahrt. Verschlüsselt (`openssl enc -aes-256-cbc -pbkdf2`, Schlüssel im Secret
`STATE_BACKUP_KEY`), weil die Artefakte eines öffentlichen Repos jeder angemeldete GitHub-Nutzer laden
kann und der Stand die schon angesehenen Match-IDs enthält (`seen`), die das Projekt nie
veröffentlicht. Fehlt das Secret, läuft alles wie bisher, mit einer Warnung im Lauf.

**Wiederherstellung.** Findet „Stand laden“ keinen Cache (GitHub löscht einen Cache nach einer Woche
ohne Zugriff; wer schreiben darf, kann ihn leeren), holt „Stand aus Backup holen“ das neueste
`state-backup`, entschlüsselt es und zählt von dort weiter. Ohne Backup, ohne Secret oder mit falschem
Schlüssel beginnt die Zählung neu; der Schutz oben hält den dünnen Stand dann von der Veröffentlichung
ab, bis er wieder die Hälfte des veröffentlichten erreicht oder ein neuer Patch beginnt.

Von Hand, etwa um einen älteren Stand als den neuesten zurückzuholen:

```
gh run list --workflow collect-builds.yml --limit 30            # den Lauf des gewünschten Tages suchen
gh run download <Lauf> -n state-backup                          # → state-backup.tar.enc
openssl enc -d -aes-256-cbc -pbkdf2 -pass pass:<Schlüssel> -in state-backup.tar.enc | tar -x
dotnet run --project src/DraftPilot.Tools -- riot status --state riot-state.json.gz
```

Damit der Sammler diesen Stand nimmt: die Caches `riot-state-…` unter *Actions → Caches* löschen und
alle neueren `state-backup`-Artefakte entfernen; der nächste Lauf stellt dann aus dem verbliebenen
neuesten Backup wieder her.

## Neu zählen nach Hotfix und Rework (`data/resets.json`, seit 17.11.2026)

Der Sammler unterscheidet nur ganze Patches. Ein Hotfix mitten im Patch mischt deshalb die Spiele
davor und danach bis zum Patchende, und in den Tagen nach einem Patch zeigt die App für einen
reworkten Champion die Zahlen seines alten Kits (der Rückfall auf den Vorpatch, oben). Dagegen steht
`data/resets.json`, von Hand gepflegt — auch am Handy über GitHub (Datei öffnen, Stift, speichern;
auf `master`, denn von dort baut jeder Lauf):

```json
[
  { "champion": "Mel", "patch": "16.23", "reason": "hotfix", "note": "Hotfix vom 20.11.: W-Schaden" },
  { "champion": "MonkeyKing", "patch": "16.24", "reason": "rework", "note": "neues Kit" }
]
```

- `champion`: der Data-Dragon-Key (`MonkeyKing`); der Anzeigename (`Wukong`, `Kai'Sa`) wird auch
  verstanden. `patch`: der Patch, in dem es passiert ist. `reason`: `hotfix` oder `rework`. `note`:
  frei; zwei Hotfixes desselben Champions im selben Patch brauchen verschiedene Notizen (das Datum),
  sonst gilt der zweite als schon angewendet.
- **Sammler:** Zu Beginn jedes Laufs wendet er die Einträge des laufenden Patches an, die der Stand
  noch nicht kennt. Der Champion verschwindet aus allen Zählungen des Patches — Lane-Zeilen, Duelle
  und `versus`, Duos, Banns, Schaden, Builds, situative Items, Matchup-Builds, im ARAM-Stand Winrate
  und Builds — und zählt ab dann neu. Der Eintrag steht danach im Stand (`appliedResets`); ein zweiter
  Lauf ändert nichts mehr. Die Spiel-Zeilen bleiben. Bis der Champion wieder genug Spiele hat, fehlt
  er in den Listen oder steht dünn da; seine Pickrate zählt bis zum Patchende nur die Spiele seit dem
  Eintrag.
- **Export im Rückfallfenster:** Solange der neue Patch zu dünn ist und der Vorpatch veröffentlicht
  wird, fehlen Champions mit einem `rework` für den neuen Patch in `meta.json`, `builds.json`,
  `situational.json` und `aram.json`. Besser keine Zahl als die des alten Kits. Ein `hotfix` ändert
  am Rückfall nichts.
- Ein unlesbarer Eintrag oder ein unbekannter Champion ist eine Warnung im Lauf, nie sein Ende.
- Tests: `tests/DraftPilot.Core.Tests/ChampionResetTests.cs`.

## Was am Ende einzutragen ist

Im Repository unter *Settings → Secrets and variables → Actions*:

- `RIOT_API_KEY` — der Personal Key. Ohne ihn tut der Workflow nichts.
- `STATE_BACKUP_KEY` — der Schlüssel für das tägliche Backup des Stands, eine lange Zufallsfolge
  (etwa aus `openssl rand -base64 32`). Außerhalb von GitHub aufbewahren: Ohne ihn ist ein Backup
  nicht zu lesen, und GitHub zeigt ein Secret nie wieder an.
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
