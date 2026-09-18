# Änderungen

Jede Version hier ist ein Release auf https://github.com/Sazzlez/DirkDraft/releases; installierte
Kopien melden sie beim nächsten Start.

## 1.1.0 — 2026-09-19

- **OP.GGs Tier zählt nicht mehr in die Prozentzahl.** Es steht weiter neben jeder Zeile, jetzt als
  grauer Hinweis statt als Argument. Gemessen an den 273 Lane-Zeilen des Gold-Snapshots erklärt das
  Tier 46,8 % der Streuung genau der Siegquote, zu der es addiert wurde — und 45,2 % der Streuung
  der *Pickrate*. Es war zur Hälfte eine zweite Portion Siegquote und zur Hälfte eine
  Beliebtheitsstimme, und zwar in voller Höhe: über die Leiter hinweg 0,16 Logit gegen 0,156 Logit
  Siegquoten-Spanne. Sichtbar wird das dort, wo bisher „S-Tier auf Top" die einzige Begründung war:
  Mordekaiser und Teemo verlassen die ersten acht, Camille (53 % gegen Darius) und Heimerdinger
  (53,2 % WR) kommen hinein. Die Ban-Punkte halbieren sich, weil das Tier dort doppelt hing.
- **Dünne Duelle behaupten nichts mehr.** Eine Matchup-Quote wird nicht mehr auf 50 % gezogen,
  sondern auf das, was die beiden Lane-Siegquoten ohnehin sagen — plus den Versatz, den OP.GGs
  Auswahl mitbringt (sie listet die Gegner, die auffallen, nicht alle). Das ist kein Detail: das
  mittlere gespeicherte Duell hat 197 Spiele gegen ein Prior-Gewicht von 150, der Prior trägt also
  43 % dessen, was der Score liest, bei 60 Spielen 71 %. In der Kreuzvalidierung über alle 1.482
  Kanten (`Tools -- matchupfit`) schlägt die neue Grundannahme die alte um 0,58 % LogLoss und
  halbiert den quadratischen Fehler beinahe (Brier 0,00236 gegen 0,00438); ein pauschaler
  Mittelwert gewann 0,03 %. Zusammen mit derselben Zentrierung im Duell-Term heißt „wir wissen es
  kaum" jetzt auch genau das — vorher war es für einen starken Champion ein Abzug.
- Die Duo-Basislinie wurde an sich selbst gemessen: sie mittelte Quoten, die vorher auf 50 %
  gezogen worden waren. Roh gemessen liegt sie bei 0,0743 statt 0,0563 Logit — 0,45 Punkte, die
  jedem Duo fehlten. Wirksam mit dem nächsten Datenabruf; `Tools -- inspect` zeigt jetzt
  gespeicherte und nachgerechnete Basislinien nebeneinander.
- Auch die Ban-Liste zählt die allgemeine Stärke nicht mehr doppelt: „schlägt unseren X" misst
  jetzt, wie viel mehr als üblich.
- **Frühe Picks sehen ihr Konterrisiko.** Solange auf deiner Lane niemand aufgedeckt ist, trägt
  jede Zeile „N offene Konter": noch freie Champions, die gegen diesen Pick besser abschneiden als
  seine Gegner üblicherweise. Im Blind-Draft liegen Singed und Nasus bei drei, Malphite bei sieben
  — bei fünf statistisch gleichwertigen Zeilen ist das der Unterschied, nach dem man sucht. Zählt
  nicht in die Prozentzahl hinein: ob der Gegner sie nimmt, sagen die Daten nicht.
- **Die Zahlen können jetzt deine Liga beschreiben.** `Tools -- settings tier gold` (oder
  `platinum`, `platinum_plus`, …) stellt das Rang-Bracket ein; Lane-Zahlen, Duelle und Tier kommen
  dann von dort. Vorher mischte ein einziger Score zwei Populationen: Lane-Stärke aus OP.GGs
  Standard-Bracket (Emerald+), Matchups aus allen Rängen. Wirksam mit dem nächsten Datenabruf, die
  Fußzeile nennt das Bracket.
- **Der Build zeigt, was es sonst noch gibt.** Unter Start/Schuhen und unter dem Kern steht „auch
  gespielt" mit Siegquote und Spielzahl jeder Alternative. Die Daten lagen längst im Cache und
  wurden weggeworfen: im letzten Abruf standen Stahlkappen mit 46,5 % oben, während Merkurstiefel
  (49,1 %) und Stiefel der Schnelligkeit (52,2 %) in derselben Datei lagen. Nichts wird vorgezogen.
- **ARAM bekommt einen ARAM-Build** statt eines Kluft-Builds gegen einen erfundenen Gegner — und
  keine Lane-Empfehlungen, keine Lane-Beschriftungen, keine Duell-Prozente mehr. Der
  Aufstellungsvergleich bleibt.
- **OP-Tier wurde falsch gelesen** — OP.GGs beste Stufe (0) galt als „unbewertet", was Jinx auf Bot
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
- Prozente im Build folgen ihrer Stichprobe: unter 50 Spielen steht „dünne Datenlage · N Spiele".
- Der Kauf-Hinweis sagt jetzt „67 % der Gegner-Champions sind physisch" statt „Gegner macht 67 %
  physischen Schaden" — gezählt werden Champions, nicht Schaden. Und er verschwindet nicht mehr,
  wenn kein Build geladen werden konnte.
- Draft-Abrufe haben zehn Sekunden statt dreißig, Fehlversuche kosten kein Kontingent mehr, und
  wenn nichts mehr geholt wird, sagt die Statuszeile das statt „versuche es weiter".
- **Ein Aussetzer der Verbindung beendet keinen Draft mehr.** Bricht der Event-Socket kurz weg,
  galt das als „Champ Select vorbei": Build weg, alle geholten Konter-Kanten weg, Abruf-Kontingent
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
  feste Zeile „DEIN MATCHUP" mit beiden Champions, Lane, Spielzahl und Siegquote — die Liste
  darunter folgt weiter der Uhr, aber dein Duell springt nicht mehr weg. Klickst du einen
  Mitspieler an, siehst du wie bisher dessen Matchup in der großen Karte.
- Ist das Spiel vorbei, zeigt das Fenster wieder den Startbildschirm. Vorher blieb die Build-Karte
  des beendeten Spiels bis zum nächsten Draft stehen — mit Runen zum Übertragen und Items zum
  Kaufen für ein Match, das längst gelaufen war.
- In der Lane-Übersicht steht jede Siegquote neben dem Champion, für den sie gilt: links dein
  Team, rechts der Gegner. Der Balken schlägt von der Mitte zur führenden Seite aus — nach links,
  wenn dein Champion vorn liegt, nach rechts, wenn der Gegner führt. Die halbe Breite sind zehn
  Punkte Abstand zu einem ausgeglichenen Duell, damit der Unterschied überhaupt sichtbar wird.
- Die Spielansicht zeigt unter den Skills alle fünf Lanes des fertigen Drafts: wer gegen wen
  steht, die Siegquote deiner Seite in jedem Duell und darunter den Draft als eine Zahl. Fehlt
  eine Duell-Statistik, bleibt die Zahl weg statt 50 % zu behaupten; eine Lane, auf der nur eine
  Seite aufgedeckt wurde, nennt trotzdem den eigenen Champion.

## 1.0.2 — 2026-09-02

- Item-Icons, die für das aktuelle Matchup neu geladen werden, erscheinen sofort. Vorher blieben
  genau diese Kacheln als Text stehen, bis irgendein anderes Ereignis das Panel neu zeichnete;
  Icons aus früheren Drafts waren immer da. Betrifft auch Runen- und Beschwörerzauber-Icons bei
  jemandem, der noch kein großes Daten-Update gelaufen hat.
- Die Build-Karte erscheint nicht mehr ohne Build. Ein Klick auf einen Mitspieler zeigte sie
  auch dann, wenn man selbst noch nicht gepickt hatte — mit leerem Inhalt und der Kopfzeile des
  vorherigen Drafts („DEIN BUILD · Darius · Top").
- Klickt man einen Mitspieler an, ist der Text auch für ihn geschrieben: „Matchup von
  Mitspieler 2" statt „Dein Matchup", und das Duell läuft für ihn, nicht für dich.
- Die Ban-Liste nennt keine Lane mehr in der Kopfzeile. Sie stand auf der eigenen Lane, während
  die Vorschläge nach den Lanes ausgewählt werden, die der Gegner noch füllen kann — „Bans für
  dich · Top" über einer Liste aus Supports.
- Solange kein gegnerischer Pick aufgedeckt ist (Blind Pick), zeigen die Team-Spalten einen
  Strich statt zweimal „50,0 % WR". Die Zahl entsteht aus dem Unterschied beider Teams; ohne
  Gegner war sie ein Ergebnis, das nichts gemessen hat.
- Fehlt die Duell-Statistik, heißt es jetzt „Für dieses Duell hat OP.GG keine Statistik" statt
  „OP.GG kennt dieses Duell nicht" — direkt darunter stand ein Build für genau dieses Duell.

## 1.0.1 — 2026-09-02

- Der Startbildschirm meldet nach der Update-Prüfung, dass die installierte Version die neueste ist
  („Version 1.0.1 · auf dem neuesten Stand"). Ohne diese Zeile war eine aktuelle Installation nicht
  von einer zu unterscheiden, deren Prüfung nie lief.

## 1.0.0 — 2026-09-02

- Erste installierbare Version: Installer mit eingebauter .NET-Runtime, Selbst-Update über GitHub
  Releases (Prüfung beim Start, Installation nur per Klick).
