using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// The draft numbers counted from ranked games, as stored between collector runs: plain counts, no
/// rates, so two partial tallies add up exactly. Rows are int arrays to keep the state file small —
/// the duo table alone runs to hundreds of thousands of rows.
/// </summary>
public sealed class RiotMetaCounts
{
    /// <summary>Games counted.</summary>
    public int Matches { get; set; }

    /// <summary>[champion, lane, games, wins].</summary>
    public List<int[]> Lanes { get; set; } = [];

    /// <summary>[champion, games, bans, physical, magic, true] — damage to champions, summed.</summary>
    public List<long[]> Champions { get; set; } = [];

    /// <summary>[lane, champion, opponent, games, wins of champion]; champion &lt; opponent.</summary>
    public List<int[]> Duels { get; set; } = [];

    /// <summary>[champion, lane, partner, partner lane, games, wins]; champion &lt; partner.</summary>
    public List<int[]> Duos { get; set; } = [];
}

/// <summary>Where a row is thin enough to leave out of the published file.</summary>
public sealed record MetaExportLimits
{
    /// <summary>
    /// A lane row needs this many games and this share of the champion's games to make the lane's
    /// candidate list. Below it the row is a stray (Yuumi top, three games) and would only crowd the
    /// list with champions nobody plays there; the lane predictor still knows them through its floor.
    /// </summary>
    public int LaneMinimumGames { get; init; } = 20;

    public double LaneMinimumRoleRate { get; init; } = 0.05;

    /// <summary>Duels and duos below this are mostly shrinkage prior anyway (1.000 and 750 games).</summary>
    public int DuelMinimumGames { get; init; } = 10;

    public int DuoMinimumGames { get; init; } = 30;
}

/// <summary>
/// Counts what the draft model reads — lane win rates, pick, ban and role rates, every duel, every
/// duo, damage type — from the same games the builds are counted from. Complete by construction:
/// every pairing that happened is counted, where OP.GG listed only the opponents that stand out.
/// </summary>
public sealed class RiotMetaTally
{
    /// <summary>
    /// A champion counts as physical (magic) when at least this share of its damage to champions is
    /// physical (magic), and as mixed in between. True damage is left out: it is neither, and itemising
    /// against it is not possible anyway.
    /// </summary>
    public const double DamageTypeShare = 0.7;

    private readonly Dictionary<(int Champion, Lane Lane), Count> _lanes = [];
    private readonly Dictionary<int, ChampionCount> _champions = [];
    private readonly Dictionary<(Lane Lane, int Champion, int Opponent), Count> _duels = [];
    private readonly Dictionary<(int Champion, Lane Lane, int Partner, Lane PartnerLane), Count> _duos = [];

    public int Matches { get; private set; }

    /// <summary>
    /// One counted game. Lane rows and damage take every player with a lane; a duel needs exactly one
    /// player per side on that lane, so a game with two "tops" on one side gives no top duel rather
    /// than a guessed one.
    /// </summary>
    public void Add(MatchReading game)
    {
        Matches++;

        foreach (var player in game.Participants)
        {
            Bump(_lanes, (player.ChampionId, player.Lane), player.Win);

            var champion = Champion(player.ChampionId);
            champion.Games++;
            champion.Physical += player.PhysicalDamage;
            champion.Magic += player.MagicDamage;
            champion.True += player.TrueDamage;
        }

        foreach (var banned in game.BannedChampions.Distinct())
            Champion(banned).Bans++;

        var sides = game.Participants.GroupBy(player => player.TeamId).ToList();

        foreach (var lane in Draft.Lanes.All)
        {
            var onLane = game.Participants.Where(player => player.Lane == lane).ToList();
            if (onLane.Count != 2 || onLane[0].TeamId == onLane[1].TeamId)
                continue;

            var (low, high) = onLane[0].ChampionId < onLane[1].ChampionId ? (onLane[0], onLane[1]) : (onLane[1], onLane[0]);
            if (low.ChampionId == high.ChampionId)
                continue;

            Bump(_duels, (lane, low.ChampionId, high.ChampionId), low.Win);
        }

        foreach (var side in sides)
        {
            var team = side.ToList();
            for (var i = 0; i < team.Count; i++)
            {
                for (var j = i + 1; j < team.Count; j++)
                {
                    var (a, b) = team[i].ChampionId < team[j].ChampionId ? (team[i], team[j]) : (team[j], team[i]);
                    if (a.ChampionId == b.ChampionId)
                        continue;

                    Bump(_duos, (a.ChampionId, a.Lane, b.ChampionId, b.Lane), a.Win);
                }
            }
        }
    }

    /// <summary>Adds a stored tally, e.g. the state of the previous run.</summary>
    public void Add(RiotMetaCounts stored)
    {
        Matches += stored.Matches;

        foreach (var row in stored.Lanes.Where(row => row.Length >= 4))
            Merge(_lanes, (row[0], (Lane)row[1]), row[2], row[3]);

        foreach (var row in stored.Champions.Where(row => row.Length >= 6))
        {
            var champion = Champion((int)row[0]);
            champion.Games += (int)row[1];
            champion.Bans += (int)row[2];
            champion.Physical += row[3];
            champion.Magic += row[4];
            champion.True += row[5];
        }

        foreach (var row in stored.Duels.Where(row => row.Length >= 5))
            Merge(_duels, ((Lane)row[0], row[1], row[2]), row[3], row[4]);

        foreach (var row in stored.Duos.Where(row => row.Length >= 6))
            Merge(_duos, (row[0], (Lane)row[1], row[2], (Lane)row[3]), row[4], row[5]);
    }

    /// <summary>Everything, for the state file; sorted so two equal tallies write equal files.</summary>
    public RiotMetaCounts Export() => new()
    {
        Matches = Matches,
        Lanes = [.. _lanes.OrderBy(p => p.Key.Champion).ThenBy(p => p.Key.Lane)
            .Select(p => new[] { p.Key.Champion, (int)p.Key.Lane, p.Value.Games, p.Value.Wins })],
        Champions = [.. _champions.OrderBy(p => p.Key)
            .Select(p => new long[] { p.Key, p.Value.Games, p.Value.Bans, p.Value.Physical, p.Value.Magic, p.Value.True })],
        Duels = [.. _duels.OrderBy(p => p.Key.Lane).ThenBy(p => p.Key.Champion).ThenBy(p => p.Key.Opponent)
            .Select(p => new[] { (int)p.Key.Lane, p.Key.Champion, p.Key.Opponent, p.Value.Games, p.Value.Wins })],
        Duos = [.. _duos.OrderBy(p => p.Key.Champion).ThenBy(p => p.Key.Lane).ThenBy(p => p.Key.Partner).ThenBy(p => p.Key.PartnerLane)
            .Select(p => new[] { p.Key.Champion, (int)p.Key.Lane, p.Key.Partner, (int)p.Key.PartnerLane, p.Value.Games, p.Value.Wins })],
    };

    /// <summary>
    /// The rows of a <see cref="MetaSnapshot"/>, in the meaning OP.GG's rows had: pick and ban rate
    /// per game, role rate as the share of the champion's games on that lane, both directions of
    /// every duel (each one measured, none inferred). No tier: that was OP.GG's own grading.
    /// </summary>
    public MetaRows Rows(MetaExportLimits? limits = null)
    {
        limits ??= new MetaExportLimits();
        var matches = Math.Max(1, Matches);

        var lanes = new List<LaneStat>();
        foreach (var ((championId, lane), count) in _lanes.OrderBy(p => p.Key.Champion).ThenBy(p => p.Key.Lane))
        {
            var champion = _champions.GetValueOrDefault(championId);
            var total = Math.Max(1, champion?.Games ?? count.Games);
            var roleRate = (double)count.Games / total;

            if (count.Games < limits.LaneMinimumGames || roleRate < limits.LaneMinimumRoleRate)
                continue;

            lanes.Add(new LaneStat
            {
                ChampionId = championId,
                Lane = lane,
                WinRate = (double)count.Wins / count.Games,
                PickRate = (double)count.Games / matches,
                BanRate = (double)(champion?.Bans ?? 0) / matches,
                RoleRate = roleRate,
                Tier = -1,
                Play = count.Games,
            });
        }

        var duels = new List<MatchupStat>();
        foreach (var ((lane, low, high), count) in _duels.OrderBy(p => p.Key.Lane).ThenBy(p => p.Key.Champion).ThenBy(p => p.Key.Opponent))
        {
            if (count.Games < limits.DuelMinimumGames)
                continue;

            var rate = (double)count.Wins / count.Games;
            duels.Add(new MatchupStat { ChampionId = low, OpponentId = high, Lane = lane, WinRate = rate, Play = count.Games, FromCompleteList = true });
            duels.Add(new MatchupStat { ChampionId = high, OpponentId = low, Lane = lane, WinRate = 1 - rate, Play = count.Games, FromCompleteList = true });
        }

        var duos = new List<SynergyStat>();
        foreach (var ((a, laneA, b, laneB), count) in _duos.OrderBy(p => p.Key.Champion).ThenBy(p => p.Key.Lane).ThenBy(p => p.Key.Partner).ThenBy(p => p.Key.PartnerLane))
        {
            if (count.Games < limits.DuoMinimumGames)
                continue;

            duos.Add(new SynergyStat
            {
                ChampionId = a,
                Lane = laneA,
                PartnerId = b,
                PartnerLane = laneB,
                WinRate = (double)count.Wins / count.Games,
                Play = count.Games,
                Tier = -1,
            });
        }

        var damage = _champions.ToDictionary(p => p.Key, p => DamageOf(p.Value));
        return new MetaRows(Matches, lanes, duels, duos, damage);
    }

    /// <summary>Physical, magic or mixed from the champion's own damage to champions.</summary>
    public static DamageType DamageOf(long physical, long magic)
    {
        var total = physical + magic;
        if (total <= 0)
            return DamageType.Unknown;

        var share = (double)physical / total;
        return share >= DamageTypeShare ? DamageType.Physical
            : 1 - share >= DamageTypeShare ? DamageType.Magic
            : DamageType.Mixed;
    }

    private static DamageType DamageOf(ChampionCount count) => DamageOf(count.Physical, count.Magic);

    /// <summary>The champion's damage type as counted so far; Unknown before its first game.</summary>
    public DamageType DamageOfChampion(int championId)
        => _champions.TryGetValue(championId, out var count) ? DamageOf(count) : DamageType.Unknown;

    private ChampionCount Champion(int id)
    {
        if (!_champions.TryGetValue(id, out var count))
            _champions[id] = count = new ChampionCount();
        return count;
    }

    private static void Bump<TKey>(Dictionary<TKey, Count> map, TKey key, bool win)
        where TKey : notnull
        => Merge(map, key, 1, win ? 1 : 0);

    private static void Merge<TKey>(Dictionary<TKey, Count> map, TKey key, int games, int wins)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var count))
            map[key] = count = new Count();
        count.Games += games;
        count.Wins += wins;
    }

    private sealed class Count
    {
        public int Games;
        public int Wins;
    }

    private sealed class ChampionCount
    {
        public int Games;
        public int Bans;
        public long Physical;
        public long Magic;
        public long True;
    }
}

/// <summary>The snapshot rows a tally yields, before Data Dragon and the baselines are added.</summary>
public sealed record MetaRows(
    int Matches,
    IReadOnlyList<LaneStat> Lanes,
    IReadOnlyList<MatchupStat> Duels,
    IReadOnlyList<SynergyStat> Duos,
    IReadOnlyDictionary<int, DamageType> Damage);
