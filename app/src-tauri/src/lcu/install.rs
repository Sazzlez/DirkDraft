// Where League is installed, from the metadata the Riot Client writes itself
// (port of DraftPilot.Core.Lcu.LeagueInstall): product settings, then the install manifest, then
// the usual folders on C:, D: and E:.

use std::path::{Path, PathBuf};

const PRODUCT_SETTINGS: &str =
    r"Riot Games\Metadata\league_of_legends.live\league_of_legends.live.product_settings.yaml";
const INSTALL_MANIFEST: &str = r"Riot Games\RiotClientInstalls.json";
const INSTALL_PATH_KEY: &str = "product_install_full_path:";

/// The install folder, or None when League cannot be found.
pub fn locate() -> Option<PathBuf> {
    let program_data = std::env::var_os("PROGRAMDATA").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(r"C:\ProgramData"));
    from_product_settings(&program_data.join(PRODUCT_SETTINGS))
        .or_else(|| from_install_manifest(&program_data.join(INSTALL_MANIFEST)))
        .or_else(from_defaults)
}

pub fn lockfile(install: &Path) -> PathBuf {
    install.join("lockfile")
}

fn from_product_settings(path: &Path) -> Option<PathBuf> {
    let text = std::fs::read_to_string(path).ok()?;
    text.lines().find_map(|line| {
        let lower = line.to_lowercase();
        let index = lower.find(INSTALL_PATH_KEY)?;
        let value = line[index + INSTALL_PATH_KEY.len()..].trim().trim_matches('"');
        let dir = PathBuf::from(value);
        dir.is_dir().then_some(dir)
    })
}

fn from_install_manifest(path: &Path) -> Option<PathBuf> {
    let text = std::fs::read_to_string(path).ok()?;
    let json: serde_json::Value = serde_json::from_str(&text).ok()?;
    let associated = json.get("associated_client")?.as_object()?;
    associated.keys().find_map(|key| {
        if !key.to_lowercase().contains("league of legends") {
            return None;
        }
        let dir = PathBuf::from(key.trim_end_matches(['/', '\\']));
        dir.is_dir().then_some(dir)
    })
}

fn from_defaults() -> Option<PathBuf> {
    const SUFFIXES: [&str; 3] = [
        r":\Riot Games\League of Legends",
        r":\Program Files\Riot Games\League of Legends",
        r":\Games\Riot Games\League of Legends",
    ];
    ["C", "D", "E"].iter().find_map(|drive| {
        SUFFIXES.iter().map(|suffix| PathBuf::from(format!("{drive}{suffix}"))).find(|dir| dir.is_dir())
    })
}
