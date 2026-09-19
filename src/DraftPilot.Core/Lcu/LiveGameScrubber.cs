using System.Text.Json.Nodes;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Replaces player names in a payload from the running game's own API, before it reaches disk.
/// <para>
/// Champion select has <see cref="SessionScrubber"/>, which knows that payload's exact shape. This
/// one cannot, and must not: the whole reason to record the game API is that its shape is unknown,
/// so the rule has to be "a property with one of these names, wherever it sits" rather than a path.
/// </para>
/// <para>
/// Names are replaced rather than removed. A recording made to find out whether a field exists must
/// not answer that question by deleting fields — "absent" and "present but blanked" are exactly the
/// distinction being looked for.
/// </para>
/// <para>
/// The mapping is stable for the lifetime of one instance, so the same summoner reads as the same
/// pseudonym on every line and a kill feed still reads as a kill feed. It is one-way: the map lives
/// in memory and is never written anywhere.
/// </para>
/// </summary>
public sealed class LiveGameScrubber
{
    /// <summary>
    /// Properties whose value is a person, wherever they appear. The Live Client Data API names
    /// people in three separate places — the player list, the active player, and the event feed —
    /// and each does it with its own spelling.
    /// </summary>
    private static readonly HashSet<string> NameFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "summonerName",
        "riotId",
        "riotIdGameName",
        "riotIdTagLine",
        "KillerName",
        "VictimName",
        "Assisters",
        "Acer",
    };

    /// <summary>
    /// Depth at which the walk gives up. The payload is a few levels deep; anything approaching
    /// this is malformed, and a recursive walk over it would take the process down with it.
    /// </summary>
    private const int MaxDepth = 64;

    private readonly Dictionary<string, string> _pseudonyms = new(StringComparer.Ordinal);

    /// <summary>How many distinct names have been replaced so far.</summary>
    public int Replaced => _pseudonyms.Count;

    /// <summary>
    /// Scrubs one payload, reporting whether it could be parsed at all. An unparsable payload comes
    /// back unchanged and must not be written — it may contain anything.
    /// </summary>
    public bool TryScrub(string json, out string scrubbed)
    {
        scrubbed = json;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        if (root is null)
            return false;

        Walk(root, 0);
        scrubbed = root.ToJsonString();
        return true;
    }

    /// <summary>Scrubs a node in place, for a caller that already parsed the payload.</summary>
    public void Scrub(JsonNode? node) => Walk(node, 0);

    private void Walk(JsonNode? node, int depth)
    {
        if (depth > MaxDepth)
            return;

        switch (node)
        {
            case JsonObject obj:
                // The key list is materialised first: replacing a value mutates the very
                // collection being enumerated, which throws halfway through otherwise.
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    if (NameFields.Contains(key))
                        Redact(obj, key);
                    else
                        Walk(obj[key], depth + 1);
                }

                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    Walk(array[index], depth + 1);

                break;
        }
    }

    /// <summary>
    /// Replaces one named property. It is usually a string, but <c>Assisters</c> is a list of them,
    /// and a field that is neither is left alone rather than guessed at.
    /// </summary>
    private void Redact(JsonObject parent, string key)
    {
        switch (parent[key])
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                parent[key] = Pseudonym(text);
                break;

            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    if (list[index] is JsonValue item && item.TryGetValue<string>(out var text))
                        list[index] = Pseudonym(text);
                }

                break;
        }
    }

    /// <summary>
    /// The stand-in for one name. An empty value stays empty: a blank name is not personal, and
    /// turning it into a pseudonym would hide that the client sent nothing there.
    /// </summary>
    private string Pseudonym(string value)
    {
        if (value.Length == 0)
            return value;

        if (_pseudonyms.TryGetValue(value, out var existing))
            return existing;

        var pseudonym = $"Spieler {_pseudonyms.Count + 1}";
        _pseudonyms[value] = pseudonym;
        return pseudonym;
    }
}
