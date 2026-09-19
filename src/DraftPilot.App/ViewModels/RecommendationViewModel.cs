using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using DraftPilot.App;
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
    private string _scoreVerdict = string.Empty;
    private string _scoreHint = string.Empty;
    private string _totalLabel = string.Empty;
    private ScoreTone _tone;
    private bool _isExpanded;
    private bool _isTopGroup;
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

    /// <summary>The score in words, so the number in the pill is not the only thing said.</summary>
    public string ScoreVerdict
    {
        get => _scoreVerdict;
        set => Set(ref _scoreVerdict, value);
    }

    /// <summary>What the pill's number means; differs between picks and bans.</summary>
    public string ScoreHint
    {
        get => _scoreHint;
        set => Set(ref _scoreHint, value);
    }

    /// <summary>Label of the breakdown's total row: what the terms add up to.</summary>
    public string TotalLabel
    {
        get => _totalLabel;
        set => Set(ref _totalLabel, value);
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
    /// Whether the error bars fail to separate this row from the leader — rank 1 and everyone tied
    /// with it. The list has to be printed in some order and that order reads as a verdict; this is
    /// what lets the window draw where the verdict actually stops, instead of leaving it in a
    /// sentence behind the chevron.
    /// <para>
    /// Always false for bans: their score is denied win-rate points, carries no error bar, and a
    /// mark meaning "statistically tied" would claim a statistic that was never computed.
    /// </para>
    /// </summary>
    public bool IsTopGroup
    {
        get => _isTopGroup;
        set => Set(ref _isTopGroup, value);
    }

    /// <summary>
    /// Applies a new recommendation. Collapses the breakdown when the champion changed, so an
    /// expanded row does not silently start showing a different champion's numbers.
    /// </summary>
    /// <param name="isBan">
    /// Bans and picks score in different units: a pick's score is an estimated win rate, a ban's
    /// the win-rate points denied to the enemy.
    /// </param>
    /// <param name="standing">
    /// How this row stands against the leader of the same list, in standard errors of the
    /// difference. Decided by the caller because only it can see the whole list.
    /// </param>
    public void Apply(
        int rank,
        Recommendation recommendation,
        ImageSource? icon,
        bool isBan,
        int precision = 1,
        ScoreStanding standing = ScoreStanding.Tied)
    {
        if (_championId != recommendation.ChampionId)
        {
            _championId = recommendation.ChampionId;
            IsExpanded = false;
        }

        Rank = rank;
        Name = recommendation.Name;
        Icon = icon;
        IsTopGroup = !isBan
            && recommendation.Uncertainty > 0
            && (rank == 1 || standing == ScoreStanding.Tied);

        var culture = CultureInfo.CurrentCulture;

        if (isBan)
        {
            // Recalibrated against what the scale actually produces. The thresholds were 4,5 and
            // 2,5 from the days when the OP.GG tier was still added into the ban value — it hung
            // twice there, and taking it out moved the whole scale down without moving them.
            // Measured over the top eight of five drafts on the stored snapshot (40 rows): maximum
            // 4,4 — so "wichtiger Bann" had become unreachable — 10 % above 3,6, a quarter above
            // 2,9, median 2,6. The bar for "important" now sits at the top tenth of what is shown,
            // and the bar for "worth it" at the middle of it.
            Score = string.Format(culture, "{0:+0.0;-0.0;0.0} Pkt", recommendation.Score);
            Tone = recommendation.Score switch
            {
                > 3.5 => ScoreTone.Strong,
                > 2.5 => ScoreTone.Fair,
                _ => ScoreTone.Weak,
            };
            // Nothing at all below the second bar, rather than eight rows of "optional". A draft
            // where no ban is urgent is a real answer, and silence gives it without spending a
            // line per row saying so — the number and its tooltip are right there.
            ScoreVerdict = recommendation.Score switch
            {
                > 3.5 => "wichtiger Bann",
                > 2.5 => "sinnvoller Bann",
                _ => string.Empty,
            };
            ScoreHint = "So viele Prozentpunkte Siegquote würde dieser Champion dem Gegner voraussichtlich "
                + "bringen — Stärke mal Wahrscheinlichkeit, dass er überhaupt genommen wird. "
                + "Der Pfeil rechts zeigt, woraus sich die Zahl zusammensetzt.";
            TotalLabel = "Wert des Banns";
        }
        else
        {
            // Two things the figure has to carry. "WR" is spelled out on it because the panel shows
            // a second kind of percentage right next to it — how likely an enemy plays that lane —
            // and the two were easy to mix up as bare numbers. And it gets only as many decimals as
            // its own error bar supports: a tenth of a point beside an error of one and a half was
            // the reason this list read as far more decided than it was.
            var margin = ScoreError.AsPoints(recommendation.Uncertainty);
            Score = $"{recommendation.Score.ToString($"P{precision}", culture)} WR";

            // The verdict answers the question the list is FOR: is this one of the picks worth
            // taking, or is something above it measurably better? Against a fixed 50 % it could not
            // — the list is always the best eight of a lane, so every row cleared any absolute bar
            // and eight identical green pills said nothing. Measured on the stored data: a top
            // draft shows eight rows between 53,4 % and 52,0 % with errors of 0,15 to 0,40 points.
            var hasError = recommendation.Uncertainty > 0;

            Tone = (hasError, standing) switch
            {
                (false, _) => ScoreTone.Weak,
                (_, ScoreStanding.Tied) => ScoreTone.Strong,
                (_, ScoreStanding.Ahead) => ScoreTone.Fair,
                _ => ScoreTone.Weak,
            };

            ScoreVerdict = (hasError, rank, standing) switch
            {
                (false, _, _) => "Datenlage zu dünn",
                (_, 1, _) => "beste Wahl der Liste",
                (_, _, ScoreStanding.Tied) => "gleichauf mit Platz 1",
                (_, _, ScoreStanding.Ahead) => "knapp dahinter",
                _ => "deutlich dahinter",
            };

            ScoreHint = "Geschätzte Siegquote dieser Aufstellung nach OP.GG-Daten — nicht deine persönliche. "
                + "50 % ist ausgeglichen. Aus den Stichprobengrößen hinter den Kriterien folgt ein "
                + $"Streubereich von ±{margin.ToString("N1", culture)} Punkten; Unterschiede darunter "
                + "bedeuten nichts. Die Einordnung darunter vergleicht mit Platz 1 der Liste, nicht "
                + "mit 50 %. Der Pfeil rechts zeigt, woraus sich die Zahl zusammensetzt.";
            TotalLabel = "Geschätzte Siegquote";
        }

        Reasons.ReplaceAll(recommendation.Reasons);

        Breakdown.Resize(recommendation.Breakdown.Count, () => new TermViewModel());
        for (var i = 0; i < recommendation.Breakdown.Count; i++)
            Breakdown[i].Apply(recommendation.Breakdown[i]);
    }
}

/// <summary>
/// One line of the score breakdown, in words.
/// <para>
/// This used to print the raw arithmetic — <c>+0,63 × 1,0 = +0,63</c> — which told a reader nothing
/// about what the criterion was or which direction was good. Now the criterion gets a verdict in
/// plain German plus a bar for its magnitude, both in percentage points of win rate; the origin of
/// the number lives in the tooltip.
/// </para>
/// </summary>
public sealed class TermViewModel : ObservableObject
{
    /// <summary>Pixels per percentage point; half-bar caps out at 30 px (≈ 4 points).</summary>
    private const double PixelsPerPoint = 7.5;

    private const double BarMax = 30;

    private string _label = string.Empty;
    private string _verdict = string.Empty;
    private string _value = string.Empty;
    private string _hint = string.Empty;
    private ReasonTone _tone;
    private bool _hasData;
    private double _barPositive;
    private double _barNegative;

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    /// <summary>What the criterion says, e.g. <c>stark dafür</c> or <c>keine Daten</c>.</summary>
    public string Verdict
    {
        get => _verdict;
        set => Set(ref _verdict, value);
    }

    /// <summary>Contribution in percentage points, or a dash when there was nothing to measure.</summary>
    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    /// <summary>What the criterion measures, plus what the number in <see cref="Value"/> means.</summary>
    public string Hint
    {
        get => _hint;
        set => Set(ref _hint, value);
    }

    public ReasonTone Tone
    {
        get => _tone;
        set => Set(ref _tone, value);
    }

    /// <summary>False dims the whole line: nothing was measured, so nothing is being claimed.</summary>
    public bool HasData
    {
        get => _hasData;
        set => Set(ref _hasData, value);
    }

    public double BarPositive
    {
        get => _barPositive;
        set => Set(ref _barPositive, value);
    }

    public double BarNegative
    {
        get => _barNegative;
        set => Set(ref _barNegative, value);
    }

    public void Apply(ScoreTerm term)
    {
        var culture = CultureInfo.CurrentCulture;

        Label = term.Label;
        HasData = term.HasData;

        // The popularity gate is a multiplier, not evidence: it scales the ban's value instead of
        // shifting a win rate, and the row says so.
        if (term.IsGate)
        {
            Verdict = !term.HasData
                ? "keine Daten"
                : term.Gate switch
                {
                    >= 0.6 => "wird oft genommen",
                    >= 0.25 => "wird gelegentlich genommen",
                    _ => "wird selten genommen",
                };

            Tone = ReasonTone.Neutral;
            Value = term.HasData ? string.Format(culture, "× {0:P0}", term.Gate) : "–";
            BarPositive = term.HasData ? Math.Min(BarMax, term.Gate * BarMax) : 0;
            BarNegative = 0;
            Hint = term.HasData
                ? $"{term.Hint}\n\nDer Wert des Banns wird mit diesem Faktor multipliziert."
                : $"{term.Hint}\n\nDazu liegen keine Daten vor.";
            return;
        }

        var points = term.Points;

        Verdict = !term.HasData
            ? "keine Daten"
            : points switch
            {
                >= 2 => "stark dafür",
                >= 0.6 => "dafür",
                > -0.6 => "neutral",
                > -2 => "dagegen",
                _ => "stark dagegen",
            };

        Tone = !term.HasData
            ? ReasonTone.Neutral
            : points switch
            {
                >= 0.6 => ReasonTone.Pro,
                <= -0.6 => ReasonTone.Contra,
                _ => ReasonTone.Neutral,
            };

        Value = term.HasData
            ? points.ToString("+0.0;-0.0;0.0", culture)
            : "–";

        BarPositive = Math.Min(BarMax, Math.Max(0, points) * PixelsPerPoint);
        BarNegative = Math.Min(BarMax, Math.Max(0, -points) * PixelsPerPoint);

        Hint = term.HasData
            ? string.Format(
                culture,
                "{0}\n\nVerschiebt die geschätzte Siegquote um {1:+0.0;-0.0;0.0} Prozentpunkte.",
                term.Hint,
                points)
            : $"{term.Hint}\n\nDazu liegen keine Daten vor — dieser Punkt zählt hier nicht mit.";
    }
}
