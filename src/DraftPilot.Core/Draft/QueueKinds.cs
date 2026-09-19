namespace DraftPilot.Core.Draft;

/// <summary>
/// What kind of queue a champion select belongs to, as far as it changes what the advice means.
/// <para>
/// Two distinctions matter. Whether the game is played on lanes — every number in the snapshot is a
/// per-lane statistic, so in ARAM or Arena the entire model describes a different game. And which
/// population OP.GG should be asked about: Solo/Duo, Flex and ARAM are three separate sets of
/// numbers behind one <c>game_mode</c> parameter, and asking for the wrong one is a silent error.
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

    /// <summary>The short-format Rift queue that replaced Quickplay on the play button.</summary>
    Swiftplay,

    Clash,
    Bots,

    /// <summary>Howling Abyss: one lane, no roles, its own item and rune logic.</summary>
    Aram,

    /// <summary>
    /// ARAM Mayhem, the German client's "ARAM: Chaos": the Howling Abyss with rotating modifiers.
    /// Its own queue and its own game mode as far as Riot is concerned — and no data source of its
    /// own, see <see cref="ModeCaveat"/>.
    /// </summary>
    AramMayhem,

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
    /// <para>
    /// Every id below was read out of the running client on 2026-09-19 rather than looked up:
    /// <c>/lol-game-queues/v1/queues</c> lists all 88 queues with their id, game mode and map. That
    /// is where ARAM Mayhem's 2400 comes from — it appears there as "ARAM: Chaos", game mode
    /// <c>KIWI</c>, map 12, alongside its tournament (2410) and near-classic (2450) variants.
    /// </para>
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
            480 => QueueKind.Swiftplay,
            700 or 720 => QueueKind.Clash,
            830 or 840 or 850 or 870 or 880 or 890 => QueueKind.Bots,
            450 => QueueKind.Aram,
            2400 or 2410 or 2450 => QueueKind.AramMayhem,
            1700 or 1710 or 1750 => QueueKind.Arena,
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
        => kind is not (QueueKind.Aram or QueueKind.AramMayhem or QueueKind.Arena or QueueKind.Rotating);

    /// <summary>Both queues on the Howling Abyss. Same map, same draft shape, same data source.</summary>
    public static bool IsAram(this QueueKind kind)
        => kind is QueueKind.Aram or QueueKind.AramMayhem;

    /// <summary>German name for the status line. Empty when there is nothing worth saying.</summary>
    public static string Display(this QueueKind kind) => kind switch
    {
        QueueKind.RankedSolo => "Ranked Solo/Duo",
        QueueKind.RankedFlex => "Ranked Flex",
        QueueKind.NormalDraft => "Normal Draft",
        QueueKind.NormalBlind => "Normal Blind",
        QueueKind.Quickplay => "Quickplay",
        QueueKind.Swiftplay => "Swiftplay",
        QueueKind.Clash => "Clash",
        QueueKind.Bots => "Co-op vs. AI",
        QueueKind.Aram => "ARAM",
        QueueKind.AramMayhem => "ARAM Mayhem",
        QueueKind.Arena => "Arena",
        QueueKind.Rotating => "Rotating Gamemode",
        QueueKind.Custom => "Custom Game",
        _ => string.Empty,
    };

    /// <summary>
    /// The value OP.GG's <c>game_mode</c> parameter takes for this queue, or empty when the source
    /// has nothing that describes it.
    /// <para>
    /// The parameter is an enum of exactly five values — <c>ranked</c>, <c>flex</c>, <c>urf</c>,
    /// <c>aram</c>, <c>nexus_blitz</c> — read off the live schema on 2026-09-19. So the mapping is
    /// not a preference: three of our queues have their own numbers, ARAM Mayhem borrows ARAM's
    /// (and says so), Arena has nothing at all, and every unranked Rift queue borrows Solo/Duo's,
    /// which is what the tool has always sent for them.
    /// </para>
    /// </summary>
    public static string OpGgMode(this QueueKind kind) => kind switch
    {
        QueueKind.RankedFlex => "flex",
        QueueKind.Aram or QueueKind.AramMayhem => "aram",
        QueueKind.Arena => string.Empty,
        QueueKind.Rotating => string.Empty,
        _ => "ranked",
    };

    /// <summary>
    /// What to tell the user when the lane model does not apply, or empty when it does. Named in
    /// full: "die Zahlen passen nicht" without saying why reads like a defect in the tool.
    /// </summary>
    public static string LaneCaveat(this QueueKind kind) => kind switch
    {
        QueueKind.Aram => "ARAM: alle Zahlen sind Lane-Statistiken von Summoner's Rift. Runen und Items "
            + "taugen als Richtung, die Reihenfolge der Vorschläge nicht.",
        QueueKind.AramMayhem => "ARAM Mayhem: alle Zahlen sind Lane-Statistiken von Summoner's Rift. "
            + "Runen und Items taugen als Richtung, die Reihenfolge der Vorschläge nicht.",
        QueueKind.Arena => "Arena: 2v2v2v2 ohne Lanes — die Vorschläge beruhen auf "
            + "Lane-Statistiken und sagen hier nichts.",
        QueueKind.Rotating => "Rotating Gamemode: die Zahlen stammen von Summoner's Rift und "
            + "beschreiben ein anderes Spiel als dieses.",
        _ => string.Empty,
    };

    /// <summary>
    /// What the mode itself costs in accuracy, independent of lanes — empty when nothing is owed.
    /// <para>
    /// Only ARAM Mayhem has something to say here today: OP.GG's <c>game_mode</c> enum has no value
    /// for it, so its build and its numbers are plain ARAM's. Mayhem changes items, gold and the
    /// map's rules, so that is a real gap and not a rounding error — but ARAM data on the same map
    /// with the same champion is closer than anything else on offer, and a build card that stays
    /// empty helps nobody.
    /// </para>
    /// </summary>
    public static string ModeCaveat(this QueueKind kind) => kind switch
    {
        QueueKind.AramMayhem => "OP.GG führt für Mayhem keine eigenen Zahlen — Build und Runen "
            + "kommen aus dem normalen ARAM.",
        _ => string.Empty,
    };
}
