using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu.Models;
using DraftPilot.Core.Lcu;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Tools;

/// <summary>
/// Writes reference results of the 1.5 model for DirkDraft 2.0: whatever the new TypeScript core
/// computes from the same inputs has to come out the same (order identical, values within
/// ±0,05 points, identical tie groups). Every file carries its inputs next to its outputs, so the
/// new tests need nothing but this folder, the recordings beside it and the curated data files.
/// <para>
/// Deterministic on purpose: a fixed snapshot (<c>golden/snapshot.json</c>), the bundled traits and
/// seat priors from <c>data/</c>, no network. ARAM statistics, which 1.5 fetches live, come from a
/// fixed table written once (<c>aram-stats.json</c>).
/// </para>
/// </summary>
internal static class GoldenCommand
{
    private const int Limit = 8;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Drafts every scoring change has to be readable on: the examples from the README and from
    /// docs/modellpruefung.md, one blind seat per lane, and a full board.
    /// </summary>
    private static readonly DraftCase[] Drafts =
    [
        new("readme-mid-vs-jax-elise-syndra", Lane.Mid, ["Jax", "Elise", "Syndra"], ["Aatrox", "LeeSin"], Hover: "Ahri"),
        new("readme-top-vs-jax", Lane.Top, ["Jax"], ["Ahri", "Thresh"], Hover: "Darius"),
        new("readme-top-blind", Lane.Top, [], ["Ahri", "Thresh"], Hover: "Malphite"),
        new("pruefung-top-vs-jax", Lane.Top, ["Jax"], [], Hover: "Singed"),
        new("pruefung-adc-vs-ezreal-karma", Lane.Adc, ["Ezreal", "Karma"], [], Hover: "Jinx"),
        new("pruefung-mid-vs-syndra", Lane.Mid, ["Syndra"], [], Hover: "Katarina"),
        new("pruefung-support-vs-cait-lux", Lane.Support, ["Caitlyn", "Lux"], [], Hover: "Thresh"),
        new("pruefung-jungle-with-yasuo", Lane.Jungle, [], ["Yasuo"], Hover: "LeeSin"),
        new("blind-top", Lane.Top, [], []),
        new("blind-jungle", Lane.Jungle, [], []),
        new("blind-mid", Lane.Mid, [], []),
        new("blind-adc", Lane.Adc, [], []),
        new("blind-support", Lane.Support, [], []),
        new("last-pick-top", Lane.Top, ["Darius", "Viego", "Ahri", "Jinx", "Thresh"], ["LeeSin", "Syndra", "Kaisa", "Nautilus"], Hover: "Garen"),
        new("last-pick-support", Lane.Support, ["Malphite", "Vi", "Orianna", "Caitlyn", "Lux"], ["Sett", "Graves", "Viktor", "Ezreal"], Hover: "Leona"),
        new("flex-enemies-mid", Lane.Mid, ["Sylas", "Pantheon", "Gragas"], ["Renekton"]),
    ];

    public static int Run(string[] args)
    {
        var root = FindRepositoryRoot();
        var outDir = Option(args, "--out") ?? Path.Combine(root, "app", "tests", "golden");
        var recordings = Option(args, "--recordings") ?? Path.Combine(root, "app", "tests", "recordings");
        var snapshotPath = Option(args, "--snapshot") ?? Path.Combine(outDir, "snapshot.json");

        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Referenz-Snapshot fehlt: {snapshotPath}");
            return 1;
        }

        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(snapshotPath), MetaJson.Default.MetaSnapshot)
            ?? throw new InvalidOperationException("Snapshot unlesbar.");
        var traits = TraitTable.Load(Path.Combine(root, "data", "champion_traits.json"));
        var seats = SeatPriors.Load(Path.Combine(root, "data", "pick_order_priors.json"));

        var aramStatsPath = Path.Combine(outDir, "aram-stats.json");
        var aramStats = LoadOrCreateAramStats(aramStatsPath, snapshot);

        Directory.CreateDirectory(Path.Combine(outDir, "drafts"));
        Directory.CreateDirectory(Path.Combine(outDir, "recordings"));
        Directory.CreateDirectory(Path.Combine(outDir, "builds"));

        var files = 0;

        foreach (var draft in Drafts)
        {
            var meta = new MetaLookup(snapshot);
            var resolver = new ChampionResolver(meta.Champions);
            var enemies = draft.Enemies.Select(name => Resolve(resolver, name)).ToList();
            var allies = draft.Allies.Select(name => Resolve(resolver, name)).ToList();
            var state = RecommendCommand.BuildState(draft.Lane, allies, enemies);
            var target = new TurnTracker().Resolve(state)
                ?? throw new InvalidOperationException($"Kein Ziel für {draft.Name}.");

            var hover = draft.Hover is { } hoverName ? Resolve(resolver, hoverName) : 0;

            var result = new
            {
                kind = "draft",
                name = draft.Name,
                input = new { lane = draft.Lane, enemies, allies, hover, limit = Limit },
                frame = Evaluate(meta, traits, seats, state, target, aramStats, hover),
            };

            Write(Path.Combine(outDir, "drafts", draft.Name + ".json"), result);
            files++;
        }

        foreach (var file in Directory.EnumerateFiles(recordings, "*.jsonl").Order(StringComparer.Ordinal))
        {
            Write(Path.Combine(outDir, "recordings", Path.GetFileNameWithoutExtension(file) + ".json"), Replay(file, snapshot, traits, seats, aramStats));
            files++;
        }

        files += WriteBuilds(root, Path.Combine(outDir, "builds"));

        Console.WriteLine($"{files} Referenzdateien → {outDir}");
        return 0;
    }

    /// <summary>
    /// Every frame of a recording, as the window would see it: the same deserialisation, the same
    /// new-draft rule and the same turn tracker carried across frames — without the 100 ms
    /// coalescing, which only merges frames and changes no result.
    /// </summary>
    private static object Replay(string path, MetaSnapshot snapshot, TraitTable traits, SeatPriors seats, IReadOnlyDictionary<int, AramStat> aramStats)
    {
        var meta = new MetaLookup(snapshot);
        var turns = new TurnTracker();
        var frames = new List<object>();
        var wasActive = false;
        var lastProgress = 0;
        var lastLocal = -1L;
        var index = -1;

        foreach (var line in File.ReadLines(path))
        {
            index++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string? sessionJson;
            string? phase;
            try
            {
                using var document = JsonDocument.Parse(line);
                var rootElement = document.RootElement;
                phase = rootElement.TryGetProperty("phase", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                sessionJson = rootElement.TryGetProperty("session", out var s) && s.ValueKind is not JsonValueKind.Null ? s.GetRawText() : null;
            }
            catch (JsonException)
            {
                frames.Add(new { line = index, skipped = "unlesbar" });
                continue;
            }

            if (phase is not null)
            {
                frames.Add(new { line = index, gameflowPhase = phase });
                continue;
            }

            ChampSelectSession? session = null;
            if (!string.IsNullOrWhiteSpace(sessionJson))
            {
                try
                {
                    session = JsonSerializer.Deserialize(sessionJson, LcuJson.Default.ChampSelectSession);
                }
                catch (JsonException)
                {
                    session = null;
                }
            }

            var state = DraftState.From(session);
            var progress = state.Unavailable.Count;
            var isNewDraft = state.IsActive && (!wasActive || progress < lastProgress || state.LocalCellId != lastLocal);
            if (isNewDraft)
                turns.Reset();

            wasActive = state.IsActive;
            lastProgress = progress;
            lastLocal = state.LocalCellId;

            var target = turns.Resolve(state);
            var hover = state.LocalSlot is { IsLocked: false, HoverChampionId: > 0 } local ? local.HoverChampionId : 0;

            frames.Add(new
            {
                line = index,
                isNewDraft,
                frame = Evaluate(meta, traits, seats, state, target, aramStats, hover),
            });
        }

        return new { kind = "recording", name = Path.GetFileName(path), frames };
    }

    /// <summary>Everything the window derives from one draft state, with the calls the window makes.</summary>
    private static object Evaluate(
        MetaLookup meta,
        TraitTable traits,
        SeatPriors seats,
        DraftState state,
        RecommendationTarget? target,
        IReadOnlyDictionary<int, AramStat> aramStats,
        int hover)
    {
        var predictor = new LanePredictor(meta, seats);
        var enemyPredictions = predictor.Predict(state.Enemies);
        var allyPredictions = predictor.Predict(state.Allies);
        var comp = new CompAnalyzer(meta, traits);
        var recommender = new Recommender(meta, traits);

        object? picks = null;
        object? bans = null;
        object? hoverResult = null;
        object? bench = null;

        if (state.IsActive && target is not null && state.Queue.UsesLanes())
        {
            picks = Set(recommender.Recommend(state, target with { Action = TurnAction.Pick }, enemyPredictions, allyPredictions, null, Limit));
            bans = Set(recommender.Recommend(state, target with { Action = TurnAction.Ban }, enemyPredictions, allyPredictions, null, Limit));
        }

        if (state.IsActive && hover != 0 && state.LocalSlot is { } mine && state.Queue.UsesLanes())
        {
            var evaluation = recommender.Evaluate(state, new RecommendationTarget(mine, TurnAction.Pick, IsFollowingTurn: false), hover, enemyPredictions, allyPredictions);
            if (evaluation is not null)
            {
                hoverResult = new
                {
                    championId = hover,
                    rank = evaluation.Rank,
                    candidates = evaluation.Ranking.Count,
                    lane = evaluation.Lane,
                    leader = evaluation.Ranking.Count > 0 ? Item(evaluation.Ranking[0]) : null,
                    item = Item(evaluation.Item),
                    decimals = ScoreError.Decimals(evaluation.Ranking.Take(Limit).ToList()),
                };
            }
        }

        if (state.IsActive && state.Queue.IsAram() && state.LocalSlot is { } own)
        {
            var stats = state.Bench.Append(own.EffectiveChampionId)
                .Where(id => id != 0 && aramStats.ContainsKey(id))
                .Distinct()
                .ToDictionary(id => id, id => aramStats[id]);

            var rows = AramAdvisor.Rank(own.EffectiveChampionId, state.Bench, stats, meta.ChampionName);
            bench = new
            {
                own = own.EffectiveChampionId,
                bench = state.Bench,
                rerolls = state.RerollsRemaining,
                tied = ScoreError.CountLeadingTies(rows),
                decimals = ScoreError.Decimals(rows),
                items = rows.Select(Item).ToList(),
            };
        }

        var balance = DraftBalance.Estimate(meta, state, allyPredictions, enemyPredictions);

        return new
        {
            state = new
            {
                state.IsActive,
                state.Phase,
                state.Queue,
                usesLanes = state.Queue.UsesLanes(),
                state.LocalCellId,
                allies = state.Allies,
                enemies = state.Enemies,
                state.AllyBans,
                state.EnemyBans,
                unavailable = state.Unavailable.Order().ToList(),
                state.Turn,
                state.Bench,
                state.BenchEnabled,
                state.RerollsRemaining,
            },
            target = target is null ? null : new { cellId = target.Slot.CellId, target.Action, target.IsFollowingTurn },
            predictions = new { enemies = Predictions(enemyPredictions), allies = Predictions(allyPredictions) },
            picks,
            bans,
            hover = hoverResult,
            comp = new
            {
                allies = Profile(comp.Analyze([.. state.Allies.Select(slot => slot.EffectiveChampionId)])),
                enemies = Profile(comp.Analyze([.. state.Enemies.Select(slot => slot.EffectiveChampionId)])),
            },
            balance = new { balance.AllyWinRate, balance.RatedChampions, balance.ContestedLanes, balance.IsComparable, balance.HasData },
            laneMatchups = LaneMatchups.For(meta, allyPredictions, enemyPredictions)
                .Select(m => new { m.Lane, m.AllyId, m.EnemyId, m.WinRate, m.Play, m.IsInferred, m.Measured })
                .ToList(),
            aramBench = bench,
        };
    }

    private static object Set(RecommendationSet set)
    {
        var isBan = set.Action == TurnAction.Ban;
        return new
        {
            set.CellId,
            set.Lane,
            set.Action,
            tied = isBan ? 0 : ScoreError.CountLeadingTies(set.Items),
            decimals = isBan ? 1 : ScoreError.Decimals(set.Items),
            items = set.Items.Select(Item).ToList(),
        };
    }

    private static object Item(Recommendation item) => new
    {
        item.ChampionId,
        item.Name,
        item.Score,
        item.Uncertainty,
        uncertaintyPoints = ScoreError.AsPoints(item.Uncertainty),
        reasons = item.Reasons.Select(reason => new { reason.Text, reason.Tone, reason.Hint }).ToList(),
        breakdown = item.Breakdown.Select(term => new { term.Kind, term.LogOdds, term.Points, term.IsGate, term.Gate, term.Label }).ToList(),
    };

    private static List<object> Predictions(LanePredictionResult result)
        => [.. result.Predictions.Select(p => (object)new
        {
            p.CellId,
            p.ChampionId,
            p.Lane,
            p.Confidence,
            p.Probabilities,
            p.IsManual,
            p.IsUncertain,
        })];

    private static object Profile(CompProfile profile) => new
    {
        profile.Count,
        profile.PhysicalShare,
        profile.MagicShare,
        profile.FrontlineCount,
        profile.RangedCount,
        profile.MaxEngage,
        profile.MaxPeel,
        profile.TotalCrowdControl,
        profile.LateScalingCount,
        profile.TraitsKnown,
        profile.TraitCoverage,
        findings = profile.Findings.Select(f => new { f.Issue, f.Text, f.Severity }).ToList(),
    };

    /// <summary>
    /// Build selection: the two OP.GG answers kept as fixtures parsed into plans, the variant ranking
    /// (Wilson lower bound) on sets with the sample sizes the README discusses, skill-order extension
    /// and the augment filter. 2.0 takes its builds from its own data, but these rules stay.
    /// </summary>
    private static int WriteBuilds(string root, string dir)
    {
        var fixtures = Path.Combine(root, "tests", "DraftPilot.Core.Tests", "Fixtures", "OpGg");
        var darius = new ChampionEntry { Id = 122, Key = "Darius", Name = "Darius" };

        var guide = MatchupGuideParser.Parse(
            File.ReadAllText(Path.Combine(fixtures, "matchup-guide-darius-jax.json")), 122, "Darius", 24, "Jax", Lane.Top, "16.18.1");
        var analysis = AnalysisBuildParser.Parse(
            OpGgResponseParser.Parse(File.ReadAllText(Path.Combine(fixtures, "analysis-build-darius-top.txt"))), darius, Lane.Top, string.Empty, "16.18.1", "ranked-all");

        Write(Path.Combine(dir, "plans.json"), new
        {
            kind = "plans",
            note = "Eingaben: tests/DraftPilot.Core.Tests/Fixtures/OpGg; FetchedAtUtc ist nicht Teil des Vergleichs.",
            matchupGuide = guide,
            laneBuild = analysis,
        });

        ItemSet Set(string label, int play, int wins) => new() { Items = [label], ItemIds = [play], Play = play, WinRate = play > 0 ? (double)wins / play : 0 };

        List<ItemSet>[] choiceCases =
        [
            [Set("dünn, glänzend", 13, 8), Set("breit", 113, 55), Set("mittel", 60, 31)],
            [Set("Stahlkappen", 49535, 23034), Set("Merkurs", 12000, 5892), Set("Schnelligkeit", 3100, 1618), Set("ohne Spiele", 0, 0)],
            [Set("a", 1, 1), Set("b", 2, 1), Set("c", 400, 200), Set("d", 400, 210)],
        ];

        Write(Path.Combine(dir, "choice.json"), new
        {
            kind = "buildChoice",
            cases = choiceCases.Select(sets => new
            {
                input = sets,
                take = 3,
                lowerBounds = sets.Select(BuildChoice.LowerBound).ToList(),
                output = BuildChoice.ByWinRate(sets, take: 3).Select(s => s.Items[0]).ToList(),
            }).ToList(),
        });

        string?[][] skillCases =
        [
            ["Q", "E", "W", "Q", "Q", "R", "Q", "E", "Q", "E", "R", "E", "E", "W", "W"],
            ["Q", "W", "E"],
            [],
            ["E", "Q", "W", "Q", "Q", "R", "Q", "E", "E", "Q", "R", "E", "E", "W", "W", "R", "W", "W"],
        ];

        Write(Path.Combine(dir, "skills.json"), new
        {
            kind = "skillPlan",
            cases = skillCases.Select(order => new { input = order, output = SkillPlan.Extend(order) }).ToList(),
        });

        var augments = new List<AugmentOption>
        {
            new() { Id = 1, Name = "Grob 170", Tier = 1, Performance = 170, PickRate = 0 },
            new() { Id = 2, Name = "Grob 2/3", Tier = 2, Performance = 170.0 * 2 / 3, PickRate = 0.001 },
            new() { Id = 3, Name = "Beliebt hoch", Tier = 1, Performance = 101.37, PickRate = 0.31 },
            new() { Id = 4, Name = "Beliebt mittel", Tier = 2, Performance = 96.12, PickRate = 0.18 },
            new() { Id = 5, Name = "Selten", Tier = 3, Performance = 99.81, PickRate = 0.02 },
            new() { Id = 6, Name = "Beliebt niedrig", Tier = 3, Performance = 88.4, PickRate = 0.15 },
            new() { Id = 7, Name = "Mittel", Tier = 2, Performance = 93.07, PickRate = 0.12 },
        };

        Write(Path.Combine(dir, "augments.json"), new
        {
            kind = "augments",
            input = augments,
            coarse = augments.Select(a => AugmentAdvisor.IsCoarse(a.Performance)).ToList(),
            output = AugmentAdvisor.Rank(augments, take: 8),
        });

        return 5;
    }

    /// <summary>
    /// The ARAM table the bench is ranked on. 1.5 fetches these live per draft; for a reference that
    /// must not move, it is written once — rounded to whole points like OP.GG's field, plays between
    /// 2.000 and 40.000 — and read back on every later run.
    /// </summary>
    private static IReadOnlyDictionary<int, AramStat> LoadOrCreateAramStats(string path, MetaSnapshot snapshot)
    {
        if (File.Exists(path))
        {
            var stored = JsonSerializer.Deserialize<List<AramStat>>(File.ReadAllText(path), Json) ?? [];
            return stored.ToDictionary(s => s.ChampionId);
        }

        var stats = snapshot.Champions
            .Select(c => new AramStat(c.Id, Math.Round(0.45 + ((c.Id * 7919L) % 110) / 1000.0, 2), 2000 + (int)((c.Id * 104729L) % 38000)))
            .OrderBy(s => s.ChampionId)
            .ToList();

        Write(path, stats);
        return stats.ToDictionary(s => s.ChampionId);
    }

    private static int Resolve(ChampionResolver resolver, string name)
        => resolver.Resolve(name) ?? throw new InvalidOperationException($"Unbekannter Champion: {name}");

    private static void Write(string path, object value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DraftPilot.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("DraftPilot.sln nicht gefunden — aus dem Repository starten.");
    }

    private sealed record DraftCase(string Name, Lane Lane, string[] Enemies, string[] Allies, string? Hover = null);
}
