// The League client: found through its lockfile, spoken to over TLS pinned to Riot's authority.
// Reads, a pushed event stream, and one write path (runes and spells, on click).

pub mod credentials;
pub mod end_of_game;
pub mod http;
pub mod import;
pub mod install;
pub mod live;
pub mod scrub;
pub mod socket;
pub mod source;
pub mod tls;
