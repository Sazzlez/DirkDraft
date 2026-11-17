using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.Riot;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// data/resets.json: a champion is counted anew after a hotfix or rework. Applied exactly once per
/// entry, out of every count of the patch, and a reworked champion is missing from the fallback files
/// of the patch before.
/// </summary>
public sealed class ChampionResetTests
{
    private const int Mel = 800;
    private const int Ahri = 103;
    private const int Jinx = 222;

    private static readonly ChampionEntry[] Champions =
    [
        new() { Id = Mel, Key = "Mel", Name = "Mel" },
        new() { Id = Ahri, Key = "Ahri", Name = "Ahri" },
        new() { Id = Jinx, Key = "Jinx", Name = "Jinx" },
        new() { Id = 62, Key = "MonkeyKing", Name = "Wukong" },
        new() { Id = 145, Key = "Kaisa", Name = "Kai'Sa" },
    ];

    private static ResetPlan Plan(string json) => new(ChampionResets.Parse(json, out _), ResetPlan.Names(Champions));

    /// <summary>A patch's counts with Mel in every kind of row, beside rows that have nothing to do with her.</summary>
    private static CollectorState State(string patch = "16.23") => new()
    {
        Patch = patch,
        Platform = "euw1",
        Meta = Counts(),
        Tally = new RiotBuildFile
        {
            Patch = patch,
            Platform = "euw1",
            Matches = new Dictionary<string, int> { ["platinum"] = 1000 },
            Builds =
            [
                new ChampionBuildStats { ChampionId = Mel, Lane = Lane.Mid, Tier = "platinum", Games = 40, Wins = 22 },
                new ChampionBuildStats { ChampionId = Ahri, Lane = Lane.Mid, Tier = "platinum", Games = 90, Wins = 46 },
            ],
        },
        Situational = [[Mel, 2, 1, 2, 0, 3089, 12, 7], [Ahri, 2, 1, 2, 0, 3089, 30, 16]],
        Matchups = [[Mel, 2, Ahri, 0, 8112, 9, 5], [Ahri, 2, Mel, 0, 8229, 9, 4], [Ahri, 2, Jinx, 0, 8229, 3, 1]],
        Games = [new int[13], new int[13]],
        Seen = [1, 2, 3],
    };

    private static RiotMetaCounts Counts() => new()
    {
        Matches = 1000,
        Lanes = [[Mel, 2, 40, 22], [Mel, 4, 6, 2], [Ahri, 2, 90, 46], [Jinx, 3, 120, 61]],
        Champions = [[Mel, 46, 310, 1000, 90000, 500], [Ahri, 90, 40, 2000, 150000, 900]],
        Duels = [[2, Ahri, Mel, 12, 5], [2, Mel, 900, 4, 3], [2, Ahri, 245, 20, 11]],
        Duos = [[Jinx, 3, Mel, 4, 5, 3], [Jinx, 3, 412, 4, 60, 33]],
        Versus = [[Ahri, 2, Mel, 4, 7, 3], [Mel, 2, 900, 0, 31, 15], [Ahri, 2, Jinx, 3, 44, 20]],
    };

    private const string MelHotfix = """[{ "champion": "Mel", "patch": "16.23", "reason": "hotfix", "note": "Hotfix vom 20.11." }]""";

    [Fact]
    public void TheFile_IsReadEntryByEntry_AndSaysWhatItCannotRead()
    {
        var entries = ChampionResets.Parse(
            """
            [
              // written by hand, on a phone too: comments and a trailing comma are forgiven
              { "champion": "MonkeyKing", "patch": "16.23", "reason": "rework", "note": "neues Kit" },
              { "champion": "Mel", "patch": "16.23.1", "reason": "Hotfix" },
              { "champion": "", "patch": "16.23", "reason": "hotfix" },
              { "champion": "Ahri", "patch": "bald", "reason": "hotfix" },
              { "champion": "Ahri", "patch": "16.23", "reason": "nerf" },
            ]
            """,
            out var problems);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsRework);
        Assert.Equal(("Mel", "hotfix", false), (entries[1].Champion, entries[1].Reason, entries[1].IsRework));
        Assert.Equal(3, problems.Count);
        Assert.All(problems, problem => Assert.Contains("übergangen", problem));

        // Nothing usable is a sentence, never an exception — the run goes on without resets.
        Assert.Empty(ChampionResets.Parse("{ kaputt", out var unreadable));
        Assert.Single(unreadable);
        Assert.Empty(ChampionResets.Parse("""{ "champion": "Mel" }""", out var notAList));
        Assert.Single(notAList);
        Assert.Empty(ChampionResets.Parse("[]", out var none));
        Assert.Empty(none);
        Assert.Empty(ChampionResets.Parse(null, out _));
    }

    [Fact]
    public void AnEntry_TakesTheChampionOutOfEveryCountOfThePatch()
    {
        var state = State();
        var lines = new List<string>();

        Assert.Equal(1, ChampionResets.Apply(state, Plan(MelHotfix), lines.Add));

        // Lane rows, her own row (games, bans, damage), builds, situational items: gone.
        Assert.DoesNotContain(state.Meta.Lanes, row => row[0] == Mel);
        Assert.DoesNotContain(state.Meta.Champions, row => row[0] == Mel);
        Assert.DoesNotContain(state.Tally.Builds, build => build.ChampionId == Mel);
        Assert.DoesNotContain(state.Situational, row => row[0] == Mel);
        // Every pair she is part of, on either side: duels, duos, pairs across lanes, matchup builds.
        Assert.Equal([[2, Ahri, 245, 20, 11]], state.Meta.Duels);
        Assert.Equal([[Jinx, 3, 412, 4, 60, 33]], state.Meta.Duos);
        Assert.Equal([[Ahri, 2, Jinx, 3, 44, 20]], state.Meta.Versus);
        Assert.Equal([[Ahri, 2, Jinx, 0, 8229, 3, 1]], state.Matchups);
        // Everybody else keeps what was counted.
        Assert.Equal([[Ahri, 2, 90, 46], [Jinx, 3, 120, 61]], state.Meta.Lanes);
        Assert.Equal(90, Assert.Single(state.Tally.Builds).Games);
        Assert.Equal([[Ahri, 2, 1, 2, 0, 3089, 30, 16]], state.Situational);
        // The games themselves stay — they are what was played — and so does what was looked at.
        Assert.Equal(2, state.Games.Count);
        Assert.Equal(1000, state.Meta.Matches);
        Assert.Equal([1, 2, 3], state.Seen);

        Assert.Single(state.AppliedResets);
        Assert.Contains("Mel", Assert.Single(lines));
    }

    [Fact]
    public void AnEntry_IsAppliedExactlyOnce()
    {
        var state = State();
        var plan = Plan(MelHotfix);
        ChampionResets.Apply(state, plan);

        // The next run counts her new games; the same entry must not take them away again.
        state.Meta.Lanes.Add([Mel, 2, 9, 5]);
        state.Tally.Builds.Add(new ChampionBuildStats { ChampionId = Mel, Lane = Lane.Mid, Tier = "platinum", Games = 9, Wins = 5 });

        Assert.Equal(0, ChampionResets.Apply(state, plan));
        Assert.Contains(state.Meta.Lanes, row => row[0] == Mel && row[2] == 9);
        Assert.Contains(state.Tally.Builds, build => build.ChampionId == Mel);

        // A second hotfix is a second entry (another note): applied once more, and only that one.
        var twice = Plan("""
            [{ "champion": "Mel", "patch": "16.23", "reason": "hotfix", "note": "Hotfix vom 20.11." },
             { "champion": "Mel", "patch": "16.23", "reason": "hotfix", "note": "Hotfix vom 25.11." }]
            """);
        Assert.Equal(1, ChampionResets.Apply(state, twice));
        Assert.DoesNotContain(state.Meta.Lanes, row => row[0] == Mel);
        Assert.Equal(2, state.AppliedResets.Count);
    }

    [Fact]
    public void OnlyEntriesOfTheRunningPatch_AreApplied_AndOnlyForKnownChampions()
    {
        var state = State("16.23");
        var lines = new List<string>();
        var plan = Plan("""
            [{ "champion": "Mel", "patch": "16.22", "reason": "hotfix" },
             { "champion": "Mel", "patch": "16.24", "reason": "rework" },
             { "champion": "Mell", "patch": "16.23", "reason": "hotfix" }]
            """);

        Assert.Equal(0, ChampionResets.Apply(state, plan, lines.Add));
        Assert.Contains(state.Meta.Lanes, row => row[0] == Mel);
        Assert.Empty(state.AppliedResets);
        // A name nobody knows is said, and stays open: the corrected entry is a new one anyway.
        Assert.Contains("unbekannt", Assert.Single(lines));

        // With the patch the old keys are of no use; they do not pile up in the state.
        state.AppliedResets = ["mel|16.22|hotfix|"];
        ChampionResets.Apply(state, ResetPlan.None);
        Assert.Empty(state.AppliedResets);
    }

    [Fact]
    public void ChampionsAreFoundByKeyAndByName()
    {
        var plan = Plan("""
            [{ "champion": "monkeyking", "patch": "16.23", "reason": "rework" },
             { "champion": "Wukong", "patch": "16.23", "reason": "hotfix" },
             { "champion": "Kai'Sa", "patch": "16.23", "reason": "hotfix" },
             { "champion": "kaisa", "patch": "16.23", "reason": "hotfix" }]
            """);

        Assert.Equal([62, 62, 145, 145], plan.Entries.Select(plan.IdOf));
    }

    [Fact]
    public async Task TheCollector_AppliesTheFileAtTheStartOfARun()
    {
        var items = ItemCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Riot", "item.json")));
        var state = State("16.22");   // the catalog's patch: no new patch starts
        var plan = Plan("""[{ "champion": "Mel", "patch": "16.22", "reason": "hotfix" }]""");
        var saved = 0;

        // No page is asked for: the run only loads, applies and saves.
        using var api = new RiotApiClient("RGAPI-test", new Unreachable());
        var options = new CollectorOptions { Tiers = ["platinum"], Duration = TimeSpan.FromMinutes(1), MaxPages = 0 };
        await new RiotCollector(api, items, options, resets: plan).RunAsync(state, _ => saved++, CancellationToken.None);

        Assert.Equal(1, saved);
        Assert.DoesNotContain(state.Meta.Lanes, row => row[0] == Mel);
        Assert.DoesNotContain(state.Tally.Builds, build => build.ChampionId == Mel);
        Assert.Single(state.AppliedResets);
        // The applied entry is part of what gets stored.
        Assert.Contains("appliedResets", System.Text.Json.JsonSerializer.Serialize(state, CollectorJson.Default.CollectorState));
    }

    [Fact]
    public void TheAramCount_LosesTheChampionToo()
    {
        var state = new AramState
        {
            Patch = "16.23",
            Platform = "euw1",
            Tally = new RiotAramFile
            {
                Patch = "16.23",
                Games = 500,
                Champions = [new AramChampion { ChampionId = Mel, Games = 60, Wins = 31 }, new AramChampion { ChampionId = Ahri, Games = 80, Wins = 40 }],
                Builds = [new ChampionBuildStats { ChampionId = Mel, Games = 60 }, new ChampionBuildStats { ChampionId = Ahri, Games = 80 }],
            },
        };
        var plan = Plan(MelHotfix);

        Assert.Equal(1, ChampionResets.Apply(state, plan));
        Assert.Equal(Ahri, Assert.Single(state.Tally.Champions).ChampionId);
        Assert.Equal(Ahri, Assert.Single(state.Tally.Builds).ChampionId);
        Assert.Equal(500, state.Tally.Games);
        Assert.Equal(0, ChampionResets.Apply(state, plan));
    }

    [Fact]
    public void TheFallbackExport_LeavesOutAChampionReworkedWithTheNewPatch()
    {
        // 16.24 has just begun: 300 games, so the files of 16.23 are published instead.
        var state = State("16.23");
        state.StartPatch("16.24", "euw1");
        state.Meta.Matches = 300;
        var plan = Plan("""
            [{ "champion": "Mel", "patch": "16.24", "reason": "rework" },
             { "champion": "Ahri", "patch": "16.24", "reason": "hotfix" },
             { "champion": "Jinx", "patch": "16.23", "reason": "rework" }]
            """);

        Assert.True(ExportSources.IsFallback(state, enoughGames: 2000));
        var source = ExportSources.Ranked(state, enoughGames: 2000, plan);

        Assert.True(source.IsFallback);
        Assert.Equal("16.23", source.Patch);
        // Mel's old kit is gone: no row of hers, alone or in a pair. A hotfix is no reason (the
        // fallback is another patch's count), and neither is a rework that came a patch earlier.
        Assert.Equal([Mel], source.LeftOut);
        Assert.DoesNotContain(source.Meta.Lanes, row => row[0] == Mel);
        Assert.DoesNotContain(source.Meta.Champions, row => row[0] == Mel);
        Assert.DoesNotContain(source.Meta.Duels, row => row[1] == Mel || row[2] == Mel);
        Assert.DoesNotContain(source.Meta.Versus, row => row[0] == Mel || row[2] == Mel);
        Assert.DoesNotContain(source.Builds.Builds, build => build.ChampionId == Mel);
        Assert.DoesNotContain(source.Situational, row => row[0] == Mel);
        Assert.Contains(source.Meta.Lanes, row => row[0] == Ahri);
        Assert.Contains(source.Meta.Lanes, row => row[0] == Jinx);
        Assert.Equal(1000, source.Meta.Matches);

        // The state keeps the previous patch whole: the measurement over three patches reads it.
        Assert.Contains(state.Previous!.Meta.Lanes, row => row[0] == Mel);
        Assert.Contains(state.Previous.Tally.Builds, build => build.ChampionId == Mel);

        // The published file itself: built from those counts, without her.
        var tally = new RiotMetaTally();
        tally.Add(source.Meta);
        var snapshot = RiotMetaExport.Build(tally.Rows(), [.. Champions], source.Patch, "platinum", "euw1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.Contains(snapshot.LaneStats, row => row.ChampionId == Ahri);
        Assert.DoesNotContain(snapshot.LaneStats, row => row.ChampionId == Mel);
        Assert.DoesNotContain(snapshot.Matchups, row => row.ChampionId == Mel || row.OpponentId == Mel);

        // Once the new patch has enough games it is published as it is, rework or not.
        state.Meta.Matches = 2500;
        Assert.False(ExportSources.Ranked(state, enoughGames: 2000, plan).IsFallback);
    }

    [Fact]
    public void TheAramFallback_LeavesTheReworkedChampionOutToo()
    {
        var previous = new RiotAramFile
        {
            Patch = "16.23",
            Games = 9000,
            Champions = [new AramChampion { ChampionId = Mel, Games = 600, Wins = 300 }, new AramChampion { ChampionId = Ahri, Games = 800, Wins = 400 }],
            Builds = [new ChampionBuildStats { ChampionId = Mel, Games = 600 }],
        };
        var state = new AramState { Patch = "16.24", Platform = "euw1", Previous = previous, Tally = new RiotAramFile { Patch = "16.24", Games = 40 } };

        var source = ExportSources.Aram(state, enoughGames: 500, Plan("""[{ "champion": "Mel", "patch": "16.24", "reason": "rework" }]"""));

        Assert.True(source.IsFallback);
        Assert.Equal([Mel], source.LeftOut);
        Assert.Equal(Ahri, Assert.Single(source.Tally.Champions).ChampionId);
        Assert.Empty(source.Tally.Builds);
        Assert.Equal(2, previous.Champions.Count);
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Dieser Lauf darf nichts abrufen.");
    }
}
