// Updates as in 1.5: one check against GitHub Releases at start, "Version X ist da" in the title
// bar, and only a click downloads, installs and restarts (fixed rule: nothing without the user's
// click). Velopack installed the app; outside an install (development) there is nothing to update.

use std::sync::Mutex;

use serde::Serialize;
use tauri::{AppHandle, State};
use tauri_plugin_window_state::{AppHandleExt, StateFlags};
use velopack::sources::GithubSource;
use velopack::{UpdateCheck, UpdateInfo, UpdateManager};

/// Where every installed copy looks; the same feed 1.5 reads, so 1.5 sees 2.0 as a normal update.
const REPO: &str = "https://github.com/Sazzlez/DirkDraft";

#[derive(Default)]
pub struct UpdaterState {
    pending: Mutex<Option<UpdateInfo>>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateStatus {
    /// False in development: the app was not installed by Velopack.
    installed: bool,
    current: Option<String>,
    /// The newer version on GitHub, if there is one.
    available: Option<String>,
}

fn manager() -> Result<UpdateManager, String> {
    UpdateManager::new(GithubSource::new(REPO, None, false), None, None).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn update_check(state: State<'_, UpdaterState>) -> Result<UpdateStatus, String> {
    let checked = tauri::async_runtime::spawn_blocking(|| {
        // Not installed: no manager at all. Installed but offline: a manager, and a failed check.
        let Ok(manager) = manager() else { return (false, None, None) };
        let current = manager.get_current_version_as_string();
        let available = match manager.check_for_updates() {
            Ok(UpdateCheck::UpdateAvailable(info)) => Some(*info),
            _ => None,
        };
        (true, Some(current), available)
    })
    .await
    .map_err(|e| e.to_string())?;

    let (installed, current, info) = checked;
    let available = info.as_ref().map(|i| i.TargetFullRelease.Version.clone());
    if let Ok(mut pending) = state.pending.lock() {
        *pending = info;
    }
    Ok(UpdateStatus { installed, current, available })
}

/// "Jetzt aktualisieren": download, install, restart. Returns only on failure.
#[tauri::command]
pub async fn update_apply(app: AppHandle, state: State<'_, UpdaterState>) -> Result<(), String> {
    let info = state.pending.lock().map_err(|e| e.to_string())?.clone().ok_or("Kein Update bereit")?;
    // The restart ends the process without the usual exit: keep size and place first.
    let _ = app.save_window_state(StateFlags::SIZE | StateFlags::POSITION);
    tauri::async_runtime::spawn_blocking(move || {
        let manager = manager()?;
        manager.download_updates(&info, None).map_err(|e| e.to_string())?;
        manager.apply_updates_and_restart(&info).map_err(|e| e.to_string())
    })
    .await
    .map_err(|e| e.to_string())?
}
