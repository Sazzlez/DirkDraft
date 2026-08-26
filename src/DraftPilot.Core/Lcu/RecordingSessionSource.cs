using System.Diagnostics;
using System.Text;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Passes a session source straight through while writing every payload to a JSONL file.
/// Recordings are the whole reason the rest of the tool can be built and tested without being in a
/// real draft.
/// </summary>
public sealed class RecordingSessionSource : ISessionSource
{
    private readonly ISessionSource _inner;
    private readonly StreamWriter _writer;
    private readonly Stopwatch _clock = new();
    private readonly Lock _writeLock = new();
    private readonly bool _keepPersonalData;

    /// <param name="keepPersonalData">
    /// Writes the payload verbatim instead of scrubbing it. Off by default: the client's session
    /// carries summoner names, puuids and a live chat JWT, none of which this tool reads and none of
    /// which belong in a file that gets shared.
    /// </param>
    public RecordingSessionSource(ISessionSource inner, string path, bool keepPersonalData = false)
    {
        _inner = inner;
        _keepPersonalData = keepPersonalData;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };

        _inner.SessionJson += OnSessionJson;
        _inner.StatusChanged += OnStatusChanged;
    }

    /// <summary>How many payloads have been written so far.</summary>
    public int FrameCount { get; private set; }

    public event Action<string?>? SessionJson;

    public event Action<ClientStatus>? StatusChanged;

    public Task RunAsync(CancellationToken ct)
    {
        _clock.Start();
        return _inner.RunAsync(ct);
    }

    private void OnSessionJson(string? json)
    {
        lock (_writeLock)
        {
            var offset = (long)_clock.Elapsed.TotalMilliseconds;
            var payload = json is null ? "null" : _keepPersonalData ? json : SessionScrubber.Scrub(json);

            _writer.Write("{\"offsetMs\":");
            _writer.Write(offset);
            _writer.Write(",\"session\":");
            _writer.Write(payload);
            _writer.WriteLine('}');
            FrameCount++;
        }

        SessionJson?.Invoke(json);
    }

    private void OnStatusChanged(ClientStatus status) => StatusChanged?.Invoke(status);

    public async ValueTask DisposeAsync()
    {
        _inner.SessionJson -= OnSessionJson;
        _inner.StatusChanged -= OnStatusChanged;
        await _inner.DisposeAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
