using System.Windows;

namespace DraftPilot.App.ViewModels;

/// <summary>
/// One team's damage mix as a split bar plus its text. The two shares always add up to one, so a
/// single bar with two fills says it without a legend per side.
/// <para>
/// GridLength rather than a percentage plus a converter, like the duel bars: a star column is a
/// proportion, so the layout does the arithmetic.
/// </para>
/// </summary>
public sealed class TeamDamageViewModel : ObservableObject
{
    private GridLength _physical = new(1, GridUnitType.Star);
    private GridLength _magic = new(0, GridUnitType.Star);
    private string _side = string.Empty;
    private string _text = "—";
    private bool _hasMix;

    /// <summary>Whose mix this is: "Dein Team" or "Gegner". Set once, never re-rendered.</summary>
    public string Side
    {
        get => _side;
        init => _side = value;
    }

    public GridLength Physical
    {
        get => _physical;
        set => Set(ref _physical, value);
    }

    public GridLength Magic
    {
        get => _magic;
        set => Set(ref _magic, value);
    }

    /// <summary>"67 % AD · 33 % AP", or why there is nothing to show.</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    /// <summary>A damage type is known for at least one champion; without it the bar stays away.</summary>
    public bool HasMix
    {
        get => _hasMix;
        set => Set(ref _hasMix, value);
    }
}

/// <summary>
/// One line of the pre-game comparison: what both teams bring to a team fight, side by side.
/// Rows are created once and updated in place, like every other list in this window.
/// <para>
/// Both sides are plain text rather than numbers, because several of these read as "2 von 5" and
/// one of them is a dash. The row is a statement about the draft, not a measurement to compute
/// with.
/// </para>
/// </summary>
public sealed class TeamStatViewModel : ObservableObject
{
    private string _label = string.Empty;
    private string _ally = "—";
    private string _enemy = "—";
    private string _hint = string.Empty;

    /// <summary>"Frontline", "CC", …</summary>
    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public string Ally
    {
        get => _ally;
        set => Set(ref _ally, value);
    }

    public string Enemy
    {
        get => _enemy;
        set => Set(ref _enemy, value);
    }

    /// <summary>Tooltip: what the line counts, and what it is good for.</summary>
    public string Hint
    {
        get => _hint;
        set => Set(ref _hint, value);
    }
}
