using System.Text.Json.Serialization;

namespace DraftPilot.Core.Lcu.Models;

/// <summary>
/// The subset of <c>/lol-champ-select/v1/session</c> we actually use. Everything is optional and
/// defaulted: Riot adds and removes fields between patches, and a missing field must degrade the
/// display rather than break the parse. The setters coalesce <see langword="null"/> because the
/// LCU sends explicit nulls (that is why the scrubber exists), and System.Text.Json writes those
/// straight over any initializer default — the initializers alone protect nothing.
/// </summary>
public sealed class ChampSelectSession
{
    private List<ChampSelectPlayer> _myTeam = [];
    private List<ChampSelectPlayer> _theirTeam = [];
    private List<List<ChampSelectAction>> _actions = [];
    private ChampSelectBans _bans = new();
    private ChampSelectTimer _timer = new();

    public long LocalPlayerCellId { get; set; } = -1;

    /// <summary>
    /// Riot's queue id, present in the first frame of every session. Zero in a custom lobby and in
    /// anything this field is missing from.
    /// </summary>
    public int QueueId { get; set; }

    public bool IsCustomGame { get; set; }

    public List<ChampSelectPlayer> MyTeam
    {
        get => _myTeam;
        set => _myTeam = value ?? [];
    }

    public List<ChampSelectPlayer> TheirTeam
    {
        get => _theirTeam;
        set => _theirTeam = value ?? [];
    }

    /// <summary>Outer list is the action group (ban round, pick round, …), inner one the simultaneous actions.</summary>
    public List<List<ChampSelectAction>> Actions
    {
        get => _actions;
        set => _actions = value ?? [];
    }

    public ChampSelectBans Bans
    {
        get => _bans;
        set => _bans = value ?? new();
    }

    public ChampSelectTimer Timer
    {
        get => _timer;
        set => _timer = value ?? new();
    }

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
    private List<int> _myTeamBans = [];
    private List<int> _theirTeamBans = [];

    public List<int> MyTeamBans
    {
        get => _myTeamBans;
        set => _myTeamBans = value ?? [];
    }

    public List<int> TheirTeamBans
    {
        get => _theirTeamBans;
        set => _theirTeamBans = value ?? [];
    }

    public int NumBans { get; set; }
}

/// <summary>
/// Only the phase name is read. The payload also carries the remaining and total milliseconds,
/// deliberately ignored: the client pushes them only when something else changes, so a display fed
/// from them either freezes or has to be extrapolated locally — and a second clock that disagrees
/// with the client's by a second or two is worse than no second clock.
/// </summary>
public sealed class ChampSelectTimer
{
    /// <summary><c>PLANNING</c>, <c>BAN_PICK</c>, <c>FINALIZATION</c>, …</summary>
    public string? Phase { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ChampSelectSession))]
[JsonSerializable(typeof(List<int>))]
public sealed partial class LcuJson : JsonSerializerContext;
