// Names and credentials never reach the page (fixed rule: no other player's name). The champion
// select session loses its chat token and every player identity (port of SessionScrubber.cs); the
// game's own API gets each name replaced by a stable "Spieler N" (port of LiveGameScrubber.cs), so
// the page can still find its own row — the active player and their row get the same pseudonym.
//
// One thing is read from the identities before they go: which teammates queued together with the
// local player. The lobby he queued from lists his party; a teammate whose identity is in that list
// keeps a bare `inParty: true`. No name and no id of anyone reaches the page for it.
//
// One thing is added: the map the draft is for (`mapId`), read from the gameflow session — whose
// player lists stay here; only that one number leaves this file.

use std::collections::{HashMap, HashSet};

use serde_json::Value;

const SESSION_FIELDS: [&str; 2] = ["chatDetails", "gameId"];
const PLAYER_FIELDS: [&str; 9] = [
    "gameName",
    "tagLine",
    "puuid",
    "obfuscatedPuuid",
    "summonerId",
    "obfuscatedSummonerId",
    "playerAlias",
    "internalName",
    "nameVisibilityType",
];

/// The local player's party as the lobby lists it (himself included): kept in memory for one
/// draft, compared with the session, never passed on.
#[derive(Debug, Default, Clone, PartialEq, Eq)]
pub struct Party {
    puuids: HashSet<String>,
    summoners: HashSet<i64>,
}

impl Party {
    /// From the answer of /lol-lobby/v2/lobby; empty for anything that is no lobby.
    pub fn from_lobby(json: &str) -> Self {
        let mut party = Self::default();
        let Ok(value) = serde_json::from_str::<Value>(json) else { return party };
        for member in value.get("members").and_then(Value::as_array).into_iter().flatten() {
            if let Some(puuid) = member.get("puuid").and_then(Value::as_str).filter(|p| !p.is_empty()) {
                party.puuids.insert(puuid.to_string());
            }
            if let Some(id) = member.get("summonerId").and_then(Value::as_i64).filter(|id| *id > 0) {
                party.summoners.insert(id);
            }
        }
        party
    }

    fn has(&self, player: &serde_json::Map<String, Value>) -> bool {
        player.get("puuid").and_then(Value::as_str).is_some_and(|p| !p.is_empty() && self.puuids.contains(p))
            || player.get("summonerId").and_then(Value::as_i64).is_some_and(|id| id > 0 && self.summoners.contains(&id))
    }
}

/// The map of the game being set up, out of /lol-gameflow/v1/session: 11 is Summoner's Rift, 12 the
/// Howling Abyss. None for anything that names no map. Nothing else of that answer is kept — it
/// lists the players.
pub fn map_id(gameflow_session: &str) -> Option<i64> {
    let value: Value = serde_json::from_str(gameflow_session).ok()?;
    let id = |v: Option<&Value>| v.and_then(Value::as_i64).filter(|id| *id > 0);
    id(value.get("map").and_then(|map| map.get("id")))
        .or_else(|| id(value.get("gameData").and_then(|game| game.get("queue")).and_then(|queue| queue.get("mapId"))))
}

/// The session without chat token and identities, or None when it is not a JSON object. The
/// teammates found in `party` keep `inParty: true` — never the local player's own seat, never an
/// enemy. `map` goes in as `mapId` where the client named one.
pub fn session(json: &str, party: &Party, map: Option<i64>) -> Option<String> {
    let mut value: Value = serde_json::from_str(json).ok()?;
    let session = value.as_object_mut()?;
    for field in SESSION_FIELDS {
        session.remove(field);
    }
    // Only what this file read itself says which map it is.
    session.remove("mapId");
    if let Some(map) = map {
        session.insert("mapId".to_string(), Value::from(map));
    }
    let local = session.get("localPlayerCellId").and_then(Value::as_i64);
    for team in ["myTeam", "theirTeam"] {
        if let Some(Value::Array(players)) = session.get_mut(team) {
            for player in players.iter_mut().filter_map(Value::as_object_mut) {
                let mate = team == "myTeam" && local.is_some() && player.get("cellId").and_then(Value::as_i64) != local && party.has(player);
                for field in PLAYER_FIELDS {
                    player.remove(field);
                }
                // Only this file says who is in the party.
                player.remove("inParty");
                if mate {
                    player.insert("inParty".to_string(), Value::Bool(true));
                }
            }
        }
    }
    Some(value.to_string())
}

/// Properties whose value is a person, wherever they sit: player list, active player, event feed.
const NAME_FIELDS: [&str; 9] = ["summonername", "riotid", "riotidgamename", "riotidtagline", "killername", "victimname", "assisters", "acer", "recipient"];
const MAX_DEPTH: usize = 64;

/// Replaces names in the game's payloads; one instance per game keeps the pseudonyms stable.
#[derive(Default)]
pub struct LiveScrubber {
    pseudonyms: HashMap<String, String>,
}

impl LiveScrubber {
    pub fn scrub(&mut self, json: &str) -> Option<String> {
        let mut value: Value = serde_json::from_str(json).ok()?;
        if let Value::String(text) = &value {
            // A bare string answer (activeplayername) IS a name.
            value = Value::String(self.pseudonym(text));
        } else {
            self.walk(&mut value, 0);
        }
        Some(value.to_string())
    }

    fn walk(&mut self, value: &mut Value, depth: usize) {
        if depth > MAX_DEPTH {
            return;
        }
        match value {
            Value::Object(map) => {
                for (key, child) in map.iter_mut() {
                    if NAME_FIELDS.contains(&key.to_lowercase().as_str()) {
                        self.redact(child);
                    } else {
                        self.walk(child, depth + 1);
                    }
                }
            }
            Value::Array(items) => items.iter_mut().for_each(|item| self.walk(item, depth + 1)),
            _ => {}
        }
    }

    fn redact(&mut self, value: &mut Value) {
        match value {
            Value::String(text) => *text = self.pseudonym(text),
            Value::Array(items) => {
                for item in items.iter_mut() {
                    if let Value::String(text) = item {
                        *text = self.pseudonym(text);
                    }
                }
            }
            _ => {}
        }
    }

    /// An empty name stays empty: blank is not personal, and a pseudonym would hide that it was blank.
    fn pseudonym(&mut self, value: &str) -> String {
        if value.is_empty() {
            return String::new();
        }
        let next = self.pseudonyms.len() + 1;
        self.pseudonyms.entry(value.to_string()).or_insert_with(|| format!("Spieler {next}")).clone()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn session_loses_token_and_identities_but_keeps_the_draft() {
        let raw = r#"{"chatDetails":{"token":"jwt"},"gameId":7,"localPlayerCellId":2,
            "myTeam":[{"cellId":2,"championId":122,"gameName":"Ich","tagLine":"EUW","puuid":"p","summonerId":1,"spell1Id":4}],
            "theirTeam":[{"cellId":5,"championId":24,"gameName":"Fremd","puuid":"q"}]}"#;
        let clean: Value = serde_json::from_str(&session(raw, &Party::default(), None).unwrap()).unwrap();
        assert!(clean.get("chatDetails").is_none());
        assert!(clean.get("gameId").is_none());
        assert_eq!(clean["localPlayerCellId"], 2);
        let me = &clean["myTeam"][0];
        assert_eq!(me["championId"], 122);
        assert_eq!(me["spell1Id"], 4);
        assert!(me.get("gameName").is_none() && me.get("puuid").is_none() && me.get("summonerId").is_none());
        assert!(clean["theirTeam"][0].get("gameName").is_none());
        assert!(!clean.to_string().contains("Fremd"));
    }

    #[test]
    fn the_map_comes_from_the_gameflow_session_and_nothing_else_does() {
        let gameflow = r#"{"gameData":{"queue":{"id":2400,"mapId":12},"teamOne":[{"summonerName":"Fremd","puuid":"q"}]},"map":{"id":12,"name":"Howling Abyss"},"phase":"ChampSelect"}"#;
        assert_eq!(map_id(gameflow), Some(12));
        assert_eq!(map_id(r#"{"gameData":{"queue":{"mapId":11}}}"#), Some(11));
        for none in ["", "null", r#"{"httpStatus":404}"#, r#"{"map":{"id":0}}"#, r#"{"map":{"id":"elf"}}"#] {
            assert_eq!(map_id(none), None, "{none}");
        }

        let raw = r#"{"localPlayerCellId":0,"mapId":99,"myTeam":[{"cellId":0,"championId":1}],"theirTeam":[]}"#;
        let with: Value = serde_json::from_str(&session(raw, &Party::default(), Some(12)).unwrap()).unwrap();
        assert_eq!(with["mapId"], 12);
        // Not read: no map at all, whatever the session itself claims.
        let without: Value = serde_json::from_str(&session(raw, &Party::default(), None).unwrap()).unwrap();
        assert!(without.get("mapId").is_none());
    }

    const DUO: &str = r#"{"localPlayerCellId":1,
        "myTeam":[{"cellId":0,"championId":1,"puuid":"random-a","summonerId":10,"gameName":"A","nameVisibilityType":"HIDDEN"},
                  {"cellId":1,"championId":2,"puuid":"me","summonerId":11,"gameName":"Ich","nameVisibilityType":"UNHIDDEN"},
                  {"cellId":2,"championId":3,"puuid":"duo","summonerId":12,"gameName":"Duo","nameVisibilityType":"UNHIDDEN"},
                  {"cellId":3,"championId":4,"puuid":"","summonerId":0,"gameName":"","inParty":true}],
        "theirTeam":[{"cellId":5,"championId":24,"puuid":"duo","summonerId":12}]}"#;

    #[test]
    fn the_party_becomes_a_bare_flag_on_the_teammates_from_the_lobby() {
        let lobby = r#"{"members":[{"puuid":"me","summonerId":11,"summonerName":"Ich"},{"puuid":"duo","summonerId":12,"summonerName":"Duo"}]}"#;
        let text = session(DUO, &Party::from_lobby(lobby), None).unwrap();
        let clean: Value = serde_json::from_str(&text).unwrap();
        let flags: Vec<bool> = clean["myTeam"].as_array().unwrap().iter().map(|p| p["inParty"] == true).collect();
        // The duo partner only: not the own seat, not a stranger, and not a flag that came from outside.
        assert_eq!(flags, [false, false, true, false]);
        assert!(clean["myTeam"][0].get("inParty").is_none() && clean["myTeam"][3].get("inParty").is_none());
        // An enemy never, whatever his identity says.
        assert!(clean["theirTeam"][0].get("inParty").is_none());
        // And nothing of anyone's identity is left.
        for word in ["random-a", "\"me\"", "\"duo\"", "Duo", "Ich", "HIDDEN", "puuid", "summonerId"] {
            assert!(!text.contains(word), "{word} left in {text}");
        }
    }

    #[test]
    fn the_party_matches_by_summoner_id_where_the_puuid_is_missing() {
        let lobby = r#"{"members":[{"summonerId":11},{"summonerId":12}]}"#;
        let clean: Value = serde_json::from_str(&session(DUO, &Party::from_lobby(lobby), None).unwrap()).unwrap();
        assert_eq!(clean["myTeam"][2]["inParty"], true);
        assert!(clean["myTeam"][0].get("inParty").is_none());
    }

    #[test]
    fn alone_or_without_a_lobby_nobody_is_in_the_party() {
        for lobby in [r#"{"members":[{"puuid":"me","summonerId":11}]}"#, "", "null", r#"{"httpStatus":404}"#, r#"{"members":[{"puuid":"","summonerId":0}]}"#] {
            let clean: Value = serde_json::from_str(&session(DUO, &Party::from_lobby(lobby), None).unwrap()).unwrap();
            assert!(clean["myTeam"].as_array().unwrap().iter().all(|p| p.get("inParty").is_none()), "{lobby}");
        }
    }

    #[test]
    fn live_names_become_stable_pseudonyms_everywhere() {
        let raw = r#"{"activePlayer":{"riotId":"Ich#EUW","riotIdGameName":"Ich"},
            "allPlayers":[{"riotId":"Ich#EUW","championName":"Ashe"},{"riotId":"Fremd#1","championName":"Jinx"}],
            "events":{"Events":[{"KillerName":"Fremd#1","VictimName":"Ich#EUW","Assisters":["Ich#EUW",""]}]}}"#;
        let mut scrubber = LiveScrubber::default();
        let clean: Value = serde_json::from_str(&scrubber.scrub(raw).unwrap()).unwrap();
        let me = clean["activePlayer"]["riotId"].as_str().unwrap().to_string();
        assert_eq!(clean["allPlayers"][0]["riotId"], me.as_str());
        assert_eq!(clean["events"]["Events"][0]["VictimName"], me.as_str());
        assert_eq!(clean["events"]["Events"][0]["Assisters"][1], "");
        assert_eq!(clean["allPlayers"][1]["championName"], "Jinx");
        let text = clean.to_string();
        assert!(!text.contains("Fremd") && !text.contains("Ich#EUW"));
    }

    #[test]
    fn a_bare_string_answer_is_a_name() {
        let mut scrubber = LiveScrubber::default();
        assert_eq!(scrubber.scrub("\"Ich#EUW\"").unwrap(), "\"Spieler 1\"");
    }
}
