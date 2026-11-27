namespace DraftPilot.Core.Data;

/// <summary>
/// The numbers behind the lane scale in the game view (docs/lane-vergleich.md; docs/modellpruefung.md,
/// Messung 35), measured at export from the collector's clean duels: what a level and 1.000 gold of
/// items are worth in each phase, and how strong each champion is in the duel on its lane in each
/// phase, beyond its level and items. The app adds both up for "you against your lane opponent" and
/// shows the champions' part per phase as „Dieses Duell“.
/// </summary>
public sealed class LaneScale
{
    /// <summary>How many duels it was fitted on (lines with the levels before the kill, all patches kept).</summary>
    public int Duels { get; set; }

    /// <summary>How far the champions' strengths are drawn to 0: the spread of the ridge, in log-odds.</summary>
    public double Tau { get; set; }

    /// <summary>One pair of weights per lane and phase.</summary>
    public List<LaneScaleWeights> Weights { get; set; } = [];

    /// <summary>One strength per champion, lane and phase that met a duel.</summary>
    public List<DuelStrength> Champions { get; set; } = [];
}

/// <param name="Lane">0 top, 1 jungle, 2 mid, 3 the bot lane (two against two, both players summed).</param>
/// <param name="Phase">0 for a mean level under 6, 1 under 11, 2 from 11 (<see cref="LaneScaleModel.PhaseOf"/>).</param>
/// <param name="Level">Log-odds per level ahead.</param>
/// <param name="Gold">Log-odds per 1.000 gold of item worth ahead.</param>
public sealed record LaneScaleWeights(int Lane, int Phase, double Level, double Gold);

/// <param name="Lane">0 top … 4 support, as in the game line.</param>
/// <param name="Strength">Log-odds this champion adds in a duel on that lane in that phase, drawn to 0.</param>
/// <param name="Duels">The duels it was in, for whoever reads the file.</param>
public sealed record DuelStrength(int ChampionId, int Lane, int Phase, double Strength, int Duels);

/// <summary>
/// Fits <see cref="LaneScale"/> on the game lines (<see cref="RiotGames.Format"/>): only lines whose
/// duels carry the levels before the kill (<see cref="RiotGames.HasLevelsBeforeDuel"/>). First the
/// weights per lane and phase, without a side (every duel also mirrored); then the champions'
/// strengths on top, each drawn to 0 with spread <see cref="Tau"/> — the variant checked on held-out
/// games in Messung 35 (+16,8 standard errors before minute 14, steps true to ±1–2 points). Weights per
/// lane rather than top, jungle and mid together: shared ones fit the jungle badly late, and the
/// champions' strengths soaked up the difference (Viego late +2,2 instead of +1,0).
/// </summary>
public static class LaneScaleModel
{
    /// <summary>The spread chosen on an inner split of the training games on 22.11.2026; 0,3 to 0,6 predict alike.</summary>
    public const double Tau = 0.45;

    /// <summary>Below this many clean duels nothing is exported and the app keeps its own weights.</summary>
    public const int MinimumDuels = 40_000;


    /// <summary>The phase of a duel from its players' mean level — the same rule as the app's.</summary>
    public static int PhaseOf(double meanLevel) => meanLevel < 6 ? 0 : meanLevel < 11 ? 1 : 2;

    private sealed record Duel(int Group, int Phase, double DLevel, double DGold, int[] Plus, int[] Minus, double Y);

    public static LaneScale? Fit(IEnumerable<int[]> lines, double tau = Tau, int minimumDuels = MinimumDuels)
    {
        var keys = new Dictionary<(int Champion, int Lane, int Phase), int>();
        var duels = new List<Duel>();
        foreach (var line in lines)
        {
            if (!RiotGames.HasLevelsBeforeDuel(line))
                continue;
            foreach (var numbers in RiotGames.Duels(line))
            {
                if (Read(line, numbers, keys) is { } duel)
                    duels.Add(duel);
            }
        }

        if (duels.Count < minimumDuels)
            return null;

        var weights = new List<LaneScaleWeights>();
        for (var group = 0; group < 4; group++)
        {
            for (var phase = 0; phase < 3; phase++)
            {
                var rows = duels.Where(d => d.Group == group && d.Phase == phase).ToList();
                var fit = ScaleFit.MirroredLogistic([.. rows.Select(d => new[] { d.DLevel, d.DGold })], [.. rows.Select(d => d.Y)], 2);
                var (level, gold) = (fit[0], fit[1]);
                weights.Add(new LaneScaleWeights(group, phase, Math.Round(level, 4), Math.Round(gold, 4)));
            }
        }

        double Offset(Duel d)
        {
            var w = weights.First(x => x.Lane == d.Group && x.Phase == d.Phase);
            return w.Level * d.DLevel + w.Gold * d.DGold;
        }

        var theta = ScaleFit.RidgeStrengths(keys.Count, [.. duels.Select(d => (d.Plus, d.Minus, d.Y))], [.. duels.Select(Offset)], tau);
        var count = new int[keys.Count];
        foreach (var d in duels)
        {
            foreach (var k in d.Plus)
                count[k]++;
            foreach (var k in d.Minus)
                count[k]++;
        }

        return new LaneScale
        {
            Duels = duels.Count,
            Tau = tau,
            Weights = weights,
            Champions = [.. keys
                .Where(pair => count[pair.Value] > 0)
                .Select(pair => new DuelStrength(pair.Key.Champion, pair.Key.Lane, pair.Key.Phase, Math.Round(theta[pair.Value], 4), count[pair.Value]))
                .OrderBy(s => s.ChampionId).ThenBy(s => s.Lane).ThenBy(s => s.Phase)],
        };
    }

    /// <summary>
    /// One duel of a line: blue's side against red's. Top, jungle and mid one against one on the same
    /// lane; the bot lane two against two. Anything else — a lane missing, a side short — is left out,
    /// as in the measurement.
    /// </summary>
    private static Duel? Read(int[] line, int[] numbers, Dictionary<(int, int, int), int> keys)
    {
        if (numbers.Length < 3)
            return null;
        int winner = numbers[1], loser = numbers[2];
        if (winner is < 0 or > 9 || loser is < 0 or > 9 || winner < 5 == loser < 5)
            return null;

        var fighters = new List<(int Place, int Champion, int Lane, int Level, int Gold)>();
        for (var i = 3; i + 4 < numbers.Length; i += 5)
        {
            var place = numbers[i];
            if (place is < 0 or > 9)
                return null;
            var (champion, lane) = RiotGames.Decode(line[place]);
            fighters.Add((place, champion, (int)lane, numbers[i + 1], numbers[i + 2]));
        }

        var winnerLane = (int)RiotGames.Decode(line[winner]).Lane;
        var bot = winnerLane >= 3;
        var size = bot ? 2 : 1;
        var blue = fighters.Where(f => f.Place < 5).ToList();
        var red = fighters.Where(f => f.Place >= 5).ToList();
        if (winnerLane < 0 || blue.Count != size || red.Count != size || fighters.Any(f => f.Lane < 0))
            return null;

        var phase = PhaseOf(fighters.Average(f => f.Level));
        int Key((int Place, int Champion, int Lane, int Level, int Gold) f)
        {
            var key = (f.Champion, f.Lane, phase);
            if (!keys.TryGetValue(key, out var id))
                keys[key] = id = keys.Count;
            return id;
        }

        var plus = blue.Select(Key).ToList();
        var minus = red.Select(Key).ToList();
        // A mirror match says nothing about the champion.
        var both = plus.Intersect(minus).ToHashSet();
        return new Duel(
            Math.Min(3, winnerLane),
            phase,
            blue.Sum(f => f.Level) - red.Sum(f => f.Level),
            (blue.Sum(f => f.Gold) - red.Sum(f => f.Gold)) / 1000.0,
            [.. plus.Where(k => !both.Contains(k))],
            [.. minus.Where(k => !both.Contains(k))],
            winner < 5 ? 1 : 0);
    }
}
