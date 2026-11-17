// The one write path, and only on the user's click: the rune page (port of RuneImporter.cs) and,
// since 07.11.2026 on the user's wish, the two summoner spells. The page decides nothing about slots
// for the spells — the TS core (core/loadout.ts) already put Flash on the player's key.
//
// Rune pages follow 1.5's rule: DirkDraft touches only pages it created itself (prefix DirkRunen,
// or DraftPilot from before the rename). When the account's page limit forces replacing a foreign
// page, nothing is written: the answer names the page and the page asks the user; only a second
// call that names the same page as agreed goes ahead. Everything is decided before the first write.
//
// Since 17.11.2026 a page is written over in place (PUT /lol-perks/v1/pages/{id}) instead of deleted
// and created anew: between the DELETE and the POST the page was gone, and a POST the client then
// refused left the user without it. 1.x only ever used POST and DELETE, so PUT is the untried part:
// a client that answers it with a refusal gets the old way (delete, create) as before.

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

/// The page the new one is written over.
#[derive(Debug, Clone, PartialEq)]
pub struct Overwrite {
    pub id: i64,
    /// The name of a foreign page the user agreed to lose; None for one of our own.
    pub foreign: Option<String>,
}

/// What has to happen to make room, decided before anything is written.
#[derive(Debug, PartialEq)]
pub enum Plan {
    /// Write the page: over `overwrite` (one of our own, or the agreed foreign one), or as a new
    /// page where a slot is free. `delete`: our other pages, removed once the new one is written.
    Go { overwrite: Option<Overwrite>, delete: Vec<i64> },
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
        // One of our own pages takes the new runes; any further ones are leftovers.
        let overwrite = ours.first().map(|&id| Overwrite { id, foreign: None });
        return Plan::Go { overwrite, delete: ours.into_iter().skip(1).collect() };
    }

    // Offer the oldest foreign page: the one whose loss is most likely already priced in.
    let Some(oldest) = editable.iter().filter(|p| !is_ours(&p.name) && p.can_delete()).min_by_key(|p| p.last_modified) else {
        return Plan::Refuse("Alle Seiten belegt und keine löschbar — bitte im Client Platz schaffen.");
    };
    if agreed != Some(oldest.name.as_str()) {
        return Plan::Ask(oldest.name.clone());
    }
    Plan::Go { overwrite: Some(Overwrite { id: oldest.id, foreign: Some(oldest.name.clone()) }), delete: ours }
}

/// The writes the import needs. The client answers them; the tests answer from a script.
trait Writes {
    async fn send(&self, method: Method, path: &str, body: Option<&serde_json::Value>) -> Result<(), String>;
}

impl Writes for Lcu {
    async fn send(&self, method: Method, path: &str, body: Option<&serde_json::Value>) -> Result<(), String> {
        Lcu::send(self, method, path, body).await
    }
}

/// The HTTP status at the head of a write's error ("405: …", http.rs), or None when nothing came
/// back at all (client gone, timeout).
fn refused_with(error: &str) -> Option<u16> {
    let (status, _) = error.split_once(':')?;
    status.trim().parse().ok().filter(|code| (100..600).contains(code))
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
                    Plan::Go { overwrite, delete } => rune_work = Some((page, overwrite, delete)),
                },
                _ => problems.push("Client hat die Runenseiten nicht verraten — läuft er noch?".into()),
            }
        }
    }

    if let Some((page, overwrite, delete)) = rune_work {
        match write_page(lcu, page, overwrite, delete).await {
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

    let result = outcome(&done, &problems);
    // The message names no player; a foreign page's name is not in it either (that one only in the question).
    log::info!("Übernehmen: {}", result.message);
    result
}

/// The page itself, as the current page. Over an existing page it is written in place, so there is
/// no moment without it; a client that refuses that gets the old way: delete it, create anew.
async fn write_page<W: Writes>(lcu: &W, page: &PageRequest, overwrite: Option<Overwrite>, delete: Vec<i64>) -> Result<(), String> {
    let body = json!({
        "name": page_name(&page.title),
        "primaryStyleId": page.primary_style_id,
        "subStyleId": page.sub_style_id,
        // Exactly 4 + 2 + 3: a plan with a fifth rune must not push ten perks at the client.
        "selectedPerkIds": page.selected_perk_ids.iter().take(9).collect::<Vec<_>>(),
        "current": true,
    });

    match &overwrite {
        // A free slot: nothing has to go.
        None => lcu.send(Method::POST, "/lol-perks/v1/pages", Some(&body)).await.map_err(|error| format!("Client hat die Seite abgelehnt ({error})."))?,
        Some(target) => match lcu.send(Method::PUT, &format!("/lol-perks/v1/pages/{}", target.id), Some(&body)).await {
            Ok(()) => {
                // "current" in the body may be ignored on an existing page: say it outright. The runes
                // are written either way, so a refusal here costs a click in the client, not the import.
                if let Err(error) = lcu.send(Method::PUT, "/lol-perks/v1/currentpage", Some(&json!(target.id))).await {
                    log::warn!("Runen: Seite geschrieben, aber nicht als aktuelle gesetzt ({error})");
                }
            }
            // The client answered and said no (PUT is untried against the real client): the way 1.x went.
            Err(error) if refused_with(&error).is_some() => {
                log::warn!("Runen: Überschreiben abgelehnt ({error}) — alter Weg: löschen und neu anlegen");
                replace_page(lcu, target, &body).await?;
            }
            // Nothing came back: nothing was written and nothing is lost.
            Err(error) => return Err(format!("Client hat die Seite nicht angenommen ({error}).")),
        },
    }

    // Our leftovers go last: the new page is safe by now, and a failure here loses nothing.
    for id in delete {
        if let Err(error) = lcu.send(Method::DELETE, &format!("/lol-perks/v1/pages/{id}"), None).await {
            log::warn!("Runen: alte DirkRunen-Seite ließ sich nicht löschen ({error})");
        }
    }
    Ok(())
}

/// The old way, for a client that refuses to write over a page: the page out, the new one in.
async fn replace_page<W: Writes>(lcu: &W, target: &Overwrite, body: &serde_json::Value) -> Result<(), String> {
    lcu.send(Method::DELETE, &format!("/lol-perks/v1/pages/{}", target.id), None).await.map_err(|error| match &target.foreign {
        Some(name) => format!("Seite „{name}“ ließ sich nicht löschen ({error})."),
        None => format!("Alte DirkRunen-Seite ließ sich nicht löschen ({error})."),
    })?;
    lcu.send(Method::POST, "/lol-perks/v1/pages", Some(body)).await.map_err(|error| {
        // The one failure that loses something: say so instead of hiding it.
        match &target.foreign {
            Some(name) => format!("Seite „{name}“ wurde entfernt, aber der Client hat die neue Seite abgelehnt ({error})."),
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
        // The first of our pages takes the new runes, the other is a leftover.
        assert_eq!(plan(&pages, 3, None), Plan::Go { overwrite: Some(Overwrite { id: 2, foreign: None }), delete: vec![3] });
    }

    #[test]
    fn asks_before_a_foreign_page_goes_and_offers_the_oldest() {
        let pages = [page(1, "Neu", 50), page(2, "Alt", 10)];
        assert_eq!(plan(&pages, 2, None), Plan::Ask("Alt".into()));
        assert_eq!(plan(&pages, 2, Some("Neu")), Plan::Ask("Alt".into()));
        assert_eq!(plan(&pages, 2, Some("Alt")), Plan::Go { overwrite: Some(Overwrite { id: 2, foreign: Some("Alt".into()) }), delete: vec![] });
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
        // A free slot and no page of ours: a new page, nothing written over.
        assert_eq!(plan(&[preset, page(1, "Meine", 1)], 2, None), Plan::Go { overwrite: None, delete: vec![] });
    }

    /// A client that answers from a script and writes down what it was asked.
    struct Script<F: Fn(&Method, &str) -> Result<(), String>> {
        answer: F,
        calls: std::sync::Mutex<Vec<String>>,
        bodies: std::sync::Mutex<Vec<serde_json::Value>>,
    }

    impl<F: Fn(&Method, &str) -> Result<(), String>> Script<F> {
        fn new(answer: F) -> Self {
            Self { answer, calls: Default::default(), bodies: Default::default() }
        }

        fn calls(&self) -> Vec<String> {
            self.calls.lock().unwrap().clone()
        }
    }

    impl<F: Fn(&Method, &str) -> Result<(), String>> Writes for Script<F> {
        async fn send(&self, method: Method, path: &str, body: Option<&serde_json::Value>) -> Result<(), String> {
            self.calls.lock().unwrap().push(format!("{method} {path}"));
            if let Some(body) = body {
                self.bodies.lock().unwrap().push(body.clone());
            }
            (self.answer)(&method, path)
        }
    }

    fn runes() -> PageRequest {
        PageRequest { title: "Darius vs Jax".into(), primary_style_id: 8000, sub_style_id: 8400, selected_perk_ids: (1..=10).collect() }
    }

    const OURS: Overwrite = Overwrite { id: 7, foreign: None };

    fn foreign() -> Overwrite {
        Overwrite { id: 4, foreign: Some("Alt".into()) }
    }

    #[tokio::test]
    async fn writes_over_an_own_page_in_place_and_makes_it_the_current_one() {
        let client = Script::new(|_, _| Ok(()));
        assert_eq!(write_page(&client, &runes(), Some(OURS), vec![9]).await, Ok(()));
        // No delete before the write: the page is never gone. The leftover goes last.
        assert_eq!(client.calls(), ["PUT /lol-perks/v1/pages/7", "PUT /lol-perks/v1/currentpage", "DELETE /lol-perks/v1/pages/9"]);
        let bodies = client.bodies.lock().unwrap();
        assert_eq!(bodies[0]["name"], "DirkRunen · Darius vs Jax");
        assert_eq!(bodies[0]["selectedPerkIds"].as_array().unwrap().len(), 9);
        assert_eq!(bodies[0]["current"], true);
        assert_eq!(bodies[1], json!(7));
    }

    #[tokio::test]
    async fn writes_over_the_agreed_foreign_page_without_deleting_it_first() {
        let client = Script::new(|_, _| Ok(()));
        assert_eq!(write_page(&client, &runes(), Some(foreign()), vec![]).await, Ok(()));
        assert_eq!(client.calls(), ["PUT /lol-perks/v1/pages/4", "PUT /lol-perks/v1/currentpage"]);
    }

    #[tokio::test]
    async fn a_free_slot_gets_a_new_page() {
        let client = Script::new(|_, _| Ok(()));
        assert_eq!(write_page(&client, &runes(), None, vec![]).await, Ok(()));
        assert_eq!(client.calls(), ["POST /lol-perks/v1/pages"]);
    }

    #[tokio::test]
    async fn a_client_that_refuses_put_gets_the_old_way() {
        let client = Script::new(|method, _| if *method == Method::PUT { Err("405: Method Not Allowed".into()) } else { Ok(()) });
        assert_eq!(write_page(&client, &runes(), Some(OURS), vec![9]).await, Ok(()));
        assert_eq!(client.calls(), ["PUT /lol-perks/v1/pages/7", "DELETE /lol-perks/v1/pages/7", "POST /lol-perks/v1/pages", "DELETE /lol-perks/v1/pages/9"]);
    }

    #[tokio::test]
    async fn the_old_way_says_what_was_lost_when_the_client_refuses_the_new_page_too() {
        let client = Script::new(|method, _| if *method == Method::DELETE { Ok(()) } else { Err("400: Invalid page".into()) });
        let problem = write_page(&client, &runes(), Some(foreign()), vec![]).await.unwrap_err();
        assert_eq!(problem, "Seite „Alt“ wurde entfernt, aber der Client hat die neue Seite abgelehnt (400: Invalid page).");
    }

    #[tokio::test]
    async fn no_answer_to_the_put_deletes_nothing() {
        // The client is gone: no status, so no "refused" — and nothing may be deleted on a guess.
        let client = Script::new(|_, _| Err("error sending request for url (https://127.0.0.1:50123/…)".into()));
        let problem = write_page(&client, &runes(), Some(foreign()), vec![9]).await.unwrap_err();
        assert!(problem.starts_with("Client hat die Seite nicht angenommen"));
        assert_eq!(client.calls(), ["PUT /lol-perks/v1/pages/4"]);
    }

    #[tokio::test]
    async fn a_page_that_is_written_but_not_made_current_is_still_an_import() {
        let client = Script::new(|_, path| if path.ends_with("currentpage") { Err("404: not found".into()) } else { Ok(()) });
        assert_eq!(write_page(&client, &runes(), Some(OURS), vec![]).await, Ok(()));
    }

    #[test]
    fn tells_a_refusal_from_no_answer() {
        assert_eq!(refused_with("405: Method Not Allowed"), Some(405));
        assert_eq!(refused_with("400: {\"message\":\"Max pages reached\"}"), Some(400));
        assert_eq!(refused_with("error sending request for url (https://127.0.0.1:50123/x): connection refused"), None);
        assert_eq!(refused_with("operation timed out"), None);
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
