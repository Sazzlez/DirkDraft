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
    /// The duel term is a difference, not an absolute rate: a champion that does against this
    /// opponent exactly what it does on the lane in general has learnt nothing from the matchup, so
    /// the term is zero. Adding the absolute rate instead counted the champion's general strength a
    /// second time — measured on the real snapshot, the duel rate tracks the lane rate with a slope
    /// of 1.23, worth 1.8 points of over-credit for a champion one standard deviation above the
    /// middle.
    /// </summary>
    [Fact]
    public void ADuelThatMatchesTheChampionsUsualRate_AddsNothing()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(EnemyMid, "EnemyMid", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Strong, Lane.Mid, winRate: 0.56, play: 40_000)
            .InLane(EnemyMid, Lane.Mid, winRate: 0.50, play: 40_000)
            // Exactly the champion's own lane rate: no news about this particular opponent.
            .Matchup(Strong, EnemyMid, Lane.Mid, winRate: 0.56, play: 40_000)
            .Build();

        // Not exactly zero: the lane rate is shrunk with prior 300 and the matchup with 150, so the
        // two differ in the fourth decimal even for identical raw rates. 0.0009 log-odds is 0.02
        // points of win rate — a rounding residual, not a term.
        Assert.Equal(0, DuelTerm(meta), precision: 2);
    }

    [Fact]
    public void ADuelAboveTheChampionsUsualRate_StillCounts()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(EnemyMid, "EnemyMid", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Strong, Lane.Mid, winRate: 0.50, play: 40_000)
            .InLane(EnemyMid, Lane.Mid, winRate: 0.50, play: 40_000)
            .Matchup(Strong, EnemyMid, Lane.Mid, winRate: 0.56, play: 40_000)
            .Build();

        Assert.True(DuelTerm(meta) > 0.2, "Ein echter Vorteil gegen den Gegner muss zählen.");
    }

    /// <summary>
    /// Two champions equally good against this opponent end up within a rounding error of each
    /// other, however different their general reputation — the duel measurement replaces the
    /// general one instead of stacking on it. Before centring, the stronger one kept its full
    /// reputation gap on top of an identical duel rate.
    /// <para>
    /// Not exactly equal, and correctly so: the opponent is only about 96 % likely to be on this
    /// lane, and for the remaining 4 % the general rate still carries the estimate. What is left is
    /// that fraction of the gap, not the whole of it.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoChampionsEquallyGoodAgainstTheOpponent_ScoreTheSameOnThatLane()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(Average, "Average", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(EnemyMid, "EnemyMid", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Strong, Lane.Mid, winRate: 0.56, play: 40_000)
            .InLane(Average, Lane.Mid, winRate: 0.50, play: 40_000)
            .InLane(EnemyMid, Lane.Mid, winRate: 0.50, play: 40_000)
            .Matchup(Strong, EnemyMid, Lane.Mid, winRate: 0.58, play: 40_000)
            .Matchup(Average, EnemyMid, Lane.Mid, winRate: 0.58, play: 40_000)
            .Build();

        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(7, EnemyMid)
            .OnClock(2, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(meta).Predict(state.Enemies);
        var items = new Recommender(meta, TraitTable.Empty).Recommend(state, target, lanes).Items;

        var gap = Math.Abs(ScoreModel.Logit(Score(items, Strong)) - ScoreModel.Logit(Score(items, Average)));
        var reputationGap = Math.Abs(ScoreModel.Logit(0.55955) - ScoreModel.Logit(0.5));

        Assert.True(
            gap < reputationGap / 10,
            $"Der Reputationsvorsprung darf nicht durchschlagen: {gap:N4} gegen {reputationGap:N4} Logit.");
    }

    /// <summary>
    /// The OP.GG tier does not move the score. It is not an independent measurement: on the stored
    /// Gold snapshot it explains 46,8 % of the variance of the very lane win rate it used to be
    /// added to, and 45,2 % of the pick rate — half a second helping of the win rate, half a
    /// popularity vote, and worth as much (0,16 log-odds across the ladder) as the win-rate spread
    /// it duplicated (0,156). It stays on screen as a chip; it stays out of the number.
    /// </summary>
    [Fact]
    public void TheOpGgTier_DoesNotMoveTheScore()
    {
        static double LaneTerm(int tier)
        {
            var meta = new MetaBuilder()
                .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
                .InLane(Strong, Lane.Mid, winRate: 0.52, play: 40_000, tier: tier)
                .Build();

            var state = DraftState.From(new SessionBuilder().LocalPlayer(2).OnClock(2, "pick").Build());
            var target = new TurnTracker().Resolve(state)!;
            var lanes = new LanePredictor(meta).Predict(state.Enemies);

            return new Recommender(meta, TraitTable.Empty).Recommend(state, target, lanes).Items
                .Single(item => item.ChampionId == Strong)
                .Breakdown.Single(term => term.Kind == ScoreTermKind.LaneStrength)
                .LogOdds ?? 0;
        }

        // Every step of the ladder, including the -1 that means "no tier at all".
        var terms = new[] { -1, 0, 1, 2, 3, 4, 5 }.Select(LaneTerm).ToList();

        Assert.All(terms, term => Assert.Equal(terms[0], term, precision: 9));
    }

    /// <summary>
    /// The tier is still shown — as context, not as an argument. A chip that scores nothing must
    /// not be coloured as if it did, or the list would look like the tier had moved it.
    /// </summary>
    [Fact]
    public void TheTierChip_IsNeutral()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Strong, Lane.Mid, winRate: 0.52, play: 40_000, tier: 1)
            .Build();

        var state = DraftState.From(new SessionBuilder().LocalPlayer(2).OnClock(2, "pick").Build());
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(meta).Predict(state.Enemies);

        var reasons = new Recommender(meta, TraitTable.Empty).Recommend(state, target, lanes).Items
            .Single(item => item.ChampionId == Strong)
            .Reasons;

        var chip = Assert.Single(reasons, reason => reason.Text.Contains("Tier", StringComparison.Ordinal));
        Assert.Equal(ReasonTone.Neutral, chip.Tone);
    }

    /// <summary>
    /// The other half of the same rule, and the reason the fix is not simply "count tier 0": rows
    /// the analysis writes for a lane the tier list never listed carry no tier AND no games. Those
    /// must stay unrated — otherwise a champion with no data at all would be nudged to the top of
    /// its lane. The game count is what tells the two apart, in old files as well as new ones.
    /// </summary>
    [Fact]
    public void ATierWithoutGamesBehind_It_StaysUnrated()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Strong, Lane.Mid, winRate: 0.5, play: 0, tier: 0)
            .Build();

        Assert.Equal(-1, meta.LaneStat(Strong, Lane.Mid)!.Value.Tier);
    }

    /// <summary>
    /// The rest of the enemy team is measured the same way as the direct duel: against what this
    /// champion does anyway. Averaged over opponents, a duel log-odds IS the champion's general
    /// strength, and that already sits in the lane term — so an edge that merely matches the
    /// champion's own rate says nothing about THESE opponents and must not move the score.
    /// <para>
    /// Uncentred it did, at 0.35 weight, and only for candidates OP.GG happens to list a counter
    /// for. That favoured the better documented picks, not the better ones.
    /// </para>
    /// </summary>
    [Fact]
    public void OffLaneDuelsAtTheChampionsUsualRate_AddNothing()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(EnemyTop, "EnemyTop", DamageType.Physical, ["Fighter"], 175, 5)
            .InLane(Strong, Lane.Mid, winRate: 0.56, play: 40_000)
            .InLane(EnemyTop, Lane.Top, winRate: 0.50, play: 40_000)
            // Recorded on THEIR lane, so this is an off-lane edge for our mid candidate — and it is
            // exactly what the champion does on its own lane anyway.
            .Matchup(Strong, EnemyTop, Lane.Top, winRate: 0.56, play: 40_000)
            .Build();

        Assert.Equal(0, OffLaneTerm(meta), precision: 2);
    }

    [Fact]
    public void OffLaneDuelsAboveTheChampionsUsualRate_StillCount()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(EnemyTop, "EnemyTop", DamageType.Physical, ["Fighter"], 175, 5)
            .InLane(Strong, Lane.Mid, winRate: 0.50, play: 40_000)
            .InLane(EnemyTop, Lane.Top, winRate: 0.50, play: 40_000)
            .Matchup(Strong, EnemyTop, Lane.Top, winRate: 0.60, play: 40_000)
            .Build();

        Assert.True(OffLaneTerm(meta) > 0.1, "Ein echter Vorteil gegen das übrige Team muss zählen.");
    }

    /// <summary>The EnemyTeam term of the one candidate that has matchup data, in log-odds.</summary>
    private static double OffLaneTerm(MetaLookup meta)
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(5, EnemyTop)
            .OnClock(2, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(meta).Predict(state.Enemies);

        return new Recommender(meta, TraitTable.Empty).Recommend(state, target, lanes).Items
            .Single(item => item.ChampionId == Strong)
            .Breakdown.Single(term => term.Kind == ScoreTermKind.EnemyTeam)
            .LogOdds ?? 0;
    }

    /// <summary>The LaneMatchup term of the one candidate that has matchup data, in log-odds.</summary>
    private static double DuelTerm(MetaLookup meta)
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(7, EnemyMid)
            .OnClock(2, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(meta).Predict(state.Enemies);

        return new Recommender(meta, TraitTable.Empty).Recommend(state, target, lanes).Items
            .Single(item => item.ChampionId == Strong)
            .Breakdown.Single(term => term.Kind == ScoreTermKind.LaneMatchup)
            .LogOdds ?? 0;
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

    /// <summary>
    /// <c>RecommendationCount</c> is user-editable JSON, and Top() clamps with Max(0, …) rather
    /// than Max(1, …) on purpose: a zero means an empty list, not a surprise single row.
    /// </summary>
    [Fact]
    public void LimitZero_ReturnsNothing()
    {
        var (state, target, lanes) = Scenario();

        Assert.Empty(Recommender().Recommend(state, target, lanes, limit: 0).Items);
    }

    /// <summary>
    /// A lane whose roster is empty must not end the list — every champion is then a candidate,
    /// scored on whatever the data says. The case is real for a snapshot built before a lane's
    /// tier list had any entries at all.
    /// </summary>
    [Fact]
    public void ALaneWithoutARoster_FallsBackToEveryChampion()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong", DamageType.Magic, ["Mage"], 550, 3)
            .Champion(Average, "Average", DamageType.Magic, ["Mage"], 525, 3)
            // Both are listed on Mid only; the advised seat sits on Top, whose roster is empty.
            .InLane(Strong, Lane.Mid, winRate: 0.54, play: 2000)
            .InLane(Average, Lane.Mid, winRate: 0.50, play: 2000)
            .Build();

        var state = DraftState.From(new SessionBuilder().LocalPlayer(0).OnClock(0, "pick").Build());
        var target = new TurnTracker().Resolve(state)!;

        var items = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty, limit: 20).Items;

        Assert.Equal(2, items.Count);
    }

    /// <summary>
    /// The ban score is denied win-rate points: the gate (how likely the enemy takes the champion)
    /// times the edge the champion would give them, in points. Pinned numerically because the three
    /// relative ban tests would all still pass if the formula silently changed scale.
    /// </summary>
    [Fact]
    public void ABanScore_IsTheGateTimesTheDeniedPoints()
    {
        var meta = new MetaBuilder()
            .Champion(Menace, "Menace", DamageType.Physical, ["Fighter"], 175, 5)
            // Tier 3 is the middle, so the tier nudge contributes nothing to this check.
            .InLane(Menace, Lane.Top, winRate: 0.56, play: 40_000, tier: 3, pickRate: 0.05, banRate: 0.05)
            .Build();

        var state = DraftState.From(new SessionBuilder().LocalPlayer(0).OnClock(0, "ban").Build());
        var target = new TurnTracker().Resolve(state)!;

        var item = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty, limit: 20).Items
            .Single(entry => entry.ChampionId == Menace);

        var strength = ScoreModel.Logit(Shrinkage.Apply(0.56, 40_000, Shrinkage.LanePrior));

        // Strength plus the seat's own lane threat at half weight — the deliberate double count.
        var threat = strength + (ScoreModel.BanLaneFocus * strength);
        var gate = Math.Clamp((0.05 * 8) + (0.05 * 4), 0, 1);

        Assert.Equal(gate * (ScoreModel.Sigmoid(threat) - 0.5) * 100, item.Score, precision: 6);
    }

    /// <summary>
    /// Only LOCKED enemies discount a lane for the ban list. A hover can still change, and treating
    /// it as taken would quietly drop that lane's champions out of the list one pick too early.
    /// </summary>
    [Fact]
    public void AHoveredEnemy_DoesNotYetCloseItsLane()
    {
        var meta = new MetaBuilder()
            .Champion(Menace, "Menace", DamageType.Physical, ["Fighter"], 175, 5)
            .Champion(EnemyTop, "EnemyTop", DamageType.Physical, ["Fighter"], 175, 5)
            .InLane(Menace, Lane.Top, winRate: 0.56, play: 40_000, pickRate: 0.05, banRate: 0.05)
            .InLane(EnemyTop, Lane.Top, winRate: 0.52, play: 40_000)
            .Build();

        static double BanValue(MetaLookup meta, SessionBuilder session)
        {
            var state = DraftState.From(session.Build());
            var target = new TurnTracker().Resolve(state)!;
            var lanes = new LanePredictor(meta).Predict(state.Enemies);

            return new Recommender(meta, TraitTable.Empty)
                .Recommend(state, target, lanes, limit: 20).Items
                .Single(entry => entry.ChampionId == Menace).Score;
        }

        var hovering = BanValue(meta, new SessionBuilder().LocalPlayer(0).Hovering(5, EnemyTop).OnClock(0, "ban"));
        var locked = BanValue(meta, new SessionBuilder().LocalPlayer(0).Locked(5, EnemyTop).OnClock(0, "ban"));

        Assert.True(hovering > locked, $"Ein Hover darf die Lane nicht schliessen ({hovering} gegen {locked}).");
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

    /// <summary>
    /// Strength and popularity have to describe the same lane. They used to be chosen
    /// independently, which let a ban read "stark auf Bot" while its value came from a third
    /// lane's pick rate.
    /// </summary>
    [Fact]
    public void BanStrengthAndPopularity_ComeFromTheSameLane()
    {
        // Mostly played mid and merely respectable there; a rare but monstrous top pick.
        var meta = new MetaBuilder()
            .Champion(Menace, "Menace", DamageType.Physical, ["Fighter"], 175, 5)
            .Champion(EnemyMid, "EnemyMid", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Menace, Lane.Mid, roleRate: 0.85, winRate: 0.50, play: 20_000, tier: 3, pickRate: 0.10)
            .InLane(Menace, Lane.Top, roleRate: 0.10, winRate: 0.58, play: 2_000, tier: 1, pickRate: 0.01)
            .InLane(EnemyMid, Lane.Mid, roleRate: 0.9, winRate: 0.50, play: 20_000)
            .Build();

        var item = BanItem(meta, Menace);

        // The mid row is the one the enemy would use, so no "stark auf Top" chip may appear.
        Assert.DoesNotContain(item.Reasons, reason => reason.Text.Contains("Top", StringComparison.Ordinal));
    }

    /// <summary>
    /// A champion whose only lane the enemy has already locked is nearly worthless to ban: they
    /// have no seat left for them. What survives is our own doubt about the lane read.
    /// </summary>
    [Fact]
    public void AChampionWhoseLaneTheEnemyFilled_IsWorthLessThanOneOnAnOpenLane()
    {
        var meta = BanMeta();

        var withoutInfo = BanScore(meta, JungleOnly, enemies: []);
        var afterTheirJunglerLocked = BanScore(meta, JungleOnly, enemies: [EnemyJungler]);

        Assert.True(withoutInfo > 0, "Ohne Information muss der Bann etwas wert sein.");
        Assert.True(
            afterTheirJunglerLocked < withoutInfo / 3,
            $"Mit gesperrtem Gegner-Jungler muss der Wert einbrechen ({afterTheirJunglerLocked} gegen {withoutInfo}).");
    }

    [Fact]
    public void AChampionOnAnOpenLane_KeepsItsBanValue()
    {
        var meta = BanMeta();

        var withoutInfo = BanScore(meta, SupportOnly, enemies: []);
        var afterTheirJunglerLocked = BanScore(meta, SupportOnly, enemies: [EnemyJungler]);

        Assert.Equal(withoutInfo, afterTheirJunglerLocked, precision: 6);
    }

    /// <summary>
    /// The first ban round has nothing locked, so nothing may be discounted — the whole point of
    /// reading the enemy lanes is that it only starts mattering once they commit.
    /// </summary>
    [Fact]
    public void WithNothingLocked_TheBanListIsUnrestricted()
    {
        var meta = BanMeta();

        var jungle = BanScore(meta, JungleOnly, enemies: []);
        var support = BanScore(meta, SupportOnly, enemies: []);

        Assert.True(jungle > 0);
        Assert.True(support > 0);
    }

    private const int JungleOnly = 401, SupportOnly = 402, EnemyJungler = 403;

    private static MetaLookup BanMeta() => new MetaBuilder()
        .Champion(JungleOnly, "JungleOnly", DamageType.Physical, ["Fighter"], 175, 5)
        .Champion(SupportOnly, "SupportOnly", DamageType.Magic, ["Tank"], 125, 6)
        .Champion(EnemyJungler, "EnemyJungler", DamageType.Physical, ["Fighter"], 175, 5)
        .InLane(JungleOnly, Lane.Jungle, roleRate: 0.98, winRate: 0.54, play: 20_000, tier: 1, pickRate: 0.10)
        .InLane(SupportOnly, Lane.Support, roleRate: 0.98, winRate: 0.54, play: 20_000, tier: 1, pickRate: 0.10)
        .InLane(EnemyJungler, Lane.Jungle, roleRate: 0.98, winRate: 0.50, play: 20_000)
        .Build();

    /// <summary>The ban score of one candidate, with the given enemies locked in.</summary>
    private static double BanScore(MetaLookup meta, int candidate, int[] enemies)
        => BanItem(meta, candidate, enemies).Score;

    private static Recommendation BanItem(MetaLookup meta, int candidate, int[]? enemies = null)
    {
        var builder = new SessionBuilder().LocalPlayer(2).OnClock(2, "ban");

        var cell = 5;
        foreach (var enemy in enemies ?? [])
            builder.Locked(cell++, enemy);

        var state = DraftState.From(builder.Build());
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(meta).Predict(state.Enemies);

        return new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, lanes, limit: 50).Items
            .Single(item => item.ChampionId == candidate);
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
    /// OP.GG lists the duos worth mentioning, not all of them, so the average stored duo sits above
    /// even — measured on a real snapshot at 0.0552 log-odds, i.e. 51,4 %. The term is therefore
    /// measured against THAT, not against 50 %: a duo that is merely as good as the usual listed
    /// one says nothing about this pick. Uncentred, having any duo row at all was worth 0,8 points,
    /// and the estimated win rate grew as allies locked in rather than with the quality of the pick.
    /// </summary>
    [Fact]
    public void ADuoAsGoodAsTheAverageListedOne_AddsNothing()
    {
        // The fixture's one duo IS the average of the file it comes from — measured on the raw
        // rate, which is also what the shrinkage now pulls towards, so the two agree exactly and
        // the term lands on zero without any rounding slack.
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong").InLane(Strong, Lane.Mid, winRate: 0.52, play: 2000)
            .Champion(AllySupport, "AllySupport")
            .Synergy(Strong, AllySupport, winRate: 0.53, play: 2000)
            .SynergyBaseline(ScoreModel.Logit(0.53))
            .Build();

        Assert.Equal(0, SynergyTerm(meta), precision: 3);
    }

    [Fact]
    public void ADuoBetterThanTheAverageListedOne_StillCounts()
    {
        var meta = new MetaBuilder()
            .Champion(Strong, "Strong").InLane(Strong, Lane.Mid, winRate: 0.52, play: 2000)
            .Champion(AllySupport, "AllySupport")
            .Synergy(Strong, AllySupport, winRate: 0.60, play: 2000)
            .SynergyBaseline(ScoreModel.Logit(Shrinkage.Apply(0.53, 2000, Shrinkage.SynergyPrior)))
            .Build();

        Assert.True(SynergyTerm(meta) > 0.1, "Ein ueberdurchschnittliches Duo muss zaehlen.");
    }

    /// <summary>The Synergy term of the one candidate with a duo row, in log-odds.</summary>
    private static double SynergyTerm(MetaLookup meta)
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(2)
            .Locked(4, AllySupport)
            .OnClock(2, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state)!;

        return new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty, limit: 50).Items
            .Single(item => item.ChampionId == Strong)
            .Breakdown.Single(term => term.Kind == ScoreTermKind.Synergy)
            .LogOdds ?? 0;
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

    /// <summary>
    /// First pick of the own team against three revealed enemies. Until the enemy composition was
    /// wired into the term, this was the emptiest row of the breakdown — "no ally picked, nothing
    /// to fit into" — in the one moment with the least other evidence: no duel, no duo, no lane
    /// opponent. The enemy line-up was analysed all along and only ever drawn in a table.
    /// </summary>
    [Fact]
    public void CompositionTerm_ReadsTheEnemyBeforeAnyAllyHasPicked()
    {
        var session = new SessionBuilder()
            .LocalPlayer(2)
            .Locked(5, EnemyTop)
            .Locked(6, Menace)
            .Locked(7, EnemyMid)
            .OnClock(2, "pick")
            .Build();

        var state = DraftState.From(session);
        var target = new TurnTracker().Resolve(state)!;
        var lanes = new LanePredictor(Meta()).Predict(state.Enemies);

        var item = Recommender().Recommend(state, target, lanes, limit: 50).Items
            .Single(entry => entry.ChampionId == CounterPick);

        // Three enemies, none of them a tank or six defence: the assassin's targets stand open.
        Assert.True(item.Breakdown.Single(term => term.Kind == ScoreTermKind.Composition).HasData);
        Assert.Contains(item.Reasons, reason => reason.Text.Contains("Backline", StringComparison.Ordinal));
    }

    /// <summary>
    /// Without a lane the base term falls back to the champion's own lane. It used to fall back to
    /// the champion's BEST lane: a maximum over five noisy rows, which systematically returns the
    /// luckiest sample and can name a lane the champion hardly plays.
    /// </summary>
    [Fact]
    public void WithoutALane_TheBaseTermUsesTheLaneTheChampionActuallyPlays()
    {
        const int Flex = 111;

        var meta = new MetaBuilder()
            .Champion(Flex, "Flex", DamageType.Magic, ["Mage"], 550, 3)
            .InLane(Flex, Lane.Mid, winRate: 0.50, play: 20000)
            .InLane(Flex, Lane.Top, winRate: 0.58, play: 120)
            .Build();

        var session = new SessionBuilder()
            .LocalPlayer(2)
            .AllyLane(2, null)
            .OnClock(2, "pick")
            .Build();

        var state = DraftState.From(session);
        var target = new TurnTracker().Resolve(state)!;

        var item = new Recommender(meta, TraitTable.Empty)
            .Recommend(state, target, LanePredictionResult.Empty, limit: 50).Items
            .Single(entry => entry.ChampionId == Flex);

        // The 20 000-game row, not the 120-game one that happens to read two points higher.
        Assert.Equal(meta.LaneStat(Flex, Lane.Mid)!.Value.WinRate, item.Score, precision: 6);
        Assert.DoesNotContain(item.Reasons, reason => reason.Text.Contains("stark auf", StringComparison.Ordinal));
    }
}
