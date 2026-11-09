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
    }
}
