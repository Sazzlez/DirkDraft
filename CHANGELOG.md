# Änderungen

Jede Version hier ist ein Release auf https://github.com/Sazzlez/DirkDraft/releases; installierte
Kopien melden sie beim nächsten Start.

## Unveröffentlicht

- **Der Spielmodus wird erkannt und steht oben rechts.** Ranked Solo/Duo, Ranked Flex, ARAM und
  ARAM Mayhem, dazu Swiftplay, Clash und die übrigen — aus der Queue-ID, die der Client ohnehin
  mitschickt. Die IDs sind nicht geraten, sondern aus dem Client selbst gelesen
  (`/lol-game-queues/v1/queues`, alle 88 Queues mit Modus und Karte): Mayhem heißt dort
  „ARAM: Chaos", läuft unter 2400 (plus 2410 Turnier und 2450 „fast klassisch") und auf derselben
  Karte wie ARAM. Nebenbei korrigiert: 480 ist Swiftplay, Arena läuft heute unter 1750.
- **Der Modus entscheidet jetzt, welche Zahlen geholt werden.** Solo/Duo, Flex und ARAM sind bei
  OP.GG drei getrennte Datenbestände hinter einem Parameter — bisher schickte das Tool dort immer
  das, was in den Einstellungen stand, also im Zweifel die Zahlen einer Queue, die gar nicht
  gespielt wurde. Counter- und Build-Abrufe folgen jetzt der erkannten Queue. Die Einstellung
  bleibt für den großen Datenabruf zuständig (der passiert, bevor irgendwer weiß, was gequeued
  wird) und kennt jetzt auch `aram`; weicht die gespeicherte Tierlist von der gespielten Queue ab,
  sagt das Fenster das als Chip.
- **Für ARAM Mayhem führt OP.GG keine eigenen Zahlen.** Nachgeprüft am Schema: der `game_mode` ist
  ein Enum aus fünf Werten, Mayhem ist keiner davon. Das Tool nimmt deshalb die ARAM-Daten
  derselben Karte — und sagt es: ein Stern hinter dem Modusnamen, ein Chip neben dem Build. Erfinden
  ist keine Option, schweigen auch nicht.
- **Der Build folgt dem Matchup — und wenn es keins gibt, der besten Winrate.** Der geratene
  Ersatzgegner ist gelöscht. Bisher galt: kein aufgedeckter Lane-Gegner → Build gegen den auf dieser
  Lane häufigsten Champion, mit dem Hinweis, dass das nur eine Richtung ist. Jetzt kommt in genau
  dieser Lage der Build, den der eigene Champion auf dieser Lane insgesamt am besten fährt. Der
  Unterschied ist die Stichprobe: bei Darius Top trägt der Matchup-Kern gegen Jax **11 Games**, der
  Lane-Build je nach Bracket **8.800 bis 40.000**. Zwei Folgen, beide erwünscht: der Build steht
  jetzt schon direkt nach dem eigenen Lock statt erst nach dem gegnerischen Pick (und damit auch der
  Runen-Knopf), und in Blind Pick und Swiftplay ist er zum ersten Mal echt.
- Wo OP.GG mehrere Varianten eines Build-Slots mit Winrate liefert **und** die Stichproben es
  hergeben, wird nach Winrate sortiert — über die Wilson-Untergrenze, nicht über die rohe Zahl. Der
  Unterschied ist nicht akademisch: in den Matchup-Daten stehen 13 Games mit 61,5 % neben 113 Games
  mit 48,7 %, und jede Regel, die das erste nach oben schiebt, schiebt Rauschen nach oben. Der
  Matchup-Build bleibt deshalb bei „meistgespielt" — dort sind alle Stichproben zu dünn. Die
  situative Schuhwahl kommt darüber ausdrücklich **nicht** zurück.
- `Tools -- watch` und `replay` zeigen den erkannten Modus in derselben Zeile wie das Fenster:
  Name, welche OP.GG-Quelle daraus folgt, ob Lanes gelten, und den Mayhem-Vorbehalt.

### Behoben

- **Der Build-Cache konnte Solo/Duo und Flex nicht auseinanderhalten.** Seit es den Build ohne
  Gegner gibt, hängt der an Queue **und** Rang-Bracket — der Dateiname kannte aber nur Patch,
  Champion, Lane und Gegner. Ein in Flex geholter Build wurde damit im nächsten Solo/Duo-Draft
  wortlos weitergereicht. Pläne tragen jetzt eine Variante (`ranked-gold`) im Dateinamen;
  Matchup-Pläne behalten ihren alten Namen, weil ihre Quelle weder Queue noch Bracket kennt. Alte
  Cache-Dateien werden verworfen statt falsch beschriftet.
- **Der Build-Abruf hatte keine Obergrenze.** Gezählt wurde er gegen das Draft-Budget, geprüft
  nicht — eine flackernde Lane-Vorhersage konnte beliebig viele Anfragen an einen fremden Server
  erzeugen. Jetzt gilt dieselbe Grenze wie für die Counter.
- **Eine angenommene eigene Lane durfte einen Gegner auswählen.** Nennt weder Client noch Vorhersage
  eine Lane, fällt der Build auf die Hauptlane des Champions zurück. Als Position ist das richtig;
  als Grundlage für „wer steht mir gegenüber" verkettet es zwei Vermutungen zu einer konkreten
  Behauptung über ein Duell. Dort gibt es jetzt nur noch den gegnerlosen Build.
- **Kopfzeile und Markierung widersprachen sich bei der Gleichauf-Gruppe.** Lag Platz 1 mit
  niemandem gleichauf, schwieg die Kopfzeile („erst ab zwei"), die Zeile trug aber trotzdem die
  Akzentkante bzw. im Testwerkzeug das `=`, das „statistisch nicht zu trennen" bedeutet. Die Regel
  liegt jetzt in `CountLeadingTies` selbst: entweder eine Gruppe ab zwei, oder keine.
- **In Modi ohne Lanes wurden Counter geholt, die nirgends angezeigt werden.** ARAM, Mayhem und
  Arena zeigen weder Vorschlagsliste noch Matchup-Panel noch Duell-Zahlen — die bis zu fünf
  Counter-Abrufe pro Draft liefen trotzdem, und auf der Heulenden Schlucht antwortet OP.GG darauf
  ohnehin mit „insufficient matchup sample". Fällt weg; der Build wird dort weiter geholt.
- Zusammengeführt: `MainLaneLogOdds` und der Build-Pfad beantworteten „auf welcher Lane spielt
  dieser Champ eigentlich" mit zwei getrennten Schleifen. Der Kommentar an der Stelle warnt genau
  davor — jetzt gibt es eine Implementierung.

## 1.2.1 — 2026-09-19

- **An der App ändert sich nichts.** Diese Version ist verhaltensgleich zu 1.2.0; sie existiert, um
  den reparierten Veröffentlichungsweg einmal echt zu gehen. Repariert wurde das, was 1.2.0 fast
  zerlegt hätte: Windows PowerShell 5.1 verpackt jede stderr-Zeile eines aufgerufenen Programms in
  einen Fehler, sobald die Ausgabe des Skripts umgeleitet wird — und Git schreibt dort
  Routinemeldungen hin („LF will be replaced by CRLF"). Der Lauf brach deshalb ab, nachdem der
  Installer fertig gepackt war und bevor Commit, Tag und Upload liefen; den Rest musste ich von
  Hand nachziehen. `release.ps1` und `publish.ps1` bewerten jedes aufgerufene Programm jetzt nur
  noch an seinem Exitcode, dem Einzigen, was es über seinen Erfolg zusagt.
- Das Testwerkzeug zeigt dieselbe Liste wie das Fenster: `Tools -- recommend` druckt jetzt
  Fehlerbalken und markiert mit `=`, was von Platz 1 statistisch nicht zu trennen ist, `--terms`
  legt jedes Kriterium einzeln offen. Ein `-` steht für „niemand" — ein leeres `""` verschluckt die
  Shell, und die nächste Liste rutschte dann unbemerkt in den Gegner-Platz.

## 1.2.0 — 2026-09-19

- **Das Fenster spricht jetzt League.** Die Texte standen in Lehrbuch-Deutsch da, während im Client
  daneben und im Voice-Chat andere Wörter fallen: aus „Siegquote" wird **Winrate**, aus „Duell"
  **Matchup**, aus „Bot" **Botlane**, aus „Konter" **Counter**, aus „Bann" **Ban**, aus „physisch /
  magisch" **AD / AP**, aus „Rüstung / Magieresistenz / Zähigkeit" **Armor / MR / Tenacity**, aus
  „Aufstellung" **Comp**, aus „Nahkampf" **Melee**, aus „Spiele" **Games** und aus „Mitspieler"
  **Teammate**. Die Teamfight-Tabelle heißt ihre Zeilen jetzt CC, Engage, Peel, Ranged und Scaling;
  der Build zeigt Start, Boots, Core und Late, die Shards Adaptive Force und Attack Speed. Im Score
  heißt die Zeile „Matchup" statt „Matchup mit Gegner" und „Enemy-Team" statt „Übrige Gegner", und
  Platz 1 ist der **beste Pick der Liste**, nicht die beste Wahl.
- **Die gegnerische Comp zählt jetzt mit.** Sie wurde die ganze Zeit berechnet, nach dem
  Lock in der Vergleichstabelle gezeichnet — und für jede Empfehlung weggeworfen: der
  Comp-Term sah ausschließlich Lücken im *eigenen* Team. Vier Regeln stellen die
  Spiegelfrage, was der Pick gegen das ausrichtet, was sie mitgebracht haben: steht ihre Backline
  ohne Frontline da, kann jemand von ihnen einen Kampf eröffnen, sind sie alle Melees, wird
  ihr Team erst spät gefährlich. Dazu das Gegengewicht — zwei Frontline-Körper kosten einen
  Assassinen Punkte. Gemessen an einem Top-Draft gegen Jax/Viego/Syndra (Frontline 0): Tryndamere,
  Pantheon und Yone kommen in die ersten acht, Teemo, Kayle und Ornn fallen heraus, Quinn gewinnt
  1,2 Punkte. Die gemessenen Matchups an der Spitze bleiben unverändert — die Regeln teilen sich die
  Deckelung des vorhandenen Terms, der Anteil ohne Winrate-Grundlage wächst also nicht.
- **Die Einordnung steht jetzt in der Liste, nicht hinter einem Pfeil.** „bester Pick der Liste",
  „gleichauf mit Platz 1", „knapp dahinter", „deutlich dahinter" wurde die ganze Zeit berechnet und
  nur *innerhalb* der aufgeklappten Aufschlüsselung gezeigt — die eine Zeile, die sagt, ob die
  Reihenfolge überhaupt etwas bedeutet, kostete einen Klick pro Zeile. Sie steht jetzt unter der
  Zahl, und eine Akzentkante markiert jede Zeile, die von Platz 1 statistisch nicht zu trennen ist.
- **Die Liste ist eine Liste, keine Kartensammlung.** Acht gerahmte Kästen ziehen acht Rahmen um
  genau die Dinge, die man vergleichen will. Jetzt trennt eine Haarlinie, der Zeiger hebt die Zeile
  hervor, und die Chips haben ihre gefüllten Pillen verloren: fast jeder Chip spricht *für* seine
  Zeile, grün färbt also die Regel und lässt nichts für die Ausnahme. Helligkeit trägt jetzt die
  Unterscheidung, Rot bleibt als einzige Farbe übrig — und wird deshalb gesehen, etwa bei den
  offenen Countern im Blind-Pick.
- Dazu aufgeräumt: die Zug-Karte ist eine Zeile statt eines 80 Pixel hohen Kastens um zwei Wörter,
  Sitzplatz-Nummern stehen nur noch dort, wo kein Champion sie schon benennt, die Lane-Auswahl
  liest sich als Text mit Pfeil statt als Formularfeld, leere Sitzplätze zeichnen keine grauen
  Kacheln mehr, und „Daten aktualisieren" ist ein ruhiger Knopf statt des lautesten Elements im
  Fenster.
- **Das Ban-Urteil war unerreichbar geworden.** Die Schwellen 4,5 und 2,5 Punkte stammen aus der
  Zeit, als OP.GGs Tier noch in den Ban-Wert einging. Gemessen über die ersten acht Bans aus fünf
  Drafts (40 Zeilen): Maximum 4,4 — „wichtiger Ban" konnte gar nicht mehr vorkommen, und die
  halbe Liste trug „optional". Jetzt liegt die Schwelle beim obersten Zehntel (3,5) und beim
  Median (2,5); darunter steht gar nichts, statt achtmal dasselbe Wort.
- Ohne Lane — Custom Games, Blind Pick ohne Zuweisung — nahm die Grundstärke bisher die **beste**
  der fünf Lane-Zeilen. Ein Maximum über fünf verrauschte Schätzungen wählt aber die glücklichste
  Stichprobe, nicht die beste Lane, und der Chip konnte eine Lane nennen, auf der der Champion
  3 % seiner Games hat. Jetzt zählt die Lane, auf der er tatsächlich gespielt wird. Damit
  beantworten nicht mehr drei Stellen dieselbe Frage auf drei Arten.

## 1.1.0 — 2026-09-19

- **OP.GGs Tier zählt nicht mehr in die Prozentzahl.** Es steht weiter neben jeder Zeile, jetzt als
  grauer Hinweis statt als Argument. Gemessen an den 273 Lane-Zeilen des Gold-Snapshots erklärt das
  Tier 46,8 % der Streuung genau der Winrate, zu der es addiert wurde — und 45,2 % der Streuung
  der *Pickrate*. Es war zur Hälfte eine zweite Portion Winrate und zur Hälfte eine
  Beliebtheitsstimme, und zwar in voller Höhe: über die Leiter hinweg 0,16 Logit gegen 0,156 Logit
  Winrate-Spanne. Sichtbar wird das dort, wo bisher „S-Tier auf Top" die einzige Begründung war:
  Mordekaiser und Teemo verlassen die ersten acht, Camille (53 % gegen Darius) und Heimerdinger
  (53,2 % WR) kommen hinein. Die Ban-Punkte halbieren sich, weil das Tier dort doppelt hing.
- **Dünne Matchups behaupten nichts mehr.** Eine Matchup-Quote wird nicht mehr auf 50 % gezogen,
  sondern auf das, was die beiden Lane-Winrates ohnehin sagen — plus den Versatz, den OP.GGs
  Auswahl mitbringt (sie listet die Gegner, die auffallen, nicht alle). Das ist kein Detail: das
  mittlere gespeicherte Matchup hat 197 Games gegen ein Prior-Gewicht von 150, der Prior trägt also
  43 % dessen, was der Score liest, bei 60 Games 71 %. In der Kreuzvalidierung über alle 1.482
  Kanten (`Tools -- matchupfit`) schlägt die neue Grundannahme die alte um 0,58 % LogLoss und
  halbiert den quadratischen Fehler beinahe (Brier 0,00236 gegen 0,00438); ein pauschaler
  Mittelwert gewann 0,03 %. Zusammen mit derselben Zentrierung im Matchup-Term heißt „wir wissen es
  kaum" jetzt auch genau das — vorher war es für einen starken Champion ein Abzug.
- Die Duo-Basislinie wurde an sich selbst gemessen: sie mittelte Quoten, die vorher auf 50 %
  gezogen worden waren. Roh gemessen liegt sie bei 0,0743 statt 0,0563 Logit — 0,45 Punkte, die
  jedem Duo fehlten. Wirksam mit dem nächsten Datenabruf; `Tools -- inspect` zeigt jetzt
  gespeicherte und nachgerechnete Basislinien nebeneinander.
- Auch die Ban-Liste zählt die allgemeine Stärke nicht mehr doppelt: „schlägt unseren X" misst
  jetzt, wie viel mehr als üblich.
- **Frühe Picks sehen ihr Counter-Risiko.** Solange auf deiner Lane niemand aufgedeckt ist, trägt
  jede Zeile „N offene Counter": noch freie Champions, die gegen diesen Pick besser abschneiden als
  seine Gegner üblicherweise. Im Blind-Draft liegen Singed und Nasus bei drei, Malphite bei sieben
  — bei fünf statistisch gleichwertigen Zeilen ist das der Unterschied, nach dem man sucht. Zählt
  nicht in die Prozentzahl hinein: ob der Gegner sie nimmt, sagen die Daten nicht.
- **Die Zahlen können jetzt deine Liga beschreiben.** `Tools -- settings tier gold` (oder
  `platinum`, `platinum_plus`, …) stellt das Rang-Bracket ein; Lane-Zahlen, Matchups und Tier kommen
  dann von dort. Vorher mischte ein einziger Score zwei Populationen: Lane-Stärke aus OP.GGs
  Standard-Bracket (Emerald+), Matchups aus allen Rängen. Wirksam mit dem nächsten Datenabruf, die
  Fußzeile nennt das Bracket.
- **Der Build zeigt, was es sonst noch gibt.** Unter Start/Boots und unter dem Core steht „auch
  gespielt" mit Winrate und Anzahl Games jeder Alternative. Die Daten lagen längst im Cache und
  wurden weggeworfen: im letzten Abruf standen Stahlkappen mit 46,5 % oben, während Merkurstiefel
  (49,1 %) und Stiefel der Schnelligkeit (52,2 %) in derselben Datei lagen. Nichts wird vorgezogen.
- **ARAM bekommt einen ARAM-Build** statt eines Summoner's-Rift-Builds gegen einen erfundenen Gegner — und
  keine Lane-Empfehlungen, keine Lane-Beschriftungen, keine Matchup-Prozente mehr. Der
  Comp-Vergleich bleibt.
- **OP-Tier wurde falsch gelesen** — OP.GGs beste Stufe (0) galt als „unbewertet", was Jinx auf Botlane
  und Thresh auf Support je drei Punkte kostete. Erledigt sich durch den Punkt ganz oben: das Tier
  zählt gar nicht mehr in den Score. Der Chip zeigt jetzt für beide korrekt OP statt gar nichts.
- **Ein Update räumt keinen laufenden Draft mehr aus.** Wer in der Lobby aktualisiert und dann in
  einen Champ Select kommt, verlor bisher mitten im Draft alle geholten Zahlen — still.
- **Der Patch wird geprüft.** Läuft das Spiel auf einem neueren Patch als die Daten, sagt die
  Fußzeile das, statt nur das Dateialter zu nennen.
- Die Farbe einer Empfehlungszeile misst den Abstand zu Platz 1 statt den zu 50 % — vorher trugen
  auf einer starken Lane alle acht Zeilen dieselbe grüne Pille.
- Ein Duo zählt nur noch, soweit es besser ist als die Duos, die OP.GG überhaupt auflistet (deren
  Mittel liegt bei 51,4 %). Vorher war allein die Existenz einer Duo-Zeile 0,8 Punkte wert.
- Prozente im Build folgen ihrer Stichprobe: unter 50 Games steht „dünne Datenlage · N Games".
- Der Kauf-Hinweis sagt jetzt „67 % der Gegner-Champions sind physisch" statt „Gegner macht 67 %
  physischen Schaden" — gezählt werden Champions, nicht Schaden. Und er verschwindet nicht mehr,
  wenn kein Build geladen werden konnte.
- Draft-Abrufe haben zehn Sekunden statt dreißig, Fehlversuche kosten kein Kontingent mehr, und
  wenn nichts mehr geholt wird, sagt die Statuszeile das statt „versuche es weiter".
- **Ein Aussetzer der Verbindung beendet keinen Draft mehr.** Bricht der Event-Socket kurz weg,
  galt das als „Champ Select vorbei": Build weg, alle geholten Counter-Kanten weg, Abruf-Kontingent
  auf null. Der Grund war, dass „der Client sagt, es läuft keins" und „die Anfrage kam nicht durch"
  denselben Wert hatten. Jetzt schließt nur eine Antwort das Panel, kein Schweigen.
- **Ein Spiel ohne Build zeigt nicht mehr den Startbildschirm.** „Wartet auf das nächste Champ
  Select" stand die ganze Partie lang da, während die fertige Lane-Übersicht ungenutzt daneben lag.
  Die Spielansicht hängt jetzt am laufenden Spiel; ohne Build bleiben Runen und Kaufreihenfolge
  weg und eine Zeile sagt, dass keiner vorliegt.
- Der Runen-Knopf versprach „funktioniert bis zum Ladebildschirm" und sperrte genau dort. Jetzt
  sagt er, was er tut: klickbar bis zum Spielstart.
- **Die Fußzeile sagt, wofür Bracket und Warteschlange überhaupt gelten.** Gemessen an OP.GGs
  Schemas nimmt nur die Champion-Analyse beide Parameter an — Tierlist, Duo-Werkzeug und
  Matchup-Guide keinen von beiden. Ein Gold-Snapshot ist in den Lane-Zahlen und Countern Gold, in
  den Duos nicht. Steht die Einstellung auf etwas anderem als die Datei, steht auch das da.
## 1.0.3 — 2026-09-04

- Um die Zug-Karte („Du pickst") läuft kein Verlaufsrahmen mehr. Sie hat jetzt denselben ruhigen
  Rand wie jede andere Karte; dass du am Zug bist, sagt die blaue Überschrift.
- Dein eigenes Matchup bleibt während Pick und Ban stehen. Über der Vorschlagsliste sitzt eine
  feste Zeile „DEIN MATCHUP" mit beiden Champions, Lane, Anzahl Games und Winrate — die Liste
  darunter folgt weiter der Uhr, aber dein Matchup springt nicht mehr weg. Klickst du einen
  Teammate an, siehst du wie bisher dessen Matchup in der großen Karte.
- Ist das Spiel vorbei, zeigt das Fenster wieder den Startbildschirm. Vorher blieb die Build-Karte
  des beendeten Spiels bis zum nächsten Draft stehen — mit Runen zum Übertragen und Items zum
  Kaufen für ein Match, das längst gelaufen war.
- In der Lane-Übersicht steht jede Winrate neben dem Champion, für den sie gilt: links dein
  Team, rechts der Gegner. Der Balken schlägt von der Mitte zur führenden Seite aus — nach links,
  wenn dein Champion vorn liegt, nach rechts, wenn der Gegner führt. Die halbe Breite sind zehn
  Punkte Abstand zu einem ausgeglichenen Matchup, damit der Unterschied überhaupt sichtbar wird.
- Die Spielansicht zeigt unter den Skills alle fünf Lanes des fertigen Drafts: wer gegen wen
  steht, die Winrate deiner Seite in jedem Matchup und darunter den Draft als eine Zahl. Fehlt
  eine Matchup-Statistik, bleibt die Zahl weg statt 50 % zu behaupten; eine Lane, auf der nur eine
  Seite aufgedeckt wurde, nennt trotzdem den eigenen Champion.

## 1.0.2 — 2026-09-02

- Item-Icons, die für das aktuelle Matchup neu geladen werden, erscheinen sofort. Vorher blieben
  genau diese Kacheln als Text stehen, bis irgendein anderes Ereignis das Panel neu zeichnete;
  Icons aus früheren Drafts waren immer da. Betrifft auch Runen- und Summoner-Spell-Icons bei
  jemandem, der noch kein großes Daten-Update gelaufen hat.
- Die Build-Karte erscheint nicht mehr ohne Build. Ein Klick auf einen Teammate zeigte sie
  auch dann, wenn man selbst noch nicht gepickt hatte — mit leerem Inhalt und der Kopfzeile des
  vorherigen Drafts („DEIN BUILD · Darius · Top").
- Klickt man einen Teammate an, ist der Text auch für ihn geschrieben: „Matchup von
  Teammate 2" statt „Dein Matchup", und das Matchup läuft für ihn, nicht für dich.
- Die Ban-Liste nennt keine Lane mehr in der Kopfzeile. Sie stand auf der eigenen Lane, während
  die Vorschläge nach den Lanes ausgewählt werden, die der Gegner noch füllen kann — „Bans für
  dich · Top" über einer Liste aus Supports.
- Solange kein gegnerischer Pick aufgedeckt ist (Blind Pick), zeigen die Team-Spalten einen
  Strich statt zweimal „50,0 % WR". Die Zahl entsteht aus dem Unterschied beider Teams; ohne
  Gegner war sie ein Ergebnis, das nichts gemessen hat.
- Fehlt die Matchup-Statistik, heißt es jetzt „Für dieses Matchup hat OP.GG keine Statistik" statt
  „OP.GG kennt dieses Matchup nicht" — direkt darunter stand ein Build für genau dieses Matchup.

## 1.0.1 — 2026-09-02

- Der Startbildschirm meldet nach der Update-Prüfung, dass die installierte Version die neueste ist
  („Version 1.0.1 · auf dem neuesten Stand"). Ohne diese Zeile war eine aktuelle Installation nicht
  von einer zu unterscheiden, deren Prüfung nie lief.

## 1.0.0 — 2026-09-02

- Erste installierbare Version: Installer mit eingebauter .NET-Runtime, Selbst-Update über GitHub
  Releases (Prüfung beim Start, Installation nur per Klick).
