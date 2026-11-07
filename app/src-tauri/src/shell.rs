// The window and the tray, with the rules 1.5 already had (docs/redesign/spec.md, section 1):
// one instance, a frameless window with our own title bar, at least 340 × 560, size and place
// remembered, X puts it in the tray, only the tray quits. No maximising: the board is laid out for
// widths, not for a full screen.
//
// Automatic showing, as in 1.5 and switchable in settings.json: champion select brings the window
// up, the game start too — never taking the focus from the client or the game. A window the game
// minimised while taking the screen comes back for the first minute; one the user minimised stays.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use serde::Serialize;
use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, TrayIconBuilder, TrayIconEvent},
    window::{Color, Effect, EffectsBuilder},
    AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindow, WebviewWindowBuilder, WindowEvent,
};
use tauri_plugin_window_state::StateFlags;

use crate::settings::{self, Settings};

const MAIN: &str = "main";
/// How long after the game start a minimised window counts as the game's doing.
const GAME_GRAB: Duration = Duration::from_secs(60);
/// The tray hint stays this long before the window goes.
const HINT_DELAY: Duration = Duration::from_millis(2600);

pub struct ShellState {
    pub settings: Mutex<Settings>,
    minimised_by_hand: AtomicBool,
    game_started: Mutex<Option<Instant>>,
    phase: Mutex<String>,
}

impl ShellState {
    pub fn flag(&self, (key, default): (&str, bool)) -> bool {
        self.settings.lock().map(|s| s.flag(key, default)).unwrap_or(default)
    }

    fn set_flag(&self, key: &str, value: bool) {
        if let Ok(mut s) = self.settings.lock() {
            s.set_flag(key, value);
        }
    }
}

pub fn run() {
    let settings = Settings::load();
    let lockfile = settings.text(settings::LOCKFILE_PATH).map(std::path::PathBuf::from);

    tauri::Builder::default()
        // Must come first: a second start hands over to the running one and quits.
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| show(app)))
        // Size and position only. Visibility is ours to decide (a window closed into the tray
        // would otherwise start hidden), and the frame never changes.
        .plugin(
            tauri_plugin_window_state::Builder::new()
                .with_state_flags(StateFlags::SIZE | StateFlags::POSITION)
                .build(),
        )
        .manage(ShellState {
            settings: Mutex::new(settings),
            minimised_by_hand: AtomicBool::new(false),
            game_started: Mutex::new(None),
            phase: Mutex::new(String::new()),
        })
        .setup(move |app| {
            crate::client::start(app.handle(), lockfile.clone());
            let window = create_window(app.handle())?;
            create_tray(app.handle())?;
            let state = app.state::<ShellState>();
            window.set_always_on_top(state.flag(settings::KEEP_ON_TOP))?;
            window.show()?;
            Ok(())
        })
        .on_window_event(|window, event| {
            let app = window.app_handle();
            let state = app.state::<ShellState>();
            match event {
                // X, Alt+F4 and "close" in the taskbar all put the window in the tray. The first
                // time, it says so first: a user who believes the tool is closed keeps talking to a
                // stale instance forever.
                WindowEvent::CloseRequested { api, .. } => {
                    api.prevent_close();
                    if state.flag(settings::TRAY_HINT_SHOWN) {
                        let _ = window.hide();
                    } else {
                        state.set_flag(settings::TRAY_HINT_SHOWN.0, true);
                        let _ = window.emit("shell://tray-hint", ());
                        let window = window.clone();
                        tauri::async_runtime::spawn(async move {
                            tokio::time::sleep(HINT_DELAY).await;
                            let _ = window.hide();
                        });
                    }
                }
                WindowEvent::Focused(true) => state.minimised_by_hand.store(false, Ordering::Relaxed),
                WindowEvent::Resized(_) => {
                    if window.is_minimized().unwrap_or(false) && game_took_the_screen(&state) {
                        if let Some(w) = app.get_webview_window(MAIN) {
                            tauri::async_runtime::spawn(async move {
                                tokio::time::sleep(Duration::from_millis(150)).await;
                                show_quietly(&w);
                            });
                        }
                    }
                }
                _ => {}
            }
        })
        .invoke_handler(tauri::generate_handler![
            crate::client::client_snapshot,
            crate::client::client_pickable,
            crate::client::client_game_version,
            crate::client::client_end_of_game,
            crate::client::client_pool_input,
            crate::client::client_import,
            crate::data::data_read,
            crate::data::data_write,
            shell_settings,
            shell_set_pinned,
            shell_minimize,
        ])
        .run(tauri::generate_context!())
        .expect("DirkDraft konnte nicht starten");
}

/// A minimise right after the game start is the game's doing, not the user's.
fn game_took_the_screen(state: &ShellState) -> bool {
    !state.minimised_by_hand.load(Ordering::Relaxed)
        && state.flag(settings::AUTO_SHOW_ON_GAME_START)
        && state.game_started.lock().ok().and_then(|t| *t).is_some_and(|at| at.elapsed() < GAME_GRAB)
}

/// The client's gameflow phase changed (called by client.rs).
pub fn on_phase(app: &AppHandle, phase: &str) {
    let state = app.state::<ShellState>();
    let previous = state.phase.lock().map(|mut p| std::mem::replace(&mut *p, phase.to_string())).unwrap_or_default();
    if previous == phase {
        return;
    }
    let Some(window) = app.get_webview_window(MAIN) else { return };

    if phase == "ChampSelect" {
        if state.flag(settings::AUTO_SHOW_ON_CHAMP_SELECT) && !state.minimised_by_hand.load(Ordering::Relaxed) {
            show_quietly(&window);
        }
        return;
    }

    if previous == "ChampSelect" && state.flag(settings::AUTO_HIDE_AFTER_CHAMP_SELECT) {
        let _ = window.hide();
    }

    // GameStart and InProgress both: the fullscreen grab happens at the latter.
    if (phase == "GameStart" || phase == "InProgress") && state.flag(settings::AUTO_SHOW_ON_GAME_START) {
        if let Ok(mut started) = state.game_started.lock() {
            *started = Some(Instant::now());
        }
        if !state.minimised_by_hand.load(Ordering::Relaxed) {
            show_quietly(&window);
        }
    }
}

/// Visible, and restored if minimised — without activating: the client or the game keeps the focus.
fn show_quietly(window: &WebviewWindow) {
    #[cfg(windows)]
    if let Ok(hwnd) = window.hwnd() {
        use windows_sys::Win32::UI::WindowsAndMessaging::{ShowWindow, SW_SHOWNOACTIVATE};
        // SAFETY: the handle belongs to our own live window; ShowWindow only changes its state.
        unsafe {
            ShowWindow(hwnd.0 as _, SW_SHOWNOACTIVATE);
        }
        return;
    }
    let _ = window.show();
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ShellSettings {
    keep_on_top: bool,
}

#[tauri::command]
fn shell_settings(state: State<'_, ShellState>) -> ShellSettings {
    ShellSettings { keep_on_top: state.flag(settings::KEEP_ON_TOP) }
}

/// The pin: always on top, remembered for the next start (keepOnTop, as in 1.5).
#[tauri::command]
fn shell_set_pinned(window: WebviewWindow, state: State<'_, ShellState>, on: bool) -> Result<(), String> {
    window.set_always_on_top(on).map_err(|e| e.to_string())?;
    state.set_flag(settings::KEEP_ON_TOP.0, on);
    Ok(())
}

/// The minimise button: by hand, so nothing brings the window back until the user does.
#[tauri::command]
fn shell_minimize(window: WebviewWindow, state: State<'_, ShellState>) -> Result<(), String> {
    state.minimised_by_hand.store(true, Ordering::Relaxed);
    window.minimize().map_err(|e| e.to_string())
}

fn create_window(app: &AppHandle) -> tauri::Result<WebviewWindow> {
    let glass = mica_available();

    // The page reads this before its first paint and draws its background to match.
    let marker = format!(
        "window.__DIRKDRAFT_SHELL__ = {{ glass: '{}' }};",
        if glass { "mica" } else { "none" }
    );

    let builder = WebviewWindowBuilder::new(app, MAIN, WebviewUrl::default())
        .title("DirkDraft")
        .inner_size(1120.0, 860.0)
        .min_inner_size(340.0, 560.0)
        .center()
        .decorations(false)
        .maximizable(false)
        // Shown by setup once the saved size and place are applied: no jump on start.
        .visible(false)
        .initialization_script(&marker);

    let builder = if glass {
        builder
            .transparent(true)
            .effects(EffectsBuilder::new().effect(Effect::Mica).build())
    } else {
        // #1c1c1e, the opaque fallback from the spec; also no white flash before the page loads.
        builder.background_color(Color(28, 28, 30, 255))
    };

    builder.build()
}

/// Mica exists from Windows 11 (build 22000). On Windows 10 the effect silently does nothing and a
/// transparent window would show the raw desktop, so it stays opaque there. Acrylic would work on
/// 10, but lags behind the mouse while the window is dragged or resized.
fn mica_available() -> bool {
    #[cfg(windows)]
    {
        windows_version::OsVersion::current().build >= 22000
    }
    #[cfg(not(windows))]
    {
        false
    }
}

fn create_tray(app: &AppHandle) -> tauri::Result<()> {
    let open = MenuItem::with_id(app, "open", "Öffnen", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, "quit", "Beenden", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&open, &quit])?;

    let mut tray = TrayIconBuilder::with_id("main")
        .tooltip("DirkDraft")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id.as_ref() {
            "open" => show(app),
            "quit" => app.exit(0),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::DoubleClick { button: MouseButton::Left, .. } = event {
                show(tray.app_handle());
            }
        });

    if let Some(icon) = app.default_window_icon() {
        tray = tray.icon(icon.clone());
    }

    tray.build(app)?;
    Ok(())
}

/// Back from the tray, the taskbar or a second start: visible, restored and in front.
fn show(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(MAIN) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}
