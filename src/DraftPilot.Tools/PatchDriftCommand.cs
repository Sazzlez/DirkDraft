using System.Text.Json;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.Tools;

/// <summary>
/// How much an older data file is still worth. Compares the lane rates of a saved snapshot with the
/// current one, champion by champion: how far they moved beyond what sampling noise alone moves
/// them, and how much of each lane's ranking survives. The answer decides how loudly the window has
/// to ask for an update — a quiet footer line is right if a patch moves little, wrong if it moves
/// the top of the list.
/// </summary>
internal static class PatchDriftCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Aufruf: patchdrift <alter-snapshot.json>");
            return 2;
        }

        var current = new SnapshotStore().Load();
        if (current is null)
        {
            Console.Error.WriteLine("Kein aktueller Snapshot vorhanden.");
            return 1;
        }

        MetaSnapshot? old;
        using (var stream = File.OpenRead(args[1]))
            old = JsonSerializer.Deserialize(stream, MetaJson.Default.MetaSnapshot);

        if (old is null)
        {
            Console.Error.WriteLine("Alter Snapshot nicht lesbar.");
            return 1;
        }

        Console.WriteLine($"Alt: {old.DataPatch} ({old.Tier}, {old.BuiltAtUtc:yyyy-MM-dd})   Neu: {current.DataPatch} ({current.Tier}, {current.BuiltAtUtc:yyyy-MM-dd})");
        Console.WriteLine();

        var before = old.LaneStats.Where(stat => stat.Play > 0).ToDictionary(stat => (stat.ChampionId, stat.Lane));
        double excess = 0, noise = 0, weights = 0;
        var moved = new List<(string Name, Lane Lane, double From, double To, double Sigmas)>();

        foreach (var stat in current.LaneStats.Where(stat => stat.Play > 0))
        {
            if (!before.TryGetValue((stat.ChampionId, stat.Lane), out var earlier))
                continue;

            var change = stat.WinRate - earlier.WinRate;
            var variance = (stat.WinRate * (1 - stat.WinRate) / stat.Play) + (earlier.WinRate * (1 - earlier.WinRate) / earlier.Play);

            excess += change * change;
            noise += variance;
            weights++;

            var name = current.Champions.FirstOrDefault(entry => entry.Id == stat.ChampionId)?.Name ?? $"#{stat.ChampionId}";
            moved.Add((name, stat.Lane, earlier.WinRate, stat.WinRate, change / Math.Sqrt(variance)));
        }

        var drift = Math.Sqrt(Math.Max(0, (excess - noise) / weights));
        Console.WriteLine($"{weights:N0} Lane-Zeilen in beiden Dateien.");
        Console.WriteLine($"  echte Verschiebung (über das Stichprobenrauschen hinaus): {drift * 100:0.00} Punkte Standardabweichung");
        Console.WriteLine($"  zum Vergleich die wahre Streuung der Lane-Stärke selbst: ~1,6 Punkte (Tools -- priors)");
        Console.WriteLine();

        Console.WriteLine("Am stärksten bewegt (in Standardfehlern):");
        foreach (var entry in moved.OrderByDescending(entry => Math.Abs(entry.Sigmas)).Take(12))
            Console.WriteLine($"  {entry.Name,-14} {entry.Lane.Display(),-9} {entry.From,7:P1} → {entry.To,7:P1}   ({entry.Sigmas:+0.0;-0.0} σ)");

        Console.WriteLine();
        Console.WriteLine("Wie viel der Rangfolge je Lane übrig bleibt:");

        foreach (var lane in Lanes.All)
        {
            var pairs = moved.Where(entry => entry.Lane == lane).ToList();
            if (pairs.Count < 5)
                continue;

            var rho = Spearman(pairs.Select(entry => entry.From).ToList(), pairs.Select(entry => entry.To).ToList());

            // The top five of the old file: how many are still in the top five now.
            var oldTop = pairs.OrderByDescending(entry => entry.From).Take(5).Select(entry => entry.Name).ToHashSet();
            var newTop = pairs.OrderByDescending(entry => entry.To).Take(5).Select(entry => entry.Name).ToHashSet();

            Console.WriteLine($"  {lane.Display(),-9} Rangkorrelation {rho:0.00} · von den alten Top 5 noch {oldTop.Intersect(newTop).Count()} in den Top 5");
        }

        return 0;
    }

    private static double Spearman(List<double> a, List<double> b)
    {
        var ra = Ranks(a);
        var rb = Ranks(b);
        var ma = ra.Average();
        var mb = rb.Average();
        var cov = ra.Zip(rb, (x, y) => (x - ma) * (y - mb)).Sum();
        var va = ra.Sum(x => (x - ma) * (x - ma));
        var vb = rb.Sum(y => (y - mb) * (y - mb));
        return cov / Math.Sqrt(va * vb);
    }

    private static List<double> Ranks(List<double> values)
    {
        var order = values.Select((value, index) => (value, index)).OrderBy(pair => pair.value).ToList();
        var ranks = new double[values.Count];
        for (var rank = 0; rank < order.Count; rank++)
            ranks[order[rank].index] = rank;
        return [.. ranks];
    }
}
