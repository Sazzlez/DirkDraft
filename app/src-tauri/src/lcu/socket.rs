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

/// Connects, subscribes and pumps until the task is aborted, reconnecting with backoff: 500 ms,
/// doubling to 5 s. The backoff resets only after a link that lived five seconds and delivered a
/// frame — a client that accepts and drops at once must not be hammered twice a second.
pub async fn run(credentials: Credentials, out: UnboundedSender<SocketEvent>) {
    let mut backoff = Duration::from_millis(500);
    loop {
        let mut connected_at: Option<Instant> = None;
        let mut delivered = false;
        let _ = pump(&credentials, &out, &mut connected_at, &mut delivered).await;

        if let Some(at) = connected_at {
            if out.send(SocketEvent::Down).is_err() {
                return;
            }
            if delivered && at.elapsed() >= Duration::from_secs(5) {
                backoff = Duration::from_millis(500);
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
