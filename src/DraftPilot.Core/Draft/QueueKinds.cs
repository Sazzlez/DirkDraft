namespace DraftPilot.Core.Draft;

/// <summary>
/// What kind of queue a champion select belongs to, as far as it changes what the advice means.
/// <para>
/// Only one distinction actually matters: whether the game is played on lanes. Every number in the
/// snapshot is a per-lane statistic, so in ARAM or Arena the entire model describes a different
/// game — and until this was read, the panel recommended lane picks there with exactly the same
/// confidence as in ranked solo.
/// </para>
/// </summary>
public enum QueueKind
{
    /// <summary>No queue id in the payload, or one this build does not know. Treated as lane-based.</summary>
    Unknown,

    RankedSolo,
    RankedFlex,
    NormalDraft,
    NormalBlind,
    Quickplay,
    Clash,
    Bots,

    /// <summary>Howling Abyss: one lane, no roles, its own item and rune logic.</summary>
    Aram,

    /// <summary>2v2v2v2 arena: no lanes at all.</summary>
    Arena,

    /// <summary>URF, One for All, and the other rotating modes.</summary>
    Rotating,

    /// <summary>A custom lobby. No queue id is sent, so this comes from its own flag.</summary>
    Custom,
}

public static class QueueKinds
{
    /// <summary>
    /// Riot's queue ids, only the ones worth naming. Deliberately incomplete: an id this build does
    /// not know stays <see cref="QueueKind.Unknown"/> and is treated as a normal lane game, because
    /// a wrong warning in ranked would cost more than a missing one in a rotating mode.
    /// </summary>
    public static QueueKind FromId(int queueId, bool isCustomGame = false)
    {
        if (isCustomGame)
            return QueueKind.Custom;

        return queueId switch
        {
            420 => QueueKind.RankedSolo,
            440 => QueueKind.RankedFlex,
            400 => QueueKind.NormalDraft,
            430 => QueueKind.NormalBlind,
            490 => QueueKind.Quickplay,
            700 or 720 => QueueKind.Clash,
            830 or 840 or 850 or 870 or 880 or 890 => QueueKind.Bots,
            450 => QueueKind.Aram,
            1700 or 1710 => QueueKind.Arena,
            900 or 1900 or 1020 or 1300 or 1400 => QueueKind.Rotating,
            _ => QueueKind.Unknown,
        };
    }

    /// <summary>
    /// Whether the five-lane model the snapshot is built on applies. A custom lobby usually is a
    /// normal Rift game, so it counts as lane-based; only the modes that genuinely have no lanes
    /// are excluded.
    /// </summary>
    public static bool UsesLanes(this QueueKind kind)
        => kind is not (QueueKind.Aram or QueueKind.Arena or QueueKind.Rotating);

    /// <summary>German name for the status line. Empty when there is nothing worth saying.</summary>
    public static string Display(this QueueKind kind) => kind switch
    {
        QueueKind.RankedSolo => "Ranked Solo/Duo",
        QueueKind.RankedFlex => "Ranked Flex",
        QueueKind.NormalDraft => "Normal Draft",
        QueueKind.NormalBlind => "Normal Blind",
        QueueKind.Quickplay => "Quickplay",
        QueueKind.Clash => "Clash",
        QueueKind.Bots => "Co-op vs. AI",
        QueueKind.Aram => "ARAM",
        QueueKind.Arena => "Arena",
        QueueKind.Rotating => "Rotating Gamemode",
        QueueKind.Custom => "Custom Game",
        _ => string.Empty,
    };

    /// <summary>
    /// What to tell the user when the lane model does not apply, or empty when it does. Named in
    /// full: "die Zahlen passen nicht" without saying why reads like a defect in the tool.
    /// </summary>
    public static string LaneCaveat(this QueueKind kind) => kind switch
    {
        QueueKind.Aram => "ARAM: alle Zahlen sind Lane-Statistiken von Summoner's Rift. Runen und Items "
            + "taugen als Richtung, die Reihenfolge der Vorschläge nicht.",
        QueueKind.Arena => "Arena: 2v2v2v2 ohne Lanes — die Vorschläge beruhen auf "
            + "Lane-Statistiken und sagen hier nichts.",
        QueueKind.Rotating => "Rotating Gamemode: die Zahlen stammen von Summoner's Rift und "
            + "beschreiben ein anderes Spiel als dieses.",
        _ => string.Empty,
    };
}
