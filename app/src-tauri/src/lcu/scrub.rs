// Names and credentials never reach the page (fixed rule: no other player's name). The champion
// select session loses its chat token and every player identity (port of SessionScrubber.cs); the
// game's own API gets each name replaced by a stable "Spieler N" (port of LiveGameScrubber.cs), so
// the page can still find its own row — the active player and their row get the same pseudonym.

use std::collections::HashMap;

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

/// The session without chat token and identities, or None when it is not a JSON object.
pub fn session(json: &str) -> Option<String> {
    let mut value: Value = serde_json::from_str(json).ok()?;
    let session = value.as_object_mut()?;
    for field in SESSION_FIELDS {
        session.remove(field);
    }
    for team in ["myTeam", "theirTeam"] {
        if let Some(Value::Array(players)) = session.get_mut(team) {
            for player in players.iter_mut().filter_map(Value::as_object_mut) {
                for field in PLAYER_FIELDS {
                    player.remove(field);
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
        let clean: Value = serde_json::from_str(&session(raw).unwrap()).unwrap();
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
