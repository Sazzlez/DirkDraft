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
