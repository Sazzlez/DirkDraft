using System.Globalization;
using System.Windows.Media;
using DraftPilot.Core.Draft;

namespace DraftPilot.App.ViewModels;

/// <summary>
/// The strip above the pick list while the own seat is hovering: what that champion would be worth
/// in this draft.
/// <para>
/// The list answers "what should I pick" with its best eight. The champion somebody is actually
/// thinking about is often the ninth or the twentieth, and then the list had nothing to say about
/// it at all. The figure here is the list's own — same scoring, same precision, measured against
/// the same leader — so a champion that does appear in both places reads the same in both.
/// </para>
/// </summary>
public sealed class HoverViewModel : ObservableObject
{
    private bool _isVisible;
    private ImageSource? _icon;
    private string _headline = string.Empty;
    private string _subline = string.Empty;
    private string _figure = string.Empty;
    private bool _hasFigure;
    private ScoreTone _tone;
    private string _note = string.Empty;

    public bool IsVisible
    {
        get => _isVisible;
        private set => Set(ref _isVisible, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set => Set(ref _icon, value);
    }

    public string Headline
    {
        get => _headline;
        private set => Set(ref _headline, value);
    }

    public string Subline
    {
        get => _subline;
        private set => Set(ref _subline, value);
    }

    public string Figure
    {
        get => _figure;
        private set => Set(ref _figure, value);
    }

    public bool HasFigure
    {
        get => _hasFigure;
        private set => Set(ref _hasFigure, value);
    }

    public ScoreTone Tone
    {
        get => _tone;
        private set => Set(ref _tone, value);
    }

    public string Note
    {
        get => _note;
        private set => Set(ref _note, value);
    }

    public void Hide() => IsVisible = false;

    /// <summary>A hover that cannot be locked: the reason instead of a figure for a pick that will not happen.</summary>
    public void ShowUnavailable(string name, ImageSource? icon, string reason)
    {
        Icon = icon;
        Headline = name;
        Subline = reason;
        Figure = string.Empty;
        HasFigure = false;
        Tone = ScoreTone.Weak;
        Note = $"{name} ist in diesem Draft nicht mehr wählbar ({reason}).";
        IsVisible = true;
    }

    /// <param name="listLength">
    /// How many rows the pick list shows. Its precision is decided by those rows, and the hover
    /// takes the same, so "57 %" in the list cannot sit under "56,8 %" here.
    /// </param>
    public void Show(PickEvaluation evaluation, ImageSource? icon, int listLength)
    {
        var culture = CultureInfo.CurrentCulture;
        var item = evaluation.Item;
        var lanePart = evaluation.Lane == Lane.Unknown ? string.Empty : $" auf {evaluation.Lane.Display()}";

        Icon = icon;
        Headline = item.Name;
        IsVisible = true;

        // Without the lane's own statistic there is no figure. The other terms are shifts away
        // from that base; summed without it they would read as an average champion pushed up or
        // down — a number nobody measured.
        if (item.Breakdown.FirstOrDefault(term => term.Kind == ScoreTermKind.LaneStrength) is { HasData: false })
        {
            Subline = $"keine OP.GG-Zahlen{lanePart}";
            Figure = string.Empty;
            HasFigure = false;
            Tone = ScoreTone.Weak;
            Note = $"OP.GG führt {item.Name}{lanePart} nicht. Ohne die Winrate auf der Lane als "
                + "Grundlage wäre jede Zahl hier geraten.";
            return;
        }

        var ranking = evaluation.Ranking;
        var leader = ranking.Count > 0 ? ranking[0] : item;
        var precision = ranking.Count > 0
            ? ScoreError.Decimals([.. ranking.Take(Math.Max(1, listLength))])
            : ScoreError.Decimals(item.Uncertainty);

        var hasError = item.Uncertainty > 0;
        var standing = ScoreError.Standing(leader.Score, leader.Uncertainty, item.Score, item.Uncertainty);

        Figure = $"{item.Score.ToString($"P{precision}", culture)} WR";
        HasFigure = true;
        Tone = RecommendationViewModel.PickTone(hasError, standing);

        // The same verdicts as the list rows, worded for a line that stands on its own.
        var verdict = (hasError, evaluation.Rank, standing) switch
        {
            (false, _, _) => "Datenlage zu dünn",
            (_, 1, _) => "bester Pick",
            (_, _, ScoreStanding.Tied) => "gleichauf mit Platz 1",
            (_, _, ScoreStanding.Ahead) => "knapp hinter Platz 1",
            _ => "deutlich hinter Platz 1",
        };

        Subline = evaluation.Rank > 0
            ? $"Platz {evaluation.Rank} von {ranking.Count}{lanePart} · {verdict}"
            : $"nicht in der Liste{lanePart} · {verdict}";

        var lines = new List<string>
        {
            $"Geschätzte Winrate von {item.Name} in diesem Draft — dieselbe Rechnung wie die Liste, "
                + "für deinen Hover. 50 % ist ausgeglichen.",
            string.Join(" · ", item.Breakdown
                .Where(term => !term.IsGate)
                .Select(term => term.HasData
                    ? $"{term.Label} {term.Points.ToString("+0.0;-0.0;0.0", culture)}"
                    : $"{term.Label} keine Daten")),
        };

        if (hasError)
        {
            lines.Add($"Streubereich ±{ScoreError.AsPoints(item.Uncertainty).ToString("N1", culture)} Punkte; "
                + "Unterschiede darunter bedeuten nichts.");
        }

        if (leader.ChampionId != item.ChampionId)
            lines.Add($"Platz 1{lanePart}: {leader.Name} mit {leader.Score.ToString($"P{precision}", culture)}.");

        if (item.Reasons.Count > 0)
            lines.Add(string.Join(" · ", item.Reasons.Select(reason => reason.Text)));

        Note = string.Join("\n", lines);
    }
}
