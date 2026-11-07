// The result of the game just played, from the client's end-of-game screen
// (/lol-end-of-game/v1/eog-stats-block). The block lists every player with name and stats; only
// three facts about the own player leave this file: champion, win, final items.

use serde::Serialize;
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EndOfGame {
    pub champion_id: i64,
    pub win: bool,
    pub items: Vec<i64>,
}

pub fn read(block: &Value) -> Option<EndOfGame> {
    let me = block.get("localPlayer")?;
    let champion_id = me.get("championId")?.as_i64().filter(|&id| id > 0)?;
    let items = me
        .get("items")
        .and_then(Value::as_array)
        .map(|list| list.iter().filter_map(Value::as_i64).filter(|&id| id > 0).collect())
        .unwrap_or_default();

    // The team's flag first; the player's own WIN stat where a client version leaves it out.
    let team_win = block
        .get("teams")
        .and_then(Value::as_array)
        .and_then(|teams| teams.iter().find(|t| t.get("isPlayerTeam").and_then(Value::as_bool) == Some(true)))
        .and_then(|team| team.get("isWinningTeam"))
        .and_then(Value::as_bool);
    let stat_win = me.get("stats").and_then(|s| s.get("WIN")).and_then(Value::as_i64).map(|w| w == 1);
    let win = team_win.or(stat_win)?;

    Some(EndOfGame { champion_id, win, items })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_champion_result_and_items_and_nothing_else() {
        let block: Value = serde_json::from_str(
            r#"{"gameId":1,"localPlayer":{"championId":22,"summonerName":"Ich","items":[3031,0,3006,6672,0,0,3363],"stats":{"WIN":1}},
                "teams":[{"isPlayerTeam":true,"isWinningTeam":false,"players":[{"summonerName":"Fremd"}]}]}"#,
        )
        .unwrap();
        let end = read(&block).unwrap();
        assert_eq!(end, EndOfGame { champion_id: 22, win: false, items: vec![3031, 3006, 6672, 3363] });
        assert!(!serde_json::to_string(&end).unwrap().contains("Fremd"));
    }

    #[test]
    fn falls_back_to_the_win_stat() {
        let block: Value = serde_json::from_str(r#"{"localPlayer":{"championId":22,"stats":{"WIN":1}}}"#).unwrap();
        assert!(read(&block).unwrap().win);
    }

    #[test]
    fn nothing_without_a_result() {
        assert!(read(&serde_json::json!({})).is_none());
        assert!(read(&serde_json::json!({"localPlayer":{"championId":0}})).is_none());
        assert!(read(&serde_json::json!({"localPlayer":{"championId":22}})).is_none());
    }
}
