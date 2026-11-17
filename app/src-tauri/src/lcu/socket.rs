// The client's event socket (port of LcuEventSocket.cs): WAMP over a WebSocket, subscribed to the
// champion select session and the gameflow phase. This keeps DirkDraft at about zero CPU while
// idle — the client pushes changes instead of being asked for them.

use std::time::{Duration, Instant};

use futures_util::{SinkExt, StreamExt};
use tokio::sync::mpsc::UnboundedSender;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tokio_tungstenite::tungstenite::http::HeaderValue;
use tokio_tungstenite::tungstenite::Message;
use tokio_tungstenite::Connector;

use super::credentials::Credentials;
use super::tls;

const SUBSCRIBE: u8 = 5;
const EVENT: u64 = 8;
const SUBSCRIPTIONS: [&str; 2] = ["OnJsonApiEvent_lol-champ-select_v1_session", "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase"];

#[derive(Debug)]
pub enum SocketEvent {
    /// Subscribed; from now on every change arrives as an Event.
    Up,
    /// The link dropped (the loop reconnects by itself).
    Down,
    Event { uri: String, kind: String, data: Option<String> },
}

/// Which failures of the reconnect loop get a log line: the first of a series. A series ends with a
/// link that held (the test the backoff uses) — so a client that is not up yet, or one that accepts
/// and drops at once, costs one line and not one every few seconds. Until 17.11.2026 the error was
/// thrown away, and a socket that never came up left no trace of why.
#[derive(Default)]
struct FailureSeries {
    failures: u32,
}

impl FailureSeries {
    /// Counts a failed attempt; true for the one to log.
    fn failed(&mut self) -> bool {
        self.failures += 1;
        self.failures == 1
    }

    /// A link held: the next failure starts a new series.
    fn held(&mut self) {
        self.failures = 0;
    }
}

/// The error as one line for the log, never with the client's password in it.
fn describe(error: &(dyn std::error::Error + Send + Sync), credentials: &Credentials) -> String {
    let text = error.to_string().replace(['\r', '\n'], " ");
    if credentials.password.is_empty() {
        return text;
    }
    text.replace(&credentials.password, "***").replace(&credentials.authorization(), "***")
}

/// Connects, subscribes and pumps until the task is aborted, reconnecting with backoff: 500 ms,
/// doubling to 5 s. The backoff resets only after a link that lived five seconds and delivered a
/// frame — a client that accepts and drops at once must not be hammered twice a second.
pub async fn run(credentials: Credentials, out: UnboundedSender<SocketEvent>) {
    let mut backoff = Duration::from_millis(500);
    let mut series = FailureSeries::default();
    loop {
        let mut connected_at: Option<Instant> = None;
        let mut delivered = false;
        if let Err(error) = pump(&credentials, &out, &mut connected_at, &mut delivered).await {
            if series.failed() {
                let stage = if connected_at.is_some() { "Verbindung abgebrochen" } else { "keine Verbindung" };
                log::warn!("Ereignis-Socket: {stage} ({}) — neue Versuche folgen, weitere Fehler dieser Serie stehen nicht im Log", describe(error.as_ref(), &credentials));
            }
        }

        if let Some(at) = connected_at {
            if out.send(SocketEvent::Down).is_err() {
                return;
            }
            if delivered && at.elapsed() >= Duration::from_secs(5) {
                backoff = Duration::from_millis(500);
                series.held();
            }
        }
        if out.is_closed() {
            return;
        }
        tokio::time::sleep(backoff).await;
        backoff = (backoff * 2).min(Duration::from_secs(5));
    }
}

async fn pump(
    credentials: &Credentials,
    out: &UnboundedSender<SocketEvent>,
    connected_at: &mut Option<Instant>,
    delivered: &mut bool,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let mut request = credentials.socket_url().into_client_request()?;
    let headers = request.headers_mut();
    headers.insert("Authorization", HeaderValue::from_str(&credentials.authorization())?);
    headers.insert("Sec-WebSocket-Protocol", HeaderValue::from_static("wamp"));

    let connector = Connector::Rustls(tls::client_config());
    let (mut socket, _) = tokio_tungstenite::connect_async_tls_with_config(request, None, false, Some(connector)).await?;

    for name in SUBSCRIPTIONS {
        socket.send(Message::text(format!("[{SUBSCRIBE},\"{name}\"]"))).await?;
    }
    *connected_at = Some(Instant::now());
    if out.send(SocketEvent::Up).is_err() {
        return Ok(());
    }

    while let Some(message) = socket.next().await {
        let text = match message? {
            Message::Text(text) => text.to_string(),
            Message::Close(_) => return Ok(()),
            _ => continue,
        };
        // The client acks each subscription with an empty frame.
        if text.is_empty() {
            continue;
        }
        if let Some(event) = parse(&text) {
            if out.send(event).is_err() {
                return Ok(());
            }
            *delivered = true;
        }
    }
    Ok(())
}

/// `[8, "OnJsonApiEvent_…", { uri, eventType, data }]`. Anything else is dropped: the socket carries
/// frames we never asked for, and a malformed one must not end the loop mid-draft.
pub fn parse(text: &str) -> Option<SocketEvent> {
    let value: serde_json::Value = serde_json::from_str(text).ok()?;
    let frame = value.as_array()?;
    if frame.len() < 3 || frame[0].as_u64()? != EVENT {
        return None;
    }
    let body = frame[2].as_object()?;
    let uri = body.get("uri")?.as_str()?.to_string();
    if uri.is_empty() {
        return None;
    }
    let kind = body.get("eventType").and_then(|t| t.as_str()).unwrap_or("Update").to_string();
    let data = body.get("data").filter(|d| !d.is_null()).map(|d| d.to_string());
    Some(SocketEvent::Event { uri, kind, data })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_an_event_frame() {
        let frame = r#"[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":"ChampSelect","eventType":"Update","uri":"/lol-gameflow/v1/gameflow-phase"}]"#;
        match parse(frame) {
            Some(SocketEvent::Event { uri, kind, data }) => {
                assert_eq!(uri, "/lol-gameflow/v1/gameflow-phase");
                assert_eq!(kind, "Update");
                assert_eq!(data.as_deref(), Some("\"ChampSelect\""));
            }
            other => panic!("{other:?}"),
        }
    }

    #[test]
    fn logs_the_first_failure_of_a_series_only() {
        let mut series = FailureSeries::default();
        // The client is not up yet: one line, then silence however long it takes.
        assert!(series.failed());
        for _ in 0..50 {
            assert!(!series.failed());
        }
        // A link held, then the socket drops again: a new series, a new line.
        series.held();
        assert!(series.failed());
        assert!(!series.failed());
    }

    #[test]
    fn the_logged_error_never_carries_the_password() {
        let credentials = Credentials { port: 50123, password: "geheim-123".to_string() };
        let error: Box<dyn std::error::Error + Send + Sync> =
            format!("HTTP error: 403 for riot:geheim-123\r\nAuthorization: {}", credentials.authorization()).into();
        let line = describe(error.as_ref(), &credentials);
        assert!(!line.contains("geheim-123"));
        assert!(!line.contains(&credentials.authorization()));
        assert!(!line.contains('\n'));
        assert!(line.starts_with("HTTP error: 403"));
    }

    #[tokio::test]
    async fn a_port_nobody_listens_on_is_an_error_before_any_link() {
        let port = std::net::TcpListener::bind("127.0.0.1:0").unwrap().local_addr().unwrap().port();
        let credentials = Credentials { port, password: "x".to_string() };
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel();
        let mut connected_at = None;
        let mut delivered = false;
        let result = pump(&credentials, &tx, &mut connected_at, &mut delivered).await;
        assert!(result.is_err());
        assert!(connected_at.is_none());
        assert!(rx.try_recv().is_err());
        assert!(!describe(result.unwrap_err().as_ref(), &credentials).is_empty());
    }

    #[test]
    fn a_delete_has_no_data() {
        let frame = r#"[8,"x",{"data":null,"eventType":"Delete","uri":"/lol-champ-select/v1/session"}]"#;
        assert!(matches!(parse(frame), Some(SocketEvent::Event { data: None, .. })));
    }

    #[test]
    fn ignores_other_frames() {
        assert!(parse("[0,\"welcome\"]").is_none());
        assert!(parse("not json").is_none());
        assert!(parse(r#"[8,"x",{"uri":""}]"#).is_none());
    }
}
