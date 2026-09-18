# Änderungen

Jede Version hier ist ein Release auf https://github.com/Sazzlez/DirkDraft/releases; installierte
Kopien melden sie beim nächsten Start.

## 1.1.0 — noch nicht veröffentlicht

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
- **OP-Tier zählt.** OP.GGs beste Stufe (0) wurde als „unbewertet" gelesen: Jinx auf Bot und Thresh
  auf Support verloren dadurch je drei Punkte und standen nicht einmal in den ersten fünf.
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
