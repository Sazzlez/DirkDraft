using System.Windows;
using System.Windows.Media;

namespace DraftPilot.App.ViewModels;

/// <summary>
/// One row of the lane overview shown while the game runs. Rows are created once and updated in
/// place, the same discipline as the seat rows: the overview is rebuilt on every render of a live
/// draft, and replacing five rows each time would restart their layout for nothing.
/// </summary>
public sealed class LaneMatchupViewModel : ObservableObject
{
    private string _lane = string.Empty;
    private string _ally = "—";
    private string _enemy = "—";
    private ImageSource? _allyIcon;
    private ImageSource? _enemyIcon;
    private string _figure = string.Empty;
    private string _enemyFigure = string.Empty;
    private bool _hasFigure;
    private ScoreTone _tone = ScoreTone.Weak;
    private string _note = string.Empty;
    private GridLength _allyRest = new(1, GridUnitType.Star);
    private GridLength _allyAdvance = new(0, GridUnitType.Star);
    private GridLength _enemyAdvance = new(0, GridUnitType.Star);
    private GridLength _enemyRest = new(1, GridUnitType.Star);

    /// <summary>"Top", "Jungle", …</summary>
    public string Lane
    {
        get => _lane;
        set => Set(ref _lane, value);
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

    public ImageSource? AllyIcon
    {
        get => _allyIcon;
        set => Set(ref _allyIcon, value);
    }

    public ImageSource? EnemyIcon
    {
        get => _enemyIcon;
        set => Set(ref _enemyIcon, value);
    }

    /// <summary>The duel's win rate as text, from our side. Empty when there is no statistic.</summary>
    public string Figure
    {
        get => _figure;
        set => Set(ref _figure, value);
    }

    /// <summary>
    /// The counterpart on their side, the same duel read the other way round.
    /// <para>
    /// Printed although it is nothing but 100 % minus ours: with a single number between two
    /// champions there is no telling whose it is, and the bar alone did not settle it either — the
    /// mark at 50 % was read as the divider between the two halves, which put the majority on the
    /// wrong side. Two numbers, each next to its own champion, cannot be misread.
    /// </para>
    /// </summary>
    public string EnemyFigure
    {
        get => _enemyFigure;
        set => Set(ref _enemyFigure, value);
    }

    /// <summary>
    /// Whether <see cref="Figure"/> carries a number. Drives the visibility of the figure badge, so
    /// a lane without a duel statistic shows the pairing and nothing that looks like a measurement.
    /// </summary>
    public bool HasFigure
    {
        get => _hasFigure;
        set => Set(ref _hasFigure, value);
    }

    /// <summary>Colour of the figure: green ahead, blue even, muted behind. Same scale as the draft.</summary>
    public ScoreTone Tone
    {
        get => _tone;
        set => Set(ref _tone, value);
    }

    /// <summary>Tooltip: sample size, or why there is no number.</summary>
    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    /// <summary>
    /// The duel as a deflection from the centre: the four star widths of the bar's columns, of
    /// which only the two around the middle carry the fill.
    /// <para>
    /// A share bar (ours from the left edge, theirs filling the rest) was the obvious build and the
    /// wrong picture: our advantage made the boundary move RIGHT, towards the opponent, and every
    /// real duel between 42 % and 58 % looked like the same half-filled bar. This one starts at the
    /// middle and reaches towards whoever is ahead, so the direction means what it looks like.
    /// </para>
    /// <para>
    /// GridLength rather than a number plus a converter: a star column is a proportion, so the
    /// layout does the arithmetic.
    /// </para>
    /// </summary>
    public GridLength AllyRest
    {
        get => _allyRest;
        set => Set(ref _allyRest, value);
    }

    public GridLength AllyAdvance
    {
        get => _allyAdvance;
        set => Set(ref _allyAdvance, value);
    }

    public GridLength EnemyAdvance
    {
        get => _enemyAdvance;
        set => Set(ref _enemyAdvance, value);
    }

    public GridLength EnemyRest
    {
        get => _enemyRest;
        set => Set(ref _enemyRest, value);
    }
}
