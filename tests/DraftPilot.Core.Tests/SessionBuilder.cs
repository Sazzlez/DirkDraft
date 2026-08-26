using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Builds champion select sessions for tests. Mirrors the real cell numbering: allies 0-4,
/// enemies 5-9.
/// </summary>
internal sealed class SessionBuilder
{
    private static readonly string[] AllyPositions = ["top", "jungle", "middle", "bottom", "utility"];

    private readonly ChampSelectSession _session = new()
    {
        LocalPlayerCellId = 2,
        Timer = new ChampSelectTimer { Phase = "BAN_PICK", AdjustedTimeLeftInPhase = 25_000 },
    };

    private readonly List<ChampSelectAction> _actions = [];

    public SessionBuilder()
    {
        for (var i = 0; i < 5; i++)
        {
            _session.MyTeam.Add(new ChampSelectPlayer { CellId = i, AssignedPosition = AllyPositions[i], Team = 1 });
            _session.TheirTeam.Add(new ChampSelectPlayer { CellId = i + 5, AssignedPosition = string.Empty, Team = 2 });
        }
    }

    public SessionBuilder LocalPlayer(long cellId)
    {
        _session.LocalPlayerCellId = cellId;
        return this;
    }

    public SessionBuilder Phase(string phase, int secondsLeft = 25)
    {
        _session.Timer.Phase = phase;
        _session.Timer.AdjustedTimeLeftInPhase = secondsLeft * 1000;
        return this;
    }

    public SessionBuilder Locked(long cellId, int championId)
    {
        Slot(cellId).ChampionId = championId;
        return this;
    }

    public SessionBuilder Hovering(long cellId, int championId)
    {
        Slot(cellId).ChampionPickIntent = championId;
        return this;
    }

    public SessionBuilder AllyLane(long cellId, string? position)
    {
        Slot(cellId).AssignedPosition = position;
        return this;
    }

    public SessionBuilder SummaryBans(int[] ally, int[] enemy)
    {
        _session.Bans.MyTeamBans.AddRange(ally);
        _session.Bans.TheirTeamBans.AddRange(enemy);
        return this;
    }

    public SessionBuilder CompletedBan(long actorCellId, int championId)
        => Action(actorCellId, championId, "ban", completed: true, inProgress: false);

    public SessionBuilder OnClock(long actorCellId, string type)
        => Action(actorCellId, championId: 0, type, completed: false, inProgress: true);

    public SessionBuilder Action(long actorCellId, int championId, string type, bool completed, bool inProgress)
    {
        _actions.Add(new ChampSelectAction
        {
            Id = _actions.Count + 1,
            ActorCellId = actorCellId,
            ChampionId = championId,
            Type = type,
            Completed = completed,
            IsInProgress = inProgress,
            IsAllyAction = actorCellId < 5,
        });

        return this;
    }

    public SessionBuilder Spectating()
    {
        _session.IsSpectating = true;
        return this;
    }

    public ChampSelectSession Build()
    {
        _session.Actions.Clear();
        if (_actions.Count > 0)
            _session.Actions.Add([.. _actions]);

        return _session;
    }

    private ChampSelectPlayer Slot(long cellId)
        => _session.MyTeam.Concat(_session.TheirTeam).First(player => player.CellId == cellId);
}
