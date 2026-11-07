// The bridge between the client/game and the page: events for every change, a snapshot for a page
// that (re)loads, and the few commands the page may call. The page gets data without names; it can
// read what the draft needs and write only runes and spells.
//
// Events: client://status (ClientStatus), client://phase (string), client://session (JSON string or
// null), game://live (JSON string or null).

use std::sync::Mutex;

use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager, State};
use tokio::sync::watch;

use crate::lcu::http::Lcu;
use crate::lcu::import::{self, ImportOutcome, ImportRequest};
use crate::lcu::source::{self, ClientStatus, Update};
use crate::lcu::live;

#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    pub status: ClientStatus,
    pub phase: Option<String>,
    pub session: Option<String>,
    pub game: Option<String>,
}

#[derive(Default)]
pub struct ClientState {
    snapshot: Mutex<Snapshot>,
    lcu: Mutex<Option<Lcu>>,
}

impl ClientState {
    fn lcu(&self) -> Option<Lcu> {
        self.lcu.lock().ok().and_then(|lcu| lcu.clone())
    }
}

/// `lockfile`: 1.5's lockfilePath setting, for an install the search does not find.
pub fn start(app: &AppHandle, lockfile: Option<std::path::PathBuf>) {
    app.manage(ClientState::default());
    let (phase_tx, phase_rx) = watch::channel(String::new());

    let handle = app.clone();
    tauri::async_runtime::spawn(source::run(lockfile, move |update| {
        let state = handle.state::<ClientState>();
        match update {
            Update::Status(status) => {
                if let Ok(mut s) = state.snapshot.lock() {
                    s.status = status;
                    // A closed client has no phase: a page that reloads now must not see the last
                    // game still running, and the game poller stops asking.
                    if !status.client_running {
                        s.phase = None;
                        s.session = None;
                    }
                }
                if !status.client_running {
                    let _ = phase_tx.send(String::new());
                }
                let _ = handle.emit("client://status", status);
            }
            Update::Phase(phase) => {
                if let Ok(mut s) = state.snapshot.lock() {
                    s.phase = Some(phase.clone());
                }
                let _ = phase_tx.send(phase.clone());
                handle.state::<crate::diagnostics::Diagnostics>().on_phase(&phase);
                crate::shell::on_phase(&handle, &phase);
                let _ = handle.emit("client://phase", phase);
            }
            Update::Session(session) => {
                if let Ok(mut s) = state.snapshot.lock() {
                    s.session = session.clone();
                }
                handle.state::<crate::diagnostics::Diagnostics>().on_session(session.as_deref());
                let _ = handle.emit("client://session", session);
            }
            Update::Client(lcu) => {
                if let Ok(mut current) = state.lcu.lock() {
                    *current = lcu;
                }
            }
        }
    }));

    let handle = app.clone();
    tauri::async_runtime::spawn(live::run(phase_rx, move |game| {
        if let Ok(mut s) = handle.state::<ClientState>().snapshot.lock() {
            s.game = game.clone();
        }
        handle.state::<crate::diagnostics::Diagnostics>().on_game(game.as_deref());
        let _ = handle.emit("game://live", game);
    }));
}

/// Everything known right now, for a page that just loaded and missed the events so far.
#[tauri::command]
pub fn client_snapshot(state: State<'_, ClientState>) -> Snapshot {
    state.snapshot.lock().map(|s| s.clone()).unwrap_or_default()
}

/// Champions the local player may pick this game (ownership and rotation applied).
#[tauri::command]
pub async fn client_pickable(state: State<'_, ClientState>) -> Result<Vec<i64>, String> {
    let lcu = state.lcu().ok_or("Client nicht verbunden")?;
    Ok(lcu.get_json("/lol-champ-select/v1/pickable-champion-ids").await.unwrap_or_default())
}

/// The patch the client runs ("16.22.1"), from the specific endpoint, else the old one.
#[tauri::command]
pub async fn client_game_version(state: State<'_, ClientState>) -> Result<Option<String>, String> {
    let lcu = state.lcu().ok_or("Client nicht verbunden")?;
    if let Some(body) = lcu.read("/lol-patch/v1/game-version").await.body {
        let version = body.trim().trim_matches('"').to_string();
        if !version.is_empty() {
            return Ok(Some(version));
        }
    }
    let build: Option<serde_json::Value> = lcu.get_json("/system/v1/builds").await;
    Ok(build.and_then(|b| b.get("version").and_then(|v| v.as_str()).map(str::to_string)))
}

/// The import button: rune page and/or spells, on the user's click only.
#[tauri::command]
pub async fn client_import(state: State<'_, ClientState>, request: ImportRequest) -> Result<ImportOutcome, String> {
    let lcu = state.lcu().ok_or("Client nicht verbunden")?;
    Ok(import::run(&lcu, request).await)
}
