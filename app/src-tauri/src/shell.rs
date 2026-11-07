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

use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, TrayIconBuilder, TrayIconEvent},
    window::{Color, Effect, EffectsBuilder},
    AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindow, WebviewWindowBuilder, WindowEvent,
};
use tauri_plugin_window_state::{AppHandleExt, StateFlags};

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
    log::info!(
        "DirkDraft {} startet · Windows-Build {} · Glas {} · Diagnose {}",
        env!("CARGO_PKG_VERSION"),
        windows_build(),
        if mica_available() && settings.flag(settings::GLASS_EFFECT.0, settings::GLASS_EFFECT.1) { "an" } else { "aus" },
        if settings.diagnostics() { "an" } else { "aus" }
    );
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
        .manage(crate::updater::UpdaterState::default())
        .manage(crate::diagnostics::Diagnostics::new(settings.diagnostics()))
        .manage(ShellState {
            settings: Mutex::new(settings),
            minimised_by_hand: AtomicBool::new(false),
            game_started: Mutex::new(None),
            phase: Mutex::new(String::new()),
        })
        .setup(move |app| {
            crate::client::start(app.handle(), lockfile.clone());
            let glass = app.state::<ShellState>().flag(settings::GLASS_EFFECT);
            let window = create_window(app.handle(), glass)?;
            create_tray(app.handle())?;
            let state = app.state::<ShellState>();
            if let Ok(settings) = state.settings.lock() {
                adopt_legacy_place(app.handle(), &window, &settings);
            }
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
                    // The plugin writes the place only when the app exits; whoever puts the window
                    // in the tray and shuts the PC down would lose it. Kept here as 1.5 did.
                    let _ = app.save_window_state(StateFlags::SIZE | StateFlags::POSITION);
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
            crate::client::client_import,
            crate::data::data_read,
            crate::data::data_write,
            crate::updater::update_check,
            crate::updater::update_apply,
            settings_get,
            settings_set,
            crate::logfile::shell_log,
            crate::diagnostics::diagnose_export,
            shell_set_pinned,
            shell_minimize,
        ])
        .run(tauri::generate_context!())
        .unwrap_or_else(|error| {
            // Most often: no WebView2 runtime. Said in the log before the process goes.
            log::error!("Start fehlgeschlagen: {error}");
            log::logger().flush();
            panic!("DirkDraft konnte nicht starten: {error}");
        });
}

pub fn windows_build() -> u32 {
    #[cfg(windows)]
    {
        windows_version::OsVersion::current().build
    }
    #[cfg(not(windows))]
    {
        0
    }
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

/// The settings page's values, 1.5's defaults filled in.
#[tauri::command]
fn settings_get(state: State<'_, ShellState>) -> serde_json::Value {
    state.settings.lock().map(|s| s.page()).unwrap_or_default()
}

/// One change from the settings page; only the page's own keys with valid values are written.
#[tauri::command]
fn settings_set(
    state: State<'_, ShellState>,
    diagnostics: State<'_, crate::diagnostics::Diagnostics>,
    key: String,
    value: serde_json::Value,
) -> Result<(), String> {
    if key == settings::DIAGNOSTICS.0 {
        diagnostics.set_enabled(value.as_bool().unwrap_or(true));
    }
    state.settings.lock().map_err(|_| "Einstellungen gesperrt".to_string())?.set_from_page(&key, value)
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

fn create_window(app: &AppHandle, wanted: bool) -> tauri::Result<WebviewWindow> {
    let available = mica_available();
    let glass = available && wanted;

    // The page reads this before its first paint and draws its background to match; canGlass
    // tells the settings sheet whether the glass switch means anything on this Windows.
    let marker = format!(
        "window.__DIRKDRAFT_SHELL__ = {{ glass: '{}', canGlass: {} }};",
        if glass { "mica" } else { "none" },
        available
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

/// 1.5 kept the window's place in settings.json (windowLeft/Top/Width/Height, in WPF's device
/// independent pixels at the primary monitor's scale; NaN = never placed). The first 2.0 start takes
/// it over; from then on the window-state plugin keeps it in its own file. A place on a monitor that
/// is gone is dropped (the window stays centred), a size larger than the monitor is cut to it.
fn adopt_legacy_place(app: &AppHandle, window: &WebviewWindow, settings: &Settings) {
    let Ok(dir) = app.path().app_config_dir() else { return };
    if dir.join(tauri_plugin_window_state::DEFAULT_FILENAME).exists() {
        return;
    }
    let Some((left, top, size)) = legacy_place(settings) else { return };
    let Ok(Some(primary)) = window.primary_monitor() else { return };
    let monitors = window.available_monitors().unwrap_or_default();
    let scale = primary.scale_factor();
    // A 1.5 window that was moved but never resized stored only its place: 2.0's own size goes there.
    let (w, h) = match size {
        Some((w, h)) => (w * scale, h * scale),
        // set_size below sets the inner size: start from the inner size, or the frame adds up.
        None => match window.inner_size() {
            Ok(s) => (s.width as f64, s.height as f64),
            Err(_) => return,
        },
    };
    let (x, y) = (left * scale, top * scale);

    // The monitor the stored place mostly lies on (its centre), if it is still attached.
    let centre = (x + w / 2.0, y + h / 2.0);
    let host = monitors.iter().find(|m| {
        let (p, s) = (m.position(), m.size());
        centre.0 >= p.x as f64 && centre.0 < p.x as f64 + s.width as f64 && centre.1 >= p.y as f64 && centre.1 < p.y as f64 + s.height as f64
    });
    let area = host.unwrap_or(&primary);
    let (max_w, max_h) = (area.size().width as f64, area.size().height as f64);
    let size = tauri::PhysicalSize::new(w.min(max_w).max(340.0 * scale) as u32, h.min(max_h).max(560.0 * scale) as u32);
    let _ = window.set_size(size);
    if host.is_some() {
        let p = area.position();
        let left = x.clamp(p.x as f64, p.x as f64 + max_w - size.width as f64);
        let top = y.clamp(p.y as f64, p.y as f64 + max_h - size.height as f64);
        let _ = window.set_position(tauri::PhysicalPosition::new(left as i32, top as i32));
    }
}

/// The 1.5 place in device-independent pixels: left and top if the window was ever moved, and its
/// size if it was ever resized too (1.5 keeps "NaN" until then).
fn legacy_place(settings: &Settings) -> Option<(f64, f64, Option<(f64, f64)>)> {
    let width = settings.number("windowWidth").filter(|w| *w > 0.0);
    let height = settings.number("windowHeight").filter(|h| *h > 0.0);
    Some((settings.number("windowLeft")?, settings.number("windowTop")?, width.zip(height)))
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

#[cfg(test)]
mod tests {
    use super::*;

    fn settings(json: &str) -> Settings {
        Settings::from_values(serde_json::from_str::<serde_json::Value>(json).unwrap().as_object().cloned().unwrap())
    }

    #[test]
    fn takes_1_5s_place_with_or_without_its_size() {
        let moved = settings(r#"{"windowLeft": 120, "windowTop": 60, "windowWidth": "NaN", "windowHeight": "NaN"}"#);
        assert_eq!(legacy_place(&moved), Some((120.0, 60.0, None)));
        let resized = settings(r#"{"WindowLeft": 120, "windowTop": 60, "windowWidth": 480, "windowHeight": 900}"#);
        assert_eq!(legacy_place(&resized), Some((120.0, 60.0, Some((480.0, 900.0)))));
        let never_moved = settings(r#"{"windowLeft": "NaN", "windowTop": "NaN", "windowWidth": 480, "windowHeight": 900}"#);
        assert_eq!(legacy_place(&never_moved), None);
    }
}
