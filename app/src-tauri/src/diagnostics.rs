// Local diagnostics, so a fault can be replayed instead of guessed: the last drafts as scrubbed
// session recordings — the format of app/tests/recordings, one {offsetMs, session|phase} per line,
// which the tests and the scenes play directly — and the last games as one scrubbed Live Client
// snapshot a minute. Sessions and snapshots arrive here already scrubbed (lcu/scrub.rs): no names,
// PUUIDs, ids or chat. Nothing leaves the machine; "Diagnose speichern" copies it with the log into a
// folder on the desktop for whoever is asked to look. Off unless switched on (settings key
// "diagnostics") or, while the switch was never used, an empty diagnostics.on lies next to
// settings.json: the developer's own machine records from the first start after the update,
// friends' machines do not (user's word).

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use tauri::{AppHandle, Manager, State};

/// About 300 KB a draft and 2 MB a game: a few megabytes in all.
const KEEP_DRAFTS: usize = 10;
const KEEP_GAMES: usize = 3;
const GAME_EVERY: Duration = Duration::from_secs(60);

struct Recording {
    file: File,
    started: Instant,
}

impl Recording {
    fn open(prefix: &str, keep: usize) -> Option<Self> {
        let dir = dir()?;
        std::fs::create_dir_all(&dir).ok()?;
        prune(&dir, prefix, keep.saturating_sub(1));
        let path = dir.join(format!("{prefix}-{}.jsonl", crate::logfile::stamp()));
        let file = OpenOptions::new().create(true).append(true).open(path).ok()?;
        Some(Self { file, started: Instant::now() })
    }

    fn line(&mut self, key: &str, json: &str) {
        let offset = self.started.elapsed().as_millis();
        let _ = writeln!(self.file, "{{\"offsetMs\":{offset},\"{key}\":{json}}}");
    }
}

#[derive(Default)]
struct Inner {
    enabled: bool,
    draft: Option<Recording>,
    /// The session ended; the draft file stays open for the phase that follows (the loading screen).
    draft_ending: bool,
    game: Option<Recording>,
    last_game_line: Option<Instant>,
    /** The newest snapshot not yet written, kept for the end of the game. */
    pending_game: Option<String>,
}

pub struct Diagnostics(Mutex<Inner>);

impl Diagnostics {
    pub fn new(enabled: bool) -> Self {
        Self(Mutex::new(Inner { enabled, ..Inner::default() }))
    }

    pub fn set_enabled(&self, enabled: bool) {
        if let Ok(mut inner) = self.0.lock() {
            inner.enabled = enabled;
            if !enabled {
                inner.draft = None;
                inner.game = None;
            }
        }
    }

    /// A champion select session (scrubbed JSON), or None when it ended.
    pub fn on_session(&self, session: Option<&str>) {
        let Ok(mut inner) = self.0.lock() else { return };
        if !inner.enabled {
            return;
        }
        match session {
            Some(json) => {
                if inner.draft.is_none() || inner.draft_ending {
                    inner.draft = Recording::open("draft", KEEP_DRAFTS);
                    inner.draft_ending = false;
                }
                if let Some(draft) = inner.draft.as_mut() {
                    draft.line("session", json);
                }
            }
            None => {
                if let Some(draft) = inner.draft.as_mut() {
                    draft.line("session", "null");
                    inner.draft_ending = true;
                }
            }
        }
    }

    /// A gameflow phase: written into the running draft; the first phase outside champion select
    /// after the session ended closes the file.
    pub fn on_phase(&self, phase: &str) {
        let Ok(mut inner) = self.0.lock() else { return };
        if !inner.enabled {
            return;
        }
        let ending = inner.draft_ending;
        if let Some(draft) = inner.draft.as_mut() {
            let quoted = serde_json::to_string(phase).unwrap_or_else(|_| "\"\"".into());
            draft.line("phase", &quoted);
            if ending && crate::lcu::source::outside_champ_select(phase) {
                inner.draft = None;
                inner.draft_ending = false;
            }
        }
    }

    /// A Live Client snapshot (scrubbed JSON), kept once a minute; None when the game's API went quiet.
    pub fn on_game(&self, game: Option<&str>) {
        let Ok(mut inner) = self.0.lock() else { return };
        if !inner.enabled {
            return;
        }
        let Some(json) = game else {
            // The API went quiet: the last snapshot holds how the game ended.
            if let Some(last) = inner.pending_game.take() {
                if let Some(file) = inner.game.as_mut() {
                    file.line("game", &last);
                }
            }
            inner.game = None;
            inner.last_game_line = None;
            return;
        };
        if inner.game.is_none() {
            inner.game = Recording::open("game", KEEP_GAMES);
            inner.last_game_line = None;
        }
        let due = inner.last_game_line.is_none_or(|at| at.elapsed() >= GAME_EVERY);
        if due {
            if let Some(file) = inner.game.as_mut() {
                file.line("game", json);
                inner.last_game_line = Some(Instant::now());
            }
            inner.pending_game = None;
        } else {
            inner.pending_game = Some(json.to_string());
        }
    }
}

pub fn dir() -> Option<PathBuf> {
    crate::logfile::dir().map(|d| d.join("diagnose"))
}

/// Deletes the oldest files of a kind until `keep` remain (names sort by their time stamp).
fn prune(dir: &Path, prefix: &str, keep: usize) {
    let Ok(entries) = std::fs::read_dir(dir) else { return };
    let mut files: Vec<PathBuf> = entries
        .filter_map(|e| e.ok().map(|e| e.path()))
        .filter(|p| p.file_name().and_then(|n| n.to_str()).is_some_and(|n| n.starts_with(&format!("{prefix}-")) && n.ends_with(".jsonl")))
        .collect();
    files.sort();
    while files.len() > keep {
        let _ = std::fs::remove_file(files.remove(0));
    }
}

/// "Diagnose speichern": the log, the recordings and a few plain facts into a new folder on the
/// desktop, opened in Explorer. Returns the folder.
#[tauri::command]
pub fn diagnose_export(app: AppHandle, shell: State<'_, crate::shell::ShellState>) -> Result<String, String> {
    let desktop = app.path().desktop_dir().map_err(|e| e.to_string())?;
    let target = desktop.join(format!("DirkDraft-Diagnose-{}", crate::logfile::stamp()));
    std::fs::create_dir_all(&target).map_err(|e| e.to_string())?;
    log::logger().flush();

    if let Some(logs) = crate::logfile::dir() {
        for name in ["dirkdraft.log", "dirkdraft.old.log"] {
            let _ = std::fs::copy(logs.join(name), target.join(name));
        }
    }
    if let Some(recordings) = dir() {
        if let Ok(entries) = std::fs::read_dir(&recordings) {
            for entry in entries.flatten() {
                let _ = std::fs::copy(entry.path(), target.join(entry.file_name()));
            }
        }
    }
    let settings = shell.settings.lock().map(|s| s.page()).unwrap_or_default();
    let info = format!(
        "DirkDraft {}\nWindows-Build {}\nEinstellungen {}\nErstellt {} (UTC)\n\nOhne Namen, PUUIDs, IDs oder Chat. Draft-Dateien lassen sich in app/tests abspielen.\n",
        env!("CARGO_PKG_VERSION"),
        crate::shell::windows_build(),
        settings,
        crate::logfile::stamp(),
    );
    std::fs::write(target.join("info.txt"), info).map_err(|e| e.to_string())?;
    log::info!("Diagnose gespeichert");

    #[cfg(windows)]
    let _ = std::process::Command::new("explorer").arg(&target).spawn();
    Ok(target.display().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn prune_keeps_the_newest() {
        let dir = std::env::temp_dir().join(format!("dirkdraft-diag-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        for stamp in ["20261107-100000", "20261107-110000", "20261107-120000"] {
            std::fs::write(dir.join(format!("draft-{stamp}.jsonl")), "x").unwrap();
        }
        std::fs::write(dir.join("game-20261107-090000.jsonl"), "x").unwrap();
        prune(&dir, "draft", 1);
        let mut left: Vec<String> = std::fs::read_dir(&dir).unwrap().map(|e| e.unwrap().file_name().into_string().unwrap()).collect();
        left.sort();
        assert_eq!(left, ["draft-20261107-120000.jsonl", "game-20261107-090000.jsonl"]);
        let _ = std::fs::remove_dir_all(dir);
    }
}
