// The window and the tray, with the rules 1.5 already had (docs/redesign/spec.md, section 1):
// one instance, a frameless window with our own title bar, at least 340 × 560, size and place
// remembered, X puts it in the tray, only the tray quits. No maximising: the board is laid out for
// widths, not for a full screen.

use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, TrayIconBuilder, TrayIconEvent},
    window::{Color, Effect, EffectsBuilder},
    AppHandle, Manager, WebviewUrl, WebviewWindow, WebviewWindowBuilder, WindowEvent,
};
use tauri_plugin_window_state::StateFlags;

const MAIN: &str = "main";

pub fn run() {
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
        .setup(|app| {
            crate::client::start(app.handle());
            let window = create_window(app.handle())?;
            create_tray(app.handle())?;
            window.show()?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            crate::client::client_snapshot,
            crate::client::client_pickable,
            crate::client::client_game_version,
            crate::client::client_end_of_game,
            crate::client::client_pool_input,
            crate::client::client_import,
        ])
        .on_window_event(|window, event| {
            // X, Alt+F4 and "close" in the taskbar all put the window in the tray.
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("DirkDraft konnte nicht starten");
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
