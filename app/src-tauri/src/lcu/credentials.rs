// Where the running client listens and with which password, out of its lockfile
// (port of DraftPilot.Core.Lcu.LcuCredentials).

use base64::Engine;

#[derive(Clone, PartialEq, Eq)]
pub struct Credentials {
    pub port: u16,
    pub password: String,
}

// Never print the password, not even into a log line by accident.
impl std::fmt::Debug for Credentials {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Credentials {{ port: {}, password: *** }}", self.port)
    }
}

impl Credentials {
    /// `LeagueClient:<pid>:<port>:<password>:https`. Anything else is None, so a half-written file
    /// (the client rewrites it while running) is simply read again.
    pub fn parse(content: &str) -> Option<Self> {
        let parts: Vec<&str> = content.trim().split(':').collect();
        if parts.len() < 4 {
            return None;
        }
        let port: u16 = parts[2].parse().ok()?;
        if port == 0 || parts[3].is_empty() {
            return None;
        }
        Some(Self { port, password: parts[3].to_string() })
    }

    pub fn http_base(&self) -> String {
        format!("https://127.0.0.1:{}", self.port)
    }

    pub fn socket_url(&self) -> String {
        format!("wss://127.0.0.1:{}/", self.port)
    }

    pub fn authorization(&self) -> String {
        let token = base64::engine::general_purpose::STANDARD.encode(format!("riot:{}", self.password));
        format!("Basic {token}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_a_lockfile() {
        let c = Credentials::parse("LeagueClient:1234:52437:s3cret:https\n").unwrap();
        assert_eq!(c.port, 52437);
        assert_eq!(c.password, "s3cret");
        assert_eq!(c.http_base(), "https://127.0.0.1:52437");
        assert_eq!(c.authorization(), "Basic cmlvdDpzM2NyZXQ=");
    }

    #[test]
    fn rejects_half_written_files() {
        assert!(Credentials::parse("").is_none());
        assert!(Credentials::parse("LeagueClient:1234").is_none());
        assert!(Credentials::parse("LeagueClient:1234:0:pw:https").is_none());
        assert!(Credentials::parse("LeagueClient:1234:70000:pw:https").is_none());
        assert!(Credentials::parse("LeagueClient:1234:5000::https").is_none());
    }

    #[test]
    fn hides_the_password_in_debug_output() {
        let c = Credentials::parse("LeagueClient:1:2:geheim:https").unwrap();
        assert!(!format!("{c:?}").contains("geheim"));
    }
}
