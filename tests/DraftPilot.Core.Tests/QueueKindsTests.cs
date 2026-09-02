using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu.Models;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Which queue a draft belongs to. Only one distinction actually changes the advice — whether the
/// game is played on lanes — because every number in the snapshot is a per-lane statistic.
/// </summary>
public class QueueKindsTests
{
    [Theory]
    [InlineData(420, QueueKind.RankedSolo)]
    [InlineData(440, QueueKind.RankedFlex)]
    [InlineData(400, QueueKind.NormalDraft)]
    [InlineData(430, QueueKind.NormalBlind)]
    [InlineData(450, QueueKind.Aram)]
    [InlineData(1700, QueueKind.Arena)]
    [InlineData(900, QueueKind.Rotating)]
    public void KnownQueueIds_AreNamed(int id, QueueKind expected)
        => Assert.Equal(expected, QueueKinds.FromId(id));

    /// <summary>
    /// An id this build has never seen is treated as a normal lane game on purpose: a wrong warning
    /// in ranked would cost more than a missing one in a rotating mode, and Riot adds queues
    /// without asking.
    /// </summary>
    [Fact]
    public void AnUnknownQueueId_IsAssumedToUseLanes()
    {
        var kind = QueueKinds.FromId(31337);

        Assert.Equal(QueueKind.Unknown, kind);
        Assert.True(kind.UsesLanes());
        Assert.Equal(string.Empty, kind.LaneCaveat());
    }

    [Fact]
    public void ACustomLobby_IsRecognisedByItsOwnFlag()
    {
        // No queue id is sent for a custom game, so the flag is the only signal.
        var kind = QueueKinds.FromId(0, isCustomGame: true);

        Assert.Equal(QueueKind.Custom, kind);

        // Usually a normal Rift game, so the lane numbers still apply.
        Assert.True(kind.UsesLanes());
    }

    [Theory]
    [InlineData(QueueKind.Aram)]
    [InlineData(QueueKind.Arena)]
    [InlineData(QueueKind.Rotating)]
    public void ModesWithoutLanes_SayWhyTheNumbersDoNotFit(QueueKind kind)
    {
        Assert.False(kind.UsesLanes());
        Assert.NotEqual(string.Empty, kind.LaneCaveat());
    }

    [Theory]
    [InlineData(QueueKind.RankedSolo)]
    [InlineData(QueueKind.NormalBlind)]
    [InlineData(QueueKind.Clash)]
    [InlineData(QueueKind.Bots)]
    public void LaneModes_SayNothing(QueueKind kind)
    {
        Assert.True(kind.UsesLanes());
        Assert.Equal(string.Empty, kind.LaneCaveat());
    }

    [Fact]
    public void TheQueueTravelsFromTheSessionIntoTheDraftState()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 450 };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 0, Team = 1 });

        Assert.Equal(QueueKind.Aram, DraftState.From(session).Queue);
    }

    /// <summary>A recording made before the field existed carries no id, and must not warn.</summary>
    [Fact]
    public void ASessionWithoutAQueueId_StaysUnknown()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0 };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 0, Team = 1 });

        Assert.Equal(QueueKind.Unknown, DraftState.From(session).Queue);
    }
}
