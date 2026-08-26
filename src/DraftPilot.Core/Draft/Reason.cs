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
public sealed record Reason(string Text, ReasonTone Tone)
{
    public static Reason Pro(string text) => new(text, ReasonTone.Pro);

    public static Reason Contra(string text) => new(text, ReasonTone.Contra);

    public static Reason Neutral(string text) => new(text, ReasonTone.Neutral);

    public override string ToString() => Text;
}
