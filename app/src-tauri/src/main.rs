// DirkDraft 2.0: the native shell around the Svelte app. The model, the views and the data live in
// TypeScript (app/src); this side owns the window, the tray and later the League client (M3).

// No console window next to the app in release builds.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod client;
mod data;
mod lcu;
mod logfile;
mod settings;
mod shell;
mod updater;

fn main() {
    // First, before anything else: Velopack starts the exe with its own arguments when it installs,
    // updates or removes the app, and expects it to do that job and exit. Not installed (dev), it
    // returns at once.
    velopack::VelopackApp::build().run();

    logfile::init();
    shell::run();
}
