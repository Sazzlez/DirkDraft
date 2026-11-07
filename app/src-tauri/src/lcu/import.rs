// The one write path, and only on the user's click: the rune page (port of RuneImporter.cs) and,
// since 07.11.2026 on the user's wish, the two summoner spells. The page decides nothing about slots
// for the spells — the TS core (core/loadout.ts) already put Flash on the player's key.
//
// Rune pages follow 1.5's rule: DirkDraft deletes only pages it created itself (prefix DirkRunen,
// or DraftPilot from before the rename). When the account's page limit forces replacing a foreign
// page, nothing is written: the answer names the page and the page asks the user; only a second
// call that names the same page as agreed goes ahead. Everything is decided before the first delete.

use reqwest::Method;
use serde::{Deserialize, Serialize};
use serde_json::json;

use super::http::Lcu;

pub const PAGE_PREFIX: &str = "DirkRunen";
const LEGACY_PREFIX: &str = "DraftPilot";
/// The client rejects longer page names (checked against the real client in 1.5).
const MAX_NAME_CHARS: usize = 30;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportRequest {
    pub page: Option<PageRequest>,
    /// Slot D, slot F.
    pub spells: Option<[i64; 2]>,
    /// The foreign page the user agreed to lose, by name.
    pub replace: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PageRequest {
    /// "Darius vs Jax" — becomes "DirkRunen · Darius vs Jax".
    pub title: String,
    pub primary_style_id: i64,
    pub sub_style_id: i64,
    /// 4 primary + 2 secondary + 3 shards.
    pub selected_perk_ids: Vec<i64>,
}

#[derive(Debug, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ImportOutcome {
    pub ok: bool,
    pub message: String,
    /// Set when a foreign page would have to go: its name, for the question to the user.
    pub confirm: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Page {
    pub id: i64,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub is_editable: bool,
    pub is_deletable: Option<bool>,
    #[serde(default)]
    pub last_modified: i64,
}

impl Page {
    fn can_delete(&self) -> bool {
        self.is_deletable.unwrap_or(self.is_editable)
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Inventory {
    owned_page_count: i64,
}

pub fn is_ours(name: &str) -> bool {
    name.starts_with(PAGE_PREFIX) || name.starts_with(LEGACY_PREFIX)
}

pub fn page_name(title: &str) -> String {
    format!("{PAGE_PREFIX} · {title}").chars().take(MAX_NAME_CHARS).collect()
}

/// What has to happen to make room, decided before anything is deleted.
#[derive(Debug, PartialEq)]
pub enum Plan {
    /// Delete these own pages (and the agreed foreign one, if any), then create.
    Go { delete: Vec<i64>, sacrifice: Option<(i64, String)> },
    /// A foreign page would have to go and the user has not agreed to this one yet.
    Ask(String),
    Refuse(&'static str),
}

pub fn plan(pages: &[Page], owned: i64, agreed: Option<&str>) -> Plan {
    if owned <= 0 {
        return Plan::Refuse("Dieser Account besitzt keine Runenseiten-Plätze.");
    }
    let editable: Vec<&Page> = pages.iter().filter(|p| p.is_editable).collect();
    let ours: Vec<i64> = editable.iter().filter(|p| is_ours(&p.name) && p.can_delete()).map(|p| p.id).collect();

    // Pages beyond our own that have to go before one slot is free. More than one would mean
    // deleting several of the user's pages for one import — refuse before anything is deleted.
    let needed = editable.len() as i64 - ours.len() as i64 - owned + 1;
    if needed > 1 {
        return Plan::Refuse("Mehr Seiten belegt als Plätze vorhanden — bitte im Client aufräumen.");
    }
    if needed < 1 {
        return Plan::Go { delete: ours, sacrifice: None };
    }

    // Offer the oldest foreign page: the one whose loss is most likely already priced in.
    let Some(oldest) = editable.iter().filter(|p| !is_ours(&p.name) && p.can_delete()).min_by_key(|p| p.last_modified) else {
        return Plan::Refuse("Alle Seiten belegt und keine löschbar — bitte im Client Platz schaffen.");
    };
    if agreed != Some(oldest.name.as_str()) {
        return Plan::Ask(oldest.name.clone());
    }
    Plan::Go { delete: ours, sacrifice: Some((oldest.id, oldest.name.clone())) }
}

pub async fn run(lcu: &Lcu, request: ImportRequest) -> ImportOutcome {
    let mut done: Vec<&str> = Vec::new();
    // What did not work, in the user's words. The two writes are independent: spells the client
    // refuses (a mode without them, champion select already over) do not cost the rune page, and a
    // rune page it refuses does not cost the spells.
    let mut problems: Vec<String> = Vec::new();

    // Runes are planned first: if the user has to be asked, nothing at all is written yet.
    let mut rune_work = None;
    if let Some(page) = &request.page {
        if page.selected_perk_ids.len() < 9 {
            problems.push("Runen unvollständig — bitte Build neu laden.".into());
        } else {
            let pages: Option<Vec<Page>> = lcu.get_json("/lol-perks/v1/pages").await;
            let inventory: Option<Inventory> = lcu.get_json("/lol-perks/v1/inventory").await;
            // A failed read is not "no pages": counting it as zero would delete something to make room.
            match (pages, inventory) {
                (Some(pages), Some(inventory)) => match plan(&pages, inventory.owned_page_count, request.replace.as_deref()) {
                    Plan::Refuse(reason) => problems.push(reason.into()),
                    Plan::Ask(name) => {
                        return ImportOutcome {
                            ok: false,
                            message: format!("Für die neue Seite müsste „{name}“ weichen."),
                            confirm: Some(name),
                        }
                    }
                    Plan::Go { delete, sacrifice } => rune_work = Some((page, delete, sacrifice)),
                },
                _ => problems.push("Client hat die Runenseiten nicht verraten — läuft er noch?".into()),
            }
        }
    }

    if let Some((page, delete, sacrifice)) = rune_work {
        match write_page(lcu, page, delete, sacrifice).await {
            Ok(()) => done.push("Runen"),
            Err(problem) => problems.push(problem),
        }
    }

    if let Some([d, f]) = request.spells {
        let body = json!({ "spell1Id": d, "spell2Id": f });
        match lcu.send(Method::PATCH, "/lol-champ-select/v1/session/my-selection", Some(&body)).await {
            Ok(()) => done.push("Zauber"),
            Err(error) => problems.push(format!("Zauber nicht gesetzt ({error}).")),
        }
    }

    outcome(&done, &problems)
}

/// The page itself: own pages out, the agreed foreign one out, the new one in as the current page.
async fn write_page(lcu: &Lcu, page: &PageRequest, delete: Vec<i64>, sacrifice: Option<(i64, String)>) -> Result<(), String> {
    for id in delete {
        lcu.send(Method::DELETE, &format!("/lol-perks/v1/pages/{id}"), None)
            .await
            .map_err(|error| format!("Alte DirkRunen-Seite ließ sich nicht löschen ({error})."))?;
    }
    if let Some((id, name)) = &sacrifice {
        lcu.send(Method::DELETE, &format!("/lol-perks/v1/pages/{id}"), None)
            .await
            .map_err(|error| format!("Seite „{name}“ ließ sich nicht löschen ({error})."))?;
    }
    let body = json!({
        "name": page_name(&page.title),
        "primaryStyleId": page.primary_style_id,
        "subStyleId": page.sub_style_id,
        // Exactly 4 + 2 + 3: a plan with a fifth rune must not push ten perks at the client.
        "selectedPerkIds": page.selected_perk_ids.iter().take(9).collect::<Vec<_>>(),
        "current": true,
    });
    lcu.send(Method::POST, "/lol-perks/v1/pages", Some(&body)).await.map_err(|error| {
        // The one failure that loses something: say so instead of hiding it.
        match &sacrifice {
            Some((_, name)) => format!("Seite „{name}“ wurde entfernt, aber der Client hat die neue Seite abgelehnt ({error})."),
            None => format!("Client hat die Seite abgelehnt ({error})."),
        }
    })
}

/// "Runen und Zauber übernommen.", or what worked and then what did not.
fn outcome(done: &[&str], problems: &[String]) -> ImportOutcome {
    let mut parts = Vec::new();
    if !done.is_empty() {
        parts.push(format!("{} übernommen.", done.join(" und ")));
    }
    parts.extend(problems.iter().cloned());
    ImportOutcome {
        ok: !done.is_empty() && problems.is_empty(),
        message: if parts.is_empty() { "Nichts zu übernehmen.".into() } else { parts.join(" ") },
        confirm: None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn page(id: i64, name: &str, modified: i64) -> Page {
        Page { id, name: name.into(), is_editable: true, is_deletable: Some(true), last_modified: modified }
    }

    #[test]
    fn replaces_own_pages_when_there_is_room() {
        let pages = [page(1, "Meine", 5), page(2, "DirkRunen · Darius vs Jax", 9), page(3, "DraftPilot · alt", 1)];
        assert_eq!(plan(&pages, 3, None), Plan::Go { delete: vec![2, 3], sacrifice: None });
    }

    #[test]
    fn asks_before_a_foreign_page_goes_and_offers_the_oldest() {
        let pages = [page(1, "Neu", 50), page(2, "Alt", 10)];
        assert_eq!(plan(&pages, 2, None), Plan::Ask("Alt".into()));
        assert_eq!(plan(&pages, 2, Some("Neu")), Plan::Ask("Alt".into()));
        assert_eq!(plan(&pages, 2, Some("Alt")), Plan::Go { delete: vec![], sacrifice: Some((2, "Alt".into())) });
    }

    #[test]
    fn refuses_to_delete_more_than_one_foreign_page() {
        let pages = [page(1, "A", 1), page(2, "B", 2), page(3, "C", 3)];
        assert!(matches!(plan(&pages, 1, Some("A")), Plan::Refuse(_)));
        assert!(matches!(plan(&pages, 0, None), Plan::Refuse(_)));
    }

    #[test]
    fn ignores_riots_preset_pages() {
        let mut preset = page(9, "Preset", 0);
        preset.is_editable = false;
        assert_eq!(plan(&[preset, page(1, "Meine", 1)], 2, None), Plan::Go { delete: vec![], sacrifice: None });
    }

    #[test]
    fn reports_what_worked_and_what_did_not() {
        assert_eq!(outcome(&["Runen", "Zauber"], &[]).message, "Runen und Zauber übernommen.");
        let partial = outcome(&["Runen"], &["Zauber nicht gesetzt (404: …).".to_string()]);
        assert_eq!(partial.message, "Runen übernommen. Zauber nicht gesetzt (404: …).");
        assert!(!partial.ok);
        assert_eq!(outcome(&[], &[]).message, "Nichts zu übernehmen.");
    }

    #[test]
    fn names_fit_the_clients_limit() {
        assert_eq!(page_name("Darius vs Jax"), "DirkRunen · Darius vs Jax");
        assert_eq!(page_name("Aurelion Sol vs Kog'Maw (Midlane)").chars().count(), 30);
    }
}
