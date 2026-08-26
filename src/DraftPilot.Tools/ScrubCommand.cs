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

        using (var writer = new StreamWriter(temporary, append: false))
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                frames++;
                writer.WriteLine(ScrubLine(line, ref scrubbed, ref unparsed));
            }
        }

        File.Move(temporary, target, overwrite: true);

        Console.WriteLine($"{frames} Frames, {scrubbed} bereinigt, {unparsed} nicht lesbar.");
        Console.WriteLine($"Geschrieben: {Path.GetFullPath(target)}");
        return 0;
    }

    /// <summary>
    /// Scrubs the <c>session</c> object inside one recording line, leaving the envelope intact.
    /// </summary>
    private static string ScrubLine(string line, ref int scrubbed, ref int unparsed)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var offset = root.TryGetProperty("offsetMs", out var offsetElement) ? offsetElement.GetInt64() : 0;

            if (!root.TryGetProperty("session", out var session) || session.ValueKind == JsonValueKind.Null)
                return $"{{\"offsetMs\":{offset},\"session\":null}}";

            if (!SessionScrubber.TryScrub(session.GetRawText(), out var clean))
            {
                unparsed++;
                return line;
            }

            scrubbed++;
            return $"{{\"offsetMs\":{offset},\"session\":{clean}}}";
        }
        catch (JsonException)
        {
            unparsed++;
            return line;
        }
    }
}
