using System.Collections.ObjectModel;
using System.Windows.Media;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.App.ViewModels;

/// <summary>One icon tile with its name: an item, a rune or a summoner spell.</summary>
public sealed class IconChipViewModel : ObservableObject
{
    private ImageSource? _icon;
    private string _label = string.Empty;
    private string? _hint;
    private bool _isHighlighted;

    public ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    /// <summary>Shown when there is no icon yet, and always in the tooltip.</summary>
    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    /// <summary>Tooltip text; null (not empty) when there is nothing to say — an empty string
    /// still renders as a tiny blank tooltip box on hover.</summary>
    public string? Hint
    {
        get => _hint;
        set => Set(ref _hint, value);
    }

    /// <summary>Keystone rune — drawn larger than the rest of the page.</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => Set(ref _isHighlighted, value);
    }

    /// <summary>
    /// The number next to an alternative, e.g. <c>49 % · 493</c>. Empty on every chip of the shown
    /// build itself — there the figure belongs to the whole row, not to each tile.
    /// </summary>
    public string Figure
    {
        get => _figure;
        set => Set(ref _figure, value);
    }

    private string _figure = string.Empty;
}

/// <summary>One level in the skill table.</summary>
public sealed class SkillCellViewModel : ObservableObject
{
    private int _level;
    private string _ability = string.Empty;
    private bool _isDerived;
    private bool _isUlt;

    public int Level
    {
        get => _level;
        set => Set(ref _level, value);
    }

    public string Ability
    {
        get => _ability;
        set => Set(ref _ability, value);
    }

    /// <summary>Levels 16-18: filled in by rule, not observed. Rendered dimmer.</summary>
    public bool IsDerived
    {
        get => _isDerived;
        set => Set(ref _isDerived, value);
    }

    public bool IsUlt
    {
        get => _isUlt;
        set => Set(ref _isUlt, value);
    }
}

/// <summary>
/// The in-game build view: runes, purchase order with item icons, and the level-by-level skill
/// table, all for the matchup that was just drafted. Shown while the game runs, so the answer to
/// "was kaufe ich als Nächstes" survives a quick tab-out.
/// </summary>
public sealed class GameBuildViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _missingNote = string.Empty;
    private string _primaryPath = string.Empty;
    private string _secondaryPath = string.Empty;
    private string _shardText = string.Empty;
    private string _coreStats = string.Empty;
    private string _skillText = string.Empty;
    private bool _hasSkillOrder;
    private bool _hasEarlyAlternatives;
    private bool _hasCoreAlternatives;

    public ObservableCollection<IconChipViewModel> PrimaryRunes { get; } = [];

    public ObservableCollection<IconChipViewModel> SecondaryRunes { get; } = [];

    public ObservableCollection<IconChipViewModel> StartItems { get; } = [];

    public ObservableCollection<IconChipViewModel> BootItems { get; } = [];

    public ObservableCollection<IconChipViewModel> CoreItems { get; } = [];

    public ObservableCollection<IconChipViewModel> LateItems { get; } = [];

    public ObservableCollection<IconChipViewModel> Spells { get; } = [];

    /// <summary>
    /// The other sets OP.GG delivered for the same slot, each with its own win rate and sample.
    /// They were always fetched, parsed and cached — and then thrown away at render time, which in
    /// a real draft meant showing Plated Steelcaps at 46,5 % out of 1974 games while Mercury's
    /// Treads (49,1 %, 493) and Boots of Swiftness (52,2 %, 136) sat unused in the same file.
    /// <para>
    /// Deliberately a second row rather than a reordering: the shown build stays what the data
    /// ranked first, and nothing here is highlighted or preferred. That distinction is the whole
    /// point — the tool says what was played, the player decides.
    /// </para>
    /// </summary>
    public ObservableCollection<IconChipViewModel> StarterAlternatives { get; } = [];

    public ObservableCollection<IconChipViewModel> BootAlternatives { get; } = [];

    public ObservableCollection<IconChipViewModel> CoreAlternatives { get; } = [];

    public ObservableCollection<SkillCellViewModel> SkillCells { get; } = [];

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => Set(ref _subtitle, value);
    }

    /// <summary>What stands in the in-game view when no build belongs to this game.</summary>
    public string MissingNote
    {
        get => _missingNote;
        private set => Set(ref _missingNote, value);
    }

    /// <summary>
    /// Heading and note for a game the tool has no build for. It says what is missing and what is
    /// there instead — and it deliberately leaves every build field untouched, so nothing from an
    /// earlier game can show through the gaps.
    /// </summary>
    public void ShowWithoutBuild(string championName, bool hasLaneOverview)
    {
        Title = championName is { Length: > 0 } ? championName : "Im Spiel";
        Subtitle = string.Empty;

        MissingNote = hasLaneOverview
            ? "Für dieses Spiel liegt kein Build vor — unten stehen die Lanes des Drafts."
            : "Für dieses Spiel liegt kein Build vor.";
    }

    public string PrimaryPath
    {
        get => _primaryPath;
        set => Set(ref _primaryPath, value);
    }

    public string SecondaryPath
    {
        get => _secondaryPath;
        set => Set(ref _secondaryPath, value);
    }

    /// <summary>Stat shards as text; their icons are generic arrows that say nothing.</summary>
    public string ShardText
    {
        get => _shardText;
        set => Set(ref _shardText, value);
    }

    /// <summary>Win rate and sample of the top core, e.g. <c>44 % · 11 Games</c>.</summary>
    public string CoreStats
    {
        get => _coreStats;
        set => Set(ref _coreStats, value);
    }

    public bool HasSkillOrder
    {
        get => _hasSkillOrder;
        set => Set(ref _hasSkillOrder, value);
    }

    /// <summary>Whether the starter or boots slot has runners-up worth a row of their own.</summary>
    public bool HasEarlyAlternatives
    {
        get => _hasEarlyAlternatives;
        set => Set(ref _hasEarlyAlternatives, value);
    }

    public bool HasCoreAlternatives
    {
        get => _hasCoreAlternatives;
        set => Set(ref _hasCoreAlternatives, value);
    }

    /// <summary>The compact priority, e.g. <c>Skills: Q &gt; E &gt; W</c>; the draft column has no
    /// room for the full 18-level table the in-game view shows.</summary>
    public string SkillText
    {
        get => _skillText;
        set => Set(ref _skillText, value);
    }

    /// <summary>Fills the view from a plan, translating names and resolving icons where present.</summary>
    /// <param name="modeLabel">
    /// What to call the mode this plan is for — "ARAM", "ARAM Mayhem". Empty for a lane build,
    /// where the lane and the opponent name it instead. Passed in rather than read off the plan
    /// because only the caller knows the queue, and Mayhem's plan is stamped "aram".
    /// </param>
    /// <param name="modeCaveat">
    /// What the mode costs, when it costs anything. Mayhem's numbers are plain ARAM's, and a card
    /// that shows them without a word claims a precision it does not have.
    /// </param>
    public void Apply(
        BuildPlan plan,
        AssetNames names,
        IconCache icons,
        string modeLabel = "",
        string modeCaveat = "")
    {
        MissingNote = string.Empty;

        var mode = modeLabel.Length > 0 ? modeLabel : BuildModes.Display(plan.Mode);

        // A build without an opponent answers for the champion, not for a pairing — naming an
        // opponent it was never measured against would be the one thing this card must not do.
        Title = (mode, plan.OpponentId) switch
        {
            ({ Length: > 0 }, _) => $"{plan.ChampionName} · {mode}",
            (_, 0) => $"{plan.ChampionName} · {plan.Lane.Display()}",
            _ => $"{plan.ChampionName} vs {plan.OpponentName} · {plan.Lane.Display()}",
        };

        var runes = plan.Runes;

        var notes = new List<string>();
        if (mode.Length == 0 && plan.OpponentId == 0)
            notes.Add("kein Matchup bekannt, bester Build der Lane");
        if (modeCaveat.Length > 0)
            notes.Add(modeCaveat);

        var caveat = notes.Count == 0 ? string.Empty : " · " + string.Join(" · ", notes);
        Subtitle = runes is null
            ? $"Patch {plan.Patch}{caveat}"
            : $"Runen: {Sample(runes.WinRate, runes.Play)} · Patch {plan.Patch}{caveat}";

        PrimaryPath = runes?.PrimaryPath ?? string.Empty;
        SecondaryPath = runes?.SecondaryPath ?? string.Empty;
        ShardText = runes is null || runes.Shards.Count == 0
            ? string.Empty
            : string.Join(" / ", runes.Shards);

        FillRunes(PrimaryRunes, runes?.PrimaryRuneIds ?? [], runes?.PrimaryRunes ?? [], names, icons, keystoneFirst: true);
        FillRunes(SecondaryRunes, runes?.SecondaryRuneIds ?? [], runes?.SecondaryRunes ?? [], names, icons, keystoneFirst: false);

        // First entry per slot, always: what the card shows is what the DATA ranked first. In a
        // matchup plan that is the most played set of this pairing; in an opponent-free plan the
        // source reports one set per slot anyway. A swap driven by our own composition read looked
        // like a recommendation the numbers never made, and that stays out. The runners-up go into
        // their own row instead, with their figures.
        FillItems(StartItems, plan.Starters.FirstOrDefault(), names, icons);
        FillItems(BootItems, plan.Boots.FirstOrDefault(), names, icons);
        FillItems(CoreItems, plan.CoreItems.FirstOrDefault(), names, icons);
        FillSingles(LateItems, plan.LateItems, names, icons);

        FillAlternatives(StarterAlternatives, plan.Starters, names, icons);
        FillAlternatives(BootAlternatives, plan.Boots, names, icons);
        FillAlternatives(CoreAlternatives, plan.CoreItems, names, icons);

        HasEarlyAlternatives = StarterAlternatives.Count > 0 || BootAlternatives.Count > 0;
        HasCoreAlternatives = CoreAlternatives.Count > 0;

        var core = plan.CoreItems.FirstOrDefault();
        CoreStats = core is null ? string.Empty : Sample(core.WinRate, core.Play);

        // Update in place like every other row — Resize(0) first tore all containers down and
        // rebuilt them, which made the spell chips visibly flicker on the second Apply (the one
        // that runs when the item icons land). Merge applies the same empty-entry filter the
        // item and rune rows get.
        var spellSet = plan.SummonerSpells.FirstOrDefault();
        var spellEntries = spellSet is null ? [] : Merge(spellSet.ItemIds, spellSet.Items);
        Spells.Resize(spellEntries.Count, () => new IconChipViewModel());

        for (var i = 0; i < spellEntries.Count; i++)
        {
            var name = names.Spell(spellEntries[i].Id, spellEntries[i].Fallback);

            Spells[i].Icon = icons.GetSpell(spellEntries[i].Id);
            Spells[i].Label = name;
            Spells[i].Hint = name.Length > 0 ? name : null;
            Spells[i].IsHighlighted = false;
        }

        SkillText = string.IsNullOrEmpty(plan.SkillPriority) ? string.Empty : $"Skills: {plan.SkillPriority}";

        var steps = SkillPlan.Extend(plan.SkillOrder);
        HasSkillOrder = steps.Count > 0;

        SkillCells.Resize(steps.Count, () => new SkillCellViewModel());
        for (var i = 0; i < steps.Count; i++)
        {
            SkillCells[i].Level = steps[i].Level;
            SkillCells[i].Ability = steps[i].Ability;
            SkillCells[i].IsDerived = steps[i].IsDerived;
            SkillCells[i].IsUlt = steps[i].Ability == "R";
        }
    }

    private static void FillRunes(
        ObservableCollection<IconChipViewModel> target,
        IReadOnlyList<int> ids,
        IReadOnlyList<string> fallbackNames,
        AssetNames names,
        IconCache icons,
        bool keystoneFirst)
    {
        // Same filter FillSingles has: an entry with neither id nor name would render as an empty
        // tile with a blank tooltip.
        var entries = Merge(ids, fallbackNames);
        target.Resize(entries.Count, () => new IconChipViewModel());

        for (var i = 0; i < entries.Count; i++)
        {
            var name = names.Rune(entries[i].Id, entries[i].Fallback);

            target[i].Icon = icons.GetRune(entries[i].Id);
            target[i].Label = name;
            target[i].Hint = name.Length > 0 ? name : null;
            target[i].IsHighlighted = keystoneFirst && i == 0;
        }
    }

    private static void FillItems(
        ObservableCollection<IconChipViewModel> target,
        ItemSet? set,
        AssetNames names,
        IconCache icons)
    {
        var entries = set is null ? [] : Merge(set.ItemIds, set.Items);
        target.Resize(entries.Count, () => new IconChipViewModel());

        for (var i = 0; i < entries.Count; i++)
        {
            var name = names.Item(entries[i].Id, entries[i].Fallback);

            target[i].Icon = icons.GetItem(entries[i].Id);
            target[i].Label = name;
            target[i].Hint = name.Length > 0 ? name : null;
            target[i].IsHighlighted = false;
        }
    }

    /// <summary>Pairs ids with their fallback names positionally, dropping entries that have neither.</summary>
    /// <summary>
    /// Below this many games a percentage is theatre, not a number. The real case: a fourth boot
    /// set with "0 % out of 3 games" would take the room of one that says something.
    /// </summary>
    private const int MinimumAlternativePlay = 20;

    /// <summary>
    /// Below this many games the win rate is not printed at all — only the sample. At fifty games
    /// one standard error is about seven percentage points, so "59 %" and "45 %" are the same
    /// statement; the matchup-specific sets regularly land there (measured: 11 games for the most
    /// played core of one stored matchup). The recommendation list next to this card has followed
    /// its own error bar for a while; the build card claimed a precision nobody could back.
    /// </summary>
    private const int MinimumPlayForRate = 50;

    /// <summary>Win rate and sample, or just the sample when the sample cannot carry a rate.</summary>
    private static string Sample(double winRate, int play) => play >= MinimumPlayForRate
        ? $"{winRate:P0} WR · {play:N0} Games"
        : play > 0
            ? $"dünne Datenlage · {play:N0} Games"
            : string.Empty;

    /// <summary>
    /// The runners-up of one slot — one chip per set, each carrying its own win rate and sample.
    /// The first set is skipped; that one is the build shown above. Nothing is reordered and
    /// nothing is marked as better: which of them is the right buy depends on the game, and the
    /// numbers are there so the player can decide it.
    /// </summary>
    private static void FillAlternatives(
        ObservableCollection<IconChipViewModel> target,
        IReadOnlyList<ItemSet> sets,
        AssetNames names,
        IconCache icons)
    {
        var entries = sets.Skip(1).Where(set => set.Play >= MinimumAlternativePlay).ToList();

        target.Resize(entries.Count, () => new IconChipViewModel());

        for (var i = 0; i < entries.Count; i++)
        {
            var set = entries[i];
            var merged = Merge(set.ItemIds, set.Items);

            var label = string.Join(
                " + ",
                merged.Select(entry => names.Item(entry.Id, entry.Fallback)).Where(name => name.Length > 0));

            target[i].Icon = merged.Count > 0 ? icons.GetItem(merged[0].Id) : null;
            target[i].Label = label;
            target[i].Figure = set.Play >= MinimumPlayForRate
                ? $"{set.WinRate:P0} · {set.Play:N0}"
                : $"{set.Play:N0} Games";
            target[i].Hint = $"{label} — {set.WinRate:P0} Winrate aus {set.Play:N0} Games, "
                + $"gewählt in {set.PickRate:P0} der Fälle. Oben steht die häufigste Wahl, nicht die beste; "
                + "welche hier richtig ist, entscheidet das Spiel.";
            target[i].IsHighlighted = false;
        }
    }

    private static List<(int Id, string Fallback)> Merge(IReadOnlyList<int> ids, IReadOnlyList<string> fallbackNames)
    {
        var count = Math.Max(ids.Count, fallbackNames.Count);
        var entries = new List<(int, string)>(count);

        for (var i = 0; i < count; i++)
        {
            var id = i < ids.Count ? ids[i] : 0;
            var fallback = i < fallbackNames.Count ? fallbackNames[i] : string.Empty;

            if (id > 0 || fallback.Length > 0)
                entries.Add((id, fallback));
        }

        return entries;
    }

    /// <summary>Late items arrive one per set; flattened into a single row of options.</summary>
    private static void FillSingles(
        ObservableCollection<IconChipViewModel> target,
        IReadOnlyList<ItemSet> sets,
        AssetNames names,
        IconCache icons)
    {
        var flattened = sets
            .Select(set => (
                Id: set.ItemIds.FirstOrDefault(),
                Name: set.Items.FirstOrDefault() ?? string.Empty,
                set.WinRate,
                set.Play))
            .Where(entry => entry.Id > 0 || entry.Name.Length > 0)
            .ToList();

        target.Resize(flattened.Count, () => new IconChipViewModel());

        for (var i = 0; i < flattened.Count; i++)
        {
            var entry = flattened[i];
            var name = names.Item(entry.Id, entry.Name);

            target[i].Icon = icons.GetItem(entry.Id);
            target[i].Label = name;
            target[i].Hint = $"{name} — {entry.WinRate:P0} WR aus {entry.Play} Games mit diesem Kauf.";
            target[i].IsHighlighted = false;
        }
    }
}
