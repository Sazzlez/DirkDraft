using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// The numbers behind the teamfight scale in the game view from minute 14 on (docs/teamfight-skala.md;
/// docs/modellpruefung.md, Messung 36), measured at export from the collector's teamfights: what a
/// level and 1.000 gold of items, Baron, the Elder, a dragon and a soul are worth to a team in a
/// fight of five against five, and how strong each champion is in teamfights in each phase. The app
/// adds them up for „Euer Team im Teamfight“.
/// </summary>
public sealed class TeamfightScale
{
    /// <summary>How many fights it was fitted on: both teams at full strength, four or more of each seen, no AFK player, no draw.</summary>
    public int Fights { get; set; }

    /// <summary>How far the champions' strengths are drawn to 0, in log-odds.</summary>
    public double Tau { get; set; }

    public TeamfightWeights Weights { get; set; } = new();

    /// <summary>One strength per champion and phase that was in a counted fight.</summary>
    public List<TeamfightStrength> Champions { get; set; } = [];
}

/// <summary>Log-odds for the team with … more than the other.</summary>
public sealed class TeamfightWeights
{
    /// <summary>Per level, the five players' levels summed.</summary>
    public double Level { get; set; }

    /// <summary>
    /// Per 1.000 gold of item worth, all five summed — from a fit without roles, for a game whose
    /// positions the app does not know (a blind pick, a custom game).
    /// </summary>
    public double Gold { get; set; }

    /// <summary>Per 1.000 gold of item worth on the carries: top, mid and ADC summed (Messung 36: about twice the rest).</summary>
    public double GoldCarry { get; set; }

    /// <summary>Per 1.000 gold of item worth on jungle and support summed.</summary>
    public double GoldRest { get; set; }

    /// <summary>Baron taken in the last <see cref="TeamfightScaleModel.BaronSeconds"/>, by one team.</summary>
    public double Baron { get; set; }

    /// <summary>The Elder taken in the last <see cref="TeamfightScaleModel.ElderSeconds"/>, by one team.</summary>
    public double Elder { get; set; }

    /// <summary>Per elemental dragon more.</summary>
    public double Dragon { get; set; }

    /// <summary>The soul, held by one team.</summary>
    public double Soul { get; set; }
}

/// <param name="Phase">0 before minute 20, 1 before minute 30, 2 from minute 30 (<see cref="TeamfightScaleModel.PhaseOf"/>).</param>
/// <param name="Strength">Log-odds the champion adds to its team in a fight in that phase, drawn to 0.</param>
/// <param name="Fights">The counted fights it was in, for whoever reads the file.</param>
public sealed record TeamfightStrength(int ChampionId, int Phase, double Strength, int Fights);

/// <summary>
/// Fits <see cref="TeamfightScale"/> on the game lines with the fight part (<see cref="RiotGames.Fights"/>):
/// the variant that passed Messung 36 — one weight each for level, item worth of the carries (top,
/// mid, ADC), item worth of jungle and support, Baron, the Elder, dragons and the soul, without a side
/// (every fight also mirrored), and the champions' strengths per phase on top, drawn to 0 with spread
/// <see cref="Tau"/>. Atakhan is left out: not once in the
/// fights of 27.11.2026, and the live game's events do not show him either.
/// </summary>
public static class TeamfightScaleModel
{
    /// <summary>The spread chosen on an inner split of the training games on 27.11.2026.</summary>
    public const double Tau = 0.06;

    /// <summary>Below this many counted fights nothing is exported and the app keeps the plain team tile.</summary>
    public const int MinimumFights = 20_000;

    /// <summary>A teamfight for the scale: at least four of each team seen in it (RiotMatchReader keeps three).</summary>
    public const int MinimumPerSide = 4;

    /// <summary>A player this many levels below the fight's median is taken for AFK.</summary>
    public const int AfkLevels = 5;

    /// <summary>How long Baron's and the Elder's buffs last; given to the whole team, as the app does.</summary>
    public const int BaronSeconds = 180;
    public const int ElderSeconds = 150;

    /// <summary>The phase of a fight from its second: before minute 20, before minute 30, after — the same rule as the app's.</summary>
    public static int PhaseOf(int second) => second < 1200 ? 0 : second < 1800 ? 1 : 2;

    private sealed record Fight(double[] X, int[] Plus, int[] Minus, double Y);

    public static TeamfightScale? Fit(IEnumerable<int[]> lines, double tau = Tau, int minimumFights = MinimumFights)
    {
        var keys = new Dictionary<(int Champion, int Phase), int>();
        var fights = new List<Fight>();
        foreach (var line in lines)
        {
            if (!RiotGames.HasFightSample(line))
                continue;
            var objectives = RiotGames.Objectives(line).ToList();
            foreach (var numbers in RiotGames.Fights(line))
            {
                if (Read(line, numbers, objectives, keys) is { } fight)
                    fights.Add(fight);
            }
        }

        if (fights.Count < minimumFights)
            return null;

        // The scale: gold of the carries and of jungle and support apart. Beside it the same fit with the
        // gold summed, whose weight the app takes where it does not know who plays which role.
        var y = fights.Select(f => f.Y).ToList();
        var w = ScaleFit.MirroredLogistic([.. fights.Select(f => f.X)], y, 7);
        var plain = ScaleFit.MirroredLogistic([.. fights.Select(f => (double[])[f.X[0], f.X[1] + f.X[2], .. f.X[3..]])], y, 6);
        var offset = fights.Select(f => f.X.Select((value, j) => value * w[j]).Sum()).ToArray();
        var theta = ScaleFit.RidgeStrengths(keys.Count, [.. fights.Select(f => (f.Plus, f.Minus, f.Y))], offset, tau);
        var count = new int[keys.Count];
        foreach (var f in fights)
        {
            foreach (var k in f.Plus.Concat(f.Minus))
                count[k]++;
        }

        return new TeamfightScale
        {
            Fights = fights.Count,
            Tau = tau,
            Weights = new TeamfightWeights
            {
                Level = Math.Round(w[0], 4),
                Gold = Math.Round(plain[1], 4),
                GoldCarry = Math.Round(w[1], 4),
                GoldRest = Math.Round(w[2], 4),
                Baron = Math.Round(w[3], 4),
                Elder = Math.Round(w[4], 4),
                Dragon = Math.Round(w[5], 4),
                Soul = Math.Round(w[6], 4),
            },
            Champions = [.. keys
                .Where(pair => count[pair.Value] > 0)
                .Select(pair => new TeamfightStrength(pair.Key.Champion, pair.Key.Phase, Math.Round(theta[pair.Value], 4), count[pair.Value]))
                .OrderBy(s => s.ChampionId).ThenBy(s => s.Phase)],
        };
    }

    /// <summary>
    /// One counted fight, side 0 (places 0–4) against side 1: the differences in level, item worth of
    /// the carries and of the rest, Baron, Elder, dragons and soul, and the champions of each side. Null for a fight the scale does
    /// not learn from — someone dead at the start, fewer than four of a side seen, an AFK player, a draw.
    /// </summary>
    private static Fight? Read(int[] line, int[] numbers, List<int[]> objectives, Dictionary<(int, int), int> keys)
    {
        if (numbers.Length < RiotGames.FightLength)
            return null;
        int second = numbers[0], kills0 = numbers[1], kills1 = numbers[2], dead = numbers[3], seen = numbers[4];
        static int Seen(int mask, int from) => Enumerable.Range(from, 5).Count(place => (mask & (1 << place)) != 0);
        if (dead != 0 || Seen(seen, 0) < MinimumPerSide || Seen(seen, 5) < MinimumPerSide || kills0 == kills1)
            return null;

        var levels = Enumerable.Range(0, 10).Select(place => numbers[5 + 2 * place]).ToArray();
        var worth = Enumerable.Range(0, 10).Select(place => numbers[6 + 2 * place]).ToArray();
        var sorted = levels.Order().ToArray();
        if (sorted[0] <= (sorted[4] + sorted[5]) / 2.0 - AfkLevels)
            return null;

        // Top, mid and ADC are the carries; jungle, support and anyone without a lane the rest.
        var carry = Enumerable.Range(0, 10).Select(place => RiotGames.Decode(line[place]).Lane is Lane.Top or Lane.Mid or Lane.Adc).ToArray();
        double Gold(int from, bool carries) => Enumerable.Range(from, 5).Where(place => carry[place] == carries).Sum(place => worth[place]);

        var x = new[]
        {
            levels[..5].Sum() - levels[5..].Sum(),
            (Gold(0, true) - Gold(5, true)) / 1000.0,
            (Gold(0, false) - Gold(5, false)) / 1000.0,
            Flag(Active(objectives, 0, 8, second, BaronSeconds)) - Flag(Active(objectives, 1, 8, second, BaronSeconds)),
            Flag(Active(objectives, 0, 7, second, ElderSeconds)) - Flag(Active(objectives, 1, 7, second, ElderSeconds)),
            Dragons(objectives, 0, second) - Dragons(objectives, 1, second),
            Flag(HasSoul(objectives, 0, second)) - Flag(HasSoul(objectives, 1, second)),
        };

        var phase = PhaseOf(second);
        int Key(int place)
        {
            var key = (RiotGames.Decode(line[place]).ChampionId, phase);
            if (!keys.TryGetValue(key, out var id))
                keys[key] = id = keys.Count;
            return id;
        }

        return new Fight(x, [.. Enumerable.Range(0, 5).Select(Key)], [.. Enumerable.Range(5, 5).Select(Key)], kills0 > kills1 ? 1 : 0);
    }

    private static double Flag(bool value) => value ? 1 : 0;

    /// <summary>Whether a side took this monster (kind) within the last <paramref name="lasts"/> seconds before the fight.</summary>
    private static bool Active(List<int[]> objectives, int side, int kind, int second, int lasts)
        => objectives.Any(o => o[1] == side && o[2] == kind && o[0] < second && second - o[0] <= lasts);

    private static int Dragons(List<int[]> objectives, int side, int second)
        => objectives.Count(o => o[1] == side && o[2] is >= 1 and <= 6 && o[0] < second);

    private static bool HasSoul(List<int[]> objectives, int side, int second)
        => objectives.Any(o => o[1] == side && o[2] is >= 21 and <= 26 && o[0] < second);
}
