// A small log file for when something goes wrong on a friend's machine: what started, whether League
// and the client were found, socket drops, imports, update checks, crashes — never a name, token or
// password. %LOCALAPPDATA%\DraftPilot\data\2.0\dirkdraft.log, kept under 256 KB: a full file moves
// to dirkdraft.old.log and a new one starts (1.5 capped its logs the same way).

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;
use std::time::{SystemTime, UNIX_EPOCH};

use log::{Level, LevelFilter, Log, Metadata, Record};

const MAX_BYTES: u64 = 256 * 1024;

struct FileLog {
    path: PathBuf,
    file: Mutex<Option<File>>,
}

pub fn dir() -> Option<PathBuf> {
    std::env::var_os("LOCALAPPDATA").map(|d| PathBuf::from(d).join("DraftPilot").join("data").join("2.0"))
}

/// Installs the logger and a panic hook that writes the crash before the process goes.
pub fn init() {
    let Some(dir) = dir() else { return };
    let _ = std::fs::create_dir_all(&dir);
    let path = dir.join("dirkdraft.log");
    let logger = FileLog { file: Mutex::new(open(&path)), path };
    // Lives as long as the process; set_logger wants a 'static (set_boxed_logger needs log's "std").
    if log::set_logger(Box::leak(Box::new(logger))).is_ok() {
        log::set_max_level(LevelFilter::Info);
    }
    let previous = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        log::error!("Absturz: {info}");
        log::logger().flush();
        previous(info);
    }));
}

fn open(path: &PathBuf) -> Option<File> {
    if std::fs::metadata(path).map(|m| m.len() > MAX_BYTES).unwrap_or(false) {
        let _ = std::fs::rename(path, path.with_file_name("dirkdraft.old.log"));
    }
    OpenOptions::new().create(true).append(true).open(path).ok()
}

impl Log for FileLog {
    fn enabled(&self, metadata: &Metadata) -> bool {
        // Velopack narrates every update check at info level, with the user's profile path in it:
        // only its warnings and errors belong here; our own update lines say what happened.
        let limit = if metadata.target().starts_with("velopack") { Level::Warn } else { Level::Info };
        metadata.level() <= limit
    }

    fn log(&self, record: &Record) {
        if !self.enabled(record.metadata()) {
            return;
        }
        let Ok(mut guard) = self.file.lock() else { return };
        // Rotate in flight too: a session that runs for days must not grow the file without end.
        if guard.as_ref().and_then(|f| f.metadata().ok()).is_some_and(|m| m.len() > MAX_BYTES) {
            *guard = None;
            *guard = open(&self.path);
        }
        if let Some(file) = guard.as_mut() {
            let message: String = record.args().to_string().chars().take(500).collect();
            let _ = writeln!(file, "{} {:5} {}", timestamp(), record.level(), message);
        }
    }

    fn flush(&self) {
        if let Ok(mut guard) = self.file.lock() {
            if let Some(file) = guard.as_mut() {
                let _ = file.flush();
            }
        }
    }
}

/// "2026-11-07 13:05:42" (UTC), without a date library: days since 1970 to a civil date.
fn timestamp() -> String {
    let (y, m, d, h, min, s) = now();
    format!("{y:04}-{m:02}-{d:02} {h:02}:{min:02}:{s:02}")
}

/// "20261107-130542" (UTC): for file names that sort by time.
pub fn stamp() -> String {
    let (y, m, d, h, min, s) = now();
    format!("{y:04}{m:02}{d:02}-{h:02}{min:02}{s:02}")
}

fn now() -> (i64, u32, u32, u64, u64, u64) {
    let secs = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    let (days, rest) = ((secs / 86_400) as i64, secs % 86_400);
    let (y, m, d) = civil(days);
    (y, m, d, rest / 3600, rest % 3600 / 60, rest % 60)
}

/// Howard Hinnant's days-to-civil.
fn civil(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    let y = yoe + era * 400 + i64::from(m <= 2);
    (y, m, d)
}

/// The page's own lines (data problems, script errors), level and length checked.
#[tauri::command]
pub fn shell_log(level: String, message: String) {
    let message: String = message.chars().take(500).collect();
    match level.as_str() {
        "error" => log::error!("Seite: {message}"),
        "warn" => log::warn!("Seite: {message}"),
        _ => log::info!("Seite: {message}"),
    }
}

#[cfg(test)]
mod tests {
    use super::civil;

    #[test]
    fn turns_days_into_dates() {
        assert_eq!(civil(0), (1970, 1, 1));
        assert_eq!(civil(20_399), (2025, 11, 7));
        assert_eq!(civil(20_764), (2026, 11, 7));
        assert_eq!(civil(11_016), (2000, 2, 29));
    }
}
