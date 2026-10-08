using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The complete duel list from OP.GG's matchup guide: every opponent a champion has on a lane, not
/// the three notable ones. Pinned against the captured guide answer (Darius Top) and against the
/// one rule that sets these duels apart — they are not a selection, so no listing offset applies.
/// </summary>
public class CompleteDuelTests
{
    private static readonly string Guide = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpGg", "matchup-guide-darius-jax.json"));

    private const int Darius = 122, Jax = 24, Nasus = 75, Garen = 86;

    private static ChampionResolver Resolver(params (int Id, string Name)[] champions)
        => new(champions.Select(champion => new ChampionEntry { Id = champion.Id, Key = champion.Name, Name = champion.Name }));

    [Fact]
    public void TheWholeListIsRead()
    {
        var resolver = Resolver((Darius, "Darius"), (Jax, "Jax"), (Nasus, "Nasus"), (Garen, "Garen"));

        var duels = LiveDraftFetcher.ParseCompleteDuels(Guide, Darius, Lane.Top, resolver);

        // Only the opponents this resolver knows survive — the fixture lists 45.
        Assert.Equal(new[] { Jax, Nasus, Garen }.Order(), duels.Select(duel => duel.OpponentId).Order());

        var nasus = duels.Single(duel => duel.OpponentId == Nasus);
        Assert.Equal(Darius, nasus.ChampionId);
        Assert.Equal(Lane.Top, nasus.Lane);
        Assert.Equal(210, nasus.Play);
        Assert.Equal(109.0 / 210, nasus.WinRate, precision: 9);
        Assert.All(duels, duel => Assert.True(duel.FromCompleteList));
    }

    /// <summary>The champion's own row is never a duel against itself.</summary>
    [Fact]
    public void NoDuelAgainstItself()
    {
        var resolver = Resolver((Darius, "Darius"), (Garen, "Garen"));

        var duels = LiveDraftFetcher.ParseCompleteDuels(Guide, Garen, Lane.Top, resolver);

        Assert.DoesNotContain(duels, duel => duel.OpponentId == Garen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"data":{"counters":"nope"}}""")]
    [InlineData("""{"data":{"counters":[{"champion_id":"x","play":-3,"win":9}]}}""")]
    public void AnUnusableAnswerGivesNoDuels(string text)
    {
        var resolver = Resolver((Darius, "Darius"));

        if (text.Length == 0)
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => LiveDraftFetcher.ParseCompleteDuels(text, Darius, Lane.Top, resolver));
            return;
        }

        Assert.Empty(LiveDraftFetcher.ParseCompleteDuels(text, Darius, Lane.Top, resolver));
    }

    /// <summary>
    /// A complete-list duel is expected where the line measured for this file puts it — in a Gold
    /// file the default-bracket duel follows the lane-rate difference only by about 0,76. A duel
    /// exactly on that line adds nothing; with the plain expectation it would have read as a
    /// deficit of the stronger champion.
    /// </summary>
    [Fact]
    public void ACompleteListDuelIsReadAgainstTheMeasuredLine()
    {
        var snapshot = new MetaBuilder()
            .Champion(Darius, "Darius")
            .Champion(Jax, "Jax")
            .InLane(Darius, Lane.Top, winRate: 0.54, play: 40_000)
            .InLane(Jax, Lane.Top, winRate: 0.48, play: 40_000)
            .Snapshot();
        snapshot.CompleteDuelSlope = 0.5;
        snapshot.CompleteDuelOffset = 0.03;
        snapshot.MatchupBaseline = -0.1;

        var meta = new MetaLookup(snapshot);
        var difference = ScoreModel.Logit(meta.LaneStat(Darius, Lane.Top)!.Value.WinRate)
            - ScoreModel.Logit(meta.LaneStat(Jax, Lane.Top)!.Value.WinRate);

        // A notable-list expectation follows the file's own bracket and carries the listing offset.
        Assert.Equal(difference - 0.1, meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);

        meta.ApplyLiveMatchups(
        [
            new MatchupStat
            {
                ChampionId = Darius,
                OpponentId = Jax,
                Lane = Lane.Top,
                WinRate = ScoreModel.Sigmoid(0.03 + (0.5 * difference)),
                Play = 400,
                FromCompleteList = true,
            },
        ]);

        Assert.Equal(0.03 + (0.5 * difference), meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);
        Assert.Equal(0, ScoreModel.Logit(meta.Matchup(Darius, Jax, Lane.Top)!.Value.WinRate) - meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);
    }

    /// <summary>An unusable line in a damaged file falls back to the plain expectation, never to nonsense.</summary>
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, 0)]
    [InlineData(-0.5, 0)]
    [InlineData(3, 0)]
    [InlineData(0.8, double.PositiveInfinity)]
    public void ABrokenLine_ReadsAsThePlainExpectation(double slope, double offset)
    {
        var snapshot = new MetaBuilder()
            .Champion(Darius, "Darius")
            .Champion(Jax, "Jax")
            .InLane(Darius, Lane.Top, winRate: 0.54, play: 40_000)
            .InLane(Jax, Lane.Top, winRate: 0.48, play: 40_000)
            .Snapshot();
        snapshot.CompleteDuelSlope = slope;
        snapshot.CompleteDuelOffset = offset;

        var meta = new MetaLookup(snapshot);
        meta.ApplyLiveMatchups([new MatchupStat { ChampionId = Darius, OpponentId = Jax, Lane = Lane.Top, WinRate = 0.5, Play = 400, FromCompleteList = true }]);

        var difference = ScoreModel.Logit(meta.LaneStat(Darius, Lane.Top)!.Value.WinRate)
            - ScoreModel.Logit(meta.LaneStat(Jax, Lane.Top)!.Value.WinRate);
        Assert.Equal(difference, meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);
    }

    [Fact]
    public void TheLineIsFittedFromTheSampleDuels()
    {
        var lanes = new List<LaneStat>();
        for (var id = 1; id <= 40; id++)
            lanes.Add(new LaneStat { ChampionId = id, Lane = Lane.Mid, WinRate = 0.47 + (id * 0.0015), Play = 5_000 });

        var duels = new List<MatchupStat>();
        foreach (var mine in lanes.Take(8))
        {
            foreach (var theirs in lanes.Where(stat => stat.ChampionId != mine.ChampionId))
            {
                var difference = ScoreModel.Logit(mine.WinRate) - ScoreModel.Logit(theirs.WinRate);
                duels.Add(new MatchupStat
                {
                    ChampionId = mine.ChampionId,
                    OpponentId = theirs.ChampionId,
                    Lane = Lane.Mid,
                    WinRate = ScoreModel.Sigmoid(0.02 + (0.76 * difference)),
                    Play = 300 + theirs.ChampionId,
                    FromCompleteList = true,
                });
            }
        }

        var (slope, offset, error, count, lists) = SnapshotBuilder.MeasureCompleteDuelLine(duels, lanes);
        Assert.Equal(0.76, slope, precision: 6);
        Assert.Equal(0.02, offset, precision: 6);
        Assert.Equal(0, error, precision: 9);   // every duel exactly on the line
        Assert.Equal(duels.Count, count);
        Assert.Equal(8, lists);

        // Too few duels: no line, the plain expectation stands.
        Assert.Equal(CompleteDuelLine.Plain, SnapshotBuilder.MeasureCompleteDuelLine(duels.Take(SnapshotBuilder.MinimumDuelsForLine - 1).ToList(), lanes));

        // A duel whose opponent has no lane row in this file cannot be placed on the line.
        var unplaced = duels.Select(duel => new MatchupStat
        {
            ChampionId = duel.ChampionId,
            OpponentId = duel.OpponentId + 1_000,
            Lane = duel.Lane,
            WinRate = duel.WinRate,
            Play = duel.Play,
        }).ToList();
        Assert.Equal(CompleteDuelLine.Plain, SnapshotBuilder.MeasureCompleteDuelLine(unplaced, lanes));

        // A wild slope is held to a sane range rather than trusted.
        var steep = duels.Select(duel => new MatchupStat
        {
            ChampionId = duel.ChampionId,
            OpponentId = duel.OpponentId,
            Lane = duel.Lane,
            WinRate = ScoreModel.Sigmoid(4 * (ScoreModel.Logit(duel.WinRate) - 0.02) / 0.76),
            Play = duel.Play,
        }).ToList();
        Assert.Equal(1.5, SnapshotBuilder.MeasureCompleteDuelLine(steep, lanes).Slope, precision: 9);
    }

    /// <summary>
    /// Every candidate reads the enemy's list from the other side. An edge exactly where its own
    /// expectation put it says nothing, so its mirror must say nothing either: both offsets belong
    /// to the direction OP.GG wrote down and flip with it. With the same sign, being named in an
    /// enemy's notable list was worth +0,06 log-odds, and a complete-list mirror minus twice the
    /// line's offset.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMirroredEdgeAtItsExpectation_ContributesNothing(bool complete)
    {
        var snapshot = new MetaBuilder()
            .Champion(Darius, "Darius")
            .Champion(Jax, "Jax")
            .InLane(Darius, Lane.Top, winRate: 0.53, play: 40_000)
            .InLane(Jax, Lane.Top, winRate: 0.48, play: 40_000)
            .Snapshot();
        snapshot.MatchupBaseline = -0.03;
        snapshot.CompleteDuelSlope = 0.76;
        snapshot.CompleteDuelOffset = 0.04;

        var meta = new MetaLookup(snapshot);
        var difference = ScoreModel.Logit(meta.LaneStat(Jax, Lane.Top)!.Value.WinRate)
            - ScoreModel.Logit(meta.LaneStat(Darius, Lane.Top)!.Value.WinRate);
        var expected = complete ? 0.04 + (0.76 * difference) : -0.03 + difference;

        // Jax's own list names Darius, exactly as expected.
        meta.ApplyLiveMatchups(
        [
            new MatchupStat
            {
                ChampionId = Jax,
                OpponentId = Darius,
                Lane = Lane.Top,
                WinRate = ScoreModel.Sigmoid(expected),
                Play = 300,
                FromCompleteList = complete,
            },
        ]);

        var stored = meta.Matchup(Jax, Darius, Lane.Top)!.Value;
        Assert.Equal(0, ScoreModel.Logit(stored.WinRate) - meta.MatchupBaseline(Jax, Darius, Lane.Top), precision: 9);

        var mirror = meta.Matchup(Darius, Jax, Lane.Top)!.Value;
        Assert.True(mirror.IsInferred);
        Assert.Equal(0, ScoreModel.Logit(mirror.WinRate) - meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);
        Assert.Equal(-meta.MatchupBaseline(Jax, Darius, Lane.Top), meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 12);
    }

    /// <summary>The same for the stored file's notable lists, whose mirrors are inferred at load.</summary>
    [Fact]
    public void AStoredMirrorAtItsExpectation_ContributesNothing()
    {
        var builder = new MetaBuilder()
            .Champion(Darius, "Darius")
            .Champion(Jax, "Jax")
            .InLane(Darius, Lane.Top, winRate: 0.53, play: 40_000)
            .InLane(Jax, Lane.Top, winRate: 0.48, play: 40_000)
            .MatchupBaseline(-0.03);
        var lanes = new MetaLookup(builder.Snapshot());
        var expected = -0.03 + ScoreModel.Logit(lanes.LaneStat(Jax, Lane.Top)!.Value.WinRate)
            - ScoreModel.Logit(lanes.LaneStat(Darius, Lane.Top)!.Value.WinRate);

        var meta = new MetaLookup(builder.Matchup(Jax, Darius, Lane.Top, ScoreModel.Sigmoid(expected), play: 200).Snapshot());

        var mirror = meta.Matchup(Darius, Jax, Lane.Top)!.Value;
        Assert.True(mirror.IsInferred);
        Assert.Equal(0, ScoreModel.Logit(mirror.WinRate) - meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);
    }

    /// <summary>
    /// A notable-list duel names an extreme and runs at more than twice the deviation of the same
    /// pair measured without selection, so the complete list wins whatever the samples — in both
    /// orders of arrival, and for the mirrored edge a candidate actually reads.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCompleteListWinsOverTheNotableOne(bool completeFirst)
    {
        var meta = new MetaLookup(new MetaBuilder()
            .Champion(Darius, "Darius")
            .Champion(Jax, "Jax")
            .InLane(Darius, Lane.Top, winRate: 0.52, play: 40_000)
            .InLane(Jax, Lane.Top, winRate: 0.49, play: 40_000)
            .Snapshot());

        var notable = new MatchupStat { ChampionId = Jax, OpponentId = Darius, Lane = Lane.Top, WinRate = 0.40, Play = 5_000 };
        var complete = new MatchupStat { ChampionId = Jax, OpponentId = Darius, Lane = Lane.Top, WinRate = 0.47, Play = 300, FromCompleteList = true };

        meta.ApplyLiveMatchups(completeFirst ? [complete] : [notable]);
        meta.ApplyLiveMatchups(completeFirst ? [notable] : [complete]);

        Assert.True(meta.Matchup(Jax, Darius, Lane.Top)!.Value.FromCompleteList);
        Assert.True(meta.Matchup(Darius, Jax, Lane.Top)!.Value.FromCompleteList);
        Assert.Equal(300, meta.Matchup(Darius, Jax, Lane.Top)!.Value.Play);
    }

    /// <summary>
    /// The rule these duels exist for. A notable-opponent edge sits above or below the plain
    /// lane-rate expectation by the listing offset; a complete-list edge does not. Shrink target and
    /// centring must both follow the edge in effect, or "exactly as expected" reads as an edge.
    /// </summary>
    [Fact]
    public void ACompleteListDuelIsMeasuredWithoutTheListingOffset()
    {
        const double Offset = -0.1;

        var snapshot = new MetaBuilder()
            .Champion(Darius, "Darius")
            .Champion(Jax, "Jax")
            .InLane(Darius, Lane.Top, winRate: 0.52, play: 40_000)
            .InLane(Jax, Lane.Top, winRate: 0.49, play: 40_000)
            .Snapshot();
        snapshot.MatchupBaseline = Offset;

        var meta = new MetaLookup(snapshot);
        var plain = ScoreModel.Logit(meta.LaneStat(Darius, Lane.Top)!.Value.WinRate)
            - ScoreModel.Logit(meta.LaneStat(Jax, Lane.Top)!.Value.WinRate);

        // Without any edge, the expectation carries the listing offset.
        Assert.Equal(plain + Offset, meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);

        // A complete-list duel exactly at the plain expectation: no offset, so it is exactly expected.
        meta.ApplyLiveMatchups(
        [
            new MatchupStat
            {
                ChampionId = Darius,
                OpponentId = Jax,
                Lane = Lane.Top,
                WinRate = ScoreModel.Sigmoid(plain),
                Play = 300,
                FromCompleteList = true,
            },
        ]);

        var view = meta.Matchup(Darius, Jax, Lane.Top)!.Value;

        Assert.True(view.FromCompleteList);
        Assert.Equal(plain, meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);
        Assert.Equal(0, ScoreModel.Logit(view.WinRate) - meta.MatchupBaseline(Darius, Jax, Lane.Top), precision: 9);

        // And from the other side, which is how a candidate meets the revealed enemy.
        var mirror = meta.Matchup(Jax, Darius, Lane.Top)!.Value;
        Assert.True(mirror.FromCompleteList);
        Assert.Equal(0, ScoreModel.Logit(mirror.WinRate) - meta.MatchupBaseline(Jax, Darius, Lane.Top), precision: 9);
    }
}
