# DirkDraft

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

## Weitergeben und aktualisieren

Freunde bekommen das Tool als Installer, nicht als Ordner: `DirkDraft-win-Setup.exe` von der
Releases-Seite (https://github.com/Sazzlez/DirkDraft/releases). Ein Doppelklick installiert es ins
eigene Benutzerprofil, legt eine Verknüpfung an und startet es — die .NET-Runtime ist eingebaut, es
muss vorher nichts installiert werden. Windows warnt beim ersten Start, weil der Installer nicht
signiert ist („Weitere Informationen" → „Trotzdem ausführen"); ein Signaturzertifikat kostet Geld
und lohnt für einen Freundeskreis nicht.

Neue Versionen holen sich installierte Kopien selbst: Beim Start fragt das Tool einmal bei GitHub
nach, ob eine neuere Version vorliegt. Gibt es eine, steht im Fenster „Version 1.1.0 ist da" mit dem
Knopf „Jetzt aktualisieren" — erst der Klick lädt und installiert, nichts passiert im Hintergrund.
Ist die Kopie aktuell, sagt die Versionszeile im Startbildschirm das auch („auf dem neuesten
Stand"). Was sich je Version geändert hat, steht in `CHANGELOG.md`.
Eine Kopie aus `build\DirkDraft\` oder aus dem Entwicklungs-Build prüft nicht; sie kann sich nicht
an Ort und Stelle ersetzen.

Eine neue Version veröffentlichen:

```powershell
.\release.ps1 -Version 1.1.0
```

Trägt die Version ins Projekt, lässt die Tests laufen, baut mit eingebauter Runtime, packt den
Installer samt Delta-Paketen (`vpk`), checkt den Versionssprung als „Release 1.1.0" ein, setzt den
Tag `v1.1.0`, schiebt beides nach GitHub und lädt dann erst das Release hoch — so zeigt der Tag
genau auf den gebauten Stand. Alles andere muss vorher eingecheckt sein; sonst bricht das Skript
ab, bevor es etwas anfasst. Der Ordner `build\Releases\` bleibt absichtlich stehen: Aus dem
letzten Vollpaket dort entsteht das kleine Delta-Paket für installierte Kopien. Voraussetzungen
einmalig: `dotnet tool install -g vpk`, `winget install GitHub.cli --scope user` und ein
`gh auth login`. Der Token kommt aus dieser Anmeldung und wird nirgends gespeichert. `-NoUpload`
baut nur den Installer nach `build\Releases\`, wenn du ihn einmal von Hand weitergeben willst. Die
Versionsnummer muss steigen, sonst sehen installierte Kopien nichts Neues.

## Erster Schritt: Daten holen

Beim ersten Start gibt es noch keine Meta-Daten. Ein Klick auf **Daten aktualisieren** holt
Tierlist, Counter und Synergien und legt sie lokal ab. Das dauert etwa vier Minuten. Es gibt keinen
Auto-Update, keinen Update-Check beim Start und keinen Hintergrund-Timer.

Der Knopf ist während eines laufenden Champ Select gesperrt, damit mitten im Draft kein Netzwerk-
oder Speicher-Peak entsteht. Bricht ein Update ab, bleibt der vorherige Stand vollständig nutzbar.

Der zweite und letzte Netzweg sind die **Draft-Daten**: Sobald im Champ Select ein Champion
aufgedeckt wird, holt das Tool dessen aktuelle Counter-Zahlen, und nach deinem eigenen Pick den
passenden Build. Das sind höchstens sechs kleine Abrufe pro Draft — einer je Gegner, einer für den
Build —, jeder nur einmal, alles lokal gecacht. Ohne diese Abrufe wäre die Vorratsmatrix pro
Champion und Lane nur etwa drei Counter tief, und genau die fünf Gegner, gegen die du wirklich
spielst, wären meistens nicht darin. Außerhalb eines Champ Select passiert nichts.

## Wo Daten liegen

| Pfad | Inhalt |
|---|---|
| `build\DraftPilot\data\` | Kuratierte Dateien, schreibgeschützt, werden mitgeliefert |
| `%LOCALAPPDATA%\DraftPilot\data\` | `snapshot.json` — vom Update-Knopf erzeugt |
| `%APPDATA%\DraftPilot\` | `settings.json` |

Die Datenordner (und die Projektnamen im Code) behalten den Arbeitstitel *DraftPilot* — eine
Umbenennung dort würde nur eine Datenmigration erzwingen, ohne dass irgendwer sie je sieht.

Nichts davon verlässt den Rechner. Keine Telemetrie, keine Datenbank, kein Cloud-Sync.

Der Bestand pflegt sich selbst: „Daten aktualisieren" **überschreibt** Snapshot und Namen atomar,
und beim Start räumt das Tool im Hintergrund auf — Build-Pläne fremder Patches oder älter als zwei
Wochen, verwaiste `.tmp`-Dateien und zu groß gewordene Logs (gekappt auf die neuesten Einträge).
Nur die Icon-Ordner bleiben unangetastet: Icons veralten nicht, und sie zu löschen hieße nur, sie
wieder herunterzuladen.

## Bedienung

- **Gegner-Block**: pro Slot der Champion, die geschätzte Lane und die Konfidenz. Unter 60 % wird die
  Zahl orange — dann lohnt ein eigener Blick. Über das Dropdown legst du eine Lane von Hand fest;
  die übrigen Slots werden sofort neu zugeordnet.
- **Dein Team**: klickbar. Die Empfehlungsliste folgt normalerweise dem Spieler am Zug; ein Klick
  schaltet auf einen anderen Slot, das Pin-Symbol oben hält sie dort fest.
- **Empfehlungen**: Der Score ist eine **geschätzte Siegquote** — Lane-Stärke, Matchups, Synergien
  und Team-Bedarf werden als Log-Odds-Verschiebungen addiert und zurück in Prozent übersetzt
  (`ScoreModel.cs` dokumentiert jede Konstante). 50 % ist ausgeglichen; Unterschiede unter einem
  halben Punkt sind Rauschen, und liegen die Spitzenkandidaten gleichauf, sagt die Kopfzeile das.
  Jeder Chip erklärt sich beim Überfahren mit der Maus; der Pfeil rechts klappt die Aufschlüsselung
  auf — pro Kriterium ein Urteil in Worten, ein Balken und der Beitrag in Prozentpunkten. Kriterien
  ohne Datengrundlage stehen dort als *keine Daten*, nicht als Null. Bans rechnen dieselbe Einheit
  aus Gegnersicht: Stärke mal Wahrscheinlichkeit, dass der Champion überhaupt genommen wird.
- **Dein Build**: nach deinem Pick Runen, Shards, Startitems, Schuhe und Kern-Items für genau dieses
  Matchup, plus Hinweise zur gegnerischen Aufstellung. **Runen übertragen** legt die Seite als
  „DirkRunen · …" im Client an und wählt sie aus — der einzige Schreibzugriff des Tools, nur auf
  Klick, und gelöscht wird nur die eigene Seite (eine fremde erst nach Nachfrage mit Namen).
- **Im Spiel**: sobald das Spiel startet, zeigt das Fenster den Build groß — Item-Bilder in
  Kaufreihenfolge (Start → Schuhe → Kern → Spät), Beschwörerzauber und die Skill-Tabelle für
  Stufe 1–18. Die Stufen 16–18 liefert die Quelle nicht; sie sind abgeleitet und blasser
  dargestellt. Das Fenster ist „immer oben", also im randlosen Fenstermodus über dem Spiel sichtbar;
  im exklusiven Vollbild versteckt Windows fremde Fenster prinzipbedingt.

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

Das App-Icon ist generiert, nicht gezeichnet — damit die nächste Änderung eine Bearbeitung ist,
keine Neuzeichnung:

```powershell
.\tools\make-app-icon.ps1 -PreviewDir icon-preview
```

Schreibt `src\DraftPilot.App\Assets\app.ico` (Kaffeetasse auf Blau-Grün-Verlauf, acht Ebenen von
16 bis 256 px) und optional eine PNG-Vorschau je Größe. Jede Größe wird frisch aus der Vektorform
gezeichnet statt aus einer großen Bitmap herunterskaliert: Eine Haarlinie wird beim Skalieren
matschig, und 16 px ist die Größe, in der ein Icon tatsächlich angeschaut wird.

## Was das Tool bewusst nicht tut

- **Kein Auto-Hover, kein Auto-Ban, kein Auto-Accept.** Du klickst selbst. Der einzige
  Schreibzugriff auf den Client ist der Runen-Import, und der passiert ausschließlich auf deinen
  Klick. Er nutzt dieselbe inoffizielle Client-Schnittstelle wie Blitz, Porofessor oder U.GG —
  Riot kann sie jederzeit ändern, dann scheitert der Import sichtbar statt still.
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
- **Die Vorrats-Counter-Matrix ist dünn.** Die Quelle liefert pro Champion und Lane nur die
  auffälligsten drei Gegner — keine vollständige Matrix. Genau dafür gibt es die automatischen
  Draft-Abrufe: für die fünf real aufgedeckten Gegner kommen dichte, aktuelle Zahlen nach. Wo
  trotzdem nichts vorliegt, sagt die Aufschlüsselung „keine Daten" statt zu raten.
- **Kleine Stichproben werden gedämpft.** Ein Duo mit 78 % Winrate über 32 Spiele ist Rauschen. Jede
  Rate läuft durch eine Bayes-Glättung, bevor daraus ein Score wird. Deshalb sehen die angezeigten
  Winrates flacher aus als auf einer Statistikseite — sie sind dafür belastbarer.
- **Vier Konstanten des Scores sind Schätzungen.** Wie stark Gegner außerhalb der eigenen Lane,
  die OP.GG-Stufe, Synergien und der Team-Bedarf zählen, steht begründet, aber unkalibriert in
  `ScoreModel.cs`. Kalibrieren ließe sich das erst an echten Ranked-Aufzeichnungen
  (`Tools -- record`) — offene Aufgabe. Die Winrate-Anteile selbst sind gemessen, nicht geschätzt.
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
