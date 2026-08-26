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
        var frames = Load(path);
        _frames = maxFrames < frames.Count ? frames.GetRange(0, Math.Max(0, maxFrames)) : frames;
        _speed = speed;
    }

    public event Action<string?>? SessionJson;

    public event Action<ClientStatus>? StatusChanged;

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

            SessionJson?.Invoke(frame.Json);
        }
    }

    private static List<Frame> Load(string path)
    {
        var frames = new List<Frame>();

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var offset = root.TryGetProperty("offsetMs", out var offsetElement) ? offsetElement.GetInt64() : 0;
            var session = root.TryGetProperty("session", out var sessionElement) && sessionElement.ValueKind is not JsonValueKind.Null
                ? sessionElement.GetRawText()
                : null;

            frames.Add(new Frame(offset, session));
        }

        return frames;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Frame(long OffsetMs, string? Json);
}
