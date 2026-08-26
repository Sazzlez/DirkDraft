using System.Text.Json.Serialization;

namespace DraftPilot.Core.Lcu.Models;

/// <summary>
/// The subset of <c>/lol-champ-select/v1/session</c> we actually use. Everything is optional and
/// defaulted: Riot adds and removes fields between patches, and a missing field must degrade the
/// display rather than break the parse.
/// </summary>
public sealed class ChampSelectSession
{
    public long LocalPlayerCellId { get; set; } = -1;

    public List<ChampSelectPlayer> MyTeam { get; set; } = [];

    public List<ChampSelectPlayer> TheirTeam { get; set; } = [];

    /// <summary>Outer list is the action group (ban round, pick round, …), inner one the simultaneous actions.</summary>
    public List<List<ChampSelectAction>> Actions { get; set; } = [];

    public ChampSelectBans Bans { get; set; } = new();

    public ChampSelectTimer Timer { get; set; } = new();

    public bool IsSpectating { get; set; }
}

public sealed class ChampSelectPlayer
{
    public long CellId { get; set; }

    /// <summary>0 until the pick is locked in. For enemies in ranked this stays 0 until reveal.</summary>
    public int ChampionId { get; set; }

    /// <summary>The champion currently hovered. Only populated for your own team.</summary>
    public int ChampionPickIntent { get; set; }

    /// <summary>One of <c>top</c>, <c>jungle</c>, <c>middle</c>, <c>bottom</c>, <c>utility</c> — or empty.</summary>
    public string? AssignedPosition { get; set; }

    public int Team { get; set; }
}

public sealed class ChampSelectAction
{
    public long Id { get; set; }

    public long ActorCellId { get; set; }

    public int ChampionId { get; set; }

    /// <summary><c>pick</c>, <c>ban</c> or <c>ten_bans_reveal</c>.</summary>
    public string? Type { get; set; }

    public bool Completed { get; set; }

    public bool IsAllyAction { get; set; }

    public bool IsInProgress { get; set; }
}

public sealed class ChampSelectBans
{
    public List<int> MyTeamBans { get; set; } = [];

    public List<int> TheirTeamBans { get; set; } = [];

    public int NumBans { get; set; }
}

public sealed class ChampSelectTimer
{
    /// <summary><c>PLANNING</c>, <c>BAN_PICK</c>, <c>FINALIZATION</c>, …</summary>
    public string? Phase { get; set; }

    public long AdjustedTimeLeftInPhase { get; set; }

    public long TotalTimeInPhase { get; set; }

    /// <summary>
    /// The client's own clock when it built this snapshot, in Unix milliseconds.
    /// <para>
    /// Needed to correct the countdown: <see cref="AdjustedTimeLeftInPhase"/> was already stale by
    /// the time the payload arrived — transport, debounce and parsing all elapse first. Treating it
    /// as current makes the displayed clock run ahead of the one in the client.
    /// </para>
    /// </summary>
    public long InternalNowInEpochMs { get; set; }

    public bool IsInfinite { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ChampSelectSession))]
[JsonSerializable(typeof(List<int>))]
public sealed partial class LcuJson : JsonSerializerContext;
