using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Tests;

/// <summary>Builds small, fully controlled <see cref="MetaLookup"/> instances for tests.</summary>
internal static class TestMeta
{
    /// <summary>Champion ids used across the tests, with the lanes they mostly play.</summary>
    public const int Aatrox = 266;
    public const int LeeSin = 64;
    public const int Ahri = 103;
    public const int Ashe = 22;
    public const int Thresh = 412;
    public const int Jax = 24;
    public const int Elise = 60;
    public const int Syndra = 134;
    public const int Lucian = 236;
    public const int Nautilus = 111;
    public const int Sylas = 517;

    /// <summary>A five-role cast with clean, unambiguous role priors.</summary>
    public static MetaLookup StandardCast()
    {
        var snapshot = new MetaSnapshot { Patch = "test" };

        AddChampion(snapshot, Aatrox, "Aatrox", (Lane.Top, 0.92), (Lane.Mid, 0.08));
        AddChampion(snapshot, LeeSin, "LeeSin", (Lane.Jungle, 0.97));
        AddChampion(snapshot, Ahri, "Ahri", (Lane.Mid, 0.95), (Lane.Support, 0.05));
        AddChampion(snapshot, Ashe, "Ashe", (Lane.Adc, 0.94), (Lane.Support, 0.06));
        AddChampion(snapshot, Thresh, "Thresh", (Lane.Support, 0.99));

        AddChampion(snapshot, Jax, "Jax", (Lane.Top, 0.86), (Lane.Jungle, 0.14));
        AddChampion(snapshot, Elise, "Elise", (Lane.Jungle, 0.93), (Lane.Top, 0.07));
        AddChampion(snapshot, Syndra, "Syndra", (Lane.Mid, 0.96));
        AddChampion(snapshot, Lucian, "Lucian", (Lane.Adc, 0.80), (Lane.Mid, 0.20));
        AddChampion(snapshot, Nautilus, "Nautilus", (Lane.Support, 0.90), (Lane.Jungle, 0.10));

        // A genuine flex pick, to check that ambiguity shows up as low confidence.
        AddChampion(snapshot, Sylas, "Sylas", (Lane.Mid, 0.45), (Lane.Jungle, 0.30), (Lane.Top, 0.25));

        return new MetaLookup(snapshot);
    }

    public static void AddChampion(
        MetaSnapshot snapshot,
        int id,
        string name,
        params (Lane Lane, double RoleRate)[] lanes)
    {
        snapshot.Champions.Add(new ChampionEntry { Id = id, Key = name, Name = name });

        foreach (var (lane, roleRate) in lanes)
        {
            snapshot.LaneStats.Add(new LaneStat
            {
                ChampionId = id,
                Lane = lane,
                RoleRate = roleRate,
                WinRate = 0.5,
                Play = 500,
            });
        }
    }

    /// <summary>Enemy seats in client order, with the given champions locked in (0 = still hidden).</summary>
    public static List<DraftSlot> EnemySlots(params int[] championIds)
    {
        var slots = new List<DraftSlot>(championIds.Length);

        for (var i = 0; i < championIds.Length; i++)
            slots.Add(new DraftSlot(i + 5, i, IsAlly: false, championIds[i], 0, Lane.Unknown));

        return slots;
    }
}
