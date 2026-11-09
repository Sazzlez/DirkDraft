# Referenzdaten (Golden) für DirkDraft 2.0

Erzeugt vom 1.5-Modell mit

```powershell
dotnet run --project src\DraftPilot.Tools -- golden
```

Der TypeScript-Kern muss aus denselben Eingaben dasselbe berechnen:

- **Reihenfolge** jeder Liste identisch,
- **Werte** (Score, Fehlerbalken, Kriterien in Punkten, Lane-Konfidenzen, Draft-Chance) auf
  **±0,05 Prozentpunkte**,
- **Gleichauf-Gruppen** (`tied`) und **Nachkommastellen** (`decimals`) identisch.

Jede Abweichung braucht eine Begründung im Commit und im CHANGELOG.

## Inhalt

| Datei | Eingabe | Ausgabe |
|---|---|
| `snapshot.json` | echter Snapshot (OP.GG, alle Ränge, Patch 16.22, gebaut mit 1.5.0) | — |
| `aram-stats.json` | feste ARAM-Tabelle (1.5 holt sie live; hier einmal erzeugt, auf ganze Prozent gerundet wie OP.GG) | — |
| `drafts/*.json` | erfundene Drafts: die `recommend`-Beispiele aus README und `docs/modellpruefung.md`, je Lane ein Blind Pick, volle Boards | Lane-Vorhersage, Picks und Bans mit Kriterien, Hover, Comp, Draft-Chance, Lane-Matchups |
| `recordings/*.json` | jede Aufzeichnung aus `../recordings/`, Frame für Frame mit demselben Neuer-Draft- und Zugregel-Ablauf wie das Fenster | dasselbe je Frame, dazu die ARAM-Bank |
| `builds/*.json` | OP.GG-Fixtures, Variantenwahl (Wilson-Untergrenze), Skill-Reihenfolge, Augment-Filter | geparste Pläne und Auswahl |

Gemeinsame Daten: `data/champion_traits.json` und `data/pick_order_priors.json` im Repository.
Kein Netz: Live-Kanten aus dem Draft (Counter, vollständige Duell-Listen) sind nicht enthalten —
die Referenz beschreibt das Modell auf dem gespeicherten Bestand.

`state.allies/enemies` folgen dem C#-Typ `DraftSlot`, Enums als camelCase-Strings, `NaN` als
`"NaN"`. `fetchedAtUtc` in den Plänen gehört nicht zum Vergleich.

## Neu in 2.1

Was das Modell seit 2.1 bewusst anders macht als 1.5. Neue Felder stehen in `ADDED_IN_2_1`
(`harness.ts`) und dürfen in den Referenz-Frames fehlen; jeder andere Unterschied zählt weiter.
Geänderte Werte sind in den Referenzdateien angepasst, mit Begründung hier, im Commit und im
CHANGELOG.

| Feld / Änderung | Warum |
|---|---|---|
| `state.allyBanIntents` (neues Feld) | Ban-Hovers der Teammates; der Ban-Vorschlag lässt aus, was ein anderer Teammate gerade bannt. In keiner Referenz-Aufzeichnung kommt ein solcher Hover vor, die Werte bleiben gleich. |
| `gap` (neues Feld) | Was dem eigenen Team für den Sitz fehlt, der als Nächstes pickt — ohne dessen eigenen Hover. Nur Anzeige (Comp-Kachel), kein Score ändert sich. |
