// Champion select straight from the running client (port of LiveSessionSource.cs + LockfileWatcher.cs):
// the lockfile says where it listens, the event socket pushes every change, and an HTTP read seeds
// the current state whenever the socket (re)connects.
//
// One task owns all state, so nothing races: lockfile ticks, socket events and seed answers arrive
// as messages and are handled in order. Rules carried over from 1.5, each paid for with a bug:
// - A lockfile that vanished only counts after 1.5 s; the client rewrites it while running.
// - Unchanged credentials change nothing (a reconnect would blink the board).
// - A dropped socket never ends the session; only the client positively answering "no champion
//   select" (404 on the reseed, a Delete event, a phase outside champion select) does.
// - Once the socket has delivered a session or phase, a slower seed answer for it is stale.
//
// The lockfile is polled twice a second instead of watched: a stat and a tiny read, nothing a
// person could measure, and none of a file watcher's missed-event corner cases.

use std::path::PathBuf;
use std::time::{Duration, Instant};

use serde::Serialize;
use tokio::sync::mpsc;
use tokio::task::JoinHandle;

use super::credentials::Credentials;
use super::http::{Lcu, Read};
use super::socket::{self, SocketEvent};
use super::{install, scrub};

const SESSION: &str = "/lol-champ-select/v1/session";
const GAMEFLOW: &str = "/lol-gameflow/v1/gameflow-phase";
const LOCKFILE_TICK: Duration = Duration::from_millis(500);
const LOCKFILE_GRACE: Duration = Duration::from_millis(1500);
/// How often a missing League installation is looked for again.
const LOCATE_RETRY: Duration = Duration::from_secs(10);

/// Phases that positively mean champion select is not running. A list of known values on purpose:
/// an unknown or empty phase leaves the board alone instead of throwing away a working draft.
const OUTSIDE_CHAMP_SELECT: [&str; 12] = [
    "None",
    "Lobby",
    "Matchmaking",
    "ReadyCheck",
    "GameStart",
    "InProgress",
    "Reconnect",
    "WaitingForStats",
    "PreEndOfGame",
    "EndOfGame",
    "TerminatedInError",
    "FailedToLaunch",
];

pub fn outside_champ_select(phase: &str) -> bool {
    OUTSIDE_CHAMP_SELECT.iter().any(|p| p.eq_ignore_ascii_case(phase))
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ClientStatus {
    pub league_found: bool,
    pub client_running: bool,
    pub socket_connected: bool,
}

const NOT_FOUND: ClientStatus = ClientStatus { league_found: false, client_running: false, socket_connected: false };
const OFFLINE: ClientStatus = ClientStatus { league_found: true, client_running: false, socket_connected: false };

/// What the source reports to the rest of the app.
pub enum Update {
    Status(ClientStatus),
    Phase(String),
    /// The session without names and tokens, or None when champion select ended.
    Session(Option<String>),
    /// The HTTP client of the running instance (for imports and reads), or None while it is down.
    Client(Option<Lcu>),
}

enum Inner {
    Socket(u64, SocketEvent),
    Seed(u64, Option<String>, Option<Read>),
}

struct Link {
    credentials: Credentials,
    lcu: Lcu,
    tasks: [JoinHandle<()>; 2],
}

struct Source<F: Fn(Update)> {
    out: F,
    tx: mpsc::UnboundedSender<Inner>,
    link: Option<Link>,
    generation: u64,
    session_open: bool,
    socket_delivered_session: bool,
    socket_delivered_phase: bool,
}

pub async fn run(explicit_lockfile: Option<PathBuf>, out: impl Fn(Update) + Send + 'static) {
    // League may be installed, or its drive attached, while DirkDraft runs: look again every few
    // seconds instead of giving up for the whole session. Said once, not on every look.
    let mut said = false;
    let lockfile = loop {
        let found = explicit_lockfile
            .clone()
            .or_else(|| install::locate().map(|dir| install::lockfile(&dir)))
            .filter(|path| path.parent().is_some_and(|dir| dir.is_dir()));
        if let Some(path) = found {
            break path;
        }
        if !said {
            out(Update::Status(NOT_FOUND));
            said = true;
        }
        tokio::time::sleep(LOCATE_RETRY).await;
    };
    out(Update::Status(OFFLINE));

    let (tx, mut rx) = mpsc::unbounded_channel();
    let mut source = Source {
        out,
        tx,
        link: None,
        generation: 0,
        session_open: false,
        socket_delivered_session: false,
        socket_delivered_phase: false,
    };
    let mut missing_since: Option<Instant> = None;
    let mut tick = tokio::time::interval(LOCKFILE_TICK);

    loop {
        tokio::select! {
            _ = tick.tick() => {
                let read = std::fs::read_to_string(&lockfile).ok().and_then(|text| Credentials::parse(&text));
                match read {
                    Some(credentials) => {
                        missing_since = None;
                        if source.link.as_ref().map(|l| &l.credentials) != Some(&credentials) {
                            source.connect(credentials);
                        }
                    }
                    None if source.link.is_some() => {
                        let since = *missing_since.get_or_insert_with(Instant::now);
                        if since.elapsed() >= LOCKFILE_GRACE {
                            missing_since = None;
                            source.disconnect();
                        }
                    }
                    None => {}
                }
            }
            Some(message) = rx.recv() => source.handle(message),
        }
    }
}

impl<F: Fn(Update)> Source<F> {
    fn connect(&mut self, credentials: Credentials) {
        self.tear_down();
        self.generation += 1;
        let generation = self.generation;

        let lcu = Lcu::new(&credentials);
        let (socket_tx, mut socket_rx) = mpsc::unbounded_channel();
        let socket_task = tokio::spawn(socket::run(credentials.clone(), socket_tx));
        let forward = self.tx.clone();
        let forward_task = tokio::spawn(async move {
            while let Some(event) = socket_rx.recv().await {
                if forward.send(Inner::Socket(generation, event)).is_err() {
                    break;
                }
            }
        });

        (self.out)(Update::Client(Some(lcu.clone())));
        (self.out)(Update::Status(ClientStatus { league_found: true, client_running: true, socket_connected: false }));
        // Safety net for a socket that never connects: without it there would be no state at all.
        self.seed(&lcu);
        self.link = Some(Link { credentials, lcu, tasks: [socket_task, forward_task] });
    }

    fn disconnect(&mut self) {
        self.tear_down();
        self.end_session();
        (self.out)(Update::Status(OFFLINE));
    }

    fn tear_down(&mut self) {
        self.socket_delivered_session = false;
        self.socket_delivered_phase = false;
        if let Some(link) = self.link.take() {
            for task in link.tasks {
                task.abort();
            }
            (self.out)(Update::Client(None));
        }
    }

    fn seed(&self, lcu: &Lcu) {
        let lcu = lcu.clone();
        let tx = self.tx.clone();
        let generation = self.generation;
        tokio::spawn(async move {
            // Phase first: started mid-game, the game view should come up at once — and outside
            // champion select, reading the session is pointless.
            let phase = lcu.read(GAMEFLOW).await.body.map(|b| b.trim().trim_matches('"').to_string()).filter(|p| !p.is_empty());
            let session = match &phase {
                Some(p) if outside_champ_select(p) => None,
                _ => Some(lcu.read(SESSION).await),
            };
            let _ = tx.send(Inner::Seed(generation, phase, session));
        });
    }

    fn handle(&mut self, message: Inner) {
        match message {
            Inner::Seed(generation, phase, session) if generation == self.generation => {
                if let Some(phase) = phase {
                    if !self.socket_delivered_phase {
                        self.phase(phase);
                    }
                }
                let Some(read) = session else { return };
                // The socket has spoken in the meantime: its event is newer.
                if self.socket_delivered_session {
                    return;
                }
                match read.body {
                    Some(json) => self.session(&json),
                    None if read.answered => self.end_session(),
                    None => {}
                }
            }
            Inner::Socket(generation, event) if generation == self.generation => self.socket(event),
            _ => {}
        }
    }

    fn socket(&mut self, event: SocketEvent) {
        match event {
            SocketEvent::Up => {
                (self.out)(Update::Status(ClientStatus { league_found: true, client_running: true, socket_connected: true }));
                // Every (re)connect reseeds: it repaints after a drop during finalization, where no
                // further session events would come.
                if let Some(link) = &self.link {
                    let lcu = link.lcu.clone();
                    self.seed(&lcu);
                }
            }
            SocketEvent::Down => {
                // Deliberately NOT ending the session (see the head of this file).
                self.socket_delivered_session = false;
                self.socket_delivered_phase = false;
                (self.out)(Update::Status(ClientStatus { league_found: true, client_running: true, socket_connected: false }));
            }
            SocketEvent::Event { uri, kind, data } => {
                if uri.eq_ignore_ascii_case(SESSION) {
                    self.socket_delivered_session = true;
                    match data {
                        Some(json) if !kind.eq_ignore_ascii_case("Delete") => self.session(&json),
                        _ => self.end_session(),
                    }
                } else if uri.eq_ignore_ascii_case(GAMEFLOW) {
                    let phase = data.unwrap_or_default().trim().trim_matches('"').to_string();
                    if !phase.is_empty() {
                        self.socket_delivered_phase = true;
                        self.phase(phase);
                    }
                }
            }
        }
    }

    fn phase(&mut self, phase: String) {
        let outside = outside_champ_select(&phase);
        (self.out)(Update::Phase(phase));
        if outside {
            self.end_session();
        }
    }

    fn session(&mut self, json: &str) {
        if let Some(clean) = scrub::session(json) {
            self.session_open = true;
            (self.out)(Update::Session(Some(clean)));
        }
    }

    fn end_session(&mut self) {
        if self.session_open {
            self.session_open = false;
            (self.out)(Update::Session(None));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn knows_the_phases_outside_champion_select() {
        assert!(outside_champ_select("InProgress"));
        assert!(outside_champ_select("lobby"));
        assert!(!outside_champ_select("ChampSelect"));
        assert!(!outside_champ_select(""));
        assert!(!outside_champ_select("SomethingNew"));
    }
}
