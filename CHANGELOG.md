# Änderungen

Jede Version hier ist ein Release auf https://github.com/Sazzlez/DirkDraft/releases; installierte
Kopien melden sie beim nächsten Start.

## Noch nicht veröffentlicht

- Ist das Spiel vorbei, zeigt das Fenster wieder den Startbildschirm. Vorher blieb die Build-Karte
  des beendeten Spiels bis zum nächsten Draft stehen — mit Runen zum Übertragen und Items zum
  Kaufen für ein Match, das längst gelaufen war.
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
