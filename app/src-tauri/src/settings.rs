// The settings 1.5 already keeps, read and written in place: %APPDATA%\DraftPilot\settings.json,
// camelCase keys (spec §5). Updating from 1.5 keeps every choice. Keys this version does not know
// stay in the file untouched; a file that cannot be read is never overwritten.

use std::path::PathBuf;

use serde_json::{Map, Value};

pub struct Settings {
    path: Option<PathBuf>,
    values: Map<String, Value>,
    /// False when the file exists but is not valid JSON: then nothing is written over it.
    writable: bool,
}

impl Settings {
    pub fn load() -> Self {
        let path = std::env::var_os("APPDATA").map(|dir| PathBuf::from(dir).join("DraftPilot").join("settings.json"));
        let Some(file) = &path else {
            return Self { path, values: Map::new(), writable: false };
        };
        match std::fs::read_to_string(file) {
            Err(_) => Self { path, values: Map::new(), writable: true },
            Ok(text) => match serde_json::from_str::<Value>(text.trim_start_matches('\u{feff}')) {
                Ok(Value::Object(values)) => Self { path, values, writable: true },
                _ => {
                    log::warn!("settings.json nicht lesbar — wird nicht überschrieben");
                    Self { path, values: Map::new(), writable: false }
                }
            },
        }
    }

    /// The value under `key`; 1.5 read keys case-insensitively, so this does too.
    fn get(&self, key: &str) -> Option<&Value> {
        self.values.get(key).or_else(|| self.values.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)).map(|(_, v)| v))
    }

    pub fn flag(&self, key: &str, default: bool) -> bool {
        self.get(key).and_then(Value::as_bool).unwrap_or(default)
    }

    /// A number as 1.5 wrote it: a JSON number, or a string (.NET writes NaN as "NaN"); None for
    /// anything not finite.
    pub fn number(&self, key: &str) -> Option<f64> {
        let value = self.get(key)?;
        let number = value.as_f64().or_else(|| value.as_str().and_then(|s| s.trim().parse::<f64>().ok()))?;
        number.is_finite().then_some(number)
    }

    pub fn text(&self, key: &str) -> Option<String> {
        self.get(key).and_then(Value::as_str).map(str::trim).filter(|s| !s.is_empty()).map(str::to_string)
    }

    pub fn set_flag(&mut self, key: &str, value: bool) {
        self.set(key, Value::Bool(value));
    }

    pub fn set(&mut self, key: &str, value: Value) {
        // Replace a differently cased key from 1.5 rather than adding a second one.
        let existing = self.values.keys().find(|k| k.eq_ignore_ascii_case(key)).cloned();
        self.values.insert(existing.unwrap_or_else(|| key.to_string()), value);
        self.save();
    }

    /// The settings the page shows and may change, with 1.5's defaults filled in.
    pub fn page(&self) -> Value {
        serde_json::json!({
            "autoShowOnChampSelect": self.flag(AUTO_SHOW_ON_CHAMP_SELECT.0, AUTO_SHOW_ON_CHAMP_SELECT.1),
            "autoShowOnGameStart": self.flag(AUTO_SHOW_ON_GAME_START.0, AUTO_SHOW_ON_GAME_START.1),
            "autoHideAfterChampSelect": self.flag(AUTO_HIDE_AFTER_CHAMP_SELECT.0, AUTO_HIDE_AFTER_CHAMP_SELECT.1),
            "keepOnTop": self.flag(KEEP_ON_TOP.0, KEEP_ON_TOP.1),
            "showUnowned": self.flag(SHOW_UNOWNED.0, SHOW_UNOWNED.1),
            "glassEffect": self.flag(GLASS_EFFECT.0, GLASS_EFFECT.1),
            "flashKey": if self.text(FLASH_KEY).as_deref() == Some("D") { "D" } else { "F" },
        })
    }

    /// A change from the page, checked against the key's kind: nothing else reaches the file.
    pub fn set_from_page(&mut self, key: &str, value: Value) -> Result<(), String> {
        let valid = match key {
            k if FLAGS.contains(&k) => value.is_boolean(),
            FLASH_KEY => matches!(value.as_str(), Some("D" | "F")),
            _ => return Err(format!("unbekannte Einstellung {key}")),
        };
        if !valid {
            return Err(format!("ungültiger Wert für {key}"));
        }
        self.set(key, value);
        Ok(())
    }

    fn save(&self) {
        let (Some(path), true) = (&self.path, self.writable) else { return };
        let Ok(text) = serde_json::to_string_pretty(&Value::Object(self.values.clone())) else { return };
        if let Some(dir) = path.parent() {
            let _ = std::fs::create_dir_all(dir);
        }
        // Written beside and moved over: a crash mid-write must not leave half a file.
        let temp = path.with_extension("json.tmp");
        if std::fs::write(&temp, text).is_ok() {
            let _ = std::fs::rename(&temp, path);
        }
    }
}

/// The keys and 1.5's defaults.
pub const AUTO_SHOW_ON_CHAMP_SELECT: (&str, bool) = ("autoShowOnChampSelect", true);
pub const AUTO_HIDE_AFTER_CHAMP_SELECT: (&str, bool) = ("autoHideAfterChampSelect", false);
pub const AUTO_SHOW_ON_GAME_START: (&str, bool) = ("autoShowOnGameStart", true);
pub const KEEP_ON_TOP: (&str, bool) = ("keepOnTop", false);
pub const TRAY_HINT_SHOWN: (&str, bool) = ("trayHintShown", false);
pub const LOCKFILE_PATH: &str = "lockfilePath";
/// 1.5: suggest champions the account does not own too.
pub const SHOW_UNOWNED: (&str, bool) = ("showUnowned", false);
/// 2.0: the key Flash goes on when spells are taken over ("D" or "F", default F).
pub const FLASH_KEY: &str = "flashKey";
/// 2.0: Mica behind the window on Windows 11 (read at start). Off = the opaque Windows 10 look —
/// the way out should the transparent window misbehave on someone's machine.
pub const GLASS_EFFECT: (&str, bool) = ("glassEffect", true);

const FLAGS: [&str; 6] = [
    AUTO_SHOW_ON_CHAMP_SELECT.0,
    AUTO_SHOW_ON_GAME_START.0,
    AUTO_HIDE_AFTER_CHAMP_SELECT.0,
    KEEP_ON_TOP.0,
    SHOW_UNOWNED.0,
    GLASS_EFFECT.0,
];

#[cfg(test)]
mod tests {
    use super::*;

    fn at(path: PathBuf) -> Settings {
        let text = std::fs::read_to_string(&path).ok();
        let values = text.and_then(|t| serde_json::from_str::<Value>(&t).ok()).and_then(|v| v.as_object().cloned()).unwrap_or_default();
        Settings { path: Some(path), values, writable: true }
    }

    #[test]
    fn keeps_unknown_keys_and_reads_any_case() {
        let dir = std::env::temp_dir().join(format!("dirkdraft-settings-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("settings.json");
        std::fs::write(&file, r#"{"KeepOnTop": true, "windowLeft": "NaN", "tier": "platinum"}"#).unwrap();

        let mut settings = at(file.clone());
        assert!(settings.flag(KEEP_ON_TOP.0, KEEP_ON_TOP.1));
        assert!(settings.flag(AUTO_SHOW_ON_CHAMP_SELECT.0, AUTO_SHOW_ON_CHAMP_SELECT.1));
        settings.set_flag(KEEP_ON_TOP.0, false);
        settings.set_flag(TRAY_HINT_SHOWN.0, true);

        let written: Value = serde_json::from_str(&std::fs::read_to_string(&file).unwrap()).unwrap();
        assert_eq!(written["KeepOnTop"], false);
        assert!(written.get("keepOnTop").is_none());
        assert_eq!(written["trayHintShown"], true);
        assert_eq!(written["windowLeft"], "NaN");
        assert_eq!(written["tier"], "platinum");
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn the_page_sees_defaults_and_may_change_only_known_keys() {
        let mut settings = Settings { path: None, values: Map::new(), writable: false };
        let page = settings.page();
        assert_eq!(page["autoShowOnChampSelect"], true);
        assert_eq!(page["flashKey"], "F");

        assert!(settings.set_from_page("flashKey", Value::from("D")).is_ok());
        assert_eq!(settings.page()["flashKey"], "D");
        assert!(settings.set_from_page("flashKey", Value::from("Q")).is_err());
        assert!(settings.set_from_page("showUnowned", Value::from(true)).is_ok());
        assert!(settings.set_from_page("showUnowned", Value::from("ja")).is_err());
        assert!(settings.set_from_page("poolAdded", serde_json::json!([122, 86])).is_err());
        assert!(settings.set_from_page("lockfilePath", Value::from("C:\\x")).is_err());
        assert!(settings.set_from_page("trayHintShown", Value::from(false)).is_err());
    }

    #[test]
    fn reads_1_5_window_numbers_and_skips_nan() {
        let values = serde_json::from_str::<Value>(r#"{"windowLeft": 812.5, "WindowTop": "40", "windowWidth": "NaN", "windowHeight": null}"#)
            .unwrap()
            .as_object()
            .cloned()
            .unwrap();
        let settings = Settings { path: None, values, writable: false };
        assert_eq!(settings.number("windowLeft"), Some(812.5));
        assert_eq!(settings.number("windowTop"), Some(40.0));
        assert_eq!(settings.number("windowWidth"), None);
        assert_eq!(settings.number("windowHeight"), None);
    }

    #[test]
    fn never_writes_over_an_unreadable_file() {
        let settings = Settings { path: None, values: Map::new(), writable: false };
        assert!(!settings.flag(KEEP_ON_TOP.0, KEEP_ON_TOP.1));
        settings.save();
    }
}
