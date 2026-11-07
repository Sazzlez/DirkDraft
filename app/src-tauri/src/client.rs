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

use crate::lcu::end_of_game;
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

pub fn start(app: &AppHandle) {
    app.manage(ClientState::default());
    let (phase_tx, phase_rx) = watch::channel(String::new());

    let handle = app.clone();
    tauri::async_runtime::spawn(source::run(None, move |update| {
        let state = handle.state::<ClientState>();
        match update {
            Update::Status(status) => {
                if let Ok(mut s) = state.snapshot.lock() {
                    s.status = status;
                }
                let _ = handle.emit("client://status", status);
            }
            Update::Phase(phase) => {
                if let Ok(mut s) = state.snapshot.lock() {
                    s.phase = Some(phase.clone());
                }
                let _ = phase_tx.send(phase.clone());
                let _ = handle.emit("client://phase", phase);
            }
            Update::Session(session) => {
                if let Ok(mut s) = state.snapshot.lock() {
                    s.session = session.clone();
                }
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

/// The game just played (champion, win, final items), once the end-of-game screen has it.
#[tauri::command]
pub async fn client_end_of_game(state: State<'_, ClientState>) -> Result<Option<end_of_game::EndOfGame>, String> {
    let lcu = state.lcu().ok_or("Client nicht verbunden")?;
    let block: Option<serde_json::Value> = lcu.get_json("/lol-end-of-game/v1/eog-stats-block").await;
    Ok(block.as_ref().and_then(end_of_game::read))
}

/// The import button: rune page and/or spells, on the user's click only.
#[tauri::command]
pub async fn client_import(state: State<'_, ClientState>, request: ImportRequest) -> Result<ImportOutcome, String> {
    let lcu = state.lcu().ok_or("Client nicht verbunden")?;
    Ok(import::run(&lcu, request).await)
}
