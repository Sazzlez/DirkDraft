using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Tests;

/// <summary>Assembles a fully controlled snapshot so scoring can be tested on exact numbers.</summary>
internal sealed class MetaBuilder
{
    private readonly MetaSnapshot _snapshot = new() { Patch = "test" };

    public MetaBuilder Champion(
        int id,
        string name,
        DamageType damage = DamageType.Unknown,
        string[]? tags = null,
        int attackRange = 0,
        int defense = 0)
    {
        _snapshot.Champions.Add(new ChampionEntry
        {
            Id = id,
            Key = name,
            Name = name,
            Damage = damage,
            Tags = [.. tags ?? []],
            AttackRange = attackRange,
            Defense = defense,
        });

        return this;
    }

    public MetaBuilder InLane(
        int championId,
        Lane lane,
        double roleRate = 0.9,
        double winRate = 0.5,
        int play = 1000,
        int tier = -1,
        double pickRate = 0,
        double banRate = 0)
    {
        _snapshot.LaneStats.Add(new LaneStat
        {
            ChampionId = championId,
            Lane = lane,
            RoleRate = roleRate,
            WinRate = winRate,
            Play = play,
            Tier = tier,
            PickRate = pickRate,
            BanRate = banRate,
        });

        return this;
    }

    public MetaBuilder Matchup(int championId, int opponentId, Lane lane, double winRate, int play = 1000)
    {
        _snapshot.Matchups.Add(new MatchupStat
        {
            ChampionId = championId,
            OpponentId = opponentId,
            Lane = lane,
            WinRate = winRate,
            Play = play,
        });

        return this;
    }

    public MetaBuilder Synergy(int championId, int partnerId, double winRate, int play = 1000, int tier = -1)
    {
        _snapshot.Synergies.Add(new SynergyStat
        {
            ChampionId = championId,
            PartnerId = partnerId,
            WinRate = winRate,
            Play = play,
            Tier = tier,
        });

        return this;
    }

    public MetaSnapshot Snapshot() => _snapshot;

    public MetaLookup Build() => new(_snapshot);
}
