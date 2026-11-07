// HTTP to the local client (port of LcuClient.cs). Reads keep apart the two things an empty answer
// can mean: the client said "there is none" (404), or nothing got through. Only the first may close
// anything — closing a running draft on the second is how a two-second hiccup used to wipe it.

use std::time::Duration;

use reqwest::{header, Method, StatusCode};
use serde::de::DeserializeOwned;

use super::credentials::Credentials;
use super::tls;

/// One answer from the client, or the absence of one.
#[derive(Debug, Clone, Default)]
pub struct Read {
    /// A body, or a 404 meaning "there is none". False: connection failed, stale password, client
    /// going down — not a statement about the game.
    pub answered: bool,
    pub body: Option<String>,
}

#[derive(Clone)]
pub struct Lcu {
    http: reqwest::Client,
    base: String,
    authorization: String,
}

impl Lcu {
    pub fn new(credentials: &Credentials) -> Self {
        Self {
            http: client(),
            base: credentials.http_base(),
            authorization: credentials.authorization(),
        }
    }

    pub async fn read(&self, path: &str) -> Read {
        let request = self
            .http
            .get(format!("{}{}", self.base, path))
            .header(header::AUTHORIZATION, &self.authorization)
            .header(header::ACCEPT, "application/json");
        match request.send().await {
            Ok(response) if response.status() == StatusCode::NOT_FOUND => Read { answered: true, body: None },
            Ok(response) if response.status().is_success() => match response.text().await {
                Ok(body) => Read { answered: true, body: Some(body) },
                Err(_) => Read::default(),
            },
            Ok(response) => {
                log::warn!("LCU {path} → {}", response.status().as_u16());
                Read::default()
            }
            Err(_) => Read::default(),
        }
    }

    pub async fn get_json<T: DeserializeOwned>(&self, path: &str) -> Option<T> {
        let body = self.read(path).await.body?;
        serde_json::from_str(&body).ok()
    }

    /// A write. Errors carry the client's own reason ("Max pages reached", …), cut to 200 characters.
    pub async fn send(&self, method: Method, path: &str, body: Option<&serde_json::Value>) -> Result<(), String> {
        let mut request = self
            .http
            .request(method, format!("{}{}", self.base, path))
            .header(header::AUTHORIZATION, &self.authorization)
            .header(header::ACCEPT, "application/json");
        if let Some(body) = body {
            request = request.json(body);
        }
        let response = request.send().await.map_err(|e| e.to_string())?;
        if response.status().is_success() {
            return Ok(());
        }
        let status = response.status().as_u16();
        let detail: String = response.text().await.unwrap_or_default().chars().take(200).collect();
        Err(format!("{status}: {detail}"))
    }
}

/// Loopback only: no proxy (it would break the connection and leak the request off the machine),
/// no redirects, five seconds.
pub fn client() -> reqwest::Client {
    reqwest::Client::builder()
        .use_preconfigured_tls((*tls::client_config()).clone())
        .no_proxy()
        .redirect(reqwest::redirect::Policy::none())
        .timeout(Duration::from_secs(5))
        .build()
        .expect("HTTP-Client für den League-Client")
}
