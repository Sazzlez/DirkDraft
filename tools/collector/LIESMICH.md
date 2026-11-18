# Sammler auf einem eigenen Rechner

Dieses Verzeichnis enthält alles, um den Sammler statt in GitHub Actions auf einem eigenen
Linux-Rechner laufen zu lassen (Raspberry Pi oder kleiner Server). **Es stellt nichts um:** Der Workflow
„Daten sammeln“ läuft weiter, bis du ihn selbst abschaltest (Abschnitt „Umstellen“).

**Stand der Prüfung:** Das Skript ist bisher nur mit Platzhalter-Befehlen nachgestellt (Trockenlauf,
erste Veröffentlichung, Schutz bei dünner Zählung, Fehler beim Sammeln, kein Netz, Backup und
Wiederherstellen). Auf einem echten Linux-Rechner mit Riot-Schlüssel, Deploy-Key und systemd ist es noch
nicht gelaufen. Der erste echte Lauf sollte deshalb der Trockenlauf aus Schritt 7 sein.

## Warum

Der Workflow startet sich selbst neu und läuft rund 22 Stunden am Tag. GitHubs Bedingungen schließen
unverhältnismäßige Last aus und, auf gehosteten Runnern, alles, was nicht Bau, Test, Auslieferung oder
Veröffentlichung des Projekts dient. Die Folgen reichen von abgebrochenen Jobs bis zum deaktivierten
Repository.

Kein Self-hosted Runner: Das Repository ist öffentlich, Pull Requests aus Forks könnten Code auf dem
Rechner ausführen. Das Skript hier holt nur den Branch `master` und führt nichts aus, was ein Fremder
einreichen kann.

## Was ein Lauf tut

`collect.sh` macht dasselbe wie der Workflow, in derselben Reihenfolge:

1. Checkout auf den neuesten `master` bringen und die Werkzeuge bauen.
2. `riot collect` (270 Minuten Ranked) und `riot aram` (60 Minuten).
3. Einmal je UTC-Tag beide Stände verschlüsselt ins Backup schreiben.
4. Exportieren (`riot export`, `riot export-aram`).
5. Den veröffentlichten Stand aus dem Branch `data` holen und mit `riot guard` vergleichen: Eine
   Zählung unter der Hälfte der veröffentlichten wird nicht veröffentlicht.
6. Ein Commit, per Deploy-Key in den Branch `data` gepusht.

Jeder Schritt läuft auch, wenn einer davor fehlgeschlagen ist. Antwortet GitHub nicht, wird nichts
veröffentlicht; der Stand bleibt auf der Platte, der nächste Lauf holt es nach.

| Datei | Inhalt |
|---|---|
| `collect.sh` | ein Lauf; `--dry-run` ohne Push, `--restore` spielt ein Backup ein |
| `collector.env.example` | Vorlage für die Einstellungen (Riot-Schlüssel, Pfade, Zeiten) |
| `dirkdraft-collector.service` | systemd-Dienst für einen Lauf |
| `dirkdraft-collector.timer` | startet den nächsten Lauf zehn Minuten nach dem Ende des letzten |

## Einrichten

Gebraucht werden: ein 64-Bit-Linux (auf dem Raspberry Pi das 64-Bit-System), `git`, `openssl`,
`flock` (Paket util-linux, überall vorinstalliert) und das .NET-10-SDK.

**1. Benutzer und Ordner.** Der Sammler läuft unter einem eigenen Benutzer ohne Anmeldung.

```bash
sudo useradd --system --home-dir /var/lib/dirkdraft-collector --shell /usr/sbin/nologin dirkdraft
```

```bash
sudo install -d -o dirkdraft -g dirkdraft -m 750 /var/lib/dirkdraft-collector /var/lib/dirkdraft-collector/.ssh
```

**2. .NET 10.** Nach Microsofts Anleitung für die Distribution installieren. Der Dienst muss `dotnet`
finden: Liegt es nicht unter `/usr/bin` oder `/usr/local/bin`, dort einen Link anlegen. Prüfen:

```bash
sudo -u dirkdraft dotnet --version
```

Die Ausgabe muss mit `10.` beginnen.

**3. Checkout.**

```bash
sudo git clone https://github.com/Sazzlez/DirkDraft.git /opt/dirkdraft
```

```bash
sudo chown -R dirkdraft:dirkdraft /opt/dirkdraft
```

**4. Deploy-Key.** Ein Schlüsselpaar nur für diesen Rechner:

```bash
sudo -u dirkdraft ssh-keygen -t ed25519 -N "" -C dirkdraft-sammler -f /var/lib/dirkdraft-collector/.ssh/dirkdraft-data
```

Den öffentlichen Teil (`dirkdraft-data.pub`) auf GitHub eintragen: Repository → Settings → Deploy keys →
Add deploy key, mit Haken bei „Allow write access“. Ein Deploy-Key mit Schreibrecht darf in jeden
Branch schreiben, nicht nur in `data`. Deshalb `master` mit einer Regel schützen (Settings → Rules), die
Deploy-Keys nicht ausnimmt; für `data` muss der erzwungene Push erlaubt bleiben.

**5. Einstellungen.**

```bash
sudo cp /opt/dirkdraft/tools/collector/collector.env.example /etc/dirkdraft-collector.env
```

```bash
sudo chown root:dirkdraft /etc/dirkdraft-collector.env && sudo chmod 640 /etc/dirkdraft-collector.env
```

In der Datei `RIOT_API_KEY` und `STATE_BACKUP_KEY` eintragen. `STATE_BACKUP_KEY` sollte derselbe Wert
sein wie das gleichnamige Repository-Secret, damit sich ein Backup aus GitHub Actions hier einspielen
lässt. `BACKUP_DIR` am besten auf einen anderen Datenträger legen (USB-Stick, NAS): Ein Backup auf
derselben SD-Karte übersteht deren Ausfall nicht.

**6. Stand übernehmen.** Ohne Stand beginnt die Zählung bei null. Das schadet den veröffentlichten
Dateien nicht (der Schutz hält sie, bis die neue Zählung die Hälfte erreicht), kostet aber Wochen. Der
Workflow legt einmal am Tag das Artefakt `state-backup` ab, sobald das Secret `STATE_BACKUP_KEY` gesetzt
ist. Herunterladen (auf einem Rechner mit `gh`), auf den Sammler-Rechner kopieren und einspielen:

```bash
gh run download <Lauf> -n state-backup --repo Sazzlez/DirkDraft
```

```bash
sudo -u dirkdraft -H /opt/dirkdraft/tools/collector/collect.sh --env /etc/dirkdraft-collector.env --restore state-backup.tar.enc
```

Liegt schon ein Stand da, ersetzt ihn erst `--force`; der alte bleibt als `*.vorher` liegen.

**7. Trockenlauf.** Alles wie ein echter Lauf, nur ohne Push. Zwei Minuten reichen, um Bau, Schlüssel,
Deploy-Key (zum Lesen) und Schutz zu prüfen:

```bash
sudo -u dirkdraft -H /opt/dirkdraft/tools/collector/collect.sh --env /etc/dirkdraft-collector.env --dry-run --minutes 2 --aram-minutes 1
```

Am Ende steht, welche Dateien veröffentlicht worden wären; sie liegen unter
`/var/lib/dirkdraft-collector/work/publish`. Der Trockenlauf zählt wirklich und teilt sich das
Anfragen-Limit des Schlüssels mit einem gerade laufenden Workflow. Deshalb kurz halten, und vor dem
Umstellen den Stand noch einmal frisch einspielen (Schritt 6 mit `--force`).

**8. Dienst einrichten** (noch nicht einschalten):

```bash
sudo cp /opt/dirkdraft/tools/collector/dirkdraft-collector.service /opt/dirkdraft/tools/collector/dirkdraft-collector.timer /etc/systemd/system/
```

```bash
sudo systemctl daemon-reload
```

## Umstellen

Erst wenn du dich dafür entscheidest. **Nie beide Sammler zugleich laufen lassen:** Zwei Stände würden
dieselben Spiele doppelt zählen, sich das Limit des Schlüssels teilen und einander die Dateien
überschreiben.

1. Kette anhalten: Repository-Variable `COLLECT_CHAIN` auf `off` setzen.
2. Warten, bis der laufende Lauf zu Ende ist.
3. Workflow abschalten (Actions → Daten sammeln → ⋯ → Disable workflow), sonst startet ihn der Zeitplan
   alle sechs Stunden wieder.
4. Das neueste `state-backup` holen und mit `--restore … --force` einspielen (Schritt 6).
5. Timer einschalten und den ersten Lauf starten:

```bash
sudo systemctl enable --now dirkdraft-collector.timer
```

```bash
sudo systemctl start --no-block dirkdraft-collector.service
```

Zurück geht es umgekehrt: Timer abschalten (`sudo systemctl disable --now dirkdraft-collector.timer`),
Workflow wieder einschalten, `COLLECT_CHAIN` löschen. GitHub zählt dann von seinem eigenen letzten Stand
weiter; was der eigene Rechner seitdem gezählt hat, kommt dort nicht an.

## Betrieb

- Mitlesen: `journalctl -u dirkdraft-collector -f`. Zeilen mit `FEHLER:` und `WARNUNG:` sagen, was war.
- Nächster Start: `systemctl list-timers dirkdraft-collector.timer`.
- Ein Lauf endet mit Code 0, wenn alles geklappt hat, sonst mit 1 (systemd zeigt ihn dann als
  fehlgeschlagen). Der Timer startet trotzdem den nächsten.
- Neuer Riot-Schlüssel: in `/etc/dirkdraft-collector.env` eintragen; der nächste Lauf nimmt ihn.
- Der Stand liegt in `/var/lib/dirkdraft-collector` (`riot-state.json.gz`, `aram-state.json.gz`), die
  täglichen Backups in `BACKUP_DIR`, 14 Tage lang. Wiederherstellen wie in Schritt 6.
- Die gezählten Spiele und Matchup-Builds zum Nachmessen des Modells (im Workflow das Artefakt `games`)
  liegen in `/var/lib/dirkdraft-collector/private` und werden nie gepusht.
- `data/resets.json` und die Traits wirken wie im Workflow: Sie kommen mit dem Checkout von `master`.
