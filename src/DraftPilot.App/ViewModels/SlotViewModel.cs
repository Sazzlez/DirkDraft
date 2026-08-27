using System.Windows.Media;
using DraftPilot.Core.Draft;

namespace DraftPilot.App.ViewModels;

/// <summary>
/// One seat row. Rows are created once and updated in place so the panel does not rebuild itself
/// several times a second during a fast ban round.
/// </summary>
public sealed class SlotViewModel : ObservableObject
{
    /// <summary>Dropdown entries; the last one clears a manual override.</summary>
    private static readonly string[] LaneChoices = ["Top", "Jungle", "Mid", "Bot", "Support", "automatisch"];

    private long _cellId = -1;
    private string _label = string.Empty;
    private string _champion = "—";
    private string _laneText = "?";
    private int _laneIndex = 5;
    private string _confidence = string.Empty;
    private bool _isUncertain;
    private bool _isLocalPlayer;
    private bool _isTarget;
    private bool _isLocked;
    private bool _hasHover;
    private bool _isManualLane;
    private bool _hasChampion;
    private string? _warning;
    private ImageSource? _icon;

    public static IReadOnlyList<string> LaneOptions => LaneChoices;

    public long CellId
    {
        get => _cellId;
        set => Set(ref _cellId, value);
    }

    /// <summary>"DU", "Ally 3" or "Enemy 2". Never a summoner name — Riot's policy forbids it.</summary>
    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public string Champion
    {
        get => _champion;
        set => Set(ref _champion, value);
    }

    /// <summary>Portrait, or null when nothing is picked or the icon is not cached.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public string LaneText
    {
        get => _laneText;
        set => Set(ref _laneText, value);
    }

    /// <summary>Selected entry of the override dropdown.</summary>
    public int LaneIndex
    {
        get => _laneIndex;
        set => Set(ref _laneIndex, value);
    }

    /// <summary>Empty for allies, whose lane the client tells us outright.</summary>
    public string Confidence
    {
        get => _confidence;
        set => Set(ref _confidence, value);
    }

    /// <summary>Prediction is shaky; the UI marks it so the user knows to look for themselves.</summary>
    public bool IsUncertain
    {
        get => _isUncertain;
        set => Set(ref _isUncertain, value);
    }

    public bool IsLocalPlayer
    {
        get => _isLocalPlayer;
        set => Set(ref _isLocalPlayer, value);
    }

    /// <summary>This is the seat the recommendation list is currently advising.</summary>
    public bool IsTarget
    {
        get => _isTarget;
        set => Set(ref _isTarget, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => Set(ref _isLocked, value);
    }

    /// <summary>Ally is hovering something but has not locked it in.</summary>
    public bool HasHover
    {
        get => _hasHover;
        set => Set(ref _hasHover, value);
    }

    public bool IsManualLane
    {
        get => _isManualLane;
        set => Set(ref _isManualLane, value);
    }

    /// <summary>A pick or hover is visible. Empty seats dim their controls rather than hide them.</summary>
    public bool HasChampion
    {
        get => _hasChampion;
        set => Set(ref _hasChampion, value);
    }

    /// <summary>Something worth flagging, e.g. an ally hovering a champion that is already banned.</summary>
    public string? Warning
    {
        get => _warning;
        set
        {
            if (Set(ref _warning, value))
                Raise(nameof(HasWarning));
        }
    }

    public bool HasWarning => !string.IsNullOrEmpty(_warning);

    /// <summary>Maps a lane onto its dropdown index.</summary>
    public static int IndexOf(Lane lane) => lane switch
    {
        Lane.Top => 0,
        Lane.Jungle => 1,
        Lane.Mid => 2,
        Lane.Adc => 3,
        Lane.Support => 4,
        _ => 5,
    };

    /// <summary>Maps a dropdown index back onto a lane; the last entry clears the override.</summary>
    public static Lane LaneAt(int index) => index switch
    {
        0 => Lane.Top,
        1 => Lane.Jungle,
        2 => Lane.Mid,
        3 => Lane.Adc,
        4 => Lane.Support,
        _ => Lane.Unknown,
    };
}
