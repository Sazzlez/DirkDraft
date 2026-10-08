namespace DraftPilot.Core.Data;

/// <summary>
/// Patch strings from three sources that agree on nothing but their first two numbers:
/// Data Dragon writes <c>16.18.1</c>, OP.GG aggregates over <c>16.18</c>, and the League client
/// answers <c>16.18.8175716+branch.releases-16-18.code.public.release</c>. Comparing them means
/// comparing that prefix and nothing else.
/// </summary>
public static class PatchVersion
{
    /// <summary>
    /// The <c>16.18</c> of any of those spellings, or empty when the value does not start with
    /// two numbers. Empty means "do not know", never "0.0" — a wrong patch comparison would be a
    /// worse answer than no comparison.
    /// </summary>
    public static string Line(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        var dots = 0;
        var end = 0;

        while (end < text.Length)
        {
            var character = text[end];

            if (character == '.')
            {
                if (++dots == 2)
                    break;
            }
            else if (!char.IsAsciiDigit(character))
            {
                break;
            }

            end++;
        }

        var line = text[..end];

        // A lone major ("16") is not a patch line, and neither is anything that stops on a dot.
        return dots >= 1 && line.Length > 0 && char.IsAsciiDigit(line[^1]) ? line : string.Empty;
    }

    /// <summary>
    /// How many patches <paramref name="newer"/> is ahead of <paramref name="older"/>, or 0 when
    /// either is unreadable or the data is not behind. Across a year boundary Riot restarts the
    /// minor number; a season has about 24 patches, which is what a major step counts as — exact
    /// enough for an error bar, and the boundary is crossed once a year.
    /// </summary>
    public static int PatchesBetween(string? older, string? newer)
    {
        if (!TryParts(Line(older), out var oldMajor, out var oldMinor) || !TryParts(Line(newer), out var newMajor, out var newMinor))
            return 0;

        var steps = ((newMajor - oldMajor) * 24) + (newMinor - oldMinor);
        return Math.Max(0, steps);

        static bool TryParts(string line, out int major, out int minor)
        {
            major = minor = 0;
            var parts = line.Split('.');
            return parts.Length == 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
        }
    }

    /// <summary>
    /// Whether two patch strings describe the same patch. An unknown on either side counts as
    /// agreement: the comparison exists to warn about a REAL difference, and a warning built on a
    /// string nobody could read would be noise.
    /// </summary>
    public static bool SameLine(string? left, string? right)
    {
        var first = Line(left);
        var second = Line(right);

        return first.Length == 0 || second.Length == 0 || string.Equals(first, second, StringComparison.Ordinal);
    }
}
