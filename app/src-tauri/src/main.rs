// DirkDraft 2.0: the native shell around the Svelte app. The model, the views and the data live in
// TypeScript (app/src); this side owns the window, the tray and later the League client (M3).

// No console window next to the app in release builds.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod client;
mod data;
mod diagnostics;
mod lcu;
mod logfile;
mod settings;
mod shell;
mod updater;

fn main() {
    // First, before anything else: Velopack starts the exe with its own arguments when it installs,
    // updates or removes the app, and expects it to do that job and exit. Not installed (dev), it
    // returns at once.
    let mut velopack = velopack::VelopackApp::build();
    // After an update the exe sits at the same path with another icon, and Explorer kept showing the
    // old one in the taskbar (1.5 → 2.0, 08.11.2026) until told that icons changed.
    #[cfg(windows)]
    {
        velopack = velopack.on_after_update_fast_callback(|_| shell::icons_changed());
    }
    velopack.run();

    logfile::init();
    shell::run();
}
