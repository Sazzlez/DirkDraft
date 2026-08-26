using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using DraftPilot.Core.Draft;

namespace DraftPilot.App.ViewModels;

/// <summary>How strong a recommendation is, used only to tint the score pill.</summary>
public enum ScoreTone
{
    Weak,
    Fair,
    Strong,
}

/// <summary>One row of the recommendation list, updated in place.</summary>
public sealed class RecommendationViewModel : ObservableObject
{
    private int _championId;
    private int _rank;
    private string _name = string.Empty;
    private string _score = string.Empty;
    private ScoreTone _tone;
    private bool _isExpanded;
    private ImageSource? _icon;

    /// <summary>Reason chips. A collection rather than one joined string so they render as chips.</summary>
    public ObservableCollection<Reason> Reasons { get; } = [];

    public ObservableCollection<TermViewModel> Breakdown { get; } = [];

    public int Rank
    {
        get => _rank;
        set => Set(ref _rank, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public string Score
    {
        get => _score;
        set => Set(ref _score, value);
    }

    public ScoreTone Tone
    {
        get => _tone;
        set => Set(ref _tone, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>
    /// Applies a new recommendation. Collapses the breakdown when the champion changed, so an
    /// expanded row does not silently start showing a different champion's numbers.
    /// </summary>
    public void Apply(int rank, Recommendation recommendation, ImageSource? icon)
    {
        if (_championId != recommendation.ChampionId)
        {
            _championId = recommendation.ChampionId;
            IsExpanded = false;
        }

        Rank = rank;
        Name = recommendation.Name;
        Icon = icon;
        Score = recommendation.Score.ToString("F2", CultureInfo.CurrentCulture);
        Tone = recommendation.Score switch
        {
            > 0.6 => ScoreTone.Strong,
            > 0.2 => ScoreTone.Fair,
            _ => ScoreTone.Weak,
        };

        Reasons.Resize(recommendation.Reasons.Count, () => Reason.Neutral(string.Empty));
        for (var i = 0; i < recommendation.Reasons.Count; i++)
            Reasons[i] = recommendation.Reasons[i];

        Breakdown.Resize(recommendation.Breakdown.Count, () => new TermViewModel());
        for (var i = 0; i < recommendation.Breakdown.Count; i++)
            Breakdown[i].Apply(recommendation.Breakdown[i]);
    }
}

/// <summary>One line of the score breakdown.</summary>
public sealed class TermViewModel : ObservableObject
{
    private string _label = string.Empty;
    private string _detail = string.Empty;

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    /// <summary>Signal, weight and resulting contribution, so the number is auditable.</summary>
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public void Apply(ScoreTerm term)
    {
        Label = term.Label;
        Detail = $"{term.Normalised,6:+0.00;-0.00;0.00} × {term.Weight:0.0}  =  {term.Contribution,6:+0.00;-0.00;0.00}";
    }
}
