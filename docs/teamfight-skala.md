# Teamfight-Skala ab Minute 14 — Konzept (Entwurf 1, 22.11.2026)

Wunsch des Nutzers: Die Kachel „Team gegen Team“ ab 14:00 soll aussagekräftig zeigen, ob das eigene
Team gerade stärker oder schwächer ist, **wenn es zum Teamfight käme**. Löst den Abschnitt „Nach der
Lane-Phase: Team gegen Team“ in `docs/lane-vergleich.md` ab.

## Was heute steht und warum es nicht reicht

Heute (`teamStandOf` in `app/src/core/strength.ts`, `TeamTile.svelte`): Level und Itemwert beider
Teams als Summen, je ein geteilter Balken, **kein Urteil** — weil nicht gemessen ist, was ein Level
oder 1.000 Gold einem Team im Kampf wert sind.

- Man muss selbst rechnen, was „+1.800 Itemwert“ bedeutet. In Minute 16 ist das viel, in Minute 35
  wenig; die Balken sehen in beiden Fällen gleich aus.
- Was Teamfights neben Gold entscheidet, fehlt ganz: Baron, Elder, Drachen und Seele, und welche
  Champions in dieser Spielphase im Teamfight stark sind (Kayle und Kassadin spät, Lee Sin und Draven
  früh).

## Die Frage, die die Skala beantwortet

> Wenn jetzt alle zehn aufeinandertreffen: Welches Team gewinnt den Teamfight eher?

Annahme: alle leben, alle sind voll da. Die Skala beschreibt — „stärker“ / „schwächer“ —, sie sagt nie
„kämpfen“, „Baron machen“ oder „auf Zeit spielen“. Sie bewegt sich still, wie die Lane-Skala (Riots
Regeln, siehe `docs/lane-vergleich.md`).

## So sieht sie aus

Dieselbe senkrechte Skala wie „Du gegen …“ in der Lane-Phase, damit man sie nicht neu lernen muss:

```
 Euer Team im Teamfight                   27:40
        ┃
   +3   ┃
   +2   ┃ ◀ ● Stärker
   +1   ┃      Teams in dieser Lage gewinnen
  ──0───┃────  rund 7 von 10 Teamfights
   -1   ┃
   -2   ┃
   -3   ┃

 Level      +4                         ▲
 Elder      ihr                        ▲
 Gold       +3.300                     ·
 Drachen    3 : 1                      ·
 Champions  Gegner                     ·
```

- **Stufe und Satz:** sieben Stufen wie die Lane-Skala (Ausgeglichen, Leicht / — / Deutlich stärker
  bzw. schwächer). Darunter, wie oft Teams in dieser Stufe den Teamfight gewonnen haben — die
  gemessene Quote der Stufe aus der Prüfung, nicht die rohe Modellzahl.
- **Woraus sie sich zusammensetzt:** eine Zeile je Faktor, nach Größe sortiert. Die Pfeile zeigen den
  Anteil des Faktors in Stufen der Skala (▲ ab einer halben Stufe für euch, ▲▲ ab anderthalb; ▼ für
  die Gegner; · zählt kaum). So sieht man, *warum* die Skala steht, wo sie steht — „Level und Elder
  für euch, die Champions für die Gegner“ —, ohne eine Formel lesen zu müssen.
- Gold und Level stehen als Vorsprung des eigenen Teams, nicht als zwei Teamsummen; zwei große
  Zahlen nebeneinander musste man erst selbst verrechnen (Nutzerwunsch 27.11.2026). Gold ist der
  Itemwert, wie überall in der Spielansicht.
- Zeilen ohne Inhalt fehlen (kein Baron aktiv → keine Baron-Zeile).
- Die Gegnerliste darunter (Stufe, Itemwert, „meiste Items“) bleibt wie heute.

## Was hineingeht

| Eingang | Woher im Spiel | Warum für Teamfights |
| --- | --- | --- |
| Itemwert beider Teams | Tab-Tabelle (Items) | Stärkster Einzelfaktor. Gewicht je Spielphase oder relativ zum Gesamtwert — 1.000 Gold in Minute 15 sind mehr als in Minute 35; welche Form, entscheidet die Messung. |
| Level beider Teams | Tab-Tabelle | Werte, Skillpunkte, Ult-Ränge (11, 16) — nur als Zahl, nie als Meldung. |
| Baron | Live-Ereignis `BaronKill` | Hand of Baron für 180 s. Die App nimmt die 180 s fürs ganze Team, der Sammler genauso, damit Messung und App dieselbe Regel haben. Angezeigt wird nur „ihr“ / „Gegner“, keine Restzeit. |
| Elder | `DragonKill` mit `Elder` | Execute für 150 s, entscheidet späte Teamfights. |
| Drachen und Seele | `DragonKill` mit Drachentyp | Stacks je Typ, Seele je Typ (Infernal, Mountain, Ocean, Cloud, Hextech, Chemtech). Wie viel welcher Typ im Teamfight wert ist, wird gemessen. |
| Champion-Stärke im Teamfight je Phase | eigene Zählung, vor dem Spiel fest | Skalierung und Teamfight-Kit zusammen (AoE-Ults wie Orianna, Amumu, Miss Fortune), bei gleichem Gold und Level, getrennt nach 14–20, 20–30 und ab 30 Minuten. Ersetzt die grobe, ungemessene Einordnung früh/mitte/spät aus den Traits. |

Nur wenn die Messung zeigt, dass es besser vorhersagt (sonst nicht drin):

- **Wo das Gold liegt:** ob 1.000 Gold auf dem ADC oder Midlaner mehr zählen als auf dem Support —
  „ein gefütterter Carry“ statt nur der Teamsumme. *Gemessen am 27.11.2026: ja, Gold auf Top, Mid und
  ADC zählt gut doppelt so viel wie auf Jungle und Support, und die Skala rechnet seitdem so (Messung
  36, Nachtrag). Der „reichste Spieler“ bringt darüber nichts mehr.*
- **Atakhan** — nur wenn die Live-API ihn meldet (zu prüfen). *Kam bis 27.11.2026 in keinem
  gezählten Spiel vor und entfällt (Messung 36).*

Resistenzen gegen die Schadensart des Gegnerteams sind nicht messbar: Der Teamfight-Teil der
Spielzeile trägt je Spieler nur Level und Itemwert. Dafür bräuchte er zwei Zahlen mehr je Spieler.

## Bewusst nicht drin

- **Wer gerade tot ist, Respawn-Timer.** Die Skala beschreibt 5 gegen 5. Mit den Toten würde sie nach
  jedem Kill springen und dann nur zeigen, was ohnehin auf dem Bildschirm steht — und sie würde zum
  „jetzt los“-Signal, genau der Fall, den Riot verbietet (Handlung aus dem Spielstand). In der Messung
  zählen deshalb nur Kämpfe, in die beide Teams vollzählig gingen.
- **Ults, Flash, Leben, Mana, Position, Vision, Welle** — nicht in der API oder Wissen, das man im
  Spiel nicht hat. Ob ein Kampf *in dieser Sekunde* gut ist, sieht man selbst.
- **Eine Vorhersage** („in fünf Minuten schwächer“, „Zeit spielt für euch“) liest sich als „auf Zeit
  spielen“ oder „jetzt erzwingen“ — eine Anweisung. Die Draft-Zeile „Gegner skalieren besser“ bleibt
  wie heute stehen.
- **Kills, CS, Türme:** Kills und CS stecken schon im Itemwert und Level; Türme entscheiden keinen
  Teamfight.
- **Gold im Beutel:** beim Gegner unbekannt, deshalb das eigene auch nicht.
- **Meldungen, Blinken, Ton** bei Baron, Seele, Level 11/16 oder fertigen Items.

## Woher die Gewichte kommen: Teamfights aus den Zeitleisten

Wie bei der Lane-Skala gemessen, nicht geschätzt — diesmal an Teamfights statt Solo-Kills. Der
Sammler liest die Zeitleisten ohnehin; **keine zusätzliche Riot-Anfrage**.

**Was als Teamfight zählt** (umgesetzt 22.11.2026, `RiotMatchReader.TeamFights` in `RiotMatch.cs`):

- Kills (`CHAMPION_KILL`), jeder höchstens **15 s** nach dem letzten Kill des Kampfes und höchstens
  **3.500 Einheiten** (`position`; die Karte ist rund 15.000 breit) von einem seiner Kills. Zwei
  Gefechte zur selben Zeit an zwei Enden der Karte bleiben zwei Kämpfe;
- gesehen sind Killer, Helfer, Opfer, wer ein Opfer traf (`victimDamageReceived`) und wen ein Opfer
  traf (`victimDamageDealt`). Wer bis zum letzten Kill tot war, zählt nicht, auch wenn eine
  Schadensliste ihn noch nennt;
- gespeichert ab **drei je Team**, damit die Messung zwischen drei und vier entscheiden kann; die Skala
  selbst soll aus Kämpfen mit **vier oder mehr je Team** lernen;
- Sieger: das Team mit mehr Kills im Kampf (Gleichstand: halb oder raus, die Messung zeigt, was besser
  vorhersagt);
- ab 14:00. Wer zu Beginn tot war, steht im Kampf (Riots Respawn-Zeit nach Level und Minute,
  `RiotMatchReader.RespawnMs`); die Messung nimmt nur Kämpfe, in die beide Teams vollzählig gingen —
  sonst lernte der Itemwert die Überzahl mit.

**Was zu jedem Kampf gespeichert wird** (Spiel-Liste Version 5, `RiotGames.Format`): Sekunde des
ersten Kills, Kills je Seite, wer zu Beginn tot war und wer zu sehen war (je ein Bit je Platz), Level
und Itemwert aller zehn eine Millisekunde vor dem ersten Kill (dieselbe Nachspiel-Regel wie bei den
Duellen). Einmal je Spiel: Drachen nach Art, Elder, Baron, Atakhan und Seelen mit Sekunde und Seite
(`ELITE_MONSTER_KILL` mit `monsterSubType`, `DRAGON_SOUL_GIVEN`). Ob Baron oder Elder zu Beginn eines
Kampfes wirkten, rechnet die Messung aus den Sekunden (180 s und 150 s fürs ganze Team, wie später die
App). Die Champions stehen schon in der Spielzeile.

**An einer echten Zeitleiste geprüft** (Platin, 32 Minuten, 22.11.2026): Alle Felder kommen wie
erwartet. Der Leser fand fünf Teamfights ab Minute 14, zwei davon mit vier oder mehr je Team (der
Kampf an Baron in Minute 26:39 als 5 gegen 4, weil Xayah tot war), dazu vier Drachen, die Cloud-Seele
und Baron. Zwei Dinge, die die Messung beachten muss: Die Schadenslisten reichen über 20 Sekunden
zurück, und in diesem Spiel war Ezreal bis Minute 22 AFK (Level 3) — solche Spiele verzerren Level und
Itemwert und gehören erkannt (Level weit unter dem Rest).

**Menge:** Jede vierte Spielzeile, höchstens 40.000 je Patch (`CollectorOptions.FightEvery`,
`FightGamesPerPatch`). Bei rund 1.400 Spielen in der Stunde sind das gut 8.000 Zeilen am Tag. Nach
dem einen Spiel geschätzt: zwei bis drei Kämpfe mit vier oder mehr je Team pro Spiel, also rund
100.000 je Patch. Das reicht für die Champion-Stärke je Phase (rund 170 Champions × 3 Phasen, zur 0
gezogen wie bei den Duellen). Eine erste Messung ist nach zwei bis drei Tagen sinnvoll.

## Rechnung und Prüfung

Gerechnet wird die Chance, den nächsten Teamfight zu gewinnen, als Log-Odds aus den Faktoren oben;
die Skala zeigt sie in Stufen (±3; die Stufenbreite legt die Messung fest, bei der Lane-Skala 0,6).

Die Faktoren kommen einzeln dazu. Jeder muss auf zurückgehaltenen Spielen (2 von 10) **mindestens
zwei Standardfehler** besser vorhersagen als die Stufe davor, sonst bleibt er draußen. Bei „irgendein
Gewinn“ käme ein Faktor, der nichts sagt, in etwa jedem dritten Lauf durch Zufall hinein.

1. Itemwert + Level, ein Gewicht
2. Gewichte je Phase, Itemwert relativ zu dem, was die Teams besitzen, oder beides
3. \+ Baron, Elder, Drachen, Seele, Atakhan
4. \+ wo das Gold liegt (Itemwert des reichsten Spielers)
5. \+ Champion-Stärke je Phase, zur 0 gezogen

Gerechnet wird ohne Seite: Jeder Kampf zählt auch gespiegelt, denn die Skala kennt kein Blau und Rot.
Den Vorteil der Seite weist der Bericht trotzdem aus. Gezählt werden nur Kämpfe:
- mit mindestens vier gesehenen Spielern je Team (`--min-side`),
- in die beide Teams vollzählig gingen,
- ohne AFK-Spieler (einer fünf oder mehr Level unter dem Median des Kampfes),
- ohne Gleichstand bei den Kills.

**Bestanden** hat sie, wenn das Team, das die Skala vorn sieht, den Teamfight öfter gewinnt — und so
oft, wie die Stufe sagt, **in jeder Phase** (14–20, 20–30, ab 30). Vorher erscheint kein Urteil; bis
dahin bleibt die heutige Kachel.

**Ergebnis vom 27.11.2026** (`docs/modellpruefung.md`, Messung 36, 45.447 gezählte Kämpfe): **bestanden.**
- **Was hineinkommt:** Level und Itemwert über alle Phasen gleich gewichtet, der Itemwert nach Rollen
  getrennt (Top, Mid und ADC 0,066 je 1.000 Gold, Jungle und Support 0,032; Nutzerfrage, Nachtrag
  vom 27.11.). Dazu Baron, Elder, Drachen und Seele sowie die Champion-Stärke je Phase (klein, keine
  über ±0,25). Kennt die App die Positionen nicht, rechnet sie mit der Gold-Summe.
- **Was draußen bleibt:** Gewichte je Phase, Itemwert relativ zum Teamwert und der reichste Spieler.
  Keines davon sagt messbar besser vorher.
- **Atakhan** kam in keinem Spiel vor und entfällt.
- **Stufen:** Mit der Stufenbreite der Lane-Skala (0,6) stimmen sie in jeder Phase oder sind leicht
  vorsichtig. Ab Minute 30 gewinnt das vorn gesehene Team etwas öfter, als die Skala sagt.

**Grenze, offen gesagt:** Gezählt sind Kämpfe, die stattgefunden haben. Ein Team, das hinten liegt,
meidet Kämpfe und nimmt eher die an, in denen es eine Chance sieht (Position, Cooldowns). Der gemessene
Vorteil ist deshalb eher zu klein als zu groß. „Gewonnen“ heißt: mehr Kills im Kampf, nicht das Objekt
danach.

## Umfang und Reihenfolge

1. **Sammler** (C#, mit Tests): Teamfights aus der Zeitleiste lesen, Spiel-Liste Version 5 —
   umgesetzt 22.11.2026, an einer echten Zeitleiste geprüft (oben). Der Sammler zählt sie erst, wenn
   der Stand auf `master` liegt (jeder Lauf baut von dort).
2. **Messwerkzeug** `npm run measure:fights -- <games.json.gz> [meta.json]`
   (`app/tools/measure/fights.ts`, Tests in `app/tests/tools/fights.test.ts`), gebaut am 22.11.2026. Es
   schreibt den Bericht nach `build/measure-fights.md`. Am Artefakt vom 22.11. läuft es durch und meldet
   noch keine Teamfights. Das Ergebnis kommt als Messung in `docs/modellpruefung.md`, sobald rund
   40.000 gezählte Kämpfe da sind (Artefakt holen: `gh run download <Lauf> -n games`).
3. **Export** der gemessenen Zahlen in die Datendatei (Gewichte, Buff-Werte, Champion-Stärke je Phase),
   wie bei der Lane-Skala (`laneScale` in `meta.json`). — Gebaut am 27.11.2026: `TeamfightScaleModel`,
   `teamfightScale` in `meta.json` ab 20.000 gezählten Kämpfen.
4. **App:** `teamStrengthOf` in `core/strength.ts`; die Live-Ereignisse (`DragonKill`, `BaronKill`)
   kommen mit `allgamedata` schon an und müssen nur gelesen werden; Kachel aus `StrengthTile.svelte`
   abgeleitet. Vorschau zuerst im localhost-Duplikat. — Gebaut am 27.11.2026 (`TeamStrengthTile.svelte`).
   - **In der Vorschau geprüft:** mit echten Zahlen und mit Beispieldaten, hell und dunkel.
   - **Aufbau:** Die Zeilen stehen nach Größe sortiert. Baron, Elder, Drachen und Seele erscheinen
     nur, wenn sie im Spiel sind.
   - **Killer-Namen:** Im Ereignis-Feed ist der Killer der Spielname ohne Tag. Er passt zum
     `riotIdGameName` der Spielerliste (in einer Aufzeichnung vom 22.11. geprüft); ohne ihn gilt der
     Beschwörername.
5. **Live-Test** mit dem Client — nur nach Rückfrage. Dabei prüfen, welche Ereignisse die Live-API für
   Drachen, Baron, Elder und Atakhan wirklich liefert. *`DragonKill` mit Drachentyp ist in einer
   Aufzeichnung vom 22.11. belegt; `BaronKill` und Elder noch in keiner.*
