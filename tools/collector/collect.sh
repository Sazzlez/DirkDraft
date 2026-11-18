#!/usr/bin/env bash
# One collector run on a machine of your own (a Raspberry Pi, a small server): what the GitHub
# workflow "Daten sammeln" (.github/workflows/collect-builds.yml) does, without GitHub Actions.
# Build, riot collect, riot aram, export, the guard before publishing (riot guard), one commit
# force-pushed to the branch "data" with a deploy key. The state stays on this machine's disk; once a
# day it is also written encrypted to a backup folder.
#
# Every step runs even when one before it failed (as in the workflow): what was counted before a
# failure is still counted, and whether a file may be published is the guard's call alone.
#
# The guide is LIESMICH.md beside this file. Nothing here switches the GitHub workflow off: never run
# both on the same key for real - two states would count the same games twice and overwrite each
# other's files.
#
#   collect.sh [--dry-run] [--minutes N] [--aram-minutes N] [--no-update] [--env FILE]
#   collect.sh --restore BACKUP.tar.enc [--force] [--env FILE]

set -u
set -o pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

say() { printf '%s %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }
warn() { printf '%s WARNUNG: %s\n' "$(date -u +%H:%M:%SZ)" "$*" >&2; }
fail() { printf '%s FEHLER: %s\n' "$(date -u +%H:%M:%SZ)" "$*" >&2; }

# ---- arguments ------------------------------------------------------------------------------------

dry_run="${DRY_RUN:-0}"
update=1
restore=""
force=0
env_file=""
arg_minutes=""
arg_aram_minutes=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) dry_run=1 ;;
    --no-update) update=0 ;;
    --force) force=1 ;;
    --minutes) arg_minutes="${2:-}"; shift ;;
    --aram-minutes) arg_aram_minutes="${2:-}"; shift ;;
    --restore) restore="${2:-}"; shift ;;
    --env) env_file="${2:-}"; shift ;;
    -h|--help) sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) fail "Unbekannte Option: $1"; exit 2 ;;
  esac
  shift
done

# ---- settings -------------------------------------------------------------------------------------

# Under systemd the unit's EnvironmentFile has set everything already. By hand: --env FILE, or
# collector.env beside this script. The file holds the Riot key: chmod 600.
if [ -z "$env_file" ] && [ -f "$here/collector.env" ]; then env_file="$here/collector.env"; fi
if [ -n "$env_file" ]; then
  if [ ! -f "$env_file" ]; then fail "Einstellungsdatei fehlt: $env_file"; exit 2; fi
  set -a
  # shellcheck disable=SC1090
  . "$env_file"
  set +a
fi

REPO_DIR="${REPO_DIR:-$(cd "$here/../.." && pwd)}"
STATE_DIR="${STATE_DIR:-/var/lib/dirkdraft-collector}"
BACKUP_DIR="${BACKUP_DIR:-$STATE_DIR/backups}"
BACKUP_KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"
TIERS="${TIERS:-platinum}"
MINUTES="${arg_minutes:-${MINUTES:-270}}"
ARAM_MINUTES="${arg_aram_minutes:-${ARAM_MINUTES:-60}}"
SOURCE_BRANCH="${SOURCE_BRANCH:-master}"
DATA_REMOTE="${DATA_REMOTE:-git@github.com:Sazzlez/DirkDraft.git}"
DATA_BRANCH="${DATA_BRANCH:-data}"
DEPLOY_KEY="${DEPLOY_KEY:-}"
COMMIT_NAME="${COMMIT_NAME:-DirkDraft-Sammler}"
COMMIT_EMAIL="${COMMIT_EMAIL:-dirkdraft-sammler@invalid}"

STATE="$STATE_DIR/riot-state.json.gz"
ARAM_STATE="$STATE_DIR/aram-state.json.gz"
work="$STATE_DIR/work"

# The deploy key and nothing else: no agent, no other identity, and a known host key from now on.
if [ -n "$DEPLOY_KEY" ]; then
  export GIT_SSH_COMMAND="ssh -i $DEPLOY_KEY -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new -o BatchMode=yes"
fi
export GIT_TERMINAL_PROMPT=0

mkdir -p "$STATE_DIR" || { fail "Kein Schreibzugriff auf $STATE_DIR"; exit 2; }

# ---- one collector at a time ------------------------------------------------------------------------

# Two collectors on one state would count the same games twice. The lock goes with the process, so a
# power cut leaves nothing behind.
exec 9>"$STATE_DIR/collector.lock"
if ! flock -n 9; then
  say "Es laeuft schon ein Sammler auf diesem Stand - nichts zu tun."
  exit 0
fi

# ---- restore --------------------------------------------------------------------------------------

# A backup of this script or of the GitHub workflow (artifact "state-backup"): the same format, both
# states in one encrypted tar.
if [ -n "$restore" ]; then
  if [ -z "${STATE_BACKUP_KEY:-}" ]; then fail "STATE_BACKUP_KEY fehlt - ohne ihn laesst sich kein Backup lesen."; exit 2; fi
  if [ ! -f "$restore" ]; then fail "Backup fehlt: $restore"; exit 2; fi
  if { [ -f "$STATE" ] || [ -f "$ARAM_STATE" ]; } && [ "$force" -ne 1 ]; then
    fail "In $STATE_DIR liegt schon ein Stand. Mit --force wird er ersetzt (der alte bleibt als *.vorher)."
    exit 2
  fi
  into="$STATE_DIR/restore.tmp"
  rm -rf "$into" && mkdir -p "$into"
  if ! openssl enc -d -aes-256-cbc -pbkdf2 -pass env:STATE_BACKUP_KEY -in "$restore" | tar -x -C "$into"; then
    rm -rf "$into"
    fail "Das Backup laesst sich nicht entschluesseln - falscher STATE_BACKUP_KEY? Der Stand ist unveraendert."
    exit 1
  fi
  found=0
  for f in riot-state.json.gz aram-state.json.gz; do
    [ -f "$into/$f" ] || continue
    if [ -f "$STATE_DIR/$f" ]; then mv -f "$STATE_DIR/$f" "$STATE_DIR/$f.vorher"; fi
    mv -f "$into/$f" "$STATE_DIR/$f"
    say "Wiederhergestellt: $STATE_DIR/$f"
    found=1
  done
  rm -rf "$into"
  if [ "$found" -ne 1 ]; then fail "Im Backup lag kein Stand."; exit 1; fi
  exit 0
fi

# ---- the run ----------------------------------------------------------------------------------------

if [ -z "${RIOT_API_KEY:-}" ]; then fail "RIOT_API_KEY fehlt (Einstellungsdatei)."; exit 2; fi
export RIOT_API_KEY

status=0
if [ "$dry_run" -eq 1 ]; then say "TROCKENLAUF: alles wie sonst, nur der Push in den Branch $DATA_BRANCH entfaellt."; fi

cd "$REPO_DIR" || { fail "Kein Checkout unter $REPO_DIR"; exit 2; }

# The code the collector builds from: the newest master, as every run of the workflow. A failed pull
# (no network yet after boot) is no reason not to count with what is there.
if [ "$update" -eq 1 ]; then
  if git fetch -q origin "$SOURCE_BRANCH" && git merge -q --ff-only FETCH_HEAD; then
    say "Code: $(git rev-parse --short HEAD) ($SOURCE_BRANCH)"
  else
    warn "Der Checkout liess sich nicht auf origin/$SOURCE_BRANCH bringen - es laeuft der Stand $(git rev-parse --short HEAD)."
  fi
fi

if ! dotnet build src/DraftPilot.Tools -c Release --nologo -v quiet; then
  fail "Der Bau ist fehlgeschlagen - kein Lauf."
  exit 1
fi
# The data files (data/resets.json, the traits) are found relative to the repository root.
tool() { dotnet run --project src/DraftPilot.Tools -c Release --no-build -- "$@"; }

say "Ranked sammeln ($MINUTES min, $TIERS)"
tool riot collect --minutes "$MINUTES" --tiers "$TIERS" --state "$STATE" || { fail "riot collect endete mit einem Fehler."; status=1; }

say "ARAM sammeln ($ARAM_MINUTES min)"
tool riot aram --minutes "$ARAM_MINUTES" --tiers "$TIERS" --state "$ARAM_STATE" || { fail "riot aram endete mit einem Fehler."; status=1; }

# ---- the daily backup -------------------------------------------------------------------------------

# The state is this machine's only copy of weeks of counting. Once per UTC day both states go into
# one encrypted file; BACKUP_DIR should be another disk than the state's (LIESMICH.md). Encrypted,
# because the state holds the match ids already looked at, which the project never hands out.
backup() {
  if [ -z "${STATE_BACKUP_KEY:-}" ]; then
    warn "STATE_BACKUP_KEY fehlt - kein Backup, der Stand liegt nur in $STATE_DIR."
    return 0
  fi
  local today target files=()
  today="$(date -u +%F)"
  target="$BACKUP_DIR/state-backup-$today.tar.enc"
  if [ -f "$target" ]; then return 0; fi
  for f in riot-state.json.gz aram-state.json.gz; do
    if [ -f "$STATE_DIR/$f" ]; then files+=("$f"); fi
  done
  if [ "${#files[@]}" -eq 0 ]; then return 0; fi
  mkdir -p "$BACKUP_DIR" || { warn "Kein Schreibzugriff auf $BACKUP_DIR - kein Backup."; return 1; }
  # Written beside and moved: a run cut short must not leave half a backup under the day's name.
  if tar -cf - -C "$STATE_DIR" "${files[@]}" | openssl enc -aes-256-cbc -pbkdf2 -salt -pass env:STATE_BACKUP_KEY -out "$target.tmp"; then
    mv -f "$target.tmp" "$target"
    say "Backup: $target"
    find "$BACKUP_DIR" -maxdepth 1 -name 'state-backup-*.tar.enc' -mtime "+$BACKUP_KEEP_DAYS" -delete
  else
    rm -f "$target.tmp"
    warn "Das Backup ist fehlgeschlagen."
    return 1
  fi
}
backup || status=1

# ---- export -----------------------------------------------------------------------------------------

rm -rf "$work" && mkdir -p "$work/publish" "$work/published" "$work/private"

# Each export by itself, so one failing does not keep the other from the guard.
if [ -f "$STATE" ]; then
  tool riot export --state "$STATE" --tier "$TIERS" --out "$work/publish/builds.json" --meta-out "$work/publish/meta.json" \
    --situational-out "$work/publish/situational.json" --games-out "$work/private/games.json.gz" --matchups-out "$work/private/matchups.json.gz" \
    --traits-out "$work/publish/traits.json" --combos-out "$work/publish/combos.json" || { fail "Der Ranked-Export ist fehlgeschlagen."; status=1; }
else
  warn "Kein Ranked-Stand ($STATE) - kein Export."
fi
if [ -f "$ARAM_STATE" ]; then
  tool riot export-aram --state "$ARAM_STATE" --out "$work/publish/aram.json" || { fail "Der ARAM-Export ist fehlgeschlagen."; status=1; }
else
  warn "Kein ARAM-Stand ($ARAM_STATE) - kein Export."
fi

# The counted games and the matchup builds, for measuring the draft model: kept here, never pushed.
if [ -f "$work/private/games.json.gz" ]; then
  mkdir -p "$STATE_DIR/private" && cp -f "$work/private/"* "$STATE_DIR/private/"
fi

# ---- what is published now ----------------------------------------------------------------------------

# Three answers, and they must not be mixed up: the branch is there (its files are the comparison),
# there is no such branch yet (first publication), or nobody answered. Without an answer nothing is
# published - a thin count must not replace the published files just because the network was down.
git ls-remote --exit-code --heads "$DATA_REMOTE" "$DATA_BRANCH" >/dev/null 2>&1
case $? in
  0)
    if git init -q "$work/fetch" && git -C "$work/fetch" fetch -q --depth 1 "$DATA_REMOTE" "$DATA_BRANCH" \
        && git -C "$work/fetch" archive FETCH_HEAD | tar -x -C "$work/published"; then
      say "Veroeffentlicht: $(ls "$work/published" | tr '\n' ' ')"
    else
      fail "Der veroeffentlichte Stand liess sich nicht holen - nichts wird veroeffentlicht."
      exit 1
    fi
    ;;
  2) say "Noch kein Branch $DATA_BRANCH - erste Veroeffentlichung." ;;
  *)
    fail "$DATA_REMOTE antwortet nicht (Netz, Deploy-Key?) - nichts wird veroeffentlicht. Der Stand ist gesichert."
    exit 1
    ;;
esac

# ---- the guard, then one commit ---------------------------------------------------------------------

keep() {
  for f in "$@"; do
    if [ -f "$work/published/$f" ]; then cp "$work/published/$f" "$work/publish/$f"; else rm -f "$work/publish/$f"; fi
  done
}
changed=0

# Ranked: three files of one count, published together or not at all. Any exit code of the guard but
# 0 keeps what is published - also when the tool itself does not run.
if tool riot guard --label Ranked --old "$work/published/meta.json" --new "$work/publish/meta.json" \
    && [ -f "$work/publish/builds.json" ] && [ -f "$work/publish/situational.json" ]; then
  changed=1
else
  keep meta.json builds.json situational.json
fi

if tool riot guard --label ARAM --old "$work/published/aram.json" --new "$work/publish/aram.json"; then
  changed=1
else
  keep aram.json
fi

# Whatever else is published and was not written anew stays on the branch.
for path in "$work/published"/*; do
  [ -f "$path" ] || continue
  f="$(basename "$path")"
  [ -f "$work/publish/$f" ] || cp "$path" "$work/publish/$f"
done

if [ "$changed" -eq 0 ]; then
  say "Nichts Neues zu veroeffentlichen - der veroeffentlichte Stand bleibt."
  exit "$status"
fi

# One commit, force-pushed: the branch holds the current files and no history of old ones.
cd "$work/publish" || exit 1
git init -q -b "$DATA_BRANCH"
git -c core.autocrlf=false add -A .
git -c user.name="$COMMIT_NAME" -c user.email="$COMMIT_EMAIL" commit -q -m "Daten $(date -u +%Y-%m-%dT%H:%MZ)" || { fail "Der Commit ist fehlgeschlagen."; exit 1; }

if [ "$dry_run" -eq 1 ]; then
  say "TROCKENLAUF: kein Push. Das waere veroeffentlicht worden ($work/publish):"
  git ls-tree -l HEAD | awk '{ printf "  %10d  %s\n", $4, $5 }'
  exit "$status"
fi

if git push -q --force "$DATA_REMOTE" "$DATA_BRANCH"; then
  say "Veroeffentlicht nach $DATA_BRANCH."
else
  fail "Der Push ist fehlgeschlagen (Deploy-Key mit Schreibrecht?). Die Dateien liegen in $work/publish."
  status=1
fi
exit "$status"
