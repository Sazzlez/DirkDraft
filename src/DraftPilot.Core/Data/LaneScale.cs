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

    private const int Sweeps = 10;

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
                var (level, gold) = FitWeights(duels.Where(d => d.Group == group && d.Phase == phase).ToList());
                weights.Add(new LaneScaleWeights(group, phase, Math.Round(level, 4), Math.Round(gold, 4)));
            }
        }

        double Offset(Duel d)
        {
            var w = weights.First(x => x.Lane == d.Group && x.Phase == d.Phase);
            return w.Level * d.DLevel + w.Gold * d.DGold;
        }

        var theta = FitStrengths(duels, keys.Count, tau, Offset);
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

    /// <summary>Logistic regression without intercept on the duels and their mirror images: Newton steps on two weights.</summary>
    private static (double Level, double Gold) FitWeights(List<Duel> duels)
    {
        double a = 0, b = 0;
        if (duels.Count == 0)
            return (a, b);
        for (var iteration = 0; iteration < 50; iteration++)
        {
            double ga = 0, gb = 0, haa = 1e-6, hab = 0, hbb = 1e-6;
            foreach (var d in duels)
            {
                // The duel and its mirror (−x, 1 − y) give the same weight and twice the gradient.
                var p = Sigmoid(a * d.DLevel + b * d.DGold);
                var w = p * (1 - p);
                var r = d.Y - p;
                ga += 2 * r * d.DLevel;
                gb += 2 * r * d.DGold;
                haa += 2 * w * d.DLevel * d.DLevel;
                hab += 2 * w * d.DLevel * d.DGold;
                hbb += 2 * w * d.DGold * d.DGold;
            }
            var det = haa * hbb - hab * hab;
            if (Math.Abs(det) < 1e-300)
                break;
            var da = (hbb * ga - hab * gb) / det;
            var db = (haa * gb - hab * ga) / det;
            a += da;
            b += db;
            if (Math.Max(Math.Abs(da), Math.Abs(db)) < 1e-9)
                break;
        }
        return (a, b);
    }

    /// <summary>
    /// The champions' strengths on top of the weights: coordinate-wise Newton steps, each strength
    /// drawn to 0 with spread <paramref name="tau"/> (as <c>fitEffects</c> in app/tools/measure/duels.ts).
    /// </summary>
    private static double[] FitStrengths(List<Duel> duels, int keys, double tau, Func<Duel, double> offset)
    {
        var theta = new double[keys];
        var z = duels.Select(offset).ToArray();
        var rows = new List<(int Duel, int Sign)>[keys];
        for (var k = 0; k < keys; k++)
            rows[k] = [];
        for (var i = 0; i < duels.Count; i++)
        {
            foreach (var k in duels[i].Plus)
                rows[k].Add((i, 1));
            foreach (var k in duels[i].Minus)
                rows[k].Add((i, -1));
        }

        var penalty = 1 / (tau * tau);
        for (var sweep = 0; sweep < Sweeps; sweep++)
        {
            for (var k = 0; k < keys; k++)
            {
                if (rows[k].Count == 0)
                    continue;
                var gradient = -theta[k] * penalty;
                var hessian = penalty;
                foreach (var (i, sign) in rows[k])
                {
                    var p = Sigmoid(z[i]);
                    gradient += sign * (duels[i].Y - p);
                    hessian += p * (1 - p);
                }
                var step = gradient / hessian;
                theta[k] += step;
                foreach (var (i, sign) in rows[k])
                    z[i] += sign * step;
            }
        }
        return theta;
    }

    private static double Sigmoid(double z) => 1 / (1 + Math.Exp(-z));
}
