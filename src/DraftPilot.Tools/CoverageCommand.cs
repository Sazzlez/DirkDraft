using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.Tools;

/// <summary>
/// How much of a real lane duel the stored file can actually speak to. OP.GG names only the three
/// most notable opponents per list, so the matchup term exists for a minority of candidates; this
/// says how small a minority, for the opponents that are actually met most often.
/// </summary>
internal static class CoverageCommand
{
    public static int Run()
    {
        var snapshot = new SnapshotStore().Load();
        if (snapshot is null)
        {
            Console.Error.WriteLine("Kein Snapshot vorhanden. Erst 'update' ausführen.");
            return 1;
        }

        var meta = new MetaLookup(snapshot);

        Console.WriteLine($"Snapshot {snapshot.Patch}, Bracket {snapshot.Tier ?? "?"}. Je Lane: die 10 meistgespielten Gegner,");
        Console.WriteLine("und wie viele Kandidaten derselben Lane eine Duell-Zahl gegen sie haben (gespeichert + gespiegelt).");
        Console.WriteLine();

        foreach (var lane in Lanes.All)
        {
            var roster = meta.Roster(lane);
            var opponents = roster
                .Select(id => (Id: id, Play: meta.LaneStat(id, lane)?.Play ?? 0))
                .OrderByDescending(entry => entry.Play)
                .Take(10)
                .ToList();

            var shares = new List<double>();
            var games = new List<int>();

            foreach (var (opponent, _) in opponents)
            {
                var candidates = roster.Where(id => id != opponent).ToList();
                var covered = candidates.Select(id => meta.Matchup(id, opponent, lane)).Where(view => view is not null).ToList();

                shares.Add((double)covered.Count / candidates.Count);
                games.AddRange(covered.Select(view => view!.Value.Play));
            }

            games.Sort();
            var medianGames = games.Count == 0 ? 0 : games[games.Count / 2];

            Console.WriteLine($"  {lane.Display(),-9} {roster.Count,3} Kandidaten · Duell-Zahl für {shares.Average():P0} von ihnen "
                + $"(Spanne {shares.Min():P0}–{shares.Max():P0}) · Median {medianGames:N0} Spiele je Duell");
        }

        Console.WriteLine();
        Console.WriteLine("Im Draft holt das Tool für jeden aufgedeckten Gegner dessen Counter live nach; OP.GG liefert dabei");
        Console.WriteLine("je Liste die drei auffälligsten (stark, schwach, je Position), zusammen etwa 6 bis 9 Duelle.");

        return 0;
    }
}
