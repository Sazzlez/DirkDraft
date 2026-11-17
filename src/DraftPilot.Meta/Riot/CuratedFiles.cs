using System.Text.Json;
using System.Text.Json.Nodes;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

/// <summary>
/// The curated files of <c>data/</c> — champion traits and combo lists — as they go to the data
/// branch beside the counted numbers. Until 17.11.2026 both were built into the app (and the traits
/// lay in the repository twice): a new champion had no traits until the next release, a reworked one
/// kept its old ones. Now <c>data/</c> is the single source, the export publishes it with every run
/// and the app downloads it with the numbers.
/// </summary>
public static class CuratedFiles
{
    /// <summary>The layout the app reads (app/src/data/remote.ts); a change the app cannot read needs a new number.</summary>
    public const int Version = 1;

    /// <summary>
    /// The file as published: the same document without its <c>$comment</c>, with the format's
    /// version, on one line. Null when it is not a JSON object carrying <paramref name="required"/> —
    /// a broken file is not published, the branch keeps the one before.
    /// </summary>
    public static string? Publish(string? json, string required)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is not JsonObject document
                || document[required] is not JsonObject)
            {
                return null;
            }

            document.Remove("$comment");
            document["version"] = Version;
            return document.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Champions of the game the traits file does not know, by name: a new champion, or a key spelled
    /// differently. Without traits a champion silently loses every comp rule that reads them.
    /// </summary>
    public static List<string> WithoutTraits(IEnumerable<ChampionEntry> champions, TraitTable traits)
        => [.. champions.Where(champion => !traits.For(champion.Key).IsKnown).Select(champion => champion.Name.Length > 0 ? champion.Name : champion.Key).Order(StringComparer.Ordinal)];
}
