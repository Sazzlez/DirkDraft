using System.Net;
using System.Runtime.ExceptionServices;

namespace DraftPilot.Meta.Riot;

/// <summary>
/// What a failed request means for a collector run. One game Riot will not hand out — a 400, a 403 on
/// that match, six 5xx in a row — is one game less and the run goes on: until 17.11.2026 any of them
/// ended the run with exit code 1 and the rest of its four and a half hours went unused.
/// <para>
/// The run ends only where going on is pointless: a 401 (the key), a 403 on anything but a single
/// game (the key again — Riot answers 403 for a few games it hides, and for every request of a
/// blocked key), or <see cref="DefaultLimit"/> failures in a row (an outage).
/// </para>
/// </summary>
public sealed class CollectorFailures(int limit = CollectorFailures.DefaultLimit)
{
    /// <summary>Failures in a row before the run gives up: no answer counted in between.</summary>
    public const int DefaultLimit = 20;

    private int _inARow;

    /// <summary>Requests that failed and were skipped.</summary>
    public int Count { get; private set; }

    /// <summary>What the last of them said.</summary>
    public string? LastReason { get; private set; }

    /// <summary>An answer came — 200 or 404 alike: Riot is there and takes the key.</summary>
    public void Succeeded() => _inARow = 0;

    /// <summary>
    /// Counts the failure and returns when the run goes on; throws when it has to end — the failure
    /// itself for a rejected key, so its message stays the one sentence it always was.
    /// </summary>
    /// <param name="game">The request was for one game (the match or its timeline).</param>
    public void Failed(RiotApiException failure, bool game)
    {
        if (failure.Status == HttpStatusCode.Unauthorized || (failure.Status == HttpStatusCode.Forbidden && !game))
            ExceptionDispatchInfo.Capture(failure).Throw();

        Count++;
        LastReason = failure.Message;
        if (++_inARow >= limit)
        {
            throw new RiotApiException(
                $"{limit} Abrufe in Folge sind fehlgeschlagen — Riot fällt aus oder nimmt den Schlüssel nicht mehr. Zuletzt: {failure.Message}",
                failure.Status);
        }
    }
}
