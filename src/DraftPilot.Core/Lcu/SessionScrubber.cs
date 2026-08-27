using System.Text.Json;
using System.Text.Json.Nodes;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Strips personal data and credentials out of a raw champion select payload before it is written
/// to disk.
/// <para>
/// The client's session carries far more than this tool reads: summoner names and tag lines, puuids,
/// summoner ids, and under <c>chatDetails</c> a live JWT for the champion select chat channel. A
/// recording is exactly the kind of file that ends up attached to a bug report, so it must not carry
/// a working credential. Nothing removed here is used by the tool.
/// </para>
/// </summary>
public static class SessionScrubber
{
    /// <summary>Top-level properties removed wholesale.</summary>
    private static readonly string[] SessionFields =
    [
        // Holds the chat channel JWT and its password.
        "chatDetails",
        "gameId",
    ];

    /// <summary>Per-player properties removed from both teams.</summary>
    private static readonly string[] PlayerFields =
    [
        "gameName",
        "tagLine",
        "puuid",
        "obfuscatedPuuid",
        "summonerId",
        "obfuscatedSummonerId",
        "playerAlias",
        "internalName",
        "nameVisibilityType",
    ];

    /// <summary>
    /// Removes personal data, reporting whether the payload could be parsed at all.
    /// </summary>
    public static bool TryScrub(string sessionJson, out string scrubbed)
    {
        scrubbed = sessionJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(sessionJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is not JsonObject session)
            return false;

        foreach (var field in SessionFields)
            session.Remove(field);

        foreach (var teamName in new[] { "myTeam", "theirTeam" })
        {
            if (session[teamName] is not JsonArray team)
                continue;

            foreach (var member in team)
            {
                if (member is not JsonObject player)
                    continue;

                foreach (var field in PlayerFields)
                    player.Remove(field);
            }
        }

        scrubbed = session.ToJsonString();
        return true;
    }
}
