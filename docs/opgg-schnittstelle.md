# Was OP.GGs MCP-Schnittstelle wirklich anbietet (gemessen am 18.09.2026)

Erhoben mit `dotnet run --project src\DraftPilot.Tools -- opgg tools` und `… opgg try …` gegen
`https://mcp-api.op.gg/mcp`. Der Befehl schreibt nichts und ändert nichts; er fragt den Server,
statt unseren eigenen Abruf-Code zu lesen — der kann nur zeigen, was wir ohnehin schon anfragen.

29 Werkzeuge insgesamt, davon 16 für League (der Rest TFT, Valorant, Esports).

## Die drei Antworten, die den größten Unterschied machen

### 1. Rang-Bracket: geht — und unsere Code-Kommentare behaupten das Gegenteil

`lol_get_champion_analysis` hat einen optionalen `tier`-Parameter. Gemessen für Darius Top,
`game_mode=ranked`, Spiele auf der Lane:

| tier | Spiele | Counter-Stichproben |
|---|---:|---|
| *(weggelassen)* | 92.578 | Teemo 1604, Anivia 243, Malzahar 160 |
| `all` | 453.688 | Teemo 17171, Dr. Mundo 11192, Yorick 9269 |
| `gold` | 100.734 | Wukong 331, Warwick 343, Malzahar 241 |
| `platinum` | 88.193 | Zaahen 777, Kennen 529, Heimerdinger 452 |
| `emerald` | 58.516 | Quinn 171, Anivia 109, Teemo 975 |
| `emerald_plus` | 92.578 | — |
| `platinum_plus` | 180.771 | — |
| `diamond_plus` | 34.062 | — |
| `silver` | 84.386 | — |

Damit steht dreierlei fest:

- **Rangfiltrierte Counter kommen nicht leer zurück.** Der Kommentar in `SnapshotBuilder.cs`
  („without it OP.GG returns no matchup data whatsoever, which was verified against the live
  endpoint") und der in `LiveDraftFetcher.cs` („rank-filtered counter samples are almost always
  empty") treffen für diese Aufrufform nicht zu. Gold liefert 241–343 Spiele je Counter.
- **Der Standard ohne `tier` ist `emerald_plus`** — die Zahl 92.578 stimmt exakt mit dem Bracket
  überein und exakt mit dem, was die Tierlist liefert.
- **Heute mischt der Score zwei Bevölkerungen:** Lane-Stärke kommt aus der Tierlist
  (= `emerald_plus`), Matchups und Synergien aus `tier: "all"` (alle Ränge). Das sind
  verschiedene Spielerpopulationen in derselben Summe.

Und die Counter selbst sind je Bracket andere Champions: in Gold sind Darius' auffälligste Gegner
Wukong/Warwick/Malzahar, in Platin Zaahen/Kennen/Heimerdinger. Das ist genau der Effekt, wegen
dem ein Gold-Spieler nicht mit Emerald+-Zahlen beraten werden sollte.

### 2. Region: geht nicht

Kein einziges Meta-Werkzeug hat einen `region`-Parameter. `region` gibt es nur bei
`lol_get_summoner_profile`, `lol_list_summoner_matches`, `lol_list_champion_leaderboard`,
`lol_get_pro_player_riot_id` und den Esports-Werkzeugen — also nur dort, wo ein Spielername
abgefragt wird, was dieses Tool bewusst nicht tut. **EUW-spezifische Meta-Daten sind über diese
Schnittstelle nicht zu haben.**

### 3. ARAM: geht, samt Builds

`game_mode` ist ein Enum: `ranked`, `flex`, `urf`, **`aram`**, `nexus_blitz`. Gemessen für Darius,
`game_mode=aram` (der `position`-Parameter wird verlangt, aber ignoriert — mid/adc/top liefern
identische Zahlen):

- `summary.average_stats`: 35.080 Spiele, Winrate 0,50
- `core_items`: Trinity Force / Sundered Sky / Sterak's Gage — **1.231 Spiele, 640 Siege**
- Counter gibt es dort nicht (`counters_meta`: „Insufficient matchup sample")

Dazu `lol_list_aram_augments` (Augment-Statistik je Champion, ab Tier 3) und `lol_list_items` mit
`map`-Filter. Eine ARAM-Tierlist über alle Champions auf einen Schlag gibt es **nicht** — die
ARAM-Zahlen müssten je Champion einzeln geholt werden (173 Aufrufe, wie die Analyse heute schon).

## Der vierte Befund: es gibt einen Build ohne Gegner

`lol_get_champion_analysis` liefert nicht nur Statistik, sondern den kompletten Build:
`runes`, `starter_items`, `boots`, `core_items`, `last_items`, `fourth/fifth/sixth_items`,
`mythic_items`, `summoner_spells`, `skills.order`, `skill_masteries`, `skill_combos`.

Gemessen für Darius Top, `tier=platinum`:

| Teil | Auswahl | Stichprobe |
|---|---|---:|
| Kern | Youmuu's Ghostblade / Dead Man's Plate / Death's Dance | 6.807 Spiele, 3.930 Siege |
| Schuhe | Beschichtete Stahlkappen | 49.535 Spiele, 24.376 Siege |
| Runen | Eroberer / Triumph / Legende: Tatendrang / Letztes Gefecht | 25.872 Spiele, 12.747 Siege |

Zum Vergleich: der **matchup-genaue** Build aus `lol_get_lane_matchup_guide` (Darius vs Jax, das
Fixture im Test) nennt als meistgespielten Kern ein Set mit **11 Spielen**.

Damit fällt eine Annahme, auf der der Blind-Pick-Pfad beruht: „OP.GG hat keinen Build ohne Gegner"
stimmt nicht. Der Ersatzgegner (`StandInOpponent`) ist nicht nötig, um überhaupt einen Build zu
zeigen — und für dünne Matchups gibt es jetzt eine belastbare Vergleichszahl.

## Was die Tierlist kann und nicht kann

`lol_list_lane_meta_champions` nimmt nur `lang`, `position`, `desired_output_fields` — **kein**
`tier`, **kein** `game_mode`, **keine** Region. Sie liefert dafür `play` **und** `win` je Zeile,
also exakte Winrates (deshalb rechnet der Builder sie aus `win/play`).

Rangspezifische Lane-Zahlen gibt es nur über die Analyse je Champion, und dort ist
`positions[].stats.win_rate` auf zwei Nachkommastellen gerundet. Exakt rekonstruierbar ist sie
über `positions[].roles[].stats.{play,win}` — die Rollen-Aufteilung deckt 98,2 % der Spiele ab.
Gegenprobe an Darius Top im Standard-Bracket: Rekonstruktion **49,90 %**, exakter Wert der
Tierlist **49,80 %** (46.105 / 92.578). Abweichung 0,10 Punkte — ein Zehntel dessen, was die
Rundung des Feldes kostet.

Ebenfalls gerundet, in beiden Quellen: `pick_rate`, `ban_rate`, `role_rate` (zwei Nachkommastellen).

## Werkzeuge, die wir nicht nutzen und die etwas könnten

- `lol_list_aram_augments` — ARAM-Augments je Champion.
- `lol_list_items` (`map`-Filter) — Item-Daten, u. a. für die ARAM-Karte.
- `lol_list_champion_details` — Fähigkeiten, Werte, Tipps für bis zu 10 Champions je Aufruf.
- `lol_list_champion_leaderboard`, `lol_get_summoner_profile`, `lol_list_summoner_matches`,
  `lol_get_pro_player_riot_id` — alle an einen Spielernamen gebunden. Kommen nicht in Frage:
  fremde Spielernamen sind hier ausgeschlossen, und eine Gewichtung nach eigenem Champion-Pool
  ebenfalls.

## Wie man das nachprüft

```powershell
dotnet run --project src\DraftPilot.Tools -- opgg tools
dotnet run --project src\DraftPilot.Tools -- opgg tools lol_get_champion_analysis
dotnet run --project src\DraftPilot.Tools -- opgg try lol_get_champion_analysis `
    game_mode=ranked champion=DARIUS position=top tier=gold `
    desired_output_fields=@data.summary.positions[].name,data.summary.positions[].stats.play
```

Ein Wert mit `@` davor ist eine Liste; `--out datei.json` schreibt die vollständige Antwort.
