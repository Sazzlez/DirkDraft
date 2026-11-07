// What "Mein Pool" is made of (core/pool.ts): the player's champion mastery and their recent games,
// both from their own client. Only champion, points, lane and queue leave this file — the match
// list also carries names and ids of everybody in those games.

use serde::Serialize;
use serde_json::Value;

use super::http::Lcu;

const MASTERY: &str = "/lol-champion-mastery/v1/local-player/champion-mastery";
const MATCHES: &str = "/lol-match-history/v1/products/lol/current-summoner/matches?begIndex=0&endIndex=50";
const SUMMONER: &str = "/lol-summoner/v1/current-summoner";

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Mastery {
    pub champion_id: i64,
    pub points: i64,
}

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayedGame {
    pub champion_id: i64,
    /// top, jungle, mid, adc, support, or "" where the game has no lane.
    pub lane: String,
    pub queue_id: i64,
}

#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PoolInput {
    pub masteries: Vec<Mastery>,
    pub games: Vec<PlayedGame>,
}

pub async fn read(lcu: &Lcu) -> PoolInput {
    let mastery: Option<Value> = lcu.get_json(MASTERY).await;
    let matches: Option<Value> = lcu.get_json(MATCHES).await;
    let puuid = lcu
        .get_json::<Value>(SUMMONER)
        .await
        .and_then(|s| s.get("puuid").and_then(Value::as_str).map(str::to_string));
    PoolInput {
        masteries: mastery.as_ref().map(masteries).unwrap_or_default(),
        games: matches.as_ref().map(|m| games(m, puuid.as_deref())).unwrap_or_default(),
    }
}

pub fn masteries(list: &Value) -> Vec<Mastery> {
    list.as_array()
        .map(|entries| {
            entries
                .iter()
                .filter_map(|e| {
                    let champion_id = e.get("championId")?.as_i64().filter(|&id| id > 0)?;
                    let points = e.get("championPoints")?.as_i64()?;
                    Some(Mastery { champion_id, points })
                })
                .collect()
        })
        .unwrap_or_default()
}

/// The own row of each game. The list for the current summoner usually holds only that row; where
/// it holds more, the PUUID picks it (and is dropped right here).
pub fn games(history: &Value, puuid: Option<&str>) -> Vec<PlayedGame> {
    let Some(list) = history.pointer("/games/games").and_then(Value::as_array) else {
        return Vec::new();
    };
    list.iter()
        .filter_map(|game| {
            let participants = game.get("participants")?.as_array()?;
            let own = if participants.len() == 1 {
                participants.first()?
            } else {
                let id = game
                    .get("participantIdentities")?
                    .as_array()?
                    .iter()
                    .find(|i| puuid.is_some() && i.pointer("/player/puuid").and_then(Value::as_str) == puuid)?
                    .get("participantId")?
                    .as_i64()?;
                participants.iter().find(|p| p.get("participantId").and_then(Value::as_i64) == Some(id))?
            };
            let champion_id = own.get("championId")?.as_i64().filter(|&id| id > 0)?;
            let lane = lane_of(
                own.pointer("/timeline/lane").and_then(Value::as_str).unwrap_or(""),
                own.pointer("/timeline/role").and_then(Value::as_str).unwrap_or(""),
            );
            let queue_id = game.get("queueId").and_then(Value::as_i64).unwrap_or(0);
            Some(PlayedGame { champion_id, lane: lane.to_string(), queue_id })
        })
        .collect()
}

/// The match list names lanes the old way: BOTTOM plus a role that tells carry from support.
pub fn lane_of(lane: &str, role: &str) -> &'static str {
    match lane.to_ascii_uppercase().as_str() {
        "TOP" => "top",
        "JUNGLE" => "jungle",
        "MIDDLE" | "MID" => "mid",
        "BOTTOM" | "BOT" if role.to_ascii_uppercase().contains("SUPPORT") => "support",
        "BOTTOM" | "BOT" => "adc",
        _ => "",
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn reads_mastery_points() {
        let list = json!([{"championId": 122, "championPoints": 120000, "puuid": "x"}, {"championId": 0, "championPoints": 5}]);
        assert_eq!(masteries(&list), vec![Mastery { champion_id: 122, points: 120000 }]);
    }

    #[test]
    fn reads_the_own_row_of_each_game() {
        let history = json!({"games": {"games": [
            {"queueId": 420, "participants": [{"participantId": 1, "championId": 122, "timeline": {"lane": "TOP", "role": "SOLO"}}],
             "participantIdentities": [{"participantId": 1, "player": {"summonerName": "Ich"}}]},
            {"queueId": 420, "participants": [
                {"participantId": 1, "championId": 22, "timeline": {"lane": "BOTTOM", "role": "DUO_CARRY"}},
                {"participantId": 2, "championId": 412, "timeline": {"lane": "BOTTOM", "role": "DUO_SUPPORT"}}],
             "participantIdentities": [
                {"participantId": 1, "player": {"puuid": "fremd", "summonerName": "Fremd"}},
                {"participantId": 2, "player": {"puuid": "ich", "summonerName": "Ich"}}]},
            {"queueId": 2400, "participants": [{"participantId": 1, "championId": 99, "timeline": {"lane": "NONE", "role": "NONE"}}]}
        ]}});
        let played = games(&history, Some("ich"));
        assert_eq!(
            played,
            vec![
                PlayedGame { champion_id: 122, lane: "top".into(), queue_id: 420 },
                PlayedGame { champion_id: 412, lane: "support".into(), queue_id: 420 },
                PlayedGame { champion_id: 99, lane: "".into(), queue_id: 2400 },
            ]
        );
        assert!(!serde_json::to_string(&played).unwrap().contains("Fremd"));
    }

    #[test]
    fn maps_the_old_lane_names() {
        assert_eq!(lane_of("MIDDLE", "SOLO"), "mid");
        assert_eq!(lane_of("BOTTOM", "CARRY"), "adc");
        assert_eq!(lane_of("BOTTOM", "SUPPORT"), "support");
        assert_eq!(lane_of("NONE", ""), "");
    }
}
