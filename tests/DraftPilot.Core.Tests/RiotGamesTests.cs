using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// How the collector keeps a game line (<see cref="RiotGames.Keep"/>): every line with minute ten,
/// the duels only in a sample — and a line whose duels were not kept must never read as a game
/// without any, nor an older line read differently than before.
/// </summary>
public class RiotGamesTests
{
    /// <summary>A line as <see cref="RiotGames.Encode"/> builds it, with or without minute ten, then the given duel numbers.</summary>
    private static int[] Line(bool minuteTen, params int[] duels)
    {
        var line = new int[RiotGames.FixedLength];
        for (var i = 0; i < 10; i++)
            line[i] = (i + 1) * 8 + i % 5 + 1;
        line[13] = -1;
        if (minuteTen)
        {
            for (var i = 0; i < 10; i++)
                (line[15 + i], line[25 + i]) = (3000 + i, 4000 + i);
        }
        return [.. line, .. duels];
    }

    // One duel at 200 s: place 0 kills place 5, both lane players with level, item worth, armor, magic resist.
    private static readonly int[] OneDuel = [13, 200, 0, 5, 0, 3, 750, 15, 0, 5, 2, 450, 0, 0];

    [Fact]
    public void ALineWithoutItsDuels_KeepsMinuteTenAndSaysSo()
    {
        var full = Line(minuteTen: true, OneDuel);

        Assert.Same(full, RiotGames.Keep(full, withDuels: true));
        Assert.True(RiotGames.HasDuelSample(full));
        Assert.Single(RiotGames.Duels(full));

        var kept = RiotGames.Keep(full, withDuels: false);
        Assert.Equal(RiotGames.FixedLength + 1, kept.Length);
        Assert.Equal(full[..RiotGames.FixedLength], kept[..RiotGames.FixedLength]);
        Assert.Equal(RiotGames.NoDuels, kept[^1]);
        Assert.True(RiotGames.HasLanePart(kept));
        Assert.False(RiotGames.HasDuelSample(kept));           // not "a game without duels"
        Assert.Empty(RiotGames.Duels(kept));
    }

    [Fact]
    public void ALineWithoutMinuteTen_EndsAfterTheDraft()
    {
        var kept = RiotGames.Keep(Line(minuteTen: false), withDuels: false);

        Assert.Equal(RiotGames.DraftLength, kept.Length);
        Assert.False(RiotGames.HasLanePart(kept));
        Assert.False(RiotGames.HasDuelSample(kept));
        // Sampled for its duels it keeps the empty lane part, as in version 2: a game without duels.
        Assert.True(RiotGames.HasDuelSample(RiotGames.Keep(Line(minuteTen: false), withDuels: true)));
    }

    [Fact]
    public void OlderLines_ReadAsBefore()
    {
        Assert.False(RiotGames.HasLanePart(new int[RiotGames.DraftLength]));     // version 1, or 2 without the lane part
        Assert.True(RiotGames.HasDuelSample(Line(minuteTen: true)));             // version 2: a sampled game without duels
        Assert.True(RiotGames.HasDuelSample(Line(minuteTen: true, OneDuel)));    // version 2 with a duel
        // No line before version 5 reads as sampled for teamfights.
        Assert.False(RiotGames.HasFightSample(new int[RiotGames.DraftLength]));
        Assert.False(RiotGames.HasFightSample(Line(minuteTen: true, OneDuel)));
        Assert.False(RiotGames.HasFightSample(Line(minuteTen: true, [.. OneDuel, RiotGames.LevelsBeforeDuel])));
        Assert.False(RiotGames.HasFightSample(Line(minuteTen: true, RiotGames.NoDuels)));
        Assert.True(RiotGames.HasLevelsBeforeDuel(Line(minuteTen: true, [.. OneDuel, RiotGames.LevelsBeforeDuel])));
        Assert.Empty(RiotGames.Fights(Line(minuteTen: true, [.. OneDuel, RiotGames.LevelsBeforeDuel])));
    }

    // One teamfight at 900 s: side 0 kills two, side 1 one; place 5 dead at the start (bit 5 = 32),
    // places 0–3 and 6–8 seen in it (463); every player level 10 with 5.000 gold of items.
    private static readonly int[] OneFight = [900, 2, 1, 32, 463, .. Enumerable.Range(0, 10).SelectMany(_ => new[] { 10, 5000 })];

    // The fight part: two objectives (Infernal drake to side 0 at 400 s, Baron to side 1 at 1.200 s), then the fight.
    private static readonly int[] FightPart = [RiotGames.FightsFollow, 6, 400, 0, 1, 1200, 1, 8, RiotGames.FightLength, .. OneFight];

    [Fact]
    public void TheFightPart_StaysOnlyWithLinesSampledForIt()
    {
        var full = Line(minuteTen: true, [.. OneDuel, RiotGames.LevelsBeforeDuel, .. FightPart]);

        Assert.Same(full, RiotGames.Keep(full, withDuels: true, withFights: true));
        Assert.True(RiotGames.HasFightSample(full));
        Assert.True(RiotGames.HasLevelsBeforeDuel(full));     // the −2 stands before the fight part now, not at the end
        Assert.Single(RiotGames.Duels(full));
        Assert.Equal(new[] { new[] { 400, 0, 1 }, new[] { 1200, 1, 8 } }, RiotGames.Objectives(full));
        Assert.Equal(OneFight, Assert.Single(RiotGames.Fights(full)));

        // Duels kept, fights not: the line as version 4 wrote it.
        var duelsOnly = RiotGames.Keep(full, withDuels: true);
        Assert.Equal(Line(minuteTen: true, [.. OneDuel, RiotGames.LevelsBeforeDuel]), duelsOnly);
        Assert.False(RiotGames.HasFightSample(duelsOnly));
        Assert.True(RiotGames.HasLevelsBeforeDuel(duelsOnly));

        // Fights kept, duels not: minute ten, the −1, then the fight part whole.
        var fightsOnly = RiotGames.Keep(full, withDuels: false, withFights: true);
        Assert.Equal([.. full[..RiotGames.FixedLength], RiotGames.NoDuels, .. FightPart], fightsOnly);
        Assert.False(RiotGames.HasDuelSample(fightsOnly));
        Assert.Empty(RiotGames.Duels(fightsOnly));
        Assert.False(RiotGames.HasLevelsBeforeDuel(fightsOnly));
        Assert.True(RiotGames.HasFightSample(fightsOnly));
        Assert.Equal(OneFight, Assert.Single(RiotGames.Fights(fightsOnly)));
        Assert.Equal(2, RiotGames.Objectives(fightsOnly).Count());

        Assert.Equal([.. full[..RiotGames.FixedLength], RiotGames.NoDuels], RiotGames.Keep(full, withDuels: false));
    }

    [Fact]
    public void AGameWithoutDuelsOrFights_SaysSoWhenSampled()
    {
        // Sampled for both, no duel happened: the fight part follows the lane part directly.
        var noDuels = Line(minuteTen: true, FightPart);
        Assert.True(RiotGames.HasDuelSample(noDuels));
        Assert.Empty(RiotGames.Duels(noDuels));
        Assert.False(RiotGames.HasLevelsBeforeDuel(noDuels));
        Assert.True(RiotGames.HasFightSample(noDuels));
        Assert.Single(RiotGames.Fights(noDuels));

        // Sampled for fights, none happened and no monster was taken: a game without teamfights.
        var noFights = Line(minuteTen: true, RiotGames.NoDuels, RiotGames.FightsFollow, 0);
        Assert.True(RiotGames.HasFightSample(noFights));
        Assert.Empty(RiotGames.Fights(noFights));
        Assert.Empty(RiotGames.Objectives(noFights));

        // Without minute ten the line ends after the draft, fights or not.
        Assert.Equal(RiotGames.DraftLength, RiotGames.Keep(Line(minuteTen: false, FightPart), withDuels: false, withFights: true).Length);
    }
}
