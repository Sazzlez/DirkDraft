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
    private bool _hasFigure;
    private ScoreTone _tone = ScoreTone.Weak;
    private string _note = string.Empty;

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
}
