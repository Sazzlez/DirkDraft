using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>Which defensive slant the enemy composition asks of the boots slot.</summary>
public enum BootsPreference
{
    None,

    /// <summary>Heavy hard crowd control: tenacity shortens every stun and root.</summary>
    Tenacity,

    /// <summary>Mostly magic damage.</summary>
    MagicResist,

    /// <summary>Mostly physical damage.</summary>
    Armor,
}

/// <summary>
/// Turns the enemy-composition hints into an actual build adjustment — for exactly one slot, the
/// boots, because that is the classic situational buy. The rule stays honest: nothing is ever
/// invented, only the alternatives OP.GG delivered for this matchup are reordered, and the UI
/// says why. Core items are deliberately left alone.
/// </summary>
public static class SituationalBuild
{
    /// <summary>Mercury's Treads: tenacity plus magic resist. Riot item ids are stable.</summary>
    public const int MercuryTreads = 3111;

    /// <summary>Plated Steelcaps: armor plus reduced basic-attack damage.</summary>
    public const int PlatedSteelcaps = 3047;

    /// <summary>
    /// What the enemy composition calls for. Thresholds match the hint chips in the build view —
    /// hint and adjustment must never contradict each other. CC outranks the damage split:
    /// tenacity is the harder requirement to cover anywhere else.
    /// </summary>
    public static BootsPreference PreferredBoots(CompProfile enemy)
    {
        if (enemy.Count < 3)
            return BootsPreference.None;

        if (enemy.TotalCrowdControl >= 6)
            return BootsPreference.Tenacity;

        if (enemy.MagicShare >= 0.65)
            return BootsPreference.MagicResist;

        if (enemy.PhysicalShare >= 0.65)
            return BootsPreference.Armor;

        return BootsPreference.None;
    }

    /// <summary>The item id that satisfies a preference, or 0 for none.</summary>
    public static int PreferredBootId(BootsPreference preference) => preference switch
    {
        // Mercs cover both: they ARE the magic-resist boot.
        BootsPreference.Tenacity or BootsPreference.MagicResist => MercuryTreads,
        BootsPreference.Armor => PlatedSteelcaps,
        _ => 0,
    };

    /// <summary>
    /// The boots set matching the preference, or <see langword="null"/> when the fetched data
    /// does not contain it — then the caller keeps the most-played set and the hint stays advice.
    /// </summary>
    public static ItemSet? PickBoots(IReadOnlyList<ItemSet> boots, BootsPreference preference)
    {
        var wanted = PreferredBootId(preference);
        if (wanted == 0)
            return null;

        return boots.FirstOrDefault(set => set.ItemIds.Contains(wanted));
    }
}
