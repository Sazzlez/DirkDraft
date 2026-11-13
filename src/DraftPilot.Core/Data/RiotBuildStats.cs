using System.Globalization;
using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>One way to fill a build slot, and how it did: games played with it, games won.</summary>
public sealed class BuildCount
{
    /// <summary>Item, rune or spell ids, in the slot's own order (see <see cref="ChampionBuildStats"/>).</summary>
    public List<int> Ids { get; set; } = [];

    /// <summary>For skill orders: the letters, e.g. <c>QEWQQRQEQEREEWW</c>. Empty otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Text { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }

    [JsonIgnore]
    public double WinRate => Games > 0 ? (double)Wins / Games : 0;

    [JsonIgnore]
    internal string Key => Text is { Length: > 0 } text ? text : string.Join(',', Ids);
}

/// <summary>
/// Everything counted for one champion on one lane in one rank bracket. Every list is sorted by
/// games, most played first; the win rates are there to be read beside them, not to reorder them.
/// </summary>
public sealed class ChampionBuildStats
{
    public int ChampionId { get; set; }

    public Lane Lane { get; set; }

    /// <summary>The rank bracket, lower case as Riot's league endpoints spell it: <c>gold</c>, <c>platinum</c>.</summary>
    public string Tier { get; set; } = string.Empty;

    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>Eleven ids each: primary style, four runes, secondary style, two runes, three shards.</summary>
    public List<BuildCount> Runes { get; set; } = [];

    public List<BuildCount> Spells { get; set; } = [];

    public List<BuildCount> Starters { get; set; } = [];

    public List<BuildCount> Boots { get; set; } = [];

    /// <summary>The first three finished items, in the order they were bought.</summary>
    public List<BuildCount> Core { get; set; } = [];

    public List<BuildCount> Fourth { get; set; } = [];

    public List<BuildCount> Fifth { get; set; } = [];

    public List<BuildCount> Sixth { get; set; } = [];

    public List<BuildCount> SupportChoice { get; set; } = [];

    /// <summary>The first fifteen skill points.</summary>
    public List<BuildCount> SkillOrder { get; set; } = [];

    /// <summary>Which ability was maxed first, second, third, e.g. <c>QEW</c>.</summary>
    public List<BuildCount> SkillMax { get; set; } = [];

    /// <summary>
    /// When the pair of boots was bought: how many finished items came before it, as text —
    /// <c>0</c> to <c>3</c>, three standing for three or more. Not a choice between builds but its
    /// timing; the win rates are counted along and say nothing by themselves.
    /// </summary>
    public List<BuildCount> BootsAfter { get; set; } = [];

    /// <summary>
    /// When the first boots of any kind were bought: what the items held were worth going into that
    /// shop visit, in steps of <see cref="RiotBuildTally.BootsWorthStep"/> gold as text — <c>0</c>
    /// (below 500: the starting items), <c>500</c>, … up to <c>4000</c> and above.
    /// </summary>
    public List<BuildCount> BootsWorth { get; set; } = [];

    /// <summary>
    /// Shop visits of players without boots who spent at least the price of Boots in the visit, by
    /// what their items were worth going in and by what they spent: <c>worth/spent</c>, the worth in
    /// the steps of <see cref="BootsWorth"/>, the spend as <c>300</c> (up to 649 gold: Boots or one
    /// small part, not both), <c>650</c> (up to 1.299) or <c>1300</c> (and more). <c>Games</c> counts
    /// visits here, not games. Since 13.11.2026.
    /// </summary>
    public List<BuildCount> BootsShop { get; set; } = [];

    /// <summary>Of the visits in <see cref="BootsShop"/>, those in which boots were bought; the same keys.</summary>
    public List<BuildCount> BootsBought { get; set; } = [];
}

/// <summary>
/// The collected statistics as one file: what the collector keeps between runs and, cut down to
/// the options worth showing, what every DirkDraft downloads. No player appears in it anywhere.
/// </summary>
public sealed class RiotBuildFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The patch line the games were played on, e.g. <c>16.22</c>.</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Riot's platform, e.g. <c>euw1</c>.</summary>
    public string Platform { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>Games counted per rank bracket.</summary>
    public Dictionary<string, int> Matches { get; set; } = [];

    public List<ChampionBuildStats> Builds { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(RiotBuildFile))]
public sealed partial class RiotBuildJson : JsonSerializerContext;

/// <summary>
/// Adds games up into <see cref="ChampionBuildStats"/>. Kept as dictionaries while counting and
/// turned back into sorted lists for storage — the lists are what a file can hold, the
/// dictionaries what makes adding a game cheap.
/// </summary>
public sealed class RiotBuildTally
{
    /// <summary>How many skill points make up a skill order: every decision before the third ultimate.</summary>
    public const int SkillOrderLength = 15;

    private readonly Dictionary<(string Tier, int Champion, Lane Lane), Bucket> _buckets = [];

    public int Buckets => _buckets.Count;

    public void Add(ParticipantBuild game, string tier)
    {
        var key = (tier, game.ChampionId, game.Lane);
        if (!_buckets.TryGetValue(key, out var bucket))
            _buckets[key] = bucket = new Bucket();

        bucket.Games++;
        if (game.Win)
            bucket.Wins++;

        if (game.Runes.Count == 11)
            bucket.Count(Slot.Runes, game.Runes, null, game.Win);

        if (game.Spells.Count == 2)
            bucket.Count(Slot.Spells, game.Spells, null, game.Win);

        if (game.Starters.Count > 0)
            bucket.Count(Slot.Starters, game.Starters, null, game.Win);

        if (game.Boots > 0)
            bucket.Count(Slot.Boots, [game.Boots], null, game.Win);

        // Only a game that got that far says anything about a slot; a 22-minute stomp with two
        // items is no vote against a third.
        if (game.Items.Count >= 3)
            bucket.Count(Slot.Core, [.. game.Items.Take(3)], null, game.Win);
        if (game.Items.Count >= 4)
            bucket.Count(Slot.Fourth, [game.Items[3]], null, game.Win);
        if (game.Items.Count >= 5)
            bucket.Count(Slot.Fifth, [game.Items[4]], null, game.Win);
        if (game.Items.Count >= 6)
            bucket.Count(Slot.Sixth, [game.Items[5]], null, game.Win);

        if (game.SupportChoice > 0)
            bucket.Count(Slot.SupportChoice, [game.SupportChoice], null, game.Win);

        if (game.SkillOrder.Length >= SkillOrderLength)
            bucket.Count(Slot.SkillOrder, [], game.SkillOrder[..SkillOrderLength], game.Win);

        if (game.SkillMax is { Length: 3 } max)
            bucket.Count(Slot.SkillMax, [], max, game.Win);

        // When the boots were bought — the pair by finished items before it, the first boots by what
        // the inventory was worth. Only games whose timeline says so.
        if (game.ItemsBeforeBoots >= 0)
            bucket.Count(Slot.BootsAfter, [], Math.Min(game.ItemsBeforeBoots, MostItemsBeforeBoots).ToString(CultureInfo.InvariantCulture), game.Win);
        if (game.WorthBeforeBoots >= 0)
            bucket.Count(Slot.BootsWorth, [], BootsWorthKey(game.WorthBeforeBoots), game.Win);

        // What players without boots do with the gold at hand: every visit in which boots were
        // affordable, and those in which they were bought.
        foreach (var visit in game.BootsVisits)
        {
            var step = BootsVisitKey(visit.Worth, visit.Spent);
            bucket.Count(Slot.BootsShop, [], step, game.Win);
            if (visit.Bought)
                bucket.Count(Slot.BootsBought, [], step, game.Win);
        }
    }

    /// <summary>
    /// The lower bounds of the spend steps of a shop visit: Boots cost 300, Boots and the smallest
    /// part of a damage item 650 (a Long Sword), and from 1.300 a large part fits beside them.
    /// </summary>
    public static readonly IReadOnlyList<int> BootsSpendSteps = [300, 650, 1300];

    /// <summary>The key of a shop visit: the worth step, a slash, the spend step — <c>1000/650</c>.</summary>
    public static string BootsVisitKey(int worth, int spent)
        => $"{BootsWorthKey(worth)}/{BootsSpendSteps.Last(step => step <= Math.Max(spent, BootsSpendSteps[0])).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Finished items before the boots are counted up to this many; more is the same answer, "after the core".</summary>
    public const int MostItemsBeforeBoots = 3;

    /// <summary>The step, in gold, the inventory's worth at the first boots is counted in.</summary>
    public const int BootsWorthStep = 500;

    /// <summary>The highest step; anything above counts there.</summary>
    public const int BootsWorthCap = 4000;

    /// <summary>The step a worth falls in, by its lower bound: 1.150 gold is <c>1000</c>.</summary>
    public static string BootsWorthKey(int worth)
        => (Math.Clamp(worth, 0, BootsWorthCap) / BootsWorthStep * BootsWorthStep).ToString(CultureInfo.InvariantCulture);

    /// <summary>Counts already in a file, e.g. from the collector's previous run.</summary>
    public void Add(IEnumerable<ChampionBuildStats> stored)
    {
        foreach (var stats in stored)
        {
            var key = (stats.Tier, stats.ChampionId, stats.Lane);
            if (!_buckets.TryGetValue(key, out var bucket))
                _buckets[key] = bucket = new Bucket();

            bucket.Games += stats.Games;
            bucket.Wins += stats.Wins;

            foreach (var slot in Enum.GetValues<Slot>())
            {
                foreach (var option in Lists(stats, slot))
                    bucket.Merge(slot, option);
            }
        }
    }

    /// <summary>
    /// The counts as sorted lists, at most <paramref name="keep"/> options per slot and none with
    /// fewer than <paramref name="minimumGames"/> games. The collector keeps a generous cut between
    /// runs — a long tail of one-off starting buys would otherwise grow the state without bound —
    /// and the published file a tighter one.
    /// </summary>
    public List<ChampionBuildStats> Export(int keep, int minimumGames = 1)
    {
        var list = new List<ChampionBuildStats>(_buckets.Count);

        foreach (var ((tier, champion, lane), bucket) in _buckets.OrderBy(b => b.Key.Tier).ThenBy(b => b.Key.Champion).ThenBy(b => b.Key.Lane))
        {
            var stats = new ChampionBuildStats
            {
                ChampionId = champion,
                Lane = lane,
                Tier = tier,
                Games = bucket.Games,
                Wins = bucket.Wins,
            };

            foreach (var slot in Enum.GetValues<Slot>())
            {
                // The timing slots are a handful of steps, not a long tail of choices: cut, by number
                // or by games, the steps left would no longer add up to what they were counted from
                // — and a visit count without its rare steps would no longer divide by the right total.
                var timing = slot is Slot.BootsAfter or Slot.BootsWorth or Slot.BootsShop or Slot.BootsBought;
                var target = Lists(stats, slot);
                target.AddRange(bucket.Options(slot)
                    .Where(option => timing || option.Games >= minimumGames)
                    .OrderByDescending(option => option.Games)
                    .ThenByDescending(option => option.Wins)
                    .ThenBy(option => option.Key, StringComparer.Ordinal)
                    .Take(timing ? int.MaxValue : keep)
                    .Select(Copy));
            }

            list.Add(stats);
        }

        return list;
    }

    private static BuildCount Copy(BuildCount option)
        => new() { Ids = [.. option.Ids], Text = option.Text, Games = option.Games, Wins = option.Wins };

    private static List<BuildCount> Lists(ChampionBuildStats stats, Slot slot) => slot switch
    {
        Slot.Runes => stats.Runes,
        Slot.Spells => stats.Spells,
        Slot.Starters => stats.Starters,
        Slot.Boots => stats.Boots,
        Slot.Core => stats.Core,
        Slot.Fourth => stats.Fourth,
        Slot.Fifth => stats.Fifth,
        Slot.Sixth => stats.Sixth,
        Slot.SupportChoice => stats.SupportChoice,
        Slot.SkillOrder => stats.SkillOrder,
        Slot.SkillMax => stats.SkillMax,
        Slot.BootsAfter => stats.BootsAfter,
        Slot.BootsWorth => stats.BootsWorth,
        Slot.BootsShop => stats.BootsShop,
        Slot.BootsBought => stats.BootsBought,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private enum Slot
    {
        Runes,
        Spells,
        Starters,
        Boots,
        Core,
        Fourth,
        Fifth,
        Sixth,
        SupportChoice,
        SkillOrder,
        SkillMax,
        BootsAfter,
        BootsWorth,
        BootsShop,
        BootsBought,
    }

    private sealed class Bucket
    {
        private readonly Dictionary<string, BuildCount>[] _slots =
            [.. Enumerable.Range(0, Enum.GetValues<Slot>().Length).Select(_ => new Dictionary<string, BuildCount>(StringComparer.Ordinal))];

        public int Games { get; set; }

        public int Wins { get; set; }

        public IEnumerable<BuildCount> Options(Slot slot) => _slots[(int)slot].Values;

        public void Count(Slot slot, IReadOnlyList<int> ids, string? text, bool win)
            => Merge(slot, new BuildCount { Ids = [.. ids], Text = text, Games = 1, Wins = win ? 1 : 0 });

        public void Merge(Slot slot, BuildCount option)
        {
            var map = _slots[(int)slot];
            if (map.TryGetValue(option.Key, out var existing))
            {
                existing.Games += option.Games;
                existing.Wins += option.Wins;
            }
            else
            {
                map[option.Key] = Copy(option);
            }
        }
    }
}
