using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu.Models;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Which queue a draft belongs to. Two distinctions change the advice — whether the game is played
/// on lanes (every number in the snapshot is a per-lane statistic) and which population OP.GG
/// should be asked about.
/// </summary>
public class QueueKindsTests
{
    [Theory]
    [InlineData(420, QueueKind.RankedSolo)]
    [InlineData(440, QueueKind.RankedFlex)]
    [InlineData(400, QueueKind.NormalDraft)]
    [InlineData(430, QueueKind.NormalBlind)]
    [InlineData(480, QueueKind.Swiftplay)]
    [InlineData(450, QueueKind.Aram)]
    [InlineData(2400, QueueKind.AramMayhem)]
    [InlineData(1700, QueueKind.Arena)]
    [InlineData(900, QueueKind.Rotating)]
    public void KnownQueueIds_AreNamed(int id, QueueKind expected)
        => Assert.Equal(expected, QueueKinds.FromId(id));

    /// <summary>
    /// The four queues the tool is actually used in have to arrive under their own name — this is
    /// the list the window shows, and a mode named wrong is worse than no name at all. The ids come
    /// from the client's own queue table (<c>/lol-game-queues/v1/queues</c>, read 2026-09-19).
    /// </summary>
    [Theory]
    [InlineData(420, "Ranked Solo/Duo")]
    [InlineData(440, "Ranked Flex")]
    [InlineData(450, "ARAM")]
    [InlineData(2400, "ARAM Mayhem")]
    public void TheFourQueuesTheToolIsFor_AreNamedInFull(int id, string expected)
        => Assert.Equal(expected, QueueKinds.FromId(id).Display());

    /// <summary>
    /// Mayhem ships as five queues — the normal one, its tournament variant, the two "fast
    /// klassisch" ones, and a custom lobby. All five are the same game as far as anything
    /// downstream is concerned: same map, no lanes, same data source.
    /// </summary>
    [Theory]
    [InlineData(2400)]
    [InlineData(2410)]
    [InlineData(2450)]
    [InlineData(3270)]
    [InlineData(3280)]
    public void EveryMayhemVariant_IsTheSameMode(int id)
        => Assert.Equal(QueueKind.AramMayhem, QueueKinds.FromId(id));

    /// <summary>
    /// Measured during a live match on 2026-09-19: a custom ARAM Chaos lobby is queue 3270 and
    /// reports itself as a custom game. Answering "Custom" to that was wrong where it counts —
    /// a custom lobby uses lanes, so the Howling Abyss was handed Summoner's Rift advice. The id
    /// knows better than the flag, so the id is asked first.
    /// </summary>
    [Fact]
    public void ACustomLobbyOfARealMode_KeepsThatMode()
    {
        var kind = QueueKinds.FromId(3270, isCustomGame: true);

        Assert.Equal(QueueKind.AramMayhem, kind);
        Assert.False(kind.UsesLanes());
        Assert.True(kind.IsAram());
    }

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
    [InlineData(QueueKind.AramMayhem)]
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
    [InlineData(QueueKind.Swiftplay)]
    [InlineData(QueueKind.Clash)]
    [InlineData(QueueKind.Bots)]
    public void LaneModes_SayNothing(QueueKind kind)
    {
        Assert.True(kind.UsesLanes());
        Assert.Equal(string.Empty, kind.LaneCaveat());
    }

    /// <summary>
    /// Both Abyss queues are one map and one data source; everything downstream keys off this
    /// rather than listing the two kinds again and forgetting one.
    /// </summary>
    [Theory]
    [InlineData(QueueKind.Aram, true)]
    [InlineData(QueueKind.AramMayhem, true)]
    [InlineData(QueueKind.RankedSolo, false)]
    [InlineData(QueueKind.Arena, false)]
    public void TheAbyssQueues_AreRecognisedTogether(QueueKind kind, bool expected)
        => Assert.Equal(expected, kind.IsAram());

    /// <summary>
    /// The mapping onto OP.GG's <c>game_mode</c> enum, which has exactly five values. Solo/Duo and
    /// Flex are separate populations there, so this is what keeps the live counters describing the
    /// queue actually being played.
    /// </summary>
    [Theory]
    [InlineData(QueueKind.RankedSolo, "ranked")]
    [InlineData(QueueKind.RankedFlex, "flex")]
    [InlineData(QueueKind.Aram, "aram")]
    [InlineData(QueueKind.AramMayhem, "aram")]
    [InlineData(QueueKind.NormalDraft, "ranked")]
    [InlineData(QueueKind.Swiftplay, "ranked")]
    [InlineData(QueueKind.Custom, "ranked")]
    [InlineData(QueueKind.Unknown, "ranked")]
    public void EachQueue_AsksOpGgForItsOwnPopulation(QueueKind kind, string expected)
        => Assert.Equal(expected, kind.OpGgMode());

    /// <summary>
    /// Arena and the rotating modes have no OP.GG mode at all. An empty string is the signal not to
    /// ask — the alternative, sending "ranked", would answer with Rift numbers about a game that
    /// has neither lanes nor the same items.
    /// </summary>
    [Theory]
    [InlineData(QueueKind.Arena)]
    [InlineData(QueueKind.Rotating)]
    public void ModesWithoutASource_AskForNothing(QueueKind kind)
        => Assert.Equal(string.Empty, kind.OpGgMode());

    /// <summary>
    /// Mayhem borrows ARAM's numbers because OP.GG has no mode of its own for it. Borrowing is
    /// fine; borrowing silently is not, so the mode owes the reader a sentence.
    /// </summary>
    [Fact]
    public void Mayhem_SaysThatItsNumbersAreBorrowedFromAram()
    {
        Assert.Contains("Mayhem", QueueKind.AramMayhem.ModeCaveat());
        Assert.Contains("ARAM", QueueKind.AramMayhem.ModeCaveat());

        // Plain ARAM borrows nothing and must stay quiet.
        Assert.Equal(string.Empty, QueueKind.Aram.ModeCaveat());
        Assert.Equal(string.Empty, QueueKind.RankedSolo.ModeCaveat());
    }

    [Fact]
    public void TheQueueTravelsFromTheSessionIntoTheDraftState()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 450 };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 0, Team = 1 });

        Assert.Equal(QueueKind.Aram, DraftState.From(session).Queue);
    }

    [Fact]
    public void AMayhemSession_ArrivesAsMayhem()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 2400 };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 0, Team = 1 });

        Assert.Equal(QueueKind.AramMayhem, DraftState.From(session).Queue);
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
