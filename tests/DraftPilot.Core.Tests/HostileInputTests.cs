using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu.Models;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The robustness promises this codebase makes in its own comments, held to account with input
/// nobody would send on purpose.
/// <para>
/// Several of these promises are load-bearing: <c>DraftState.From</c> says "never throws on odd
/// input" because it runs inside the socket's receive loop, where an exception kills the connection
/// for the rest of the draft. The score says it produces a win rate, which means a number — one NaN
/// anywhere in the terms rides straight into the sort and scrambles the whole list. Riot and OP.GG
/// both change shapes between patches without asking, so "odd input" is not hypothetical.
/// </para>
/// </summary>
public class HostileInputTests
{
    // ----- the champion select payload --------------------------------------------------------

    /// <summary>
    /// Explicit nulls at every nesting level. The LCU really does send them — that is why the
    /// model's setters coalesce — and a null inside a team list would otherwise take out the
    /// projection for the whole draft.
    /// </summary>
    [Fact]
    public void ASessionFullOfNulls_ProducesAState()
    {
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = 0,
            MyTeam = null!,
            TheirTeam = null!,
            Actions = null!,
            Bans = null!,
            Timer = null!,
            BenchChampions = null!,
        };

        session.MyTeam.Add(null!);
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 122, Team = 1 });
        session.Actions.Add(null!);
        session.Actions.Add([null!]);

        var state = DraftState.From(session);

        Assert.True(state.IsActive);
        Assert.Single(state.Allies);
        Assert.Empty(state.Bench);
    }

    /// <summary>
    /// More than five seats, negative ids, duplicate cell ids — every shape that says "the client
    /// changed" rather than "the player did something". None of them may throw, and the seat index
    /// must stay dense, because SeatPriors addresses seats by it.
    /// </summary>
    [Fact]
    public void AMisshapenTeam_DoesNotThrowAndKeepsIndicesDense()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 7 };

        for (var i = 0; i < 8; i++)
            session.MyTeam.Add(new ChampSelectPlayer { CellId = 3, ChampionId = -5, Team = 1 });

        session.MyTeam.Insert(3, null!);

        var state = DraftState.From(session);

        Assert.Equal(8, state.Allies.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], state.Allies.Select(slot => slot.Index));
    }

    [Fact]
    public void ABenchOfNullsAndZeros_IsEmptyRatherThanBroken()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 450, BenchEnabled = true };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 122, Team = 1 });
        session.BenchChampions.Add(null!);
        session.BenchChampions.Add(new BenchChampion { ChampionId = 0 });

        Assert.Empty(DraftState.From(session).Bench);
    }

    /// <summary>A negative reroll count is not a count; it must not reach the header as one.</summary>
    [Fact]
    public void ANegativeRerollCount_IsClampedToZero()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 450, RerollsRemaining = -3 };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = 122, Team = 1 });

        Assert.Equal(0, DraftState.From(session).RerollsRemaining);
    }

    // ----- the score --------------------------------------------------------------------------

    /// <summary>
    /// A snapshot carrying values no measurement produces. One NaN in a win rate would ride through
    /// the logit into the sum, and <c>OrderByDescending</c> over NaN puts rows in an arbitrary order
    /// — a scrambled list that looks exactly like a normal one.
    /// </summary>
    [Fact]
    public void ASnapshotWithImpossibleNumbers_StillProducesFiniteScores()
    {
        var meta = new MetaBuilder()
            .Champion(1, "NaN").InLane(1, Lane.Top, winRate: double.NaN, play: 1_000)
            .Champion(2, "Unendlich").InLane(2, Lane.Top, winRate: double.PositiveInfinity, play: 1_000)
            .Champion(3, "Negativ").InLane(3, Lane.Top, winRate: -4, play: -20)
            .Champion(4, "Zuviel").InLane(4, Lane.Top, winRate: 12, play: int.MaxValue)
            .Champion(5, "Normal").InLane(5, Lane.Top, winRate: 0.52, play: 20_000)
            .Champion(9, "Gegner").InLane(9, Lane.Top, winRate: 0.5, play: 20_000)
            .Matchup(1, 9, Lane.Top, winRate: double.NaN, play: 500)
            .Matchup(5, 9, Lane.Top, winRate: 0.55, play: 500)
            .Build();

        var state = RecommendCommand_BuildState(Lane.Top, allies: [], enemies: [9]);
        var target = new TurnTracker().Resolve(state)!;
        var predictions = new LanePredictor(meta).Predict(state.Enemies);

        var set = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, predictions, limit: 10);

        Assert.NotEmpty(set.Items);

        foreach (var item in set.Items)
        {
            Assert.True(double.IsFinite(item.Score), $"{item.Name} hat den Score {item.Score}.");
            Assert.InRange(item.Score, 0, 1);
            Assert.True(double.IsFinite(item.Uncertainty), $"{item.Name} hat den Fehler {item.Uncertainty}.");
            Assert.True(item.Uncertainty >= 0, $"{item.Name} hat einen negativen Fehler.");
        }

        // A total order: sorted descending, and every pair comparable. NaN breaks both silently.
        for (var i = 1; i < set.Items.Count; i++)
            Assert.True(set.Items[i - 1].Score >= set.Items[i].Score);
    }

    /// <summary>
    /// The same for bans, whose score is a different unit and whose gate multiplies rather than
    /// adds — a NaN rate there produces a NaN gate and a list ordered by nothing.
    /// </summary>
    [Fact]
    public void BansSurviveImpossibleRatesToo()
    {
        var meta = new MetaBuilder()
            .Champion(1, "NaN").InLane(1, Lane.Mid, winRate: double.NaN, pickRate: double.NaN, banRate: -1)
            .Champion(2, "Normal").InLane(2, Lane.Mid, winRate: 0.53, pickRate: 0.08, banRate: 0.04)
            .Build();

        var state = RecommendCommand_BuildState(Lane.Mid, allies: [], enemies: []);
        var target = new TurnTracker().Resolve(state)! with { Action = TurnAction.Ban };

        var set = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty, limit: 10);

        Assert.All(set.Items, item => Assert.True(
            double.IsFinite(item.Score), $"{item.Name} hat den Ban-Wert {item.Score}."));
    }

    /// <summary>An empty snapshot answers with nothing, not with an exception.</summary>
    [Fact]
    public void AnEmptySnapshot_AnswersWithAnEmptySet()
    {
        var state = RecommendCommand_BuildState(Lane.Top, allies: [], enemies: []);
        var target = new TurnTracker().Resolve(state)!;

        var set = new Recommender(MetaLookup.Empty, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty);

        Assert.Empty(set.Items);
    }

    // ----- the Abyss ranking --------------------------------------------------------------------

    [Fact]
    public void TheBenchRankingSurvivesImpossibleRates()
    {
        Dictionary<int, AramStat> stats = new()
        {
            [1] = new AramStat(1, double.NaN, 5_000),
            [2] = new AramStat(2, 4, 5_000),
            [3] = new AramStat(3, -1, 5_000),
            [4] = new AramStat(4, 0.52, int.MaxValue),
        };

        var rows = AramAdvisor.Rank(1, [2, 3, 4], stats, id => $"#{id}");

        Assert.All(rows, row =>
        {
            Assert.True(double.IsFinite(row.Score), $"{row.Name} hat den Score {row.Score}.");
            Assert.InRange(row.Score, 0, 1);
            Assert.True(double.IsFinite(row.Uncertainty) && row.Uncertainty >= 0);
        });

        for (var i = 1; i < rows.Count; i++)
            Assert.True(rows[i - 1].Score >= rows[i].Score);
    }

    // ----- the two response parsers -------------------------------------------------------------

    /// <summary>
    /// Malformed JSON has to arrive as a JsonException, because that is the one the fetch catches
    /// by name to tell "OP.GG answered something we do not understand" from a transport failure.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("nicht mal json")]
    public void TheMatchupGuideRejectsMalformedJsonAsJsonException(string payload)
        => Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => MatchupGuideParser.Parse(payload, 1, "A", 2, "B", Lane.Top, "16.18.1"));

    /// <summary>
    /// Valid JSON of the wrong shape is a different case entirely: the endpoint answered, the
    /// fields are simply not there. That must come back as an empty plan, not as an exception —
    /// "no build for this pairing" is a normal answer.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{"core_items":"nicht mal eine liste","rune_pages":42}}""")]
    [InlineData("""{"data":{"core_items":[{"play":"viele","ids":[null,"x"]}]}}""")]
    [InlineData("""{"data":{"rune_pages":[{"play":5,"primary_rune_ids":[1,2]}]}}""")]
    public void TheMatchupGuideTurnsAnUnexpectedShapeIntoAnEmptyPlan(string payload)
    {
        var plan = MatchupGuideParser.Parse(payload, 1, "A", 2, "B", Lane.Top, "16.18.1");

        Assert.True(plan.IsEmpty);
        Assert.Equal(1, plan.ChampionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("class Foo: bar")]
    [InlineData("Foo(")]
    [InlineData("Foo(Bar(1,2")]
    public void TheTupleParserRejectsGarbageWithItsOwnException(string payload)
        => Assert.Throws<OpGgParseException>(() => OpGgResponseParser.Parse(payload));

    /// <summary>
    /// A node that parsed but holds nothing of what a reader asks for. Every accessor has to answer
    /// with a default rather than throw — the whole point of the node wrapper.
    /// </summary>
    [Fact]
    public void ReadingFieldsThatAreNotThere_AnswersWithDefaults()
    {
        var node = OpGgResponseParser.Parse("class Foo: bar\n\nFoo(null)");

        Assert.Equal(0, node["gibtesnicht"]["auchnicht"].AsInt());
        Assert.Null(node["gibtesnicht"].AsText());
        Assert.Empty(node["gibtesnicht"].Items);
    }

    // ----- the snapshot on disk -----------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"version":999999,"champions":[{"id":1,"name":"A"}]}""")]
    [InlineData("""{"champions":[]}""")]
    public void ACorruptSnapshotFile_IsRefusedWithAReasonRatherThanAnException(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dirkdraft-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "snapshot.json");

        try
        {
            File.WriteAllText(path, content);

            var result = new SnapshotStore(path).LoadWithStatus();

            Assert.Null(result.Snapshot);
            Assert.NotEqual(SnapshotLoadStatus.Ok, result.Status);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The same session the recommendation command builds, so the hostile cases run through the
    /// exact shape the rest of the tool sees rather than a simplified one.
    /// </summary>
    private static DraftState RecommendCommand_BuildState(Lane lane, List<int> allies, List<int> enemies)
    {
        string[] positions = ["top", "jungle", "middle", "bottom", "utility"];
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = (int)lane,
            Timer = new ChampSelectTimer { Phase = "BAN_PICK" },
        };

        var queue = new Queue<int>(allies);

        for (var i = 0; i < 5; i++)
        {
            var champion = i == (int)lane ? 0 : queue.Count > 0 ? queue.Dequeue() : 0;
            session.MyTeam.Add(new ChampSelectPlayer
            {
                CellId = i,
                ChampionId = champion,
                AssignedPosition = positions[i],
                Team = 1,
            });
        }

        for (var i = 0; i < 5; i++)
        {
            session.TheirTeam.Add(new ChampSelectPlayer
            {
                CellId = i + 5,
                ChampionId = i < enemies.Count ? enemies[i] : 0,
                Team = 2,
            });
        }

        session.Actions.Add([
            new ChampSelectAction
            {
                Id = 1,
                ActorCellId = (int)lane,
                Type = "pick",
                IsAllyAction = true,
                IsInProgress = true,
            },
        ]);

        return DraftState.From(session);
    }
}
