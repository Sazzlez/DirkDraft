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
    public const int EnemyArmor = 5;
    public const int EnemyMagicResist = 6;
    public const int EnemySustain = 7;

    public static readonly string[] Names = ["all", "physical", "magic", "tanks", "cc", "armor", "magicResist", "sustain"];

    /// <summary>Armor from the enemies' finished items and boots together — two to three armor items.</summary>
    public const int ArmorHeavy = 150;

    /// <summary>Magic resist from their items together — two to three MR items.</summary>
    public const int MagicResistHeavy = 120;

    /// <summary>Items that heal their owner from damage (life steal, omnivamp), across the enemy team.</summary>
    public const int SustainNeeded = 2;

    /// <summary>A damage type counts as dominant from this share of the known enemies' damage on.</summary>
    public const double DominantShare = 0.6;

    /// <summary>Frontline as the composition rules see it: tagged Tank, or Riot's defense rating ≥ 6.</summary>
    public const int FrontlineDefense = 6;

    public const int TanksNeeded = 2;
    public const int HardCcNeeded = 3;

    public static List<int> Of(IEnumerable<EnemyProfile> enemies) => Of(enemies, default);

    /// <param name="items">What the enemies have built at the moment in question.</param>
    public static List<int> Of(IEnumerable<EnemyProfile> enemies, EnemyItems items)
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
        if (items.Armor >= ArmorHeavy)
            flags.Add(EnemyArmor);
        if (items.MagicResist >= MagicResistHeavy)
            flags.Add(EnemyMagicResist);
        if (items.SustainItems >= SustainNeeded)
            flags.Add(EnemySustain);

        return flags;
    }

    /// <summary>The enemies' item totals from their inventories; only finished items and boots count.</summary>
    public static EnemyItems ItemsOf(IEnumerable<int> enemyItems, ItemCatalog catalog)
    {
        int armor = 0, magicResist = 0, sustain = 0;
        foreach (var item in enemyItems)
        {
            if (!catalog.CountsForSituation(item))
                continue;
            armor += catalog.Armor(item);
            magicResist += catalog.MagicResist(item);
            if (catalog.IsSustain(item))
                sustain++;
        }

        return new EnemyItems(armor, magicResist, sustain);
    }
}

/// <summary>The three facts about an enemy the situations are made of.</summary>
public readonly record struct EnemyProfile(DamageType Damage, bool Frontline, bool HardCc);

/// <summary>What the enemy team had built at a moment, summed.</summary>
public readonly record struct EnemyItems(int Armor, int MagicResist, int SustainItems);

/// <summary>
/// The game state at a buy, from the team's gold lead: whoever is behind buys differently and loses
/// more often anyway, so purchases are compared only with purchases made in the same state.
/// </summary>
public static class GameStates
{
    public const int Even = 0;
    public const int Ahead = 1;
    public const int Behind = 2;

    /// <summary>
    /// About two kills and a dragon's worth: below this either side can still turn a fight, above it
    /// the build is made from the front (or from behind).
    /// </summary>
    public const long LeadThreshold = 1500;

    public static int Of(long goldLead)
        => goldLead >= LeadThreshold ? Ahead : goldLead <= -LeadThreshold ? Behind : Even;
}

/// <summary>
/// situational.json: for each champion, lane, slot (0 boots, 1 items four to six, 2 items one to three),
/// situation and game
/// state (0 even, 1 ahead, 2 behind), how often each item was bought and won — with the slot's total,
/// item 0, to compare it with. Rows are int arrays [champion, lane, slot, situation, state, item, games,
/// wins]; class rows carry the class index first.
/// </summary>
public sealed class RiotSituationalFile
{
    public const int CurrentVersion = 2;

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
/// Counts situational buys: for every player, the boots, the first three items (the core) and the
/// fourth to sixth item, each under the situations and the game state at the moment it was bought. What the app reads from it first is how
/// much more often an item is bought in a situation than otherwise — players react to what they see;
/// the win rate, compared within the same game state, only guards against suggesting what does worse.
/// </summary>
public sealed class RiotSituationalTally
{
    public const int BootsSlot = 0;
    public const int LateSlot = 1;

    /// <summary>
    /// Items one to three. Most reactions to an enemy line-up happen here — Mikael's Blessing second
    /// against heavy crowd control, an armor item third against a fed AD — and for supports nearly all:
    /// they rarely reach a fourth item (none counted for Soraka or Braum in 6.185 games).
    /// </summary>
    public const int CoreSlot = 2;

    private readonly Dictionary<Key, (int Games, int Wins)> _rows = [];

    private readonly record struct Key(int Owner, Lane Lane, int Slot, int Situation, int State, int Item);

    /// <param name="catalog">
    /// Item stats for the situations made of the enemies' items, judged at the moment of each buy;
    /// null counts only the line-up situations.
    /// </param>
    public void Add(MatchReading game, Func<int, EnemyProfile> profile, ItemCatalog? catalog = null)
    {
        foreach (var player in game.Participants)
        {
            if (player.Lane == Lane.Unknown)
                continue;

            var enemyPlayers = game.Participants.Where(other => other.TeamId != player.TeamId).ToList();
            var profiles = enemyPlayers.Select(other => profile(other.ChampionId)).ToList();

            // The moment of a buy: the line-up is fixed, the enemies' items and the gold lead are not.
            (List<int> Situations, int State) At(long timeMs) => (
                catalog is null
                    ? Situations.Of(profiles)
                    : Situations.Of(profiles, Situations.ItemsOf(enemyPlayers.SelectMany(enemy => enemy.OwnedAt(timeMs)), catalog)),
                GameStates.Of(game.GoldLead(player.TeamId, timeMs)));

            if (player.Boots > 0)
                Count(player, BootsSlot, [(player.Boots, At(player.BootsTimeMs))]);

            // The core: every finished item among the first three, each under its own moment.
            if (player.Items.Count >= 1)
            {
                Count(player, CoreSlot, [.. Enumerable.Range(0, Math.Min(3, player.Items.Count))
                    .Select(i => (player.Items[i], At(i < player.ItemTimes.Count ? player.ItemTimes[i] : long.MaxValue)))]);
            }

            // Items four to six: a game that never got there says nothing about them.
            if (player.Items.Count >= 4)
            {
                Count(player, LateSlot, [.. Enumerable.Range(3, Math.Min(3, player.Items.Count - 3))
                    .Select(i => (player.Items[i], At(i < player.ItemTimes.Count ? player.ItemTimes[i] : long.MaxValue)))]);
            }
        }
    }

    public void Add(IEnumerable<int[]> stored)
    {
        foreach (var row in stored.Where(row => row.Length >= 8))
            Bump(new Key(row[0], (Lane)row[1], row[2], row[3], row[4], row[5]), row[6], row[7]);
    }

    public List<int[]> Export() => Rows(_rows);

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
        int minimumGames = 10,
        int minimumClassGames = 50)
    {
        var classNames = classOf.Values.Distinct().Order(StringComparer.Ordinal).ToList();
        var classIndex = classNames.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);

        var classRows = new Dictionary<Key, (int Games, int Wins)>();
        foreach (var (key, (games, wins)) in _rows)
        {
            if (!classOf.TryGetValue(key.Owner, out var name))
                continue;
            var classKey = key with { Owner = classIndex[name] };
            var sum = classRows.GetValueOrDefault(classKey);
            classRows[classKey] = (sum.Games + games, sum.Wins + wins);
        }

        return new RiotSituationalFile
        {
            Patch = patch,
            Platform = platform,
            CollectedAtUtc = collectedAtUtc,
            ClassNames = classNames,
            Rows = Rows(Cut(_rows, minimumGames)),
            Classes = Rows(Cut(classRows, minimumClassGames)),
        };
    }

    /// <summary>
    /// Item rows whose games over all game states reach the minimum, with every state of theirs and
    /// the slot totals they are compared with.
    /// </summary>
    private static Dictionary<Key, (int Games, int Wins)> Cut(Dictionary<Key, (int Games, int Wins)> rows, int minimum)
    {
        var kept = rows
            .Where(p => p.Key.Item != 0)
            .GroupBy(p => p.Key with { State = 0 })
            .Where(group => group.Sum(p => p.Value.Games) >= minimum)
            .Select(group => group.Key)
            .ToHashSet();
        var totals = kept.Select(key => key with { Item = 0 }).ToHashSet();
        return rows
            .Where(p => p.Key.Item == 0 ? totals.Contains(p.Key with { State = 0 }) : kept.Contains(p.Key with { State = 0 }))
            .ToDictionary(p => p.Key, p => p.Value);
    }

    private static List<int[]> Rows(Dictionary<Key, (int Games, int Wins)> rows) =>
        [.. rows
            .OrderBy(p => p.Key.Owner).ThenBy(p => p.Key.Lane).ThenBy(p => p.Key.Slot).ThenBy(p => p.Key.Situation)
            .ThenBy(p => p.Key.State).ThenBy(p => p.Key.Item)
            .Select(p => new[] { p.Key.Owner, (int)p.Key.Lane, p.Key.Slot, p.Key.Situation, p.Key.State, p.Key.Item, p.Value.Games, p.Value.Wins })];

    /// <summary>
    /// The slot total once per (situation, state) any of the slot's buys happened in; each item under
    /// the situations and state of its own buy.
    /// </summary>
    private void Count(ParticipantBuild player, int slot, IReadOnlyList<(int Item, (List<int> Situations, int State) At)> buys)
    {
        var win = player.Win ? 1 : 0;
        var totals = new HashSet<(int Situation, int State)>();
        foreach (var (item, (situations, state)) in buys.DistinctBy(buy => buy.Item))
        {
            foreach (var situation in situations)
            {
                totals.Add((situation, state));
                Bump(new Key(player.ChampionId, player.Lane, slot, situation, state, item), 1, win);
            }
        }

        foreach (var (situation, state) in totals)
            Bump(new Key(player.ChampionId, player.Lane, slot, situation, state, 0), 1, win);
    }

    private void Bump(Key key, int games, int wins)
    {
        var current = _rows.GetValueOrDefault(key);
        _rows[key] = (current.Games + games, current.Wins + wins);
    }
}
