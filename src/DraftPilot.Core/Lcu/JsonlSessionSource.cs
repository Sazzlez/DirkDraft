using System.Text.Json;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Replays a recording made by <see cref="RecordingSessionSource"/>. Used by the tests and by
/// <c>DraftPilot.Tools replay</c> so the UI and the recommendation engine can be exercised without
/// a running client.
/// </summary>
public sealed class JsonlSessionSource : ISessionSource
{
    private readonly IReadOnlyList<Frame> _frames;
    private readonly double _speed;

    /// <param name="path">A JSONL recording.</param>
    /// <param name="speed">
    /// Playback multiplier against the recorded timing. Use <see cref="double.PositiveInfinity"/>
    /// to replay with no waiting at all, which is what the tests want.
    /// </param>
    /// <param name="maxFrames">
    /// Stop after this many frames instead of playing to the end. Lets a caller hold the draft at a
    /// chosen moment, which is what the screenshot harness needs.
    /// </param>
    public JsonlSessionSource(string path, double speed = 1.0, int maxFrames = int.MaxValue)
    {
        var frames = Load(path, out var skipped);
        _frames = maxFrames < frames.Count ? frames.GetRange(0, Math.Max(0, maxFrames)) : frames;
        _speed = speed;
        SkippedLines = skipped;
    }

    /// <summary>Lines the recording contained but the parser could not read (e.g. a torn last
    /// line from a killed recorder). Surfaced so the replay command can report them.</summary>
    public int SkippedLines { get; }

    public event Action<string?>? SessionJson;

    public event Action<ClientStatus>? StatusChanged;

    public event Action<string>? GameflowPhase;

    /// <summary>Number of payloads in the recording.</summary>
    public int FrameCount => _frames.Count;

    public async Task RunAsync(CancellationToken ct)
    {
        StatusChanged?.Invoke(new ClientStatus(LeagueFound: true, ClientRunning: true, SocketConnected: true, "Wiedergabe"));

        long previousOffset = 0;

        foreach (var frame in _frames)
        {
            if (ct.IsCancellationRequested)
                return;

            var wait = double.IsPositiveInfinity(_speed) ? 0 : (frame.OffsetMs - previousOffset) / _speed;
            previousOffset = frame.OffsetMs;

            if (wait > 1)
                await Task.Delay(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(false);

            // A phase frame drives the gameflow event; it is not a session payload (and must not
            // read as "session ended").
            if (frame.Phase is not null)
            {
                GameflowPhase?.Invoke(frame.Phase);
                continue;
            }

            SessionJson?.Invoke(frame.Json);
        }
    }

    private static List<Frame> Load(string path, out int skipped)
    {
        var frames = new List<Frame>();
        skipped = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // An unreadable line — typically the torn last line of a recording ended with Ctrl+C —
            // costs that frame, not the whole replay.
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                var offset = root.TryGetProperty("offsetMs", out var offsetElement)
                    && offsetElement.ValueKind == JsonValueKind.Number
                    && offsetElement.TryGetInt64(out var parsedOffset)
                        ? parsedOffset
                        : 0;

                var phase = root.TryGetProperty("phase", out var phaseElement)
                    && phaseElement.ValueKind == JsonValueKind.String
                        ? phaseElement.GetString()
                        : null;

                var session = root.TryGetProperty("session", out var sessionElement) && sessionElement.ValueKind is not JsonValueKind.Null
                    ? sessionElement.GetRawText()
                    : null;

                frames.Add(new Frame(offset, session, phase));
            }
            catch (JsonException)
            {
                skipped++;
            }
        }

        return frames;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Frame(long OffsetMs, string? Json, string? Phase = null);
}
