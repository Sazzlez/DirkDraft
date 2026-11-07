// The kept copies of the downloaded data, as files: %LOCALAPPDATA%\DraftPilot\data\2.0\. The page
// may read and write exactly these names and nothing else — no path from the page ever reaches the
// file system.

use std::path::PathBuf;

const NAMES: [&str; 4] = ["meta.json", "builds.json", "mayhem.json", "situational.json"];

/// The file for a name the page may use: one of NAMES, or one of them plus ".etag".
pub fn path_for(name: &str) -> Option<PathBuf> {
    let base = name.strip_suffix(".etag").unwrap_or(name);
    if !NAMES.contains(&base) {
        return None;
    }
    let dir = std::env::var_os("LOCALAPPDATA").map(PathBuf::from)?.join("DraftPilot").join("data").join("2.0");
    Some(dir.join(name))
}

#[tauri::command]
pub fn data_read(name: String) -> Result<Option<String>, String> {
    let path = path_for(&name).ok_or("Name nicht erlaubt")?;
    match std::fs::read_to_string(path) {
        Ok(text) => Ok(Some(text)),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

#[tauri::command]
pub fn data_write(name: String, text: String) -> Result<(), String> {
    let path = path_for(&name).ok_or("Name nicht erlaubt")?;
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    }
    // Written beside and moved over: a crash mid-write keeps the last good copy.
    let temp = path.with_extension("tmp");
    std::fs::write(&temp, text).map_err(|e| e.to_string())?;
    std::fs::rename(&temp, &path).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn allows_only_the_known_files() {
        assert!(path_for("meta.json").is_some());
        assert!(path_for("builds.json.etag").is_some());
        assert!(path_for("history.json").is_none());
        assert!(path_for("../settings.json").is_none());
        assert!(path_for("..\\meta.json").is_none());
        assert!(path_for("snapshot.json").is_none());
        assert!(path_for("").is_none());
    }
}
