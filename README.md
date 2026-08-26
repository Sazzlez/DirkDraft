# DraftPilot

Pick- und Ban-Assistent für League of Legends. Liest den Champ Select live mit, schätzt die Lanes
des Gegners und schlägt Picks und Bans vor — für dich und für jeden Mitspieler, der am Zug ist.

## Bauen und starten

```powershell
.\publish.ps1
```

Baut nach `build\DraftPilot\` und legt eine Verknüpfung auf den Desktop. Doppelklick startet das
Tool. Es bleibt im Infobereich; Rechtsklick auf das Symbol: *Öffnen · Daten aktualisieren · Beenden*.

Voraussetzung ist das .NET 9 Desktop Runtime, das hier schon installiert ist. Der Build bleibt
dadurch bei etwa 0,7 MB statt der ~120 MB einer selbstenthaltenden Variante.

## Erster Schritt: Daten holen

Beim ersten Start gibt es noch keine Meta-Daten. Ein Klick auf **Daten aktualisieren** holt
Tierlist, Counter und Synergien und legt sie lokal ab. Das dauert etwa vier Minuten und ist der
**einzige** Weg, auf dem das Tool ins Netz geht — es gibt keinen Auto-Update, keinen Update-Check
beim Start und keinen Hintergrund-Timer.

Der Knopf ist während eines laufenden Champ Select gesperrt, damit mitten im Draft kein Netzwerk-
oder Speicher-Peak entsteht. Bricht ein Update ab, bleibt der vorherige Stand vollständig nutzbar.

## Wo Daten liegen

| Pfad | Inhalt |
|---|---|
| `build\DraftPilot\data\` | Kuratierte Dateien, schreibgeschützt, werden mitgeliefert |
| `%LOCALAPPDATA%\DraftPilot\data\` | `snapshot.json` — vom Update-Knopf erzeugt |
| `%APPDATA%\DraftPilot\` | `settings.json` |

Nichts davon verlässt den Rechner. Keine Telemetrie, keine Datenbank, kein Cloud-Sync.

## Bedienung

- **Gegner-Block**: pro Slot der Champion, die geschätzte Lane und die Konfidenz. Unter 60 % wird die
  Zahl orange — dann lohnt ein eigener Blick. Über das Dropdown legst du eine Lane von Hand fest;
  die übrigen Slots werden sofort neu zugeordnet.
- **Dein Team**: klickbar. Die Empfehlungsliste folgt normalerweise dem Spieler am Zug; ein Klick
  schaltet auf einen anderen Slot, das Pin-Symbol oben hält sie dort fest.
- **Empfehlungen**: Score, Begründungs-Chips, und über den Pfeil rechts die vollständige
  Aufschlüsselung jedes Terms. Pick oder Ban richtet sich nach der laufenden Action.
- **Presets**: *Meta* gewichtet die Tierlist, *Counter* das Lane-Matchup, *Teamcomp* Synergien und
  Comp-Bedarf.

## Kommandozeile

Für Entwicklung und Diagnose:

```powershell
dotnet run --project src\DraftPilot.Tools -- watch
dotnet run --project src\DraftPilot.Tools -- record aufzeichnung.jsonl
dotnet run --project src\DraftPilot.Tools -- replay aufzeichnung.jsonl max
dotnet run --project src\DraftPilot.Tools -- probe
dotnet run --project src\DraftPilot.Tools -- events
dotnet run --project src\DraftPilot.Tools -- scrub aufzeichnung.jsonl
dotnet run --project src\DraftPilot.Tools -- update
dotnet run --project src\DraftPilot.Tools -- icons
dotnet run --project src\DraftPilot.Tools -- inspect
dotnet run --project src\DraftPilot.Tools -- recommend mid "Jax,Elise,Syndra" "Aatrox,LeeSin"
```

`record` und `replay` sind der Grund, warum die Logik ohne laufenden Client entwickelt und getestet
werden kann.

`probe` prüft die Verbindung schichtweise — Installation, Lockfile, Zertifikatskette, HTTP, Socket.
Von außen sehen alle fünf Schichten gleich aus („nicht verbunden"), und genau das hat schon einmal
Zeit gekostet. `events` schreibt jedes Client-Event roh mit, wenn man wissen muss, *warum* das
Werkzeug einen Zustand annimmt.

### Aufzeichnungen enthalten keine persönlichen Daten

Die Session des Clients trägt deutlich mehr, als dieses Werkzeug liest: Summoner-Name und Tag-Line,
`puuid`, `summonerId` und unter `chatDetails` ein **gültiges JWT für den Champ-Select-Chat**. Eine
Aufzeichnung ist genau die Datei, die man an einen Fehlerbericht hängt, deshalb entfernt `record`
diese Felder von sich aus. Keines davon wird von der App gelesen.

Ältere Aufzeichnungen räumt `scrub <datei>` nachträglich auf — überschreibt die Datei, oder schreibt
mit einem zweiten Argument in eine neue.

## Oberfläche prüfen und Icon bauen

Das Fenster kann sich selbst in eine PNG rendern. Ohne das ließe sich an Layout und Kontrast nur
raten:

```powershell
dotnet run --project src\DraftPilot.App -- --demo aufzeichnung.jsonl --frames 5 --screenshot ui.png
```

`--frames N` hält die Wiedergabe nach N Frames an, damit ein bestimmter Draft-Moment im Bild landet.
Neben `ui.png` entsteht `ui-dropdown.png`: ein Popup lebt in einem eigenen Fenster und taucht in
einer Aufnahme des Hauptfensters nicht auf, muss also getrennt erfasst werden.

Das App-Icon ist generiert, nicht gezeichnet — damit es reproduzierbar bleibt:

```powershell
.\tools\make-icon.ps1 -PreviewPath icon-preview.png
```

Schreibt `src\DraftPilot.App\Assets\app.ico` mit acht Ebenen von 16 bis 256 px und optional eine
vergrößerte Prüfansicht. Die kleinen Ebenen haben absichtlich dickere Balken: Proportionen, die bei
256 px stimmen, werden bei 16 px zu drei grauen Flecken.

## Was das Tool bewusst nicht tut

- **Keine Schreibzugriffe auf den Client.** Kein Auto-Hover, kein Auto-Ban, kein Auto-Accept. Du
  klickst selbst.
- **Keine Namen fremder Spieler.** Riots Richtlinie verlangt das im Champ Select, und das Tool
  braucht sie ohnehin nicht — es zeigt `Mitspieler 3` und `Gegner 2`.
- **Keine Cooldown- oder Ult-Timer.** Ebenfalls Richtlinie.
- **Keine Gewichtung nach deinem Champion-Pool.** Mastery und persönliche Winrate werden nicht
  abgefragt. Bewertet wird die Draft-Situation, nicht deine Gewohnheit. Nur Champions, die du nicht
  freigeschaltet hast, fallen aus deiner eigenen Liste — die kannst du in den Einstellungen wieder
  einblenden.
- **Kein Autostart, kein Dienst.** Das Tool läuft, wenn du es startest.

## Grenzen der Datenlage

Das solltest du wissen, bevor du den Empfehlungen zu viel zutraust:

- **Tierlist und Lane-Verteilung sind solide.** Die Rollen-Anteile, aus denen die Lane-Vorhersage
  rechnet, sind Verhältnisse großer Zahlen und entsprechend belastbar.
- **Counter-Daten sind dünn.** Die Quelle liefert pro Champion und Lane nur die auffälligsten drei
  Gegner, insgesamt etwa 600 Kanten für 147 von 173 Champions — keine vollständige Matrix. Wo keine
  Daten vorliegen, wirkt das Preset *Counter* schlicht nicht, und die Tierlist dominiert.
- **Kleine Stichproben werden gedämpft.** Ein Duo mit 78 % Winrate über 32 Spiele ist Rauschen. Jede
  Rate läuft durch eine Bayes-Glättung, bevor daraus ein Score wird. Deshalb sehen die angezeigten
  Winrates flacher aus als auf einer Statistikseite — sie sind dafür belastbarer.
- **Die kuratierten Traits deckt nicht alle Champions ab.** Engage, Peel, CC und Scaling stehen für
  166 der 173 Champions in `data\champion_traits.json`. Für die übrigen — meist ganz neue — feuern
  die davon abhängigen Comp-Regeln nicht, statt zu raten. Die Anzeige nennt die Trait-Abdeckung,
  wenn sie unter 100 % liegt. Die Datei ist normales JSON und darf gerne korrigiert werden.
- **Der Sitzplatz-Prior ist abgeschaltet, weil die Annahme dahinter gemessen falsch war.** Die Idee:
  der Client listet Teams in kanonischer Rollenreihenfolge, also wäre Sitzplatz 2 Mid und Sitzplatz 3
  ADC. Eine aufgezeichnete 5v5-Turnierentwurf-Session sagt etwas anderes — der Client listete
  `top, jungle, bottom, middle, utility`. Sitzplätze 0, 1 und 4 passten, 2 und 3 waren getauscht.
  Die Messung stammt aus einem Custom Game, wo Positionen nicht von der Spielsuche vergeben werden,
  klärt also nicht, was Ranked Solo/Duo tut — sie klärt nur, dass man die kanonische Reihenfolge
  nicht einfach annehmen darf. Und die Reihenfolge, auf die es ankommt, ist die des **Gegner**-Teams,
  die der Client nie verrät; die lässt sich nur nach einem Spiel gegen die Match-History prüfen.
  Deshalb steht `data\pick_order_priors.json` auf gleichverteilt: kein Hinweis, statt eines Hinweises,
  von dem bekannt ist, dass er teils falsch liegt. Die Vorhersage stützt sich damit allein auf die
  Rollenverteilung des Champions — den belastbaren Teil der Daten.

## Aufbau

| Projekt | Zweck |
|---|---|
| `DraftPilot.Core` | LCU-Anbindung, Draft-Zustand, Lane-Vorhersage, Comp-Analyse, Recommender. Keine UI, keine ausgehenden Netzzugriffe. |
| `DraftPilot.Meta` | Der einzige Netzwerkcode: OP.GG-Abruf und Snapshot-Erzeugung. |
| `DraftPilot.App` | Das WPF-Fenster. |
| `DraftPilot.Tools` | Kommandozeile für Aufzeichnung, Wiedergabe, Update, Diagnose. |

## Wenn etwas nicht geht

- `%LOCALAPPDATA%\DraftPilot\data\crash.log` — unbehandelte Ausnahmen.
- `%LOCALAPPDATA%\DraftPilot\data\binding-trace.log` — Fehler in der Oberfläche. Setze
  `DRAFTPILOT_TRACE=1`, um das auch im Release-Build einzuschalten.
- Findet das Tool League nicht, trage den Pfad zur `lockfile` in `settings.json` unter
  `lockfilePath` ein.
