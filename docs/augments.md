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

## 2. Die API des laufenden Spiels (Port 2999): offen, Messung vorbereitet

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

## Stand der Entscheidung

- **Nicht gebaut**, in keinem Modus und keinem Bildschirm (1.3.0): 0 Treffer für `augment` in
  `src/` und `tests/`.
- **Voraussetzung für "die drei im Moment der Wahl"**: Punkt 2 muss positiv ausfallen. Fällt er
  negativ aus, ist die Variante technisch unmöglich und nicht bloß unentschieden.
- **Voraussetzung für ein Urteil statt einer Liste**: eine Stichprobe zu `performance`. Solange die
  fehlt, wäre jede Rangfolge eine erfundene Genauigkeit — dieselbe Regel wie überall sonst hier.
