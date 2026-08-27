using System.Text.Json;
using DraftPilot.Core.Lcu;

namespace DraftPilot.Tools;

/// <summary>
/// Rewrites an existing recording without personal data. Needed because recordings made before the
/// recorder scrubbed by default still carry summoner names, puuids and a live chat JWT.
/// </summary>
internal static class ScrubCommand
{
    public static int Run(string path, string? outputPath)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Datei nicht gefunden: {path}");
            return 2;
        }

        var target = outputPath ?? path;
        var temporary = target + ".tmp";
        var frames = 0;
        var scrubbed = 0;
        var unparsed = 0;

        // No BOM, matching the recorder: recordings are line-oriented JSON.
        using (var writer = new StreamWriter(temporary, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                frames++;

                if (ScrubLine(line, ref scrubbed, ref unparsed) is { } cleaned)
                    writer.WriteLine(cleaned);
            }
        }

        File.Move(temporary, target, overwrite: true);

        Console.WriteLine($"{frames} Frames, {scrubbed} bereinigt, {unparsed} nicht bereinigbar (ausgelassen).");
        Console.WriteLine($"Geschrieben: {Path.GetFullPath(target)}");

        // A script piping this must be able to SEE that some frames could not be cleaned.
        return unparsed > 0 ? 1 : 0;
    }

    /// <summary>
    /// Scrubs the <c>session</c> object inside one recording line, leaving the envelope intact.
    /// Returns <see langword="null"/> for lines that cannot be cleaned — they are DROPPED, never
    /// copied (PII) and never replaced by a null frame (which would mean "session ended" to the
    /// replay and slam the panel shut mid-recording).
    /// </summary>
    private static string? ScrubLine(string line, ref int scrubbed, ref int unparsed)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var offset = root.TryGetProperty("offsetMs", out var offsetElement)
                && offsetElement.ValueKind == JsonValueKind.Number
                && offsetElement.TryGetInt64(out var parsedOffset)
                    ? parsedOffset
                    : 0;

            // Newer recordings interleave gameflow-phase frames. Rebuilt from the parsed values,
            // never passed through verbatim: a hand-crafted line carrying BOTH a phase and a
            // session would otherwise smuggle its session past the scrubbing below.
            if (root.TryGetProperty("phase", out var phaseElement) && phaseElement.ValueKind == JsonValueKind.String)
            {
                var phaseText = System.Text.Json.JsonEncodedText.Encode(phaseElement.GetString() ?? string.Empty);
                return $"{{\"offsetMs\":{offset},\"phase\":\"{phaseText}\"}}";
            }

            if (!root.TryGetProperty("session", out var session) || session.ValueKind == JsonValueKind.Null)
                return $"{{\"offsetMs\":{offset},\"session\":null}}";

            if (!SessionScrubber.TryScrub(session.GetRawText(), out var clean))
            {
                // NEVER the original: the whole point of this command is that the output carries
                // no summoner names, puuids or chat tokens.
                unparsed++;
                return null;
            }

            scrubbed++;
            return $"{{\"offsetMs\":{offset},\"session\":{clean}}}";
        }
        catch (JsonException)
        {
            unparsed++;
            return null;
        }
    }
}
