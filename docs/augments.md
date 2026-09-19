# Augments: was die Schnittstellen hergeben (Stand 2026-09-19)

Die Frage: Kann DirkDraft im Spielbildschirm bei ARAM Mayhem die drei angebotenen Augments zeigen
und bewerten? Dafür braucht es zwei Dinge — eine Quelle, die sagt **welche drei** angeboten werden,
und Zahlen, die sagen **welches davon gut ist**. Beide sind getrennt zu prüfen.

## 1. Der League-Client (LCU): nein, gemessen

Abgefragt über `/help?format=Full` (3,08 MB, die Selbstauskunft des Clients).

| Suche | Treffer |
|---|---|
| Endpunkt-Pfade mit `augment` | **0** |
| Typen mit `Mayhem` | nur `LolEventHubSeasonPassSubType.Mayhem` und `LolTftPassSeasonPassSubType.Mayhem` |

Was es an `augment` gibt, gehört nicht hierher:

- `earlyAugments` / `midAugments` / `lateAugments` → Elementtyp
  `LolCosmeticsCosmeticsTFTPlaybookAugment`, also **Teamfight Tactics**.
- `skinAugments`, `ChampionSkinAugment`, `backdropAugments` → **Kosmetik**, nicht Spielmechanik.
- `loyaltyMayhemBPAugmentsCount` → ein **Battlepass-Zähler** neben `loyaltyTFTCompanionCount`.
- `playerAugment1` … `playerAugment6` → `int32`, direkt neben `perk0`…`perk5` in der
  Teilnehmer-Statistik. Das ist **Nachspiel**-Historie, keine laufende Auswahl.

Damit ist der Client als Live-Quelle erledigt. Nicht bewiesen ist, dass es gar keine Quelle gibt —
der Client ist nur eine von zweien.

## 2. Die API des laufenden Spiels (Port 2999): nein, gemessen im Spiel

Das Spiel bedient eine zweite, völlig eigene Schnittstelle, mit der die App bisher **nie** gesprochen
hat (`grep -rn "2999\|liveclientdata"` über `src/` → 0 Treffer). Der Spielbildschirm lebt heute allein
von der Gameflow-Phase des Clients plus dem Build aus dem Champ Select. Sie ist die einzige verbliebene
Stelle, an der ein Live-Augment auftauchen könnte.

Gemessen wird mit `Tools -- ingame [datei.jsonl]`. Der Befehl wartet, bis ein Spiel läuft, und rät
dann **nicht**, sondern beobachtet drei Dinge gleichzeitig:

1. **Welche Endpunkte antworten** — die geratenen Augment-Namen, und zusätzlich alles, was das Spiel
   in seinem eigenen Schema (`/swagger/v3/openapi.json`) auflistet.
2. **Welche Felder die Nutzlast hat** — jeder Eigenschaftspfad wird beim ersten Auftreten gemeldet.
   Ein Augment-Feld kann nicht erscheinen, ohne aufzufallen, egal wie es heißt.
3. **Welche Events es gibt** — jeder neue `EventName`.

Punkt 1 wird alle 45 Sekunden wiederholt, nicht nur beim ersten Kontakt: Ein Endpunkt, der nur
während eines Augment-Angebots existiert, wäre einer einmaligen Abfrage im Ladebildschirm entgangen.

Spielernamen ersetzt `LiveGameScrubber` vor dem Schreiben durch Platzhalter — die Aufzeichnung
enthält alle zehn Spieler, und sie soll teilbar sein. Ersetzt statt gelöscht: Die Aufzeichnung
entsteht, um herauszufinden, ob ein Feld existiert; diese Frage durch Löschen von Feldern zu
beantworten, wäre widersinnig.

Vorab gegen einen Stellvertreter-Server geprüft (`DIRKDRAFT_INGAME_ORIGIN`), weil die echte Messung
**einmalig** in einem Spiel stattfindet, das sich nicht wiederholen lässt. Verifiziert: ein mitten im
Spiel neu antwortender Endpunkt, ein aus dem Schema ergänzter Pfad, ein neu auftauchendes
verschachteltes Feld, ein neuer Event-Name, und dass kein echter Name in die Datei gelangt.

### Das Ergebnis (ARAM: Chaos, 2026-09-19)

| Messung | Wert |
|---|---:|
| Abfragen während des Spiels | 73 |
| Verschiedene Feldpfade gesehen | 128 |
| Events | 5 (`GameStart`, `MinionsSpawning`, `FirstBrick`, `TurretKilled`, `ChampionKill`) |
| Endpunkte, die je antworteten | 11 von 18 |
| Selbstdokumentation des Spiels | 26.883 Zeichen, 13 `liveclientdata`-Endpunkte |
| **Vorkommen von „augment" — Endpunkt, Feld, Event, Schema** | **0** |

Die Augment-Endpunkte, die es *nicht* gibt (alle 404): `/liveclientdata/activeplayeraugments`,
`/liveclientdata/playeraugments`, `/liveclientdata/augments`.

### Was dieser Durchlauf beweist — und was nicht

**Bewiesen, unabhängig vom einzelnen Spiel:** Es gibt keinen Augment-Endpunkt. Das Schema des
Spiels ist seine vollständige Selbstauskunft, und „augment" kommt darin null Mal vor.

**Nicht bewiesen: dass kein Augment-Feld in `allgamedata` auftaucht.** Das beobachtete Spiel war
eine **Custom-Lobby (Queue 3270), und die hatte überhaupt keine Augments** — die Nachspiel-Statistik
des Clients führt für sie `playerAugment1..6` durchgehend auf `0`. Ein Negativbefund aus einem
Spiel ohne Augments sagt über Augments nichts. Zwei weitere Einschränkungen desselben Durchlaufs:

- Die Sonde musste mitten im Spiel neu gestartet werden (Positivlisten-Fix), wodurch die Spielzeit
  **112 s bis 172 s** unbeobachtet blieb.
- Die Formbeobachtung meldet ein Feld beim ersten Auftreten. Ein Feld, das nur während des
  Auswahlfensters existiert und danach verschwindet, wäre nur zu sehen, wenn eine Abfrage genau
  hineinfällt.

**Offen bleibt daher die eigentliche Frage.** Sie ist mit einem Durchlauf in einem echten,
gematchten ARAM: Chaos (Queue **2400**) zu schließen, ohne Neustart-Lücke. Der Gegencheck dafür ist
billig und eindeutig: Die Nachspiel-Statistik muss danach `playerAugment1..6 != 0` zeigen, sonst
war auch dieser Durchlauf leer.

### Gegenprobe: Augments gibt es in diesem Modus wirklich

Aus der Match-Historie des Clients, gemessen am selben Tag für ein echtes Mayhem-Spiel
(Queue 2400, 916 s):

| `playerAugment` | ID | `nameTRA` | `rarity` |
|---|---:|---|---|
| 1 | 1129 | Magierschütze | kGold |
| 2 | 1156 | Wooglets Hexenhaube | kPrismatic |
| 3 | 2132 | Hexer-Safttüte | kGold |
| 4 | 1308 | FeuerFuchs | kSilver |

Das belegt dreierlei: Der Modus vergibt Augments, der Client **speichert** sie (nach dem Spiel), und
die ID-Tabelle aus Punkt 4 löst sie vollständig auf — Name und Seltenheit ohne Netzzugriff.

### Zwei Nebenbefunde

**Der Modus heißt im Spiel `KIWI`.** `gameData.gameMode` meldet für ARAM: Chaos nicht „ARAM" oder
„MAYHEM", sondern Riots internen Codenamen — wie `CHERRY` für Arena. Die Queue-Tabelle des Clients
bestätigt das für alle fünf Chaos-Queues.

**Die Sonde durfte anfangs zu viel.** Das Schema des Spiels listet nicht nur Abfragen, sondern auch
`/Exit`, `/Cancel`, `/Subscribe` und `/AsyncDelete` — und die Endpunkt-Abfrage rief sie auf, in
einem laufenden Spiel, alle 45 Sekunden. Folgenlos geblieben, aber nicht durch Entwurf. Seitdem
gilt eine Positivliste (`/liveclientdata/`, `/swagger/`) statt einer Verbotsliste: Eine Verbotsliste
müsste jedes Verb kennen, das Riot im nächsten Patch ergänzt.

## 3. Die Zahlen: vorhanden, aber ohne Stichprobe

`lol_list_aram_augments` (OP.GG) liefert echte, lokalisierte Daten: `id`, `name`, `tier`,
`performance`, `popular`. Was fehlt, ist die **Stichprobengröße**, und `performance` hat keine
dokumentierte Skala. Für Darius gemessen:

| Augment | Tier | performance | popular |
|---|---:|---:|---:|
| Brennen aufwerten | 3 | 84,38 | 0,22 |
| Quantenberechnung | 5 | 124,67 | **0,00** |
| Hexer-Safttüte | 5 | 46,36 | **0,00** |

Die Extremwerte sitzen genau dort, wo `popular` null ist — das ist Rauschen. Ohne Stichprobe lässt
es sich weder glätten (`Shrinkage`) noch mit einem Fehlerbalken versehen (`ScoreError`), und jede
andere Zahl im Tool trägt einen. Dazu nimmt das Werkzeug nur `champion_id` und `lang`, **kein**
`game_mode`: Es könnte ARAM und Mayhem gar nicht auseinanderhalten.

## 4. Die Namenstabelle: vollständig vorhanden, lokal

Der Client serviert unter `/lol-game-data/assets/v1/cherry-augments.json` (118 KB) **552 Augments**
mit `id`, `augmentNameId`, `nameTRA` (lokalisierter Name), `rarity` und Icon-Pfad. Kein Netz nötig,
keine dritte Verbindung: Der Client läuft ohnehin.

Gemessen dazu:

- Alle drei Augments aus der OP.GG-Messung stehen **wortgleich** in der Tabelle. Ein Join über den
  Anzeigenamen funktioniert also.
- Aber 116 von 436 Namen sind doppelt vergeben — meist eine Arena- und eine ARAM-Variante desselben
  Augments (`QuantumComputing` 66 und `ARAM_QuantumComputing` 1066). Bei **genau einem** Paar
  weichen die Daten ab: „Henker" ist als `ARAM_Executioner` Gold, als `Executioner` Silber.
- Das Präfix `ARAM_` taugt **nicht** als Filter für den ARAM-Pool: „Hexer-Safttüte" heißt
  `WarlockJuicebox` ohne Präfix und wird von OP.GG trotzdem für ARAM geführt.

## Stand der Entscheidung

- **Nicht gebaut**, in keinem Modus und keinem Bildschirm: 0 Treffer für `augment` in `src/` und
  `tests/` (ausgenommen die Sonde, die nur misst).
- **„Die drei im Moment der Wahl": noch offen.** Ein eigener Endpunkt ist ausgeschlossen; ein Feld
  in `allgamedata` ist es nicht, weil das Messspiel keine Augments hatte. Ein Durchlauf in
  Queue 2400 entscheidet es.
- **„Nachschlagen vorab" bleibt möglich**, aber nur als Liste ohne Urteil. Voraussetzung für ein
  Urteil wäre eine Stichprobe zu `performance` (Punkt 3), und die liefert OP.GG nicht. Solange sie
  fehlt, wäre jede Rangfolge eine erfundene Genauigkeit — dieselbe Regel wie überall sonst hier.
