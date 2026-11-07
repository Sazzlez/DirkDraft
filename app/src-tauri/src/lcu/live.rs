// The running game's own API (Live Client Data, https://127.0.0.1:2999), asked once a second while
// the client says a game is on. Read-only, local, the API Riot provides for exactly this. Names are
// replaced before anything leaves this file; the pseudonyms start over with every game.

use std::time::Duration;

use tokio::sync::watch;

use super::http;
use super::scrub::LiveScrubber;

const ALL_GAME_DATA: &str = "https://127.0.0.1:2999/liveclientdata/allgamedata";
const INTERVAL: Duration = Duration::from_secs(1);

pub fn in_game(phase: &str) -> bool {
    matches!(phase, "GameStart" | "InProgress" | "Reconnect")
}

/// Calls `out` with each scrubbed snapshot, and once with None when the game's API goes quiet.
pub async fn run(mut phase: watch::Receiver<String>, out: impl Fn(Option<String>) + Send + 'static) {
    let client = http::client();
    let mut scrubber = LiveScrubber::default();
    let mut answering = false;

    loop {
        if !in_game(&phase.borrow_and_update()) {
            if answering {
                answering = false;
                scrubber = LiveScrubber::default();
                out(None);
            }
            if phase.changed().await.is_err() {
                return;
            }
            continue;
        }

        let answer = match client.get(ALL_GAME_DATA).send().await {
            Ok(response) if response.status().is_success() => response.text().await.ok(),
            _ => None,
        };
        match answer.and_then(|body| scrubber.scrub(&body)) {
            Some(clean) => {
                answering = true;
                out(Some(clean));
            }
            // Loading screen or the game closing: say so once, keep asking while the phase says game.
            None if answering => {
                answering = false;
                out(None);
            }
            None => {}
        }

        tokio::select! {
            _ = tokio::time::sleep(INTERVAL) => {}
            changed = phase.changed() => if changed.is_err() { return },
        }
    }
}
