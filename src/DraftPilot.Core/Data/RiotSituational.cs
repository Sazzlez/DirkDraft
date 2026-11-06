using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// What the enemy line-up looks like, coarse on purpose: few situations, so each collects enough
/// games. The app computes the same flags from the same definitions in the game (core/situational.ts);
/// the two must stay in step.
/// </summary>
public static class Situations
{
    public const int All = 0;
    public const int Physical = 1;
    public const int Magic = 2;
    public const int Tanks = 3;
    public const int HardCc = 4;

    public static readonly string[] Names = ["all", "physical", "magic", "tanks", "cc"];

    /// <summary>A damage type counts as dominant from this share of the known enemies' damage on.</summary>
    public const double DominantShare = 0.6;

    /// <summary>Frontline as the composition rules see it: tagged Tank, or Riot's defense rating ≥ 6.</summary>
    public const int FrontlineDefense = 6;

    public const int TanksNeeded = 2;
    public const int HardCcNeeded = 3;

    public static List<int> Of(IEnumerable<EnemyProfile> enemies)
    {
        var list = enemies.ToList();
        var flags = new List<int> { All };

        double physical = 0, magic = 0;
        var known = 0;
        foreach (var enemy in list)
        {
            switch (enemy.Damage)
            {
                case DamageType.Physical:
                    physical++;
                    break;
                case DamageType.Magic:
                    magic++;
                    break;
                case DamageType.Mixed:
                    physical += 0.5;
                    magic += 0.5;
                    break;
                default:
                    continue;
            }

            known++;
        }

        if (known >= 3 && physical / known >= DominantShare)
            flags.Add(Physical);
        if (known >= 3 && magic / known >= DominantShare)
            flags.Add(Magic);
        if (list.Count(enemy => enemy.Frontline) >= TanksNeeded)
            flags.Add(Tanks);
        if (list.Count(enemy => enemy.HardCc) >= HardCcNeeded)
            flags.Add(HardCc);

        return flags;
    }
}

/// <summary>The three facts about an enemy the situations are made of.</summary>
public readonly record struct EnemyProfile(DamageType Damage, bool Frontline, bool HardCc);

/// <summary>
/// situational.json: for each champion, lane, slot (0 boots, 1 items four to six) and situation,
/// how each item did — and the slot's total, item 0, to compare it with. Rows are int arrays:
/// [champion, lane, slot, situation, item, games, wins]; class rows carry the class index first.
/// </summary>
public sealed class RiotSituationalFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    public List<string> Situations { get; set; } = [.. Data.Situations.Names];

    /// <summary>Riot's first class tag per champion, in the order of <see cref="ClassNames"/>.</summary>
    public List<string> ClassNames { get; set; } = [];

    public List<int[]> Rows { get; set; } = [];

    public List<int[]> Classes { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(RiotSituationalFile))]
public sealed partial class RiotSituationalJson : JsonSerializerContext;

/// <summary>
/// Counts situational choices: for every player, the boots and the fourth to sixth item, under each
/// situation the enemy line-up was in. Comparing an item with its own slot's total in the same
/// situation keeps the obvious bias out — whoever wins buys more items — though not every bias:
/// a defensive item bought from behind still carries some of that game.
/// </summary>
public sealed class RiotSituationalTally
{
    public const int BootsSlot = 0;
    public const int LateSlot = 1;

    private readonly Dictionary<(int Champion, Lane Lane, int Slot, int Situation, int Item), (int Games, int Wins)> _rows = [];

    public void Add(MatchReading game, Func<int, EnemyProfile> profile)
    {
        foreach (var player in game.Participants)
        {
            if (player.Lane == Lane.Unknown)
                continue;

            var enemies = game.Participants.Where(other => other.TeamId != player.TeamId).Select(other => profile(other.ChampionId));
            var situations = Situations.Of(enemies);

            if (player.Boots > 0)
                Count(player, BootsSlot, situations, [player.Boots]);

            // Items four to six: a game that never got there says nothing about them.
            if (player.Items.Count >= 4)
                Count(player, LateSlot, situations, [.. player.Items.Skip(3).Take(3)]);
        }
    }

    public void Add(IEnumerable<int[]> stored)
    {
        foreach (var row in stored.Where(row => row.Length >= 7))
            Bump((row[0], (Lane)row[1], row[2], row[3], row[4]), row[5], row[6]);
    }

    public List<int[]> Export() =>
        [.. _rows.OrderBy(p => p.Key.Champion).ThenBy(p => p.Key.Lane).ThenBy(p => p.Key.Slot).ThenBy(p => p.Key.Situation).ThenBy(p => p.Key.Item)
            .Select(p => new[] { p.Key.Champion, (int)p.Key.Lane, p.Key.Slot, p.Key.Situation, p.Key.Item, p.Value.Games, p.Value.Wins })];

    /// <summary>
    /// The published file: champion rows from <paramref name="minimumGames"/> on, class rows summed over
    /// every champion of the class (before any cut) from <paramref name="minimumClassGames"/> on. Slot
    /// totals (item 0) are kept wherever an item row of theirs is.
    /// </summary>
    public RiotSituationalFile File(
        string patch,
        string platform,
        DateTimeOffset collectedAtUtc,
        IReadOnlyDictionary<int, string> classOf,
        int minimumGames = 20,
        int minimumClassGames = 100)
    {
        var classNames = classOf.Values.Distinct().Order(StringComparer.Ordinal).ToList();
        var classIndex = classNames.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);

        var classRows = new Dictionary<(int Class, Lane Lane, int Slot, int Situation, int Item), (int Games, int Wins)>();
        foreach (var ((champion, lane, slot, situation, item), (games, wins)) in _rows)
        {
            if (!classOf.TryGetValue(champion, out var name))
                continue;
            var key = (classIndex[name], lane, slot, situation, item);
            var sum = classRows.GetValueOrDefault(key);
            classRows[key] = (sum.Games + games, sum.Wins + wins);
        }

        return new RiotSituationalFile
        {
            Patch = patch,
            Platform = platform,
            CollectedAtUtc = collectedAtUtc,
            ClassNames = classNames,
            Rows = Cut(_rows, minimumGames),
            Classes = Cut(classRows, minimumClassGames),
        };

        static List<int[]> Cut<TKey>(Dictionary<TKey, (int Games, int Wins)> rows, int minimum)
            where TKey : notnull
        {
            var items = rows.Where(p => Item(p.Key) != 0 && p.Value.Games >= minimum).ToList();
            var totals = items.Select(p => Total(p.Key)).ToHashSet();
            return [.. rows
                .Where(p => Item(p.Key) == 0 ? totals.Contains(Total(p.Key)) : p.Value.Games >= minimum)
                .Select(p => Flatten(p.Key, p.Value))
                .OrderBy(row => row[0]).ThenBy(row => row[1]).ThenBy(row => row[2]).ThenBy(row => row[3]).ThenBy(row => row[4])];
        }

        static int Item<TKey>(TKey key) => key is ValueTuple<int, Lane, int, int, int> t ? t.Item5 : 0;

        static (int, Lane, int, int) Total<TKey>(TKey key)
            => key is ValueTuple<int, Lane, int, int, int> t ? (t.Item1, t.Item2, t.Item3, t.Item4) : default;

        static int[] Flatten<TKey>(TKey key, (int Games, int Wins) value)
            => key is ValueTuple<int, Lane, int, int, int> t
                ? [t.Item1, (int)t.Item2, t.Item3, t.Item4, t.Item5, value.Games, value.Wins]
                : [];
    }

    private void Count(ParticipantBuild player, int slot, List<int> situations, IReadOnlyList<int> items)
    {
        var win = player.Win ? 1 : 0;
        foreach (var situation in situations)
        {
            Bump((player.ChampionId, player.Lane, slot, situation, 0), 1, win);
            foreach (var item in items.Distinct())
                Bump((player.ChampionId, player.Lane, slot, situation, item), 1, win);
        }
    }

    private void Bump((int, Lane, int, int, int) key, int games, int wins)
    {
        var current = _rows.GetValueOrDefault(key);
        _rows[key] = (current.Games + games, current.Wins + wins);
    }
}
