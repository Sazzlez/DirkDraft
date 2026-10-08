# DirkDraft

Pick- und Ban-Assistent für League of Legends. Liest den Champ Select live mit, schätzt die Lanes
des Gegners und schlägt Picks und Bans vor — für dich und für jeden Teammate, der am Zug ist.

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
passenden Build. Das sind ein paar kleine Abrufe pro Draft — einer je Gegner, einer für den Build,
gedeckelt auf sechzehn für den ganzen Draft —, jeder nur einmal, alles lokal gecacht. Ohne diese
Abrufe wäre die Vorratsmatrix pro Champion und Lane nur etwa drei Counter tief, und genau die fünf
Gegner, gegen die du wirklich spielst, wären meistens nicht darin. Jeder Abruf hat zehn Sekunden
Zeit — länger als eine Pick-Phase dauert, hilft nicht mehr —, und was dreimal scheitert, wird für
diesen Draft nicht mehr versucht; die Statuszeile sagt beides. Außerhalb eines Champ Select
passiert nichts.

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
- **Fenster**: Das X legt es in den Infobereich (Beenden per Rechtsklick auf das Symbol dort), der
  Strich daneben in die Taskleiste. Die Größe lässt sich an allen Rändern ziehen, mindestens
  1000 × 640, und wird gespeichert; was nicht hineinpasst, scrollt. Der Pin ganz links hält das
  Fenster über allen anderen — standardmäßig aus.
- **Dein Team**: klickbar. Die Empfehlungsliste folgt normalerweise dem Spieler am Zug; ein Klick
  schaltet auf einen anderen Slot, bis der Zug weitergeht.
- **Empfehlungen**: Der Score ist eine **geschätzte Winrate** — Lane-Stärke, Matchups, Synergien
  und Team-Bedarf werden als Log-Odds-Verschiebungen addiert und zurück in Prozent übersetzt
  (`ScoreModel.cs` dokumentiert jede Konstante). Alles darin ist eine gemessene Quote mit
  Stichprobe; OP.GGs Tier steht als Hinweis daneben und zählt nicht mit, weil es zur Hälfte
  dieselbe Winrate und zur Hälfte Pickrate ist. 50 % ist ausgeglichen; Unterschiede unter einem
  halben Punkt sind Rauschen, und liegen die Spitzenkandidaten gleichauf, sagt die Kopfzeile das.
  Jeder Chip erklärt sich beim Überfahren mit der Maus; der Pfeil rechts klappt die Aufschlüsselung
  auf — pro Kriterium ein Urteil in Worten, ein Balken und der Beitrag in Prozentpunkten. Kriterien
  ohne Datengrundlage stehen dort als *keine Daten*, nicht als Null. Unter jeder Zahl steht ihre
  Einordnung — *bester Pick der Liste*, *gleichauf mit Platz 1*, *knapp dahinter*, *deutlich
  dahinter* —, und eine Akzentkante links markiert jede Zeile, die von Platz 1 statistisch nicht zu
  trennen ist. Beides misst den Abstand zu Platz 1 derselben Liste, nicht den zu 50 %: auf einer
  starken Lane liegt jede Zeile über 50 %, und das sagt nichts darüber, welche man nehmen sollte.
  Die Comp beider Seiten zählt mit: was dem eigenen Team fehlt, und was der Pick gegen das
  ausrichtet, was der Gegner mitgebracht hat — offene Backline, Engage ohne Antwort, ein Team, das
  erst spät gefährlich wird. Diese Regeln sind der einzige Teil des Scores ohne Winrate-Grundlage
  und deshalb gedeckelt.
  Solange auf deiner Lane niemand aufgedeckt ist, trägt jede Zeile zusätzlich **„N offene
  Counter"**: Champions, die noch frei sind und gegen diesen Pick besser abschneiden als seine
  Gegner üblicherweise. Das ist die Frage, die ein früher Pick wirklich hat — und sie zählt
  bewusst *nicht* in die Prozentzahl hinein, denn ob der Gegner sie nimmt, sagen die Daten nicht.
  Der Tooltip nennt die Namen mit Winrate und Anzahl Games. Gezählt wird, was OP.GG als auffällige
  Gegner kennt; kein Chip heißt „keiner fällt auf", nicht „es gibt keinen". Bans rechnen dieselbe
  Einheit aus Gegnersicht: Stärke mal Wahrscheinlichkeit, dass der Champion überhaupt genommen wird.
- **Dein Hover**: Sobald du einen Champion hoverst — in der Planungsphase, während der Bans oder in
  deinem Zug —, steht über der Liste eine feste Zeile mit seiner geschätzten Winrate in diesem Draft
  und seinem Platz auf deiner Lane („Platz 11 von 58 auf Toplane · deutlich hinter Platz 1"). Das
  ist dieselbe Rechnung wie jede Zeile der Liste, also auch für Champions, die nicht unter den
  ersten acht stehen; der Tooltip schlüsselt sie auf. Hat OP.GG für den Champion auf dieser Lane
  keine Zahlen, steht das dort statt einer Prozentzahl. Nach deinem Lock nimmt „Dein Matchup" den
  Platz ein.
- **Dein Build**: nach deinem Pick Runen, Shards, Startitems, Boots und Core-Items, plus Hinweise
  zur gegnerischen Comp. Welche Quelle das ist, entscheidet eine Frage — **steht der Lane-Gegner?**
  - **Ja:** der Build für genau dieses Matchup. Unter Start/Boots und unter dem Core steht
    „auch gespielt": die übrigen Sätze, die OP.GG zu dieser Paarung mitliefert, mit Winrate und
    Anzahl Games. Nichts davon wird vorgezogen oder hervorgehoben — oben steht die häufigste Wahl
    dieses Matchups, und welche davon richtig ist, entscheidet das Spiel.
  - **Nein** — Blind Pick, Swiftplay, oder einfach die Minute, bevor der Gegner aufgedeckt ist:
    dann steht dort der Build, den dein Champion **auf dieser Lane insgesamt am besten fährt**.
    Kein geratener Ersatzgegner mehr. Der Unterschied ist die Stichprobe: gemessen an Darius Top
    trägt der Matchup-Kern gegen Jax 11 Games, der Lane-Build derselben Quelle je nach Bracket
    8.800 bis 40.000. Ein Chip sagt „Kein Matchup bekannt — bester Build der Lane"; sobald der
    Gegner steht, wird auf den Matchup-Build umgestellt.

  Unter 50 Games steht statt einer Prozentzahl „dünne Datenlage": dort ist ein Standardfehler rund
  sieben Punkte breit.
  **Runen übertragen** legt die Seite als „DirkRunen · …" im Client an und wählt sie aus — der
  einzige Schreibzugriff des Tools, nur auf Klick, und gelöscht wird nur die eigene Seite (eine
  fremde erst nach Nachfrage mit Namen).
- **Der Spielmodus** steht oben rechts, direkt neben der Phase, und wird aus der Queue-ID des
  Clients erkannt — Ranked Solo/Duo, Ranked Flex, ARAM, ARAM Mayhem und die übrigen. Er ist nicht
  Deko, sondern die Voraussetzung für alles darunter: Solo/Duo, Flex und ARAM sind bei OP.GG drei
  getrennte Datenbestände, und die Counter- und Build-Abrufe folgen dem erkannten Modus, nicht einer
  Einstellung. Wenn die gespeicherte Tierlist für eine andere Queue geholt wurde, sagt das Fenster
  das als Chip.
- **In ARAM und ARAM Mayhem** gibt es keine Lanes, also auch keine Lane-Empfehlungen:
  Lane-Beschriftungen und Matchup-Prozente bleiben weg. Stattdessen beantwortet die breite Spalte
  die Frage, die es dort wirklich gibt — **behalten oder tauschen?** Dein Champion steht zusammen
  mit allen auf der Bank in einer Liste, nach ARAM-Winrate sortiert, jeder mit seiner Stichprobe;
  darunter der Vergleich beider Comps. Dazu der **ARAM-Build** für den Champion, den du gerade
  hast.

  Die ARAM-Zahlen sind gröber als alles andere im Tool, und die Liste sagt es: OP.GG liefert für
  ARAM nur eine auf zwei Nachkommastellen gerundete Winrate — einen Prozentpunkt Auflösung — und
  der `positions`-Block, aus dem sich die Rift-Zahlen exakt rekonstruieren lassen, kommt dort leer
  zurück. Die Rundung steckt im Fehlerbalken, deshalb stehen fein getrennte Champions ehrlich als
  „gleichauf" da. Getauscht wird nichts automatisch; die Reroll-Anzahl steht in der Kopfzeile.

  Für **Mayhem** führt OP.GG keine eigenen Zahlen — Build, Runen und Bank-Winrates kommen aus dem
  normalen ARAM auf derselben Karte, und ein Stern hinter dem Modusnamen sagt genau das.
- **Im Spiel**: sobald das Spiel startet, zeigt das Fenster den Build groß — Item-Bilder in
  Kaufreihenfolge (Start → Boots → Core → Late), Summoner Spells und die Skill-Tabelle für
  Stufe 1–18. Die Stufen 16–18 liefert die Quelle nicht; sie sind abgeleitet und blasser
  dargestellt. Läuft das Spiel auf demselben Bildschirm, liegt das Fenster dahinter — es sei denn,
  der Pin in der Titelleiste ist an; dann ist es im randlosen Fenstermodus über dem Spiel sichtbar.
  Im exklusiven Vollbild versteckt Windows fremde Fenster prinzipbedingt.

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
dotnet run --project src\DraftPilot.Tools -- recommend top Jax "Ahri,Thresh" --live
dotnet run --project src\DraftPilot.Tools -- noise top Jax "Ahri,Thresh"
dotnet run --project src\DraftPilot.Tools -- priors
dotnet run --project src\DraftPilot.Tools -- coverage
dotnet run --project src\DraftPilot.Tools -- guidecheck
dotnet run --project src\DraftPilot.Tools -- settings
dotnet run --project src\DraftPilot.Tools -- settings tier gold
dotnet run --project src\DraftPilot.Tools -- opgg tools
dotnet run --project src\DraftPilot.Tools -- opgg try lol_get_champion_analysis game_mode=aram champion=DARIUS position=mid desired_output_fields=@data.core_items.ids_names,data.core_items.play,data.core_items.win
```

`record` und `replay` sind der Grund, warum die Logik ohne laufenden Client entwickelt und getestet
werden kann.

`recommend` druckt dieselbe Liste wie das Fenster, einschließlich Fehlerbalken und der Marke `=`
für jede Zeile, die von Platz 1 statistisch nicht zu trennen ist — eine Score-Änderung muss hier
lesbar sein, bevor jemand die Oberfläche aufmacht. `--terms` hängt jedem Eintrag seine Kriterien
einzeln an, dieselben Zahlen wie hinter dem Pfeil im Fenster. Ein `-` steht für „niemand": ein
leeres `""` verschluckt PowerShell, und die nächste Liste rutscht dann in den Gegner-Platz, ohne
dass es jemand merkt — `recommend top - "Ahri,Thresh"` meint Blind Pick mit zwei eigenen Picks.
`--live` holt vorher, was der Draft für jeden aufgedeckten Gegner live holt (Counter und
vollständige Duell-Liste), `--counters` nur die Counter — so lässt sich die Wirkung des Abrufs auf
die Liste direkt ablesen.

Die Messwerkzeuge hinter dem Modell, alle nur lesend: `noise` zieht denselben Draft wiederholt aus
seinen Stichproben und prüft, ob die Reihenfolge etwas bedeutet. `priors` misst die
Glättungsgewichte, indem es die Spiele jeder Zeile zufällig halbiert und mit der einen Hälfte die
andere vorhersagt. `coverage` sagt, für wie viele Kandidaten überhaupt eine Duell-Zahl gegen die
häufigsten Gegner vorliegt. `guidecheck` holt für 16 Champions die vollständigen Duell-Listen und
misst, ob sie zu den gespeicherten Zahlen passen und wie stark die Counter-Listen übertreiben (32
Abrufe). Die Ergebnisse stehen in `docs\modellpruefung.md`.

`probe` prüft die Verbindung schichtweise — Installation, Lockfile, Zertifikatskette, HTTP, Socket.
Von außen sehen alle fünf Schichten gleich aus („nicht verbunden"), und genau das hat schon einmal
Zeit gekostet. `events` schreibt jedes Client-Event roh mit, wenn man wissen muss, *warum* das
Werkzeug einen Zustand annimmt.

`opgg` fragt die Schnittstelle selbst: `opgg tools` listet alle Werkzeuge samt Argument-Schemas,
`opgg tools <name>` das vollständige Schema eines einzelnen, und `opgg try <name> schlüssel=wert …`
ruft eines einmal auf und zeigt die rohe Antwort (`@a,b,c` ist eine Liste, `--out datei.json`
schreibt alles). Der Grund dafür steht in `docs\opgg-schnittstelle.md`: mehrere Annahmen dieses
Projekts über das, was OP.GG *nicht* kann, waren aus dem eigenen Abruf-Code abgelesen — und falsch.

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
  braucht sie ohnehin nicht — es zeigt `Teammate 3` und `Gegner 2`.
- **Keine Cooldown- oder Ult-Timer.** Ebenfalls Richtlinie.
- **Keine Gewichtung nach deinem Champion-Pool.** Mastery und persönliche Winrate werden nicht
  abgefragt. Bewertet wird die Draft-Situation, nicht deine Gewohnheit. Nur Champions, die du nicht
  freigeschaltet hast, fallen aus deiner eigenen Liste — die kannst du in den Einstellungen wieder
  einblenden.
- **Kein Autostart, kein Dienst.** Das Tool läuft, wenn du es startest.

## Welche Liga die Zahlen beschreiben

Standardmäßig holt das Tool die Matchups über alle Ränge und die Tierlist aus OP.GGs eigenem
Standard-Bracket (gemessen: Emerald und höher). Wer in Gold spielt, wird damit aus einer anderen
Spielerpopulation beraten — und die unterscheidet sich: Darius' auffälligste Gegner auf Top heißen
in Gold Wukong, Warwick und Malzahar, in Platin Zaahen, Kennen und Heimerdinger.

```powershell
dotnet run --project src\DraftPilot.Tools -- settings tier gold
```

Möglich sind `iron`, `bronze`, `silver`, `gold`, `platinum`, `emerald`, `diamond`, `master`,
die Sammel-Brackets `emerald_plus`, `platinum_plus`, `diamond_plus` — oder `all` für alle Ränge
zusammen. Ein benanntes Bracket gilt für Lane-Zahlen, Matchups und Tier; Synergien und der
Matchup-Build kennen bei OP.GG keinen Rangfilter und kommen weiter aus dem Standard-Bracket.
Dasselbe gilt für die Warteschlange: Solo/Duo gegen Flex unterscheidet nur die Champion-Analyse.
Die Fußzeile nennt im Langtext, was wofür gilt, und der Wechsel wird mit dem nächsten
**Daten aktualisieren** wirksam — bis dahin sagt die Fußzeile auch das.

Für welche Queue die gespeicherte Datei gebaut wird, steht daneben:

```powershell
dotnet run --project src\DraftPilot.Tools -- settings gamemode flex
```

Möglich sind `ranked`, `flex` und `aram`. Diese Einstellung betrifft **nur** den großen Datenabruf —
er passiert, bevor irgendwer weiß, was gequeued wird. Im Draft selbst zählt die Queue, die der
Client meldet: Counter- und Build-Abrufe folgen ihr, egal was hier steht. Weicht die gespeicherte
Tierlist von der gespielten Queue ab, sagt das Fenster das während des Drafts als Chip.

Regionale Daten (EUW, NA …) gibt es über diese Schnittstelle nicht: der Region-Parameter existiert
nur bei den Werkzeugen, die einen Spielernamen abfragen, und die bleiben hier ungenutzt. Details
und die Messungen dazu: `docs\opgg-schnittstelle.md`.

## Grenzen der Datenlage

Das solltest du wissen, bevor du den Empfehlungen zu viel zutraust:

- **Tierlist und Lane-Verteilung sind solide.** Die Rollen-Anteile, aus denen die Lane-Vorhersage
  rechnet, sind Verhältnisse großer Zahlen und entsprechend belastbar.
- **Die gespeicherte Counter-Matrix ist dünn — der Draft füllt sie für den echten Gegner auf.**
  OP.GG nennt pro Champion und Lane nur die drei auffälligsten Gegner. Gegen die zehn
  meistgespielten Gegner einer Lane hat der Vorrat deshalb nur für 14 bis 23 % der Kandidaten eine
  Duell-Zahl (`Tools -- coverage`). Sobald ein Gegner aufgedeckt ist, holt der Draft dessen
  **vollständige** Duell-Liste auf seiner Lane (OP.GGs Matchup-Guide, 39 bis 57 Gegner je Champion)
  — dann hat fast jeder Kandidat eine Zahl gegen ihn. Ausnahme Jungle: dafür liefert OP.GG keine
  Liste. Die vollständige Liste kennt keinen Rang-Filter; gemessen weicht sie im Mittel 0,36 Punkte
  von dem ab, was die Gold-Lane-Raten erwarten lassen (`Tools -- guidecheck`).
- **„Die auffälligsten Gegner" sind eine Auswahl der Extreme.** Am selben Tag für dieselben Paare
  gemessen, weichen die Counter-Listen im Mittel 5,0 Punkte von der Erwartung ab, die vollständigen
  Listen 2,3 — und von einer Counter-Abweichung findet sich in der unabhängigen Messung nur etwa
  ein Achtel wieder. Liegt für ein Paar beides vor, zählt deshalb die vollständige Liste.
- **Ein Matchup bewegt weniger, als es sich anfühlt.** Jenseits der Lane-Stärke beider Champions
  verschiebt ein Duell die Winrate um etwa 1,6 Punkte (Standardabweichung). Ein einzelnes Duell
  über ein paar hundert Games ist deshalb zum großen Teil Zufall und wird kräftig geglättet: bei 500
  Games zählt etwa ein Drittel seiner Abweichung. Wo eine Zahl „aus N Games" zitiert, steht der
  gemessene Wert; die Prozentzahl daneben ist die Schätzung, mit der gerechnet wird.
- **Duos zählen nur, wo sie mehr sind als zwei starke Champions.** Eine Duo-Winrate enthält etwa
  zu 0,4 die Einzelstärke beider Partner. Gezählt wird nur, was darüber hinausgeht, und geglättet
  mit dem gemessenen Gewicht (`Tools -- priors`: 750, vorher 100 und damit sechsfach zu ernst).
- **Die Reihenfolge der Liste ist oft Rauschen.** Zieht man denselben Draft wiederholt aus seinen
  Stichproben (`Tools -- noise`), bleibt Platz 1 je nach Datenlage nur in etwa der Hälfte bis fast
  allen Ziehungen derselbe Champion. Deshalb sagt die Kopfzeile, wie viele Zeilen gleichauf liegen,
  jede Zeile nennt ihren Abstand zu Platz 1 in Worten, und die gleichauf liegenden tragen dieselbe
  Kante.
- **Drei Konstanten des Scores sind Schätzungen.** Wie stark Gegner außerhalb der eigenen Lane,
  Synergien (zusätzlich zur gemessenen Glättung) und der Team-Bedarf zählen, steht begründet, aber
  unkalibriert in `ScoreModel.cs`. Kalibrieren ließe sich das nur an Spielausgängen, und die eigenen
  reichen dafür nicht (bei 1.000 Spielen läge der Fehler eines Gewichts bei etwa 0,6). Gemessen
  sind dagegen alle Winrates und alle Glättungsgewichte.
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
