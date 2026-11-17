// The downloaded data: fetched here and kept as files in %LOCALAPPDATA%\DraftPilot\data\2.0\. The
// page names a file and gets its text — exactly these names and nothing else; no path from the page
// ever reaches the file system, and the page never fetches or writes a file itself.
//
// One check (data_check) is one conditional GET per file, with the ETag stored beside the kept copy:
// an unchanged file costs a 304 and touches nothing. A new file is staged beside the kept one
// ("<name>.new"); the page reads and checks it and then says commit — it becomes the kept copy — or
// discard, and that version is not fetched again. So a broken download never replaces a good copy.
//
// Until 17.11.2026 the page did all of this through data_read and data_write: on every check without
// new data it read 40,6 MB and wrote 20,3 MB through the IPC, every half hour, and both commands were
// synchronous — on the main thread.

use std::io;
use std::path::PathBuf;

use futures_util::future::join_all;
use serde::Serialize;

use crate::net::{self, Fetched};

/// The files of the branch "data" (app/src/data/remote.ts FILES): the two lists must agree.
pub const NAMES: [&str; 6] = ["meta.json", "builds.json", "aram.json", "situational.json", "traits.json", "combos.json"];

const BASE: &str = "https://raw.githubusercontent.com/Sazzlez/DirkDraft/data";

/// The folder the files are kept in.
pub struct Store {
    dir: PathBuf,
}

impl Store {
    pub fn user() -> Option<Self> {
        let dir = std::env::var_os("LOCALAPPDATA").map(PathBuf::from)?.join("DraftPilot").join("data").join("2.0");
        Some(Self { dir })
    }

    #[cfg(test)]
    pub fn at(dir: PathBuf) -> Self {
        Self { dir }
    }

    /// The file for a name the page may use — one of NAMES — with one of this module's suffixes.
    fn path(&self, name: &str, suffix: &str) -> Result<PathBuf, String> {
        if !NAMES.contains(&name) {
            return Err("Name nicht erlaubt".to_string());
        }
        Ok(self.dir.join(format!("{name}{suffix}")))
    }

    /// The kept copy, or None.
    pub fn read(&self, name: &str) -> Result<Option<String>, String> {
        read_text(self.path(name, "")?)
    }

    /// The download waiting for the page's verdict, or None.
    pub fn read_staged(&self, name: &str) -> Result<Option<String>, String> {
        read_text(self.path(name, ".new")?)
    }

    /// The ETags the server may answer "not modified" for: the kept copy's — worthless without the
    /// copy itself — and the one of a version the page threw away.
    fn validators(&self, name: &str) -> Vec<String> {
        let mut etags = Vec::new();
        let kept = self.path(name, "").map(|p| p.exists()).unwrap_or(false);
        for (suffix, usable) in [(".etag", kept), (".rejected", true)] {
            if !usable {
                continue;
            }
            if let Ok(Some(etag)) = self.path(name, suffix).and_then(read_text) {
                let etag = etag.trim();
                if !etag.is_empty() {
                    etags.push(etag.to_string());
                }
            }
        }
        etags
    }

    /// A new version from the server, beside the kept copy. Written to a temporary file and moved:
    /// a crash mid-write leaves nothing half.
    fn stage(&self, name: &str, bytes: &[u8], etag: Option<&str>) -> Result<(), String> {
        std::fs::create_dir_all(&self.dir).map_err(|e| e.to_string())?;
        let staged = self.path(name, ".new")?;
        let temp = self.path(name, ".new.tmp")?;
        std::fs::write(&temp, bytes).map_err(|e| e.to_string())?;
        std::fs::rename(&temp, &staged).map_err(|e| e.to_string())?;
        let tag = self.path(name, ".new.etag")?;
        match etag {
            Some(etag) => std::fs::write(tag, etag).map_err(|e| e.to_string()),
            None => remove(tag),
        }
    }

    /// The staged version is good: it becomes the kept copy, with its ETag.
    pub fn commit(&self, name: &str) -> Result<(), String> {
        let staged = self.path(name, ".new")?;
        if !staged.exists() {
            return Err("Nichts bereitgestellt".to_string());
        }
        std::fs::rename(staged, self.path(name, "")?).map_err(|e| e.to_string())?;
        let tag = self.path(name, ".new.etag")?;
        if tag.exists() {
            std::fs::rename(tag, self.path(name, ".etag")?).map_err(|e| e.to_string())?;
        } else {
            remove(self.path(name, ".etag")?)?;
        }
        remove(self.path(name, ".rejected")?)
    }

    /// The staged version is unusable: gone, and its ETag remembered so it is not fetched again.
    pub fn discard(&self, name: &str) -> Result<(), String> {
        let tag = self.path(name, ".new.etag")?;
        if tag.exists() {
            std::fs::rename(tag, self.path(name, ".rejected")?).map_err(|e| e.to_string())?;
        }
        remove(self.path(name, ".new")?)
    }

    /// What an earlier check staged and nobody decided on (the page went away in between): fetched
    /// again rather than trusted.
    fn clear_staged(&self) {
        for name in NAMES {
            for suffix in [".new", ".new.etag", ".new.tmp"] {
                if let Ok(path) = self.path(name, suffix) {
                    let _ = std::fs::remove_file(path);
                }
            }
        }
    }
}

fn read_text(path: PathBuf) -> Result<Option<String>, String> {
    match std::fs::read_to_string(path) {
        Ok(text) => Ok(Some(text)),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

fn remove(path: PathBuf) -> Result<(), String> {
    match std::fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e.to_string()),
    }
}

/// What one check found (app/src/data/remote.ts CheckOutcome).
#[derive(Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CheckOutcome {
    /// Files with a new version staged.
    pub staged: Vec<String>,
    pub problems: Vec<String>,
    /// The server answered for at least one file.
    pub reached: bool,
}

/// One conditional GET per file, all at once. Only a 200 writes anything.
pub async fn check(store: &Store, client: &reqwest::Client, base: &str) -> CheckOutcome {
    store.clear_staged();
    let answers = join_all(NAMES.iter().map(|name| async move {
        let fetched = net::get(client, &format!("{base}/{name}"), &store.validators(name)).await;
        (*name, fetched)
    }))
    .await;

    let mut outcome = CheckOutcome::default();
    for (name, fetched) in answers {
        match fetched {
            Ok(Fetched::NotModified) => outcome.reached = true,
            Ok(Fetched::Body { bytes, etag }) => {
                outcome.reached = true;
                match store.stage(name, &bytes, etag.as_deref()) {
                    Ok(()) => outcome.staged.push(name.to_string()),
                    Err(error) => outcome.problems.push(format!("{name}: {error}")),
                }
            }
            Err(error) => outcome.problems.push(format!("{name}: {error}")),
        }
    }
    outcome
}

fn store() -> Result<Store, String> {
    Store::user().ok_or_else(|| "Kein Datenordner".to_string())
}

// Every command is async: Tauri runs those off the main thread, where a read of six megabytes or
// a round of requests has no business.

#[tauri::command]
pub async fn data_read(name: String) -> Result<Option<String>, String> {
    store()?.read(&name)
}

#[tauri::command]
pub async fn data_read_staged(name: String) -> Result<Option<String>, String> {
    store()?.read_staged(&name)
}

#[tauri::command]
pub async fn data_check() -> Result<CheckOutcome, String> {
    let store = store()?;
    let outcome = check(&store, net::client(), BASE).await;
    log::info!(
        "Daten-Prüfung: {} neu, {} Probleme, Server {}",
        outcome.staged.len(),
        outcome.problems.len(),
        if outcome.reached { "erreicht" } else { "nicht erreicht" }
    );
    Ok(outcome)
}

#[tauri::command]
pub async fn data_commit(name: String) -> Result<(), String> {
    store()?.commit(&name)
}

#[tauri::command]
pub async fn data_discard(name: String) -> Result<(), String> {
    store()?.discard(&name)
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;
    use std::io::{BufRead, BufReader, Write};
    use std::net::TcpListener;
    use std::sync::{Arc, Mutex};

    use super::*;

    fn temp_store(tag: &str) -> Store {
        let dir = std::env::temp_dir().join(format!("dirkdraft-data-test-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        Store::at(dir)
    }

    /// What the test server has: file name → (ETag, body).
    type Files = Arc<Mutex<HashMap<String, (String, String)>>>;

    /// A local server speaking just enough HTTP: 304 when If-None-Match names the file's ETag, 200
    /// with body and ETag otherwise, 404 for a file it does not have. Records every If-None-Match.
    fn serve(files: Files) -> (String, Arc<Mutex<Vec<(String, String)>>>) {
        let listener = TcpListener::bind("127.0.0.1:0").expect("lokaler Port");
        let base = format!("http://{}", listener.local_addr().expect("Adresse"));
        let seen = Arc::new(Mutex::new(Vec::new()));
        let record = seen.clone();
        std::thread::spawn(move || {
            for stream in listener.incoming() {
                let Ok(mut stream) = stream else { continue };
                let mut reader = BufReader::new(stream.try_clone().expect("Stream"));
                let mut request = String::new();
                let _ = reader.read_line(&mut request);
                let name = request.split_whitespace().nth(1).unwrap_or("/").trim_start_matches('/').to_string();
                let mut validators = String::new();
                loop {
                    let mut line = String::new();
                    if reader.read_line(&mut line).unwrap_or(0) == 0 || line.trim().is_empty() {
                        break;
                    }
                    if let Some(value) = line.to_ascii_lowercase().strip_prefix("if-none-match:") {
                        validators = value.trim().to_string();
                    }
                }
                record.lock().expect("Protokoll").push((name.clone(), validators.clone()));
                let answer = match files.lock().expect("Dateien").get(&name) {
                    None => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".to_string(),
                    Some((etag, _)) if validators.split(',').any(|v| v.trim() == etag.to_ascii_lowercase()) => {
                        format!("HTTP/1.1 304 Not Modified\r\nETag: {etag}\r\nConnection: close\r\n\r\n")
                    }
                    Some((etag, body)) => {
                        format!("HTTP/1.1 200 OK\r\nETag: {etag}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}", body.len())
                    }
                };
                let _ = stream.write_all(answer.as_bytes());
            }
        });
        (base, seen)
    }

    fn files(entries: &[(&str, &str, &str)]) -> Files {
        Arc::new(Mutex::new(entries.iter().map(|(name, etag, body)| (name.to_string(), (etag.to_string(), body.to_string()))).collect()))
    }

    #[test]
    fn allows_only_the_known_files() {
        let store = temp_store("names");
        assert!(store.path("meta.json", "").is_ok());
        assert!(store.path("builds.json", ".etag").is_ok());
        // The curated files published with the numbers (remote.ts FILES): the two lists must agree.
        assert!(store.path("traits.json", "").is_ok());
        assert!(store.path("combos.json", ".new").is_ok());
        for name in ["history.json", "../settings.json", "..\\meta.json", "snapshot.json", "meta.json.etag", "", "meta.json/../x"] {
            assert!(store.path(name, "").is_err(), "{name}");
            assert!(store.read(name).is_err(), "{name}");
            assert!(store.commit(name).is_err(), "{name}");
        }
    }

    #[tokio::test]
    async fn a_new_file_is_staged_and_kept_only_on_commit() {
        let store = temp_store("commit");
        let server = files(&[("meta.json", "\"v1\"", "{\"games\":1}")]);
        let (base, _) = serve(server);
        let client = net::build(false);

        let outcome = check(&store, &client, &base).await;
        assert_eq!(outcome.staged, vec!["meta.json"]);
        assert!(outcome.reached);
        // The other five are not published yet: said, and no reason to fail.
        assert_eq!(outcome.problems.len(), 5);
        assert!(outcome.problems.iter().all(|p| p.ends_with("HTTP 404")), "{:?}", outcome.problems);

        // Staged is not kept: the page has not said it is good yet.
        assert_eq!(store.read("meta.json").unwrap(), None);
        assert_eq!(store.read_staged("meta.json").unwrap().as_deref(), Some("{\"games\":1}"));
        store.commit("meta.json").unwrap();
        assert_eq!(store.read("meta.json").unwrap().as_deref(), Some("{\"games\":1}"));
        assert_eq!(store.read_staged("meta.json").unwrap(), None);
        assert!(store.commit("meta.json").is_err());
    }

    #[tokio::test]
    async fn a_check_without_new_data_writes_nothing() {
        let store = temp_store("unchanged");
        let server = files(&[("meta.json", "\"v1\"", "{\"games\":1}")]);
        let (base, seen) = serve(server);
        let client = net::build(false);
        check(&store, &client, &base).await;
        store.commit("meta.json").unwrap();
        let kept = store.path("meta.json", "").unwrap();
        let written = std::fs::metadata(&kept).unwrap().modified().unwrap();
        let before: Vec<_> = std::fs::read_dir(&store.dir).unwrap().map(|e| e.unwrap().file_name()).collect();

        for _ in 0..3 {
            let outcome = check(&store, &client, &base).await;
            assert!(outcome.staged.is_empty());
            assert!(outcome.reached);
        }

        // Asked with the stored ETag, answered 304: the same files, the kept one untouched.
        assert!(seen.lock().unwrap().iter().any(|(name, validators)| name == "meta.json" && validators == "\"v1\""));
        let after: Vec<_> = std::fs::read_dir(&store.dir).unwrap().map(|e| e.unwrap().file_name()).collect();
        assert_eq!(before, after);
        assert_eq!(std::fs::metadata(&kept).unwrap().modified().unwrap(), written);
    }

    #[tokio::test]
    async fn a_discarded_version_is_not_fetched_again_and_the_copy_stays() {
        let store = temp_store("discard");
        let server = files(&[("meta.json", "\"v1\"", "{\"games\":1}")]);
        let (base, seen) = serve(server.clone());
        let client = net::build(false);
        check(&store, &client, &base).await;
        store.commit("meta.json").unwrap();

        // A broken version is published; the page reads it and says no.
        server.lock().unwrap().insert("meta.json".to_string(), ("\"v2\"".to_string(), "{ kaputt".to_string()));
        assert_eq!(check(&store, &client, &base).await.staged, vec!["meta.json"]);
        store.discard("meta.json").unwrap();
        assert_eq!(store.read("meta.json").unwrap().as_deref(), Some("{\"games\":1}"));
        assert_eq!(store.read_staged("meta.json").unwrap(), None);

        // Next time both ETags are named, and the broken version costs a 304.
        assert!(check(&store, &client, &base).await.staged.is_empty());
        assert!(seen.lock().unwrap().iter().any(|(name, validators)| name == "meta.json" && validators == "\"v1\", \"v2\""));

        // The version after it is taken again.
        server.lock().unwrap().insert("meta.json".to_string(), ("\"v3\"".to_string(), "{\"games\":3}".to_string()));
        assert_eq!(check(&store, &client, &base).await.staged, vec!["meta.json"]);
        store.commit("meta.json").unwrap();
        assert_eq!(store.read("meta.json").unwrap().as_deref(), Some("{\"games\":3}"));
        assert!(!store.path("meta.json", ".rejected").unwrap().exists());
    }

    /// The real thing, by hand (`cargo test -- --ignored`): GitHub over the public roots, the ETag
    /// it hands out, and "not modified" on the second round.
    #[tokio::test]
    #[ignore = "braucht das Netz"]
    async fn the_real_branch_answers_and_the_second_check_costs_nothing() {
        let store = temp_store("live");
        let first = check(&store, net::client(), BASE).await;
        assert!(first.reached, "{:?}", first.problems);
        assert!(first.staged.contains(&"meta.json".to_string()), "{:?}", first.problems);
        for name in &first.staged {
            assert!(store.read_staged(name).unwrap().unwrap().starts_with('{'), "{name}");
            store.commit(name).unwrap();
        }
        let second = check(&store, net::client(), BASE).await;
        assert!(second.reached);
        assert!(second.staged.is_empty(), "{:?}", second.staged);
    }

    #[tokio::test]
    async fn an_etag_without_its_copy_is_not_sent_and_no_answer_is_a_problem() {
        let store = temp_store("lost");
        let server = files(&[("meta.json", "\"v1\"", "{\"games\":1}")]);
        let (base, _) = serve(server);
        let client = net::build(false);
        check(&store, &client, &base).await;
        store.commit("meta.json").unwrap();

        // The copy is gone, its ETag is still there: "not modified" would leave nothing to read.
        std::fs::remove_file(store.path("meta.json", "").unwrap()).unwrap();
        assert!(store.validators("meta.json").is_empty());
        assert_eq!(check(&store, &client, &base).await.staged, vec!["meta.json"]);

        // Nobody listening: every file is a problem, nothing was reached, nothing is staged.
        let outcome = check(&store, &client, "http://127.0.0.1:9").await;
        assert!(!outcome.reached);
        assert!(outcome.staged.is_empty());
        assert_eq!(outcome.problems.len(), NAMES.len());
    }
}
