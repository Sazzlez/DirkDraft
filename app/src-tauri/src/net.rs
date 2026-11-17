// HTTP to the public internet: GitHub (the data files) and Data Dragon (names and item tables). A
// client of its own — the one in lcu/http.rs trusts nothing but Riot's authority for the local
// client and could reach neither. This one checks against the public roots (webpki-roots, compiled
// in), follows the system's proxy and speaks https only.
//
// The page does not fetch these itself any more (17.11.2026): a conditional request and a file on
// disk are the shell's business, and what crosses to the page is only what is new.

use std::sync::OnceLock;
use std::time::Duration;

use reqwest::{header, StatusCode};

pub fn client() -> &'static reqwest::Client {
    static CLIENT: OnceLock<reqwest::Client> = OnceLock::new();
    CLIENT.get_or_init(|| build(true))
}

/// `https_only`: false only in tests, which answer from a local port.
pub fn build(https_only: bool) -> reqwest::Client {
    reqwest::Client::builder()
        .https_only(https_only)
        .user_agent(concat!("DirkDraft/", env!("CARGO_PKG_VERSION")))
        .connect_timeout(Duration::from_secs(10))
        .timeout(Duration::from_secs(60))
        .redirect(reqwest::redirect::Policy::limited(3))
        .build()
        .expect("HTTP-Client für GitHub und Data Dragon")
}

/// What a conditional GET brought.
#[derive(Debug)]
pub enum Fetched {
    /// The server has what one of the validators names: nothing was sent.
    NotModified,
    Body { bytes: Vec<u8>, etag: Option<String> },
}

/// One GET; `validators` are ETags sent as If-None-Match, so an unchanged file costs a few hundred
/// bytes. Err: no answer, or a status that is neither 200 nor 304.
pub async fn get(client: &reqwest::Client, url: &str, validators: &[String]) -> Result<Fetched, String> {
    let mut request = client.get(url);
    if !validators.is_empty() {
        request = request.header(header::IF_NONE_MATCH, validators.join(", "));
    }
    let response = request.send().await.map_err(short)?;
    match response.status() {
        StatusCode::NOT_MODIFIED => Ok(Fetched::NotModified),
        StatusCode::OK => {
            let etag = response.headers().get(header::ETAG).and_then(|v| v.to_str().ok()).map(str::to_string);
            let bytes = response.bytes().await.map_err(short)?.to_vec();
            Ok(Fetched::Body { bytes, etag })
        }
        status => Err(format!("HTTP {}", status.as_u16())),
    }
}

/// The failure without the URL: what went wrong is enough for the log.
fn short(error: reqwest::Error) -> String {
    if error.is_timeout() {
        "Zeitüberschreitung".to_string()
    } else if error.is_connect() {
        "keine Verbindung".to_string()
    } else {
        error.without_url().to_string()
    }
}
