# Übergabe vom 11.11.2026 — für die Arbeit am Freitag (Party-Erkennung, Phase 2)

Diese Datei ist für Claude Code im Projektordner. Sie sagt, was am 11.11.2026 außerhalb dieses
Ordners geändert wurde (drei Commits, als Patch eingespielt), was davon noch ungeprüft ist und was in
den nächsten Phasen mitzunehmen ist. Details und Zahlen: `docs/modellpruefung.md`, Messung 16 bis 20,
und `CHANGELOG.md` unter 2.1.0.

## 1. Was geändert wurde

| Commit | Inhalt | Dateien |
|---|---|---|
| Build-Wahl, nächster Kauf und situative Items korrigiert | Meistgespielte Option führt; eine andere nur, wenn klar besser (`LEAD_SIGMA`) und bei Schuhen, Kern, Items 4–6 auch im selben Spielstand; Mejai's nie (`boughtFromAhead`). κ-Schätzung entzerrt. Situative Items: Klasse zählt in der Lage und sonst gleich, fünf eigene Käufe nötig. | `app/src/core/riotBuilds.ts`, `situational.ts`, `game.ts`, `live/session.ts` |
| Einkaufsliste im Spiel und Kaufzeitpunkt der Schuhe | „Jetzt kaufen“ als Liste fürs aktuelle Gold (`basketOf`). Sammler zählt `bootsAfter` und `bootsWorth`; die App liest sie (`bootsAfterItems`, `bootsWorth` im Plan). | `app/src/core/game.ts`, `riotBuilds.ts`, `view.ts`, `data/gameView.ts`, `ui/game/GameArea.svelte`, `src/DraftPilot.Core/Data/RiotMatch.cs`, `RiotBuildStats.cs` |
| Situative Items übernehmen ihren Platz im Build von selbst | `rateSuggestions`, `situationalBuild`, `SwapTracker`; Markierung im Pfad, in der Liste und in der Kachel. | `app/src/core/situational.ts`, `game.ts`, `live/session.ts`, `ui/game/GameArea.svelte` |

**Geänderte Regel des Nutzers (11.11.2026):** Situative Items waren „Alternative, nie Ersatz“. Das gilt
nicht mehr: Sie übernehmen von selbst einen Platz im Build, markiert als situativ. Nicht zurückdrehen.
Alle anderen festen Regeln gelten unverändert (keine Namen, keine Timer, nichts automatisch im Client,
Pool und Nachspiel bleiben draußen).

## 2. Was noch ungeprüft ist — zuerst erledigen

Die Änderungen entstanden in einer Sitzung ohne .NET, ohne `node_modules` und ohne Client.

1. `dotnet test` — der C#-Teil (Sammler) wurde **nie kompiliert**. Der Workflow
   `collect-builds.yml` baut aus `master`; ein Compile-Fehler stoppt die Datensammlung.
2. `cd app; npx vitest run; npm run check` — die TS-Tests liefen nur unter Bun (624 grün); die
   Svelte-Komponente `GameArea.svelte` wurde nie geprüft oder gerendert.
3. `npm run shots` und die Spielszene ansehen: Einkaufsliste mit mehreren Käufen, oranger Punkt im
   Build-Pfad, Kachel „Situative Items“ mit „im Build“ und „Alternative“.
4. Live-Test im Spiel nur nach Rückfrage, wie immer.

## 3. Freitag: Party-Erkennung (Wunsch des Nutzers vom 11.11.2026)

Ziel: Der Team-Modus plant nur die Premade-Plätze gemeinsam und schaltet sich selbst, statt nur für
volle Fünfer von Hand. Auslöser ist die Testrunde des Nutzers im Duo.

1. **Quelle entscheiden.** `dotnet run --project src/DraftPilot.Tools -- party` im Champ Select, einmal
   im Duo (Solo/Duo) und einmal im 3er-Flex. Offen: Antwortet die Lobby im Champ Select noch, und sind
   die PUUIDs der Randoms in Solo/Duo gefüllt? Stimmen Lobby und Sichtbarkeit überein, reicht die
   Session und der Lobby-Abruf entfällt.
2. **Erkennung in Rust, vor dem Scrubber** (`app/src-tauri/src/lcu/scrub.rs` entfernt `puuid` und
   `nameVisibilityType`): dort vergleichen und je Platz nur `inParty: true/false` in die bereinigte
   Session schreiben. Keine Namen, keine IDs in der Seite.
3. **Kern:** `engine.ts` bekommt die Party-Plätze; `planTeam` und `planTeamBans` filtern darauf.
   Randoms bleiben wie heute (Hover gilt als vergeben, Klick zeigt ihre Liste). Einstellung
   „Team-Modus“ wird zu automatisch / aus. („Früh dran — Pick-Reihenfolge tauschen?“ ist seit dem
   11.11.2026 auf Nutzerwunsch entfernt und kommt nicht zurück.)
4. **Anzeige:** Markierung der Premade-Plätze in `TeamColumn` (ohne Namen), Etikett „dein Duo“ an der
   Synergie-Begründung, Team-Plan-Kachel mit „geplant für euch 3“.
5. **Rechnung unverändert:** Die Partner-Synergie wird beschriftet, nicht höher gewichtet (das wäre
   ein ungemessenes Gewicht). Golden-Tests bleiben wie sie sind; neue Aufzeichnungen mit Party-Flag
   für Duo, 3er und 5er.

## 4. Phase 2 (Messung an ~150.000 Spielen): zusätzlich mitmessen

Zu dem, was `docs/draft-konzept.md` für Phase 2 vorsieht, kommen aus dem 11.11. diese Nachmessungen.
Ergebnisse jeweils in die genannte Messung in `docs/modellpruefung.md` eintragen.

| Messung | Was nachzumessen ist |
|---|---|
| 16 Build-Wahl | κ je Slot und die Abweichungen vom Meistgespielten auf der vollen Menge. Offen: Innerhalb eines Slots gewinnt die 1.000 Gold teurere Option um rund 3 Punkte seltener (Kern, Item 4, Schuhe) — den Preis als eigene Größe messen. Mit den Spielzeilen prüfen, ob die 13 Abweichungen auf zurückgehaltenen Spielen halten. |
| 18 Offene Counter | Der Zufallsvergleich (markierte Counter gegen erwartete) mit der vollen Menge und über drei Patches. Erst wenn die markierten deutlich über der Zufallserwartung liegen, die Schwelle `COUNTER_MINIMUM_EDGE` oder die Glättung je Lane anfassen; bis dahin erscheinen die Hinweise zur Pick-Reihenfolge nicht, und das ist richtig so. |
| 19 Kaufzeitpunkt der Schuhe | Sobald `bootsAfter` und `bootsWorth` einige Tage gezählt sind: Verteilungen je Lane, Anteil der Champion-Lanes mit einer anderen Stelle als „nach dem ersten Item“, und ob „Stiefel zuerst, sobald der Itemwert die Stufe der Hälfte erreicht“ zu dem passt, was Spieler mit knappem Gold kaufen. Der Nutzer hat offen gelassen, ob Stiefel oder das größte Teil Vorrang haben soll (`basketOf`, `bootsDue`). |
| 20 Situative Items im Build | Schwachstelle: Lagen aus Gegner-Items messen zum Teil nur „später im Spiel“. Mit den Spielzeilen prüfen, ob ein Item in der Lage öfter gekauft wird als zur selben Spielzeit ohne die Lage. Wenn nicht: Übernahme auf die Lagen aus der Aufstellung und die Schuhe begrenzen (`ITEM_SITUATIONS`). |

Phase 3 baut auf denselben Dateien auf (`riotBuilds.ts`, `situational.ts`, `game.ts`, `session.ts`).
Vor Änderungen dort die Tests vom 11.11. lesen (`app/tests/model/ownData.test.ts`,
`situational.test.ts`, `game.test.ts`): Sie halten fest, welche Fälle die neuen Regeln abdecken.

## 5. Nicht vergessen

- Die Build-Fehler aus Messung 16 sind in 2.0.0 live (Jinx: Boots of Swiftness). Der erste der drei
  Commits behebt sie allein und ließe sich als 2.0.1 vorziehen — Release nur mit OK des Nutzers.
- Die Zählung des Kaufzeitpunkts beginnt erst, wenn der Sammler aus `master` mit den Änderungen läuft,
  und braucht 30 Spiele je Champion-Lane. Bis dahin: Paar nach dem ersten Item, Stiefel mit dem Gold,
  das nach den Teilen bleibt.
