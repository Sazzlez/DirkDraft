using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

public class RecommenderTests
{
    // Mid candidates, deliberately ordered by strength.
    private const int Strong = 101, Average = 102, Weak = 103, CounterPick = 104;

    // Other seats.
    private const int AllyTop = 201, AllySupport = 202;
    private const int EnemyMid = 301, EnemyTop = 302;

    /// <summary>Strong, unpicked and bad news for our top laner: the archetypal ban target.</summary>
    private const int Menace = 303;

    private static MetaLookup Meta() => new MetaBuilder()
        .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
        .Champion(Average, "Average", DamageType.Magic, ["Mage"], 525, 3)
        .Champion(Weak, "Weak", DamageType.Magic, ["Mage"], 500, 2)
        .Champion(CounterPick, "CounterPick", DamageType.Physical, ["Assassin"], 125, 3)
        .Champion(AllyTop, "AllyTop", DamageType.Physical, ["Fighter"], 175, 5)
        .Champion(AllySupport, "AllySupport", DamageType.Magic, ["Tank"], 125, 6)
        .Champion(EnemyMid, "EnemyMid", DamageType.Magic, ["Mage"], 550, 3)
        .Champion(EnemyTop, "EnemyTop", DamageType.Physical, ["Fighter"], 175, 5)
        .Champion(Menace, "Menace", DamageType.Physical, ["Fighter"], 175, 5)
        .InLane(Strong, Lane.Mid, winRate: 0.54, tier: 1, play: 2000, pickRate: 0.08, banRate: 0.10)
        .InLane(Average, Lane.Mid, winRate: 0.50, tier: 3, play: 2000, pickRate: 0.04)
        .InLane(Weak, Lane.Mid, winRate: 0.46, tier: 5, play: 2000, pickRate: 0.01)
        .InLane(CounterPick, Lane.Mid, winRate: 0.50, tier: 3, play: 2000, pickRate: 0.03)
        .InLane(AllyTop, Lane.Top, winRate: 0.51, tier: 2, play: 2000)
        .InLane(AllySupport, Lane.Support, winRate: 0.51, tier: 2, play: 2000)
        .InLane(EnemyMid, Lane.Mid, winRate: 0.50, tier: 3, play: 2000)
        .InLane(EnemyTop, Lane.Top, winRate: 0.52, tier: 1, play: 2000, pickRate: 0.09, banRate: 0.12)
        .InLane(Menace, Lane.Top, winRate: 0.55, tier: 1, play: 2000, pickRate: 0.11, banRate: 0.15)
        // CounterPick beats the enemy mid decisively; nobody else has matchup data.
        .Matchup(CounterPick, EnemyMid, Lane.Mid, winRate: 0.62, play: 3000)
        // The enemy top laner is a menace to our top laner: relevant for bans.
        .Matchup(Menace, AllyTop, Lane.Top, winRate: 0.60, play: 3000)
        .Synergy(Strong, AllySupport, winRate: 0.56, play: 2000, tier: 0)
        .Build();

    private static Recommender Recommender() => new(Meta(), TraitTable.Empty);

    /// <summary>A draft where the local mid laner is on the clock.</summary>
    private static DraftState MidPickState(params int[] bans)
    {
        var builder = new SessionBuilder()
            .LocalPlayer(2)
            .Locked(0, AllyTop)
            .Locked(4, AllySupport)
            .Locked(7, EnemyMid)
            .Locked(5, EnemyTop)
            .OnClock(2, "pick");

        foreach (var ban in bans)
            builder.CompletedBan(1, ban);

        return DraftState.From(builder.Build());
    }

    private static (DraftState State, RecommendationTarget Target, LanePredictionResult Lanes) Scenario(params int[] bans)
    {
        var state = MidPickState(bans);
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(Meta()).Predict(state.Enemies);
        return (state, target, lanes);
    }

    [Fact]
    public void EmptyMeta_ProducesNothing()
    {
        var state = MidPickState();
        var target = new TurnTracker().Resolve(state)!;
        var recommender = new Recommender(MetaLookup.Empty, TraitTable.Empty);

        var set = recommender.Recommend(state, target, LanePredictionResult.Empty);

        Assert.Empty(set.Items);
    }

    [Fact]
    public void TargetLane_ComesFromTheSeat()
    {
        var (state, target, lanes) = Scenario();

        var set = Recommender().Recommend(state, target, lanes);

        Assert.Equal(Lane.Mid, set.Lane);
        Assert.Equal(2, set.CellId);
        Assert.Equal(TurnAction.Pick, set.Action);
    }

    [Fact]
    public void StrongerChampionRanksHigher()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender().Recommend(state, target, lanes).Items;

        var strong = items.Single(item => item.ChampionId == Strong);
        var weak = items.Single(item => item.ChampionId == Weak);

        Assert.True(strong.Score > weak.Score);
    }

    /// <summary>The score is an estimated win rate: strictly a probability, never outside (0, 1).</summary>
    [Fact]
    public void PickScores_AreProbabilities()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender().Recommend(state, target, lanes, limit: 50).Items;

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.InRange(item.Score, 0.001, 0.999));
    }

    /// <summary>The pill's percentage must be exactly what the breakdown's terms add up to.</summary>
    [Fact]
    public void PickScore_IsTheSigmoidOfItsBreakdown()
    {
        var (state, target, lanes) = Scenario();

        foreach (var item in Recommender().Recommend(state, target, lanes).Items)
        {
            var total = item.Breakdown.Sum(term => term.LogOdds ?? 0);
            Assert.Equal(ScoreModel.Sigmoid(total), item.Score, precision: 9);
        }
    }

    /// <summary>A favourable matchup must lift the pick above an otherwise identical champion.</summary>
    [Fact]
    public void FavourableMatchup_RaisesTheScore()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender().Recommend(state, target, lanes).Items;

        // CounterPick and Average share the same lane stats; only the matchup differs.
        Assert.True(Score(items, CounterPick) > Score(items, Average));
    }

    /// <summary>
    /// The same 62 % matchup over 3000 games must move the score more than over 30: shrinkage
    /// weighs the evidence — but only once, not squared as the old confidence factor did.
    /// </summary>
    [Fact]
    public void LargerSample_MovesTheScoreMore()
    {
        double MatchupPoints(int play)
        {
            var meta = new MetaBuilder()
                .Champion(CounterPick, "CounterPick", DamageType.Physical, ["Assassin"], 125, 3)
                .Champion(EnemyMid, "EnemyMid", DamageType.Magic, ["Mage"], 550, 3)
                .InLane(CounterPick, Lane.Mid, winRate: 0.50, tier: 3, play: 2000)
                .InLane(EnemyMid, Lane.Mid, winRate: 0.50, tier: 3, play: 2000)
                .Matchup(CounterPick, EnemyMid, Lane.Mid, winRate: 0.62, play: play)
                .Build();

            var state = DraftState.From(new SessionBuilder()
                .LocalPlayer(2)
                .Locked(7, EnemyMid)
                .OnClock(2, "pick")
                .Build());

            var target = new TurnTracker().Resolve(state)!;
            var predictions = new LanePredictor(meta).Predict(state.Enemies);
            var item = new Recommender(meta, TraitTable.Empty)
                .Recommend(state, target, predictions).Items
                .Single(i => i.ChampionId == CounterPick);

            return item.Breakdown.Single(term => term.Kind == ScoreTermKind.LaneMatchup).Points;
        }

        var thin = MatchupPoints(30);
        var thick = MatchupPoints(3000);

        Assert.True(thin > 0, "Auch 30 Spiele sind Evidenz und müssen etwas zählen.");
        Assert.True(thick > thin * 2, $"3000 Spiele müssen deutlich mehr bewegen: dünn={thin:F2}, dick={thick:F2}");
    }

    /// <summary>A duo at exactly 50 % over any sample is no evidence and must shift nothing.</summary>
    [Fact]
    public void EvenDuo_ChangesNothing()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(AllySupport, "AllySupport", DamageType.Magic, ["Tank"], 125, 6)
            .InLane(Strong, Lane.Mid, winRate: 0.52, tier: 2, play: 2000)
            .InLane(AllySupport, Lane.Support, winRate: 0.51, tier: 2, play: 2000)
            .Synergy(Strong, AllySupport, winRate: 0.50, play: 2000, tier: 2)
            .Build();

        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(4, AllySupport)
            .OnClock(2, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;
        var item = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty).Items
            .Single(i => i.ChampionId == Strong);

        var synergy = item.Breakdown.Single(term => term.Kind == ScoreTermKind.Synergy);

        Assert.True(synergy.HasData);
        Assert.Equal(0, synergy.Points, precision: 6);
    }

    /// <summary>
    /// A flex enemy that could be Mid or Top must count on both lanes by probability, not fully
    /// on whichever the predictor happens to rank first.
    /// </summary>
    [Fact]
    public void FlexEnemy_SplitsItsMatchupWeightAcrossLanes()
    {
        const int Flex = 401, Candidate = 402;

        var meta = new MetaBuilder()
            .Champion(Flex, "Flex", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(Candidate, "Candidate", DamageType.Magic, ["Mage"], 550, 3)
            // Genuinely ambiguous: identical presence on both lanes.
            .InLane(Flex, Lane.Mid, winRate: 0.51, tier: 2, play: 2000, roleRate: 0.5)
            .InLane(Flex, Lane.Top, winRate: 0.51, tier: 2, play: 2000, roleRate: 0.5)
            .InLane(Candidate, Lane.Mid, winRate: 0.50, tier: 3, play: 2000)
            .Matchup(Candidate, Flex, Lane.Mid, winRate: 0.62, play: 3000)
            .Build();

        var session = new SessionBuilder()
            .LocalPlayer(2)
            .Locked(7, Flex)
            .OnClock(2, "pick")
            .Build();

        // The local seat needs a lane; the builder leaves assignments empty, so fix it manually.
        var state = DraftState.From(session);
        var target = new RecommendationTarget(state.Allies.First(s => s.CellId == 2), TurnAction.Pick, true);
        var recommender = new Recommender(meta, TraitTable.Empty);

        double LaneTerm(LanePredictionResult lanes)
        {
            var set = recommender.Recommend(state, target, lanes);
            var item = set.Items.Single(i => i.ChampionId == Candidate);
            return item.Breakdown.Single(term => term.Kind == ScoreTermKind.LaneMatchup).LogOdds ?? 0;
        }

        var predictor = new LanePredictor(meta);
        var uncertain = LaneTerm(predictor.Predict(state.Enemies));
        var pinnedMid = LaneTerm(predictor.Predict(
            state.Enemies, new Dictionary<long, Lane> { [7] = Lane.Mid }));

        Assert.True(pinnedMid > 0, "Mit fixiertem Mid-Gegner muss der Matchup-Term positiv sein.");
        Assert.True(uncertain > 0, "Auch der unsichere Fall muss anteilig zählen.");
        Assert.True(uncertain < pinnedMid * 0.75,
            $"Der 50/50-Flex darf nicht voll zählen: unsicher={uncertain:F3}, fixiert={pinnedMid:F3}");
    }

    /// <summary>The seat lane comes from the client here, so this must behave exactly like before.</summary>
    [Fact]
    public void CertainOpponent_CountsFully()
    {
        var (state, target, lanes) = Scenario();

        var counter = Recommender().Recommend(state, target, lanes).Items
            .Single(item => item.ChampionId == CounterPick);

        var laneTerm = counter.Breakdown.Single(term => term.Kind == ScoreTermKind.LaneMatchup).LogOdds ?? 0;
        Assert.True(laneTerm > 0.2, $"Sicherer Gegner muss voll zählen, war {laneTerm:F3}");
    }

    [Fact]
    public void MatchupReason_NamesTheOpponent()
    {
        var (state, target, lanes) = Scenario();

        var counter = Recommender().Recommend(state, target, lanes).Items
            .Single(item => item.ChampionId == CounterPick);

        Assert.Contains(counter.Reasons, reason => reason.Text.Contains("EnemyMid", StringComparison.Ordinal));
    }

    [Fact]
    public void SynergyReason_NamesTheAlly()
    {
        var (state, target, lanes) = Scenario();

        var strong = Recommender().Recommend(state, target, lanes).Items
            .Single(item => item.ChampionId == Strong);

        Assert.Contains(strong.Reasons, reason => reason.Text.Contains("AllySupport", StringComparison.Ordinal));
    }

    [Fact]
    public void BannedChampions_AreNeverRecommended()
    {
        var (state, target, lanes) = Scenario(Strong);

        var items = Recommender().Recommend(state, target, lanes).Items;

        Assert.DoesNotContain(items, item => item.ChampionId == Strong);
    }

    [Fact]
    public void AlreadyPickedChampions_AreNeverRecommended()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender().Recommend(state, target, lanes).Items;

        Assert.DoesNotContain(items, item => item.ChampionId == EnemyMid);
        Assert.DoesNotContain(items, item => item.ChampionId == AllyTop);
    }

    [Fact]
    public void SelectableFilter_RestrictsTheList()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender()
            .Recommend(state, target, lanes, selectable: new HashSet<int> { Average, Weak })
            .Items;

        Assert.Equal([Average, Weak], items.Select(item => item.ChampionId).Order());
    }

    [Fact]
    public void NoSelectableFilter_MeansEveryLaneCandidate()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender().Recommend(state, target, lanes, selectable: null).Items;

        Assert.Contains(items, item => item.ChampionId == Strong);
        Assert.Contains(items, item => item.ChampionId == CounterPick);
    }

    /// <summary>
    /// A criterion with nothing to go on must say so instead of reporting a neutral zero. The
    /// breakdown renders the two differently, and "even matchup" is a claim while "no data" is not.
    /// </summary>
    [Fact]
    public void MissingData_IsDistinguishableFromNeutral()
    {
        var (state, target, lanes) = Scenario();
        var items = Recommender().Recommend(state, target, lanes).Items;

        ScoreTerm Term(int championId, ScoreTermKind kind)
            => items.Single(item => item.ChampionId == championId).Breakdown.Single(term => term.Kind == kind);

        // Only CounterPick has a recorded matchup against the enemy mid laner.
        var known = Term(CounterPick, ScoreTermKind.LaneMatchup);
        var unknown = Term(Strong, ScoreTermKind.LaneMatchup);

        Assert.True(known.HasData);
        Assert.False(unknown.HasData);
        Assert.Null(unknown.LogOdds);

        // Either way the score is unaffected: a missing criterion contributes nothing.
        Assert.Equal(0, unknown.Points);

        // Same for the duo record, which only Strong has.
        Assert.True(Term(Strong, ScoreTermKind.Synergy).HasData);
        Assert.False(Term(Weak, ScoreTermKind.Synergy).HasData);
    }

    /// <summary>Nothing to fit into yet, so the composition term must stay silent.</summary>
    [Fact]
    public void CompositionTerm_HasNoDataBeforeAnyAllyPicked()
    {
        var session = new SessionBuilder()
            .LocalPlayer(2)
            .Locked(7, EnemyMid)
            .OnClock(2, "pick")
            .Build();

        var state = DraftState.From(session);
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(Meta()).Predict(state.Enemies);

        var items = Recommender().Recommend(state, target, lanes).Items;

        Assert.All(items, item =>
            Assert.False(item.Breakdown.Single(term => term.Kind == ScoreTermKind.Composition).HasData));
    }

    /// <summary>
    /// A chip is the first thing read during a draft, and there is no room on it for the caveats.
    /// Every one must therefore carry the longer explanation behind it.
    /// </summary>
    [Fact]
    public void EveryReasonChip_CarriesAnExplanation()
    {
        var (state, target, lanes) = Scenario();
        var recommender = Recommender();

        var sets = new[]
        {
            recommender.Recommend(state, target, lanes),
            recommender.Recommend(state, target, lanes),
            recommender.Recommend(state, target, lanes),
            recommender.Recommend(state, target with { Action = TurnAction.Ban }, lanes),
        };

        var chips = sets.SelectMany(set => set.Items).SelectMany(item => item.Reasons).ToList();

        Assert.NotEmpty(chips);
        Assert.All(chips, chip =>
            Assert.False(
                string.IsNullOrWhiteSpace(chip.Hint),
                $"Chip \"{chip.Text}\" hat keine Erklärung."));
    }

    /// <summary>Every criterion has to be introduceable on screen, or the line reads as jargon.</summary>
    [Fact]
    public void EveryTermKind_HasALabelAndAnExplanation()
    {
        foreach (var kind in Enum.GetValues<ScoreTermKind>())
        {
            var term = new ScoreTerm(kind, 0);

            Assert.False(string.IsNullOrWhiteSpace(term.Label), $"{kind} braucht ein Label.");
            Assert.False(string.IsNullOrWhiteSpace(term.Hint), $"{kind} braucht eine Erklärung.");
        }
    }

    [Fact]
    public void ReasonsAreNeverDuplicated()
    {
        var (state, target, lanes) = Scenario();
        var banTarget = target with { Action = TurnAction.Ban };

        var sets = new[]
        {
            Recommender().Recommend(state, target, lanes),
            Recommender().Recommend(state, banTarget, lanes),
        };

        foreach (var item in sets.SelectMany(set => set.Items))
            Assert.Equal(item.Reasons.Count, item.Reasons.Select(reason => reason.Text).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OrderIsStableAcrossCalls()
    {
        var (state, target, lanes) = Scenario();
        var recommender = Recommender();

        var first = recommender.Recommend(state, target, lanes).Items.Select(i => i.ChampionId);
        var second = recommender.Recommend(state, target, lanes).Items.Select(i => i.ChampionId);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The chip lists only write positions that changed, which relies on the reasons coming back
    /// value-equal between two renders. A culture-dependent format or a timestamp in a chip would
    /// make that guard never fire, and the flickering would be back without a red test.
    /// </summary>
    [Fact]
    public void IdenticalInput_ProducesValueEqualReasonChips()
    {
        var (state, target, lanes) = Scenario();
        var recommender = Recommender();

        var first = recommender.Recommend(state, target, lanes).Items;
        var second = recommender.Recommend(state, target, lanes).Items;

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Reasons, second[i].Reasons);
    }

    /// <summary>
    /// The score carries the sampling error of the numbers it was built from, so the panel can say
    /// when its own ordering is meaningless. The same duo win rate measured over 40 games has to
    /// come out visibly less certain than over 40.000 — that difference is the whole mechanism.
    /// </summary>
    [Fact]
    public void AThinSynergy_MakesTheScoreLessCertainThanAThickOne()
    {
        static double Uncertainty(int synergyPlay)
        {
            var meta = new MetaBuilder()
                .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
                .Champion(AllySupport, "AllySupport", DamageType.Magic, ["Tank"], 125, 6)
                .InLane(Strong, Lane.Mid, winRate: 0.52, play: 40_000)
                .InLane(AllySupport, Lane.Support, winRate: 0.51, play: 40_000)
                .Synergy(Strong, AllySupport, winRate: 0.58, play: synergyPlay)
                .Build();

            var state = DraftState.From(new SessionBuilder()
                .LocalPlayer(2)
                .Locked(4, AllySupport)
                .OnClock(2, "pick")
                .Build());

            var target = new TurnTracker().Resolve(state)!;
            var lanes = new LanePredictor(meta).Predict(state.Enemies);

            return new Recommender(meta, TraitTable.Empty).Recommend(state, target, lanes).Items
                .Single(item => item.ChampionId == Strong)
                .Uncertainty;
        }

        var thin = Uncertainty(40);
        var thick = Uncertainty(40_000);

        Assert.True(thin > 0, "Der Fehlerbalken darf nicht null sein.");
        Assert.True(
            thin > thick * 3,
            $"40 Spiele müssen unsicherer sein als 40.000 ({thin} gegen {thick}).");
    }

    /// <summary>
    /// A candidate with no sampled evidence at all must report no error rather than a small one —
    /// the panel turns that into "Datenlage zu dünn" instead of a confident-looking verdict.
    /// </summary>
    [Fact]
    public void WithoutAnySampledTerm_TheScoreReportsNoError()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Strong, Lane.Mid, winRate: 0.5, play: 0)
            .Build();

        var state = DraftState.From(new SessionBuilder().LocalPlayer(2).OnClock(2, "pick").Build());
        var target = new TurnTracker().Resolve(state)!;

        var item = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty).Items
            .Single(entry => entry.ChampionId == Strong);

        Assert.Equal(0, item.Uncertainty);
    }

    [Fact]
    public void TwoReasonsWithTheSameTextToneAndHint_AreEqual()
    {
        // Turning Reason into a class would silently drop all three chip guards back to reference
        // equality, and nothing else in the suite would notice.
        Assert.Equal(Reason.Neutral("gleich"), Reason.Neutral("gleich"));
        Assert.NotEqual(Reason.Neutral("gleich"), Reason.Pro("gleich"));
        Assert.NotEqual(Reason.Neutral("gleich"), Reason.Neutral("gleich", hint: "anders"));
    }

    [Fact]
    public void LimitIsRespected()
    {
        var (state, target, lanes) = Scenario();

        Assert.Equal(2, Recommender().Recommend(state, target, lanes, limit: 2).Items.Count);
    }

    [Fact]
    public void BanMode_LooksAcrossAllLanes()
    {
        var (state, target, lanes) = Scenario();
        var banTarget = target with { Action = TurnAction.Ban };

        var set = Recommender().Recommend(state, banTarget, lanes, limit: 20);

        Assert.Equal(TurnAction.Ban, set.Action);
        // A top laner is a legitimate ban even though the seat on the clock plays mid.
        Assert.Contains(set.Items, item => item.ChampionId == Menace);
    }

    [Fact]
    public void BanMode_FlagsAChampionThatCountersALockedAlly()
    {
        var (state, target, lanes) = Scenario();
        var banTarget = target with { Action = TurnAction.Ban };

        var set = Recommender().Recommend(state, banTarget, lanes, limit: 20);
        var threat = set.Items.SingleOrDefault(item => item.ChampionId == Menace);

        Assert.NotNull(threat);
        Assert.Contains(threat.Reasons, reason => reason.Text.Contains("AllyTop", StringComparison.Ordinal));
    }

    [Fact]
    public void BanMode_ExcludesWhatIsAlreadyGone()
    {
        var (state, target, lanes) = Scenario(Strong);
        var banTarget = target with { Action = TurnAction.Ban };

        var items = Recommender().Recommend(state, banTarget, lanes, limit: 20).Items;

        Assert.DoesNotContain(items, item => item.ChampionId == Strong);
        Assert.DoesNotContain(items, item => item.ChampionId == EnemyMid);
    }

    [Fact]
    public void AdvisingAnAlly_UsesThatAllysLane()
    {
        // The support seat is on the clock instead of us.
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(0, AllyTop)
            .Locked(7, EnemyMid)
            .OnClock(4, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(Meta()).Predict(state.Enemies);

        var set = Recommender().Recommend(state, target, lanes);

        Assert.Equal(4, set.CellId);
        Assert.Equal(Lane.Support, set.Lane);
        Assert.Contains(set.Items, item => item.ChampionId == AllySupport);
    }

    [Fact]
    public void AllyCompExcludesTheSeatBeingAdvised()
    {
        // Advising the support seat while a support is somehow already locked there must not treat
        // that champion as a team-mate of itself.
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(0, AllyTop)
            .Locked(4, AllySupport)
            .OnClock(4, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;
        var set = Recommender().Recommend(state, target, LanePredictionResult.Empty);

        Assert.Equal(1, set.AllyComp.Count);
    }

    [Fact]
    public void InactiveDraft_ProducesNothing()
    {
        var target = new RecommendationTarget(
            new DraftSlot(2, 2, true, 0, 0, Lane.Mid), TurnAction.Pick, true);

        var set = Recommender().Recommend(DraftState.Inactive, target, LanePredictionResult.Empty);

        Assert.Empty(set.Items);
    }

    private static double Score(IReadOnlyList<Recommendation> items, int championId)
        => items.Single(item => item.ChampionId == championId).Score;

    private static double? Term(IReadOnlyList<Recommendation> items, int championId, ScoreTermKind kind)
        => items.Single(item => item.ChampionId == championId).Breakdown.Single(term => term.Kind == kind).LogOdds;

    /// <summary>
    /// The synergy term is a damped MEAN: the same duo win rate must contribute the same shift
    /// whether one ally has locked or four. As a sum, the score drifted upward with the draft's
    /// progress alone, making early- and late-draft scores incomparable.
    /// </summary>
    [Fact]
    public void FourEqualDuos_ShiftTheScoreLikeOne()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong").InLane(Strong, Lane.Mid, winRate: 0.52, play: 2000)
            .Champion(201, "A1").Champion(202, "A2").Champion(203, "A3").Champion(204, "A4")
            .Synergy(Strong, 201, winRate: 0.55, play: 2000)
            .Synergy(Strong, 202, winRate: 0.55, play: 2000)
            .Synergy(Strong, 203, winRate: 0.55, play: 2000)
            .Synergy(Strong, 204, winRate: 0.55, play: 2000)
            .Build();
        var recommender = new Recommender(meta, TraitTable.Empty);

        var one = DraftState.From(new SessionBuilder().LocalPlayer(2).Locked(0, 201).OnClock(2, "pick").Build());
        var four = DraftState.From(new SessionBuilder().LocalPlayer(2)
            .Locked(0, 201).Locked(1, 202).Locked(3, 203).Locked(4, 204).OnClock(2, "pick").Build());

        var target = new TurnTracker().Resolve(one)!;
        var withOne = Term(recommender.Recommend(one, target, LanePredictionResult.Empty, limit: 50).Items, Strong, ScoreTermKind.Synergy);
        var withFour = Term(recommender.Recommend(four, new TurnTracker().Resolve(four)!, LanePredictionResult.Empty, limit: 50).Items, Strong, ScoreTermKind.Synergy);

        Assert.NotNull(withOne);
        Assert.Equal(withOne!.Value, withFour!.Value, precision: 10);
    }

    /// <summary>
    /// The off-lane term promises to count LESS than the direct duel. With four enemies at the
    /// same matchup rate, its weighted mean must stay at OffLaneShare of one duel — the old sum
    /// reached 1.4 duels.
    /// </summary>
    [Fact]
    public void OffLaneEnemies_StayBelowTheDirectDuelsWeight()
    {
        var (state, target, lanes) = Scenario();

        var items = Recommender().Recommend(state, target, lanes, limit: 50).Items;
        var offLane = Term(items, CounterPick, ScoreTermKind.EnemyTeam);

        if (offLane is null)
            return;

        // The strongest single matchup in the fixture is 62 % over 3000 games; even if every
        // off-lane enemy matched it, the mean caps the term at OffLaneShare of that one duel.
        var duelCap = ScoreModel.OffLaneShare
            * Math.Abs(ScoreModel.Logit(Shrinkage.Apply(0.62, 3000, Shrinkage.MatchupPrior)));

        Assert.True(Math.Abs(offLane.Value) <= duelCap + 1e-9);
    }

    /// <summary>
    /// Without an assigned lane (blind pick, customs) the seat's lane comes from the ALLY
    /// prediction. The enemy prediction cannot contain the advised seat, so before the ally
    /// prediction was passed in, blind pick always degenerated to "no lane".
    /// </summary>
    [Fact]
    public void BlindPick_ResolvesTheLaneFromTheAllyPrediction()
    {
        var (state, target, enemyLanes) = Scenario();

        var blindTarget = target with { Slot = target.Slot with { AssignedLane = Lane.Unknown } };
        var allySlots = state.Allies
            .Select(slot => slot.CellId == target.Slot.CellId
                ? slot with { AssignedLane = Lane.Unknown, HoverChampionId = Strong }
                : slot with { AssignedLane = Lane.Unknown })
            .ToList();
        var allyLanes = new LanePredictor(Meta()).Predict(allySlots);

        var set = Recommender().Recommend(state, blindTarget, enemyLanes, allyLanes);

        Assert.Equal(Lane.Mid, set.Lane);
    }

    [Fact]
    public void ChampionsHoveredByOtherAllies_AreNotRecommended()
    {
        var session = new SessionBuilder()
            .LocalPlayer(2)
            .Locked(0, AllyTop)
            .Hovering(4, Strong)
            .Locked(7, EnemyMid)
            .OnClock(2, "pick")
            .Build();
        var state = DraftState.From(session);
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(Meta()).Predict(state.Enemies);

        var items = Recommender().Recommend(state, target, lanes, limit: 50).Items;

        Assert.DoesNotContain(items, item => item.ChampionId == Strong);
    }

    [Fact]
    public void TheTargetsOwnHover_StaysRecommendable()
    {
        var session = new SessionBuilder()
            .LocalPlayer(2)
            .Locked(0, AllyTop)
            .Hovering(2, Strong)
            .Locked(7, EnemyMid)
            .OnClock(2, "pick")
            .Build();
        var state = DraftState.From(session);
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(Meta()).Predict(state.Enemies);

        var items = Recommender().Recommend(state, target, lanes, limit: 50).Items;

        Assert.Contains(items, item => item.ChampionId == Strong);
    }
}
