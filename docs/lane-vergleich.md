# Lane-Vergleich im Spiel — Konzept (Entwurf 2, 08.11.2026)

Wunsch: im Bereich Spiel eine Skala, die nach oben ausschlägt, wenn man gerade stärker ist als der
Lane-Gegner, und nach unten, wenn man schwächer ist — aus Items, Champion-Werten und Level. Damit
man einschätzen kann, ob man kämpfen oder lieber passiv spielen sollte.

## Riots Regeln setzen die Grenze

- Riot genehmigt keine Apps, die Spielern Entscheidungen vorgeben, und keine Produkte, die
  spielbezogene Informationen liefern, die der Spieler sonst nicht hätte
  ([Game Policy, League](https://developer.riotgames.com/docs/lol)).
- Erlaubt ist, wichtige Entscheidungen hervorzuheben und Optionen zu zeigen — die Entscheidung
  selbst muss beim Spieler bleiben ([General Policies, Game Integrity](https://developer.riotgames.com/policies/general)).
- Overwolfs Zusammenfassung der Riot-Regeln nennt als ausdrücklich verboten: Meldungen, die aus dem
  Spielstand eine Handlung ableiten (ihr Beispiel: Gegner hat zu wenig Level, also ganken), und
  Meldungen, wenn ein Champion einen Power-Spike erreicht (Beispiel: Level 6)
  ([Overwolf, Riot-Compliance](https://dev.overwolf.com/ow-native/guides/game-compliance/riot-games)).

**Folge:** Die Skala beschreibt — „stärker“ / „schwächer“ —, sie sagt nie „kämpfen“ oder „passiv
spielen“. Sie bewegt sich still: kein Aufblinken, kein Ton, keine Meldung, wenn sie die Mitte
kreuzt oder ein Gegner Level 6 erreicht. Alles, was hineingeht, steht auf der Tab-Tabelle (Level,
Items) oder steht schon vor dem Spiel fest (Champion-Werte, Statistik aus eigener Zählung).

## Die Skala

```
 Du gegen Darius          12:40
        ┃
   +3   ┃
   +2   ┃
   +1   ┃ ◀ ● stärker
  ──0───┃────────────
   -1   ┃
   -2   ┃
   -3   ┃
        ┃
 Level   8 : 7      +1
 Items   3.450 : 2.800   +650
 Duell   früh Darius · Mitte offen · spät du
```

Mitte = ausgeglichen; nach oben stärker, nach unten schwächer, sieben Stufen. Darunter klein, woraus
sie sich zusammensetzt, damit man ihr nicht blind glauben muss.

### Woraus die Skala rechnet

| Eingang | Woher | Wie es eingeht |
| --- | --- | --- |
| Level beider | Live-API (Tab-Tabelle) | Level-Abstand |
| Items beider | Live-API (Tab-Tabelle) | Itemwert-Abstand (Ladenpreise, Komponenten mitgezählt; Tränke, Wards, Schmuck nicht) |
| Item-Werte | Data Dragon | Rüstung zählt gegen physischen Gegner, Magieresistenz gegen magischen — mehr, als der Preis sagt |
| Champion-Stärke | eigene Zählung | Wie stark genau dieser Champion gegen genau diesen Gegner in diesem Abschnitt ist |

**Champion-Werte — warum gemessen statt gerechnet:** Die Grundwerte aus Data Dragon (Leben,
Rüstung, Angriffsschaden, je Level) zeigen nur einen Teil der Stärke. Den meisten Schaden machen die
Fähigkeiten: Für einen Magier ist ein AP-Item in den Grundwerten wertlos, weil die AP-Anteile seiner
Fähigkeiten dort nicht stehen; dass Kassadin früh schwach und spät stark ist, steht in keinem
Grundwert. Eine Skala nur aus Grundwerten läge bei vielen Duellen falsch herum.

Deshalb lernt die Skala aus den Spielen, die der Sammler ohnehin liest: Jeder Solo-Kill zwischen
Lane-Gegnern sagt, wer das Duell gewonnen hat, bei welchem Level und Itemwert beider. Daraus misst
ein Modell:

- wie viel ein Level-Abstand wert ist,
- wie viel 1.000 Gold in Items wert sind (und Resistenzen gegen die passende Schadensart),
- wie stark Champion X gegen Champion Y ist, getrennt nach Abschnitt (Level 1–5, 6–10, 11+).

Der dritte Teil enthält Grundwerte und Fähigkeiten zusammen. Ergebnis ist die geschätzte Chance,
ein Duell jetzt zu gewinnen; die Skala zeigt sie als Stufe (50 % = Mitte). Paarwerte mit wenigen
Spielen werden zum Schnitt des Champions gegen alle Gegner auf der Lane gezogen, wie beim
Lane-Matchup im Draft.

**Solange zu wenig gezählt ist** (erste Woche nach der Sammler-Erweiterung, seltene Paare) rechnet
die Skala nur aus Level und Itemwert und sagt das darunter.

### Lanes

- Top, Mid: gegen den Gegner auf derselben Lane.
- Bot: zwei gegen zwei (ADC und Support zusammen), aus Kills, an denen nur die vier beteiligt sind.
- Jungle: gegen den gegnerischen Jungler.
- Gegner bestimmen: `position` aus der Live-API, sonst die Lanes aus dem Draft (auch die von Hand
  gesetzten). Nur Champion-Bild und -Name, nie ein Spielername.

### Bewusst nicht drin

- Anweisungen (kämpfen, passiv, ganken) — Riot verbietet sie.
- Meldungen bei Level 6 oder fertigem Item — verbotene Power-Spike-Meldungen.
- Ult oder Flash des Gegners — feste Regel, keine Timer.
- Leben und Mana des Gegners, wo der Jungler steht, Gold im Beutel des Gegners — liefert die API
  nicht oder wäre Wissen, das man im Spiel nicht hat. Das eigene unausgegebene Gold zählt deshalb
  auch nicht mit.

**Grenze, offen gesagt:** Ob ein Trade in dieser Sekunde gut ist, hängt an Leben, Mana,
Fähigkeiten, Welle und Jungler. Die Skala zeigt, wer stärker ist, wenn beide voll da sind — nicht
den Moment. Den sieht man selbst auf dem Bildschirm.

## Daten: was der Sammler zusätzlich zählt (Phase 1d, umgesetzt 08.11.2026)

Die Zeitleiste jedes Spiels liest der Sammler schon (Team-Gold je Minute, Käufe für die Builds).
Alles Neue steht darin — **keine zusätzliche Riot-Anfrage**. Format: Spiel-Liste Version 2
(`RiotGames.Format`, `docs/redesign/daten.md`).

- Kills zwischen Lane-Gegnern (`CHAMPION_KILL`: `killerId`, `victimId`,
  `assistingParticipantIds`); Solo-Kill = keine Helfer, Bot = nur die vier Bot-Spieler beteiligt;
- zu jedem dieser Kills Level und Itemwert beider aus dem Minuten-Bild davor (`participantFrames`:
  `level`, `totalGold` − `currentGold`), dazu die Resistenzen aus dem Inventar, das die
  Kauf-Ereignisse ergeben;
- je Platz Gold und Erfahrung mit 10 min (zur Prüfung, ob die Skala vorhersagt, wer die Lane
  gewinnt).

Gegen echte Spiele geprüft (08.11., ein Lauf von einer Minute): das Zeitleisten-Format passt
(`assistingParticipantIds`, `LEVEL_UP` mit Level, `participantFrames` mit Level, Gold, Erfahrung).
Ein Platin-Spiel hat rund **15 Lane-Duelle** (280 in 19 Spielen; ein nachgezähltes Spiel: 60 Kills,
davon 10 allein auf Top/Jungle/Mid und 10 unter den vier Bot-Spielern). Deshalb trägt nur **jede achte
Spielzeile, höchstens 40.000 je Patch**, den Lane-Teil — rund 600.000 Duelle je Patch, etwa 12 MB
gepackt; alle Zeilen den Lane-Teil tragen zu lassen, hätte den Bestand rund zwanzigmal so groß gemacht.
Seit Version 3 (09.11.) tragen alle Zeilen Gold und Erfahrung mit 10 min, nur die Duelle bleiben bei
jeder achten; eine Zeile ohne ihre Duelle schließt mit −1 (`RiotGames.Keep`, `HasDuelSample`). Wer Duelle
zählt, nimmt nur Zeilen mit `HasDuelSample` — sonst sähe jede übrige wie ein Spiel ohne Duelle aus.

Die Zeilen gehen ins Artefakt `games`; das Modell wird beim Export gerechnet, in die Datendatei
kommen nur seine Zahlen (Level-Gewicht, Gold-Gewicht, Champion-Stärke je Abschnitt, Paarwerte ab
30 Duellen). Laufender Patch plus zwei ältere. Der Itemwert in der App muss dieselbe Regel nehmen
(ohne Tränke, Wards, Schmuck, Support-Quest; `ItemCatalog.CountsForWorth`).

## Prüfung, bevor die Skala erscheint

Auf zurückgehaltenen Spielen: Gewinnt der Champion, den die Skala oben sieht, das nächste Duell
tatsächlich öfter — und so oft, wie sie sagt (bei „+2“ etwa so oft, wie die Stufe verspricht)?
Und schlägt die Variante mit Champion-Stärke die reine Level-plus-Items-Variante? Wenn nicht,
erscheint nur die einfachere.

## Umfang und Reihenfolge

1. **Sammler-Erweiterung zuerst** (C#, mit Tests), damit bis 2.1 genug Duelle gezählt sind —
   erledigt 08.11.2026.
2. **Skala aus Level und Itemwert:** nur TypeScript (`core/game.ts`, `data/gameView.ts`, neue
   Kachel); Level, Items, Scores und Position kommen schon an, der Scrubber ersetzt nur Namen.
3. **Modell mit Champion-Stärke:** nach einer Woche Daten, zuerst prüfen (passt zur Phase-2-Messung).
4. **Dieselben Paarwerte im Draft** (Nutzerwunsch 09.11.2026, vorgemerkt): Das Lane-Matchup im Draft
   zeigt, wer die Duelle in welchem Abschnitt gewinnt (Level 1–5, 6–10, 11+), gemessen aus den
   Solo-Kills, wo sie die Prüfung oben bestehen; sonst nichts. Vor dem Spiel, ohne Anweisung („wer ist
   wann stärker“, nie „kämpf jetzt“), keine KI-Texte. Gilt nur, wenn Schritt 3 messbar besser ist.
5. **Live-Test mit dem Client** — nur nach Rückfrage.

Veröffentlichung mit 2.1.

## Vor dem Bau zu prüfen

- Füllt die Live-API `position` in Ranked für alle zehn? Im ARAM-Mitschnitt steht überall `NONE`;
  sonst gelten die Draft-Lanes.
- ~~Liefert die Zeitleiste die nötigen Felder?~~ Ja (08.11.); der Itemwert kommt aus den
  nachgespielten Käufen, nicht aus `totalGold` − `currentGold`, damit er dem Inventar entspricht,
  das die Live-API zeigt.
