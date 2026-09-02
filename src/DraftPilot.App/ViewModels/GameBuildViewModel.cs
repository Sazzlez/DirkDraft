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
    private string _primaryPath = string.Empty;
    private string _secondaryPath = string.Empty;
    private string _shardText = string.Empty;
    private string _coreStats = string.Empty;
    private string _skillText = string.Empty;
    private bool _hasSkillOrder;

    public ObservableCollection<IconChipViewModel> PrimaryRunes { get; } = [];

    public ObservableCollection<IconChipViewModel> SecondaryRunes { get; } = [];

    public ObservableCollection<IconChipViewModel> StartItems { get; } = [];

    public ObservableCollection<IconChipViewModel> BootItems { get; } = [];

    public ObservableCollection<IconChipViewModel> CoreItems { get; } = [];

    public ObservableCollection<IconChipViewModel> LateItems { get; } = [];

    public ObservableCollection<IconChipViewModel> Spells { get; } = [];

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

    /// <summary>Win rate and sample of the top core, e.g. <c>44 % · 11 Spiele</c>.</summary>
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

    /// <summary>The compact priority, e.g. <c>Skills: Q &gt; E &gt; W</c>; the draft column has no
    /// room for the full 18-level table the in-game view shows.</summary>
    public string SkillText
    {
        get => _skillText;
        set => Set(ref _skillText, value);
    }

    /// <summary>Fills the view from a plan, translating names and resolving icons where present.</summary>
    /// <param name="bootsPreference">
    /// What the enemy composition asks of the boots slot. When the fetched alternatives contain
    /// the matching boot, THAT set is shown (highlighted, with the reason in its tooltip) instead
    /// of the most-played one — the one build slot that adapts to the situation hints.
    /// </param>
    /// <param name="isStandIn">
    /// The opponent in <paramref name="plan"/> is a stand-in, because the queue never revealed the
    /// real one. Naming it as such matters: the runes and the skill order carry over, the item core
    /// is the part that was chosen against somebody else.
    /// </param>
    public void Apply(
        BuildPlan plan,
        AssetNames names,
        IconCache icons,
        BootsPreference bootsPreference = BootsPreference.None,
        bool isStandIn = false)
    {
        Title = isStandIn
            ? $"{plan.ChampionName} · {plan.Lane.Display()} — gegen den üblichen Gegner ({plan.OpponentName})"
            : $"{plan.ChampionName} vs {plan.OpponentName} · {plan.Lane.Display()}";

        var runes = plan.Runes;
        var caveat = isStandIn ? " · Gegner unbekannt, Items nur als Richtung" : string.Empty;
        Subtitle = runes is null
            ? $"Patch {plan.Patch}{caveat}"
            : $"Runen: {runes.WinRate:P0} WR über {runes.Play} Spiele · Patch {plan.Patch}{caveat}";

        PrimaryPath = runes?.PrimaryPath ?? string.Empty;
        SecondaryPath = runes?.SecondaryPath ?? string.Empty;
        ShardText = runes is null || runes.Shards.Count == 0
            ? string.Empty
            : string.Join(" / ", runes.Shards);

        FillRunes(PrimaryRunes, runes?.PrimaryRuneIds ?? [], runes?.PrimaryRunes ?? [], names, icons, keystoneFirst: true);
        FillRunes(SecondaryRunes, runes?.SecondaryRuneIds ?? [], runes?.SecondaryRunes ?? [], names, icons, keystoneFirst: false);

        FillItems(StartItems, plan.Starters.FirstOrDefault(), names, icons);
        FillBoots(plan, names, icons, bootsPreference);
        FillItems(CoreItems, plan.CoreItems.FirstOrDefault(), names, icons);
        FillSingles(LateItems, plan.LateItems, names, icons);

        var core = plan.CoreItems.FirstOrDefault();
        CoreStats = core is null ? string.Empty : $"{core.WinRate:P0} WR · {core.Play} Spiele";

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

    /// <summary>
    /// The boots slot: the situational set when the data offers it, the most-played one
    /// otherwise. A situational pick is highlighted and its tooltip says WHY it was preferred,
    /// with the sample behind it — an unexplained swap would just look like a wrong number.
    /// </summary>
    private void FillBoots(BuildPlan plan, AssetNames names, IconCache icons, BootsPreference preference)
    {
        var situational = SituationalBuild.PickBoots(plan.Boots, preference);
        FillItems(BootItems, situational ?? plan.Boots.FirstOrDefault(), names, icons);

        if (situational is null)
            return;

        var reason = preference switch
        {
            BootsPreference.Tenacity => "wegen der vielen Betäubungen im Gegnerteam vorgezogen",
            BootsPreference.MagicResist => "wegen des überwiegend magischen Gegner-Schadens vorgezogen",
            _ => "wegen des überwiegend physischen Gegner-Schadens vorgezogen",
        };

        foreach (var chip in BootItems)
        {
            chip.IsHighlighted = true;
            chip.Hint = $"{chip.Label} — {reason} · {situational.WinRate:P0} WR aus {situational.Play} Spielen.";
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
            target[i].Hint = $"{name} — {entry.WinRate:P0} WR aus {entry.Play} Spielen mit diesem Kauf.";
            target[i].IsHighlighted = false;
        }
    }
}
