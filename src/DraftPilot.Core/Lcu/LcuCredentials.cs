namespace DraftPilot.Core.Lcu;

/// <summary>Connection details for the local League client, parsed out of its lockfile.</summary>
public sealed record LcuCredentials(int Port, string Password)
{
    public string HttpBase => $"https://127.0.0.1:{Port}";

    public Uri WebSocketUri => new($"wss://127.0.0.1:{Port}/");

    /// <summary>
    /// Parses a lockfile body of the form <c>LeagueClient:&lt;pid&gt;:&lt;port&gt;:&lt;password&gt;:https</c>.
    /// Returns <see langword="null"/> for anything that does not look like one, so a half-written
    /// file (the watcher can see it mid-write) is simply retried rather than throwing.
    /// </summary>
    public static LcuCredentials? TryParse(string? lockfileContent)
    {
        if (string.IsNullOrWhiteSpace(lockfileContent))
            return null;

        var parts = lockfileContent.Trim().Split(':');
        if (parts.Length < 4)
            return null;

        if (!int.TryParse(parts[2], out var port) || port is <= 0 or > 65535)
            return null;

        return string.IsNullOrEmpty(parts[3]) ? null : new LcuCredentials(port, parts[3]);
    }
}
