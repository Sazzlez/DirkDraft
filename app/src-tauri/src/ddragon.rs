// Data Dragon's tables — champion and item names, prices and recipes, spells, runes — for one game
// version, fetched here and kept on disk: %LOCALAPPDATA%\DraftPilot\data\2.0\ddragon\<version>\
// <language>\. A version's files never change, so a kept one is never asked for again, and a start
// without a network still has names.
//
// Until 17.11.2026 the page fetched them itself, once per run: a start without a network left the
// names empty until the next start, and a new patch of the client was never picked up. Which version
// is wanted is the page's call (the client's own patch, else the data's); which one that is on Data
// Dragon — and which kept one stands in when nothing can be fetched — is decided here.

use std::path::PathBuf;

use futures_util::future::join_all;
use serde::Serialize;

use crate::net::{self, Fetched};

const BASE: &str = "https://ddragon.leagueoflegends.com";

/// The four tables of a version, by their file names on Data Dragon.
const FILES: [&str; 4] = ["champion.json", "item.json", "summoner.json", "runesReforged.json"];

/// The tables as text, for the page to parse (app/src/data/assets.ts).
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Assets {
    /// The Data Dragon version they are of ("16.23.1") — not always the one asked for.
    pub version: String,
    pub champion: String,
    pub item: String,
    pub summoner: String,
    pub runes: String,
}

/// "16.23" out of "16.23.1" or "16.23.455.1234"; None for anything else.
fn line(version: &str) -> Option<String> {
    let mut parts = version.split('.');
    let major = parts.next()?;
    let minor = parts.next()?;
    let number = |part: &str| !part.is_empty() && part.len() <= 4 && part.bytes().all(|b| b.is_ascii_digit());
    (number(major) && number(minor)).then(|| format!("{major}.{minor}"))
}

/// A version string that may name a folder: digits and dots, nothing else.
fn safe_version(version: &str) -> bool {
    !version.is_empty() && version.len() <= 24 && version.split('.').all(|part| !part.is_empty() && part.bytes().all(|b| b.is_ascii_digit()))
}

/// "de_DE": two letters, an underscore, two letters.
fn safe_language(language: &str) -> bool {
    let bytes = language.as_bytes();
    bytes.len() == 5 && bytes[2] == b'_' && bytes[..2].iter().all(u8::is_ascii_lowercase) && bytes[3..].iter().all(u8::is_ascii_uppercase)
}

/// The version to load for what is wanted, out of the versions there are (newest first, as Data
/// Dragon lists them): the very one, else the newest of its patch — the client names a build Data
/// Dragon may not carry, the data name a patch only — else the newest of all.
pub fn pick_version(wanted: &str, versions: &[String]) -> Option<String> {
    if versions.iter().any(|v| v == wanted) {
        return Some(wanted.to_string());
    }
    if let Some(wanted_line) = line(wanted) {
        if let Some(found) = versions.iter().find(|v| line(v).as_deref() == Some(wanted_line.as_str())) {
            return Some(found.clone());
        }
    }
    versions.first().cloned()
}

pub struct Cache {
    dir: PathBuf,
}

impl Cache {
    pub fn user() -> Option<Self> {
        let dir = std::env::var_os("LOCALAPPDATA").map(PathBuf::from)?.join("DraftPilot").join("data").join("2.0").join("ddragon");
        Some(Self { dir })
    }

    #[cfg(test)]
    pub fn at(dir: PathBuf) -> Self {
        Self { dir }
    }

    fn folder(&self, version: &str, language: &str) -> PathBuf {
        self.dir.join(version).join(language)
    }

    /// All four tables of a version, or None when one is missing.
    fn read(&self, version: &str, language: &str) -> Option<Assets> {
        let folder = self.folder(version, language);
        let text = |name: &str| std::fs::read_to_string(folder.join(name)).ok();
        Some(Assets {
            version: version.to_string(),
            champion: text(FILES[0])?,
            item: text(FILES[1])?,
            summoner: text(FILES[2])?,
            runes: text(FILES[3])?,
        })
    }

    fn write(&self, version: &str, language: &str, texts: &[String; 4]) -> Result<(), String> {
        let folder = self.folder(version, language);
        std::fs::create_dir_all(&folder).map_err(|e| e.to_string())?;
        for (name, text) in FILES.iter().zip(texts) {
            // Beside and moved over: a version folder holds whole files or none.
            let temp = folder.join(format!("{name}.tmp"));
            std::fs::write(&temp, text).map_err(|e| e.to_string())?;
            std::fs::rename(&temp, folder.join(name)).map_err(|e| e.to_string())?;
        }
        Ok(())
    }

    /// The versions kept whole for a language, newest first.
    fn kept_versions(&self, language: &str) -> Vec<String> {
        let mut versions: Vec<String> = std::fs::read_dir(&self.dir)
            .map(|entries| {
                entries
                    .filter_map(|entry| entry.ok()?.file_name().into_string().ok())
                    .filter(|version| safe_version(version) && self.read(version, language).is_some())
                    .collect()
            })
            .unwrap_or_default();
        let key = |version: &String| version.split('.').map(|part| part.parse::<u32>().unwrap_or(0)).collect::<Vec<_>>();
        versions.sort_by_key(|version| std::cmp::Reverse(key(version)));
        versions
    }

    fn versions_file(&self) -> PathBuf {
        self.dir.join("versions.json")
    }
}

/// Data Dragon's list of versions, newest first: asked for, kept for the next start without a
/// network, and read from that copy when the server does not answer. None without either.
async fn versions(cache: &Cache, client: &reqwest::Client, base: &str) -> Option<(Vec<String>, bool)> {
    let parse = |text: &str| serde_json::from_str::<Vec<String>>(text).ok().filter(|list| !list.is_empty());
    if let Ok(Fetched::Body { bytes, .. }) = net::get(client, &format!("{base}/api/versions.json"), &[]).await {
        if let Some(list) = std::str::from_utf8(&bytes).ok().and_then(parse) {
            let _ = std::fs::create_dir_all(&cache.dir);
            let _ = std::fs::write(cache.versions_file(), &bytes);
            return Some((list, true));
        }
    }
    let kept = std::fs::read_to_string(cache.versions_file()).ok()?;
    parse(&kept).map(|list| (list, false))
}

/// The tables for the wanted version: from disk when they are kept, else fetched and kept. When
/// nothing can be fetched, the newest kept version stands in — of the same patch if there is one.
pub async fn load(cache: &Cache, client: &reqwest::Client, base: &str, wanted: &str, language: &str) -> Result<Assets, String> {
    if !safe_language(language) {
        return Err("Sprache nicht erlaubt".to_string());
    }
    if !safe_version(wanted) {
        return Err("Version nicht erlaubt".to_string());
    }

    let listed = versions(cache, client, base).await;
    let kept = cache.kept_versions(language);
    let version = match &listed {
        Some((list, _)) => pick_version(wanted, list),
        // No list at all: the wanted version only if it is kept; the fallback below otherwise.
        None => kept.iter().find(|v| *v == wanted).cloned(),
    };

    if let Some(version) = version.filter(|v| safe_version(v)) {
        if let Some(assets) = cache.read(&version, language) {
            return Ok(assets);
        }
        let urls: Vec<String> = FILES.iter().map(|name| format!("{base}/cdn/{version}/data/{language}/{name}")).collect();
        let answers = join_all(urls.iter().map(|url| net::get(client, url, &[]))).await;
        let mut texts: Vec<String> = Vec::with_capacity(FILES.len());
        for answer in answers {
            if let Ok(Fetched::Body { bytes, .. }) = answer {
                if let Ok(text) = String::from_utf8(bytes) {
                    // A table is a JSON object or array; an error page is neither.
                    if text.trim_start().starts_with(['{', '[']) {
                        texts.push(text);
                    }
                }
            }
        }
        if let Ok(texts) = <[String; 4]>::try_from(texts) {
            if let Err(error) = cache.write(&version, language, &texts) {
                log::warn!("Data Dragon {version}: nicht gespeichert ({error})");
            }
            let [champion, item, summoner, runes] = texts;
            return Ok(Assets { version, champion, item, summoner, runes });
        }
    }

    // Nothing fetched: what is kept, the wanted patch first.
    let stand_in = pick_version(wanted, &kept).ok_or_else(|| "Data Dragon nicht erreichbar und keine Kopie".to_string())?;
    cache.read(&stand_in, language).ok_or_else(|| "Kopie nicht lesbar".to_string())
}

#[tauri::command]
pub async fn assets_load(patch: String, language: String) -> Result<Assets, String> {
    let cache = Cache::user().ok_or_else(|| "Kein Datenordner".to_string())?;
    let assets = load(&cache, net::client(), BASE, &patch, &language).await;
    match &assets {
        Ok(found) => log::info!("Data Dragon: gewünscht {patch}, geladen {}", found.version),
        Err(error) => log::warn!("Data Dragon: gewünscht {patch}, {error}"),
    }
    assets
}

#[cfg(test)]
mod tests {
    use std::io::{BufRead, BufReader, Write};
    use std::net::TcpListener;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::{Arc, Mutex};

    use super::*;

    fn list(versions: &[&str]) -> Vec<String> {
        versions.iter().map(|v| v.to_string()).collect()
    }

    #[test]
    fn picks_the_very_version_else_the_newest_of_its_patch_else_the_newest() {
        let versions = list(&["16.23.2", "16.23.1", "16.22.1", "16.21.1"]);
        assert_eq!(pick_version("16.23.1", &versions).as_deref(), Some("16.23.1"));
        // The data name a patch, the client a build Data Dragon does not carry.
        assert_eq!(pick_version("16.22", &versions).as_deref(), Some("16.22.1"));
        assert_eq!(pick_version("16.23.455.1234", &versions).as_deref(), Some("16.23.2"));
        // A patch Data Dragon does not have yet: the newest there is.
        assert_eq!(pick_version("16.24.1", &versions).as_deref(), Some("16.23.2"));
        assert_eq!(pick_version("16.23.1", &[]), None);
    }

    #[test]
    fn only_versions_and_languages_may_name_a_folder() {
        for version in ["16.23.1", "16.23", "16.23.455.1234"] {
            assert!(safe_version(version), "{version}");
        }
        for version in ["", "..", "16.23/..", "16..1", "16.23.x", "..\\..\\x", "latest"] {
            assert!(!safe_version(version), "{version}");
        }
        assert!(safe_language("de_DE"));
        for language in ["", "de", "de_de", "../..", "de_DE/", "DE_de"] {
            assert!(!safe_language(language), "{language}");
        }
    }

    fn temp_cache(tag: &str) -> Cache {
        let dir = std::env::temp_dir().join(format!("dirkdraft-ddragon-test-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        Cache::at(dir)
    }

    /// A stand-in for Data Dragon: the version list and, for every version on it, four tables that
    /// say which version they are. Counts the requests; answers nothing while `down` is set.
    fn serve(versions: &'static [&'static str]) -> (String, Arc<Mutex<Vec<String>>>, Arc<AtomicBool>) {
        let listener = TcpListener::bind("127.0.0.1:0").expect("lokaler Port");
        let base = format!("http://{}", listener.local_addr().expect("Adresse"));
        let seen = Arc::new(Mutex::new(Vec::new()));
        let down = Arc::new(AtomicBool::new(false));
        let (record, off) = (seen.clone(), down.clone());
        std::thread::spawn(move || {
            for stream in listener.incoming() {
                let Ok(mut stream) = stream else { continue };
                let mut reader = BufReader::new(stream.try_clone().expect("Stream"));
                let mut request = String::new();
                let _ = reader.read_line(&mut request);
                loop {
                    let mut header = String::new();
                    if reader.read_line(&mut header).unwrap_or(0) == 0 || header.trim().is_empty() {
                        break;
                    }
                }
                let path = request.split_whitespace().nth(1).unwrap_or("/").to_string();
                record.lock().expect("Protokoll").push(path.clone());
                let body = if off.load(Ordering::SeqCst) {
                    None
                } else if path == "/api/versions.json" {
                    Some(format!("[{}]", versions.iter().map(|v| format!("\"{v}\"")).collect::<Vec<_>>().join(",")))
                } else {
                    versions.iter().find(|v| path.starts_with(&format!("/cdn/{v}/data/de_DE/"))).map(|v| format!("{{\"version\":\"{v}\",\"file\":\"{path}\"}}"))
                };
                let answer = match body {
                    Some(body) => format!("HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}", body.len()),
                    None => "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".to_string(),
                };
                let _ = stream.write_all(answer.as_bytes());
            }
        });
        (base, seen, down)
    }

    #[tokio::test]
    async fn a_version_is_fetched_once_and_read_from_disk_after_that() {
        let cache = temp_cache("once");
        let (base, seen, _) = serve(&["16.23.1", "16.22.1"]);
        let client = net::build(false);

        let first = load(&cache, &client, &base, "16.22", "de_DE").await.unwrap();
        assert_eq!(first.version, "16.22.1");
        assert!(first.item.contains("item.json"));
        assert!(first.runes.contains("runesReforged.json"));
        let fetched = seen.lock().unwrap().iter().filter(|p| p.starts_with("/cdn/")).count();
        assert_eq!(fetched, 4);

        // Again: the list is asked (a new patch may have come), the tables are not.
        let again = load(&cache, &client, &base, "16.22", "de_DE").await.unwrap();
        assert_eq!(again.version, "16.22.1");
        assert_eq!(seen.lock().unwrap().iter().filter(|p| p.starts_with("/cdn/")).count(), 4);
    }

    #[tokio::test]
    async fn a_new_client_patch_loads_the_new_version() {
        let cache = temp_cache("patch");
        let (base, _, _) = serve(&["16.23.1", "16.22.1"]);
        let client = net::build(false);

        assert_eq!(load(&cache, &client, &base, "16.22.1", "de_DE").await.unwrap().version, "16.22.1");
        // The client patched; its build number is not on Data Dragon, its patch is.
        let patched = load(&cache, &client, &base, "16.23.455.1234", "de_DE").await.unwrap();
        assert_eq!(patched.version, "16.23.1");
        assert!(patched.champion.contains("\"16.23.1\""));
        assert_eq!(cache.kept_versions("de_DE"), list(&["16.23.1", "16.22.1"]));
    }

    #[tokio::test]
    async fn without_a_network_the_kept_copy_stands_in_and_the_next_try_fetches() {
        let cache = temp_cache("offline");
        let (base, _, down) = serve(&["16.23.1", "16.22.1"]);
        let client = net::build(false);

        // Offline from the start, nothing kept: said, not invented.
        down.store(true, Ordering::SeqCst);
        assert!(load(&cache, &client, &base, "16.22", "de_DE").await.is_err());

        // Online: fetched and kept.
        down.store(false, Ordering::SeqCst);
        assert_eq!(load(&cache, &client, &base, "16.22", "de_DE").await.unwrap().version, "16.22.1");

        // Offline again: the kept version — also for a patch that is not kept, rather than nothing.
        down.store(true, Ordering::SeqCst);
        assert_eq!(load(&cache, &client, &base, "16.22", "de_DE").await.unwrap().version, "16.22.1");
        assert_eq!(load(&cache, &client, &base, "16.23.455.1234", "de_DE").await.unwrap().version, "16.22.1");

        // Back online: the new patch is fetched now.
        down.store(false, Ordering::SeqCst);
        assert_eq!(load(&cache, &client, &base, "16.23.455.1234", "de_DE").await.unwrap().version, "16.23.1");
    }

    #[tokio::test]
    async fn refuses_what_could_leave_the_folder() {
        let cache = temp_cache("names");
        let client = net::build(false);
        assert!(load(&cache, &client, "http://127.0.0.1:9", "../../x", "de_DE").await.is_err());
        assert!(load(&cache, &client, "http://127.0.0.1:9", "16.22.1", "../..").await.is_err());
        assert!(!cache.dir.exists());
    }
}
