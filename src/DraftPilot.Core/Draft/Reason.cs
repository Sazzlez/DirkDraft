namespace DraftPilot.Core.Draft;

/// <summary>Whether a reason argues for or against the entry it is attached to.</summary>
public enum ReasonTone
{
    /// <summary>Pure context, e.g. a pick rate. Neither for nor against.</summary>
    Neutral,

    /// <summary>Speaks for this recommendation.</summary>
    Pro,

    /// <summary>Speaks against it — shown so a high total score cannot hide a real drawback.</summary>
    Contra,
}

/// <summary>
/// One reason chip. Carries its tone explicitly because the code that writes the text is the only
/// place that reliably knows the sign; parsing it back out of German prose would be guesswork.
/// </summary>
/// <param name="Text">
/// What fits on a chip. Written so it stands on its own: a chip that needs the tooltip to make any
/// sense at all is a chip nobody can use mid-draft.
/// </param>
/// <param name="Hint">
/// The full story behind the chip — where the number comes from, how solid it is, what the term
/// means. Shown on hover, so the short text can stay short without becoming cryptic.
/// </param>
public sealed record Reason(string Text, ReasonTone Tone, string? Hint = null)
{
    public static Reason Pro(string text, string? hint = null) => new(text, ReasonTone.Pro, hint);

    public static Reason Contra(string text, string? hint = null) => new(text, ReasonTone.Contra, hint);

    public static Reason Neutral(string text, string? hint = null) => new(text, ReasonTone.Neutral, hint);

    public override string ToString() => Text;
}
