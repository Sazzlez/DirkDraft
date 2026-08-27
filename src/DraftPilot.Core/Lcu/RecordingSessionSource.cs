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
    private bool _closed;

    /// <summary>Frames whose payload could not be scrubbed and was replaced by a placeholder.</summary>
    public int UnscrubbedFrames { get; private set; }

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

        // No BOM, matching what the scrub command writes: recordings are line-oriented JSON and
        // some strict consumers reject a byte-order mark.
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        _inner.SessionJson += OnSessionJson;
        _inner.StatusChanged += OnStatusChanged;
        _inner.GameflowPhase += OnGameflowPhase;
    }

    /// <summary>How many payloads have been written so far.</summary>
    public int FrameCount { get; private set; }

    public event Action<string?>? SessionJson;

    public event Action<ClientStatus>? StatusChanged;

    public event Action<string>? GameflowPhase;

    public Task RunAsync(CancellationToken ct)
    {
        _clock.Start();
        return _inner.RunAsync(ct);
    }

    private void OnSessionJson(string? json)
    {
        lock (_writeLock)
        {
            // A payload arriving between unsubscribe and writer disposal must not write into a
            // dead stream — that exception would land in the socket thread.
            if (!_closed)
            {
                var offset = (long)_clock.Elapsed.TotalMilliseconds;
                string? payload;

                if (json is null)
                {
                    payload = "null";
                }
                else if (_keepPersonalData)
                {
                    payload = Sanitize(json);
                }
                else if (SessionScrubber.TryScrub(json, out var scrubbed))
                {
                    payload = Sanitize(scrubbed);
                }
                else
                {
                    // NEVER the raw payload: it carries summoner names, puuids and a live chat
                    // JWT, and this file is exactly the one that gets attached to a bug report.
                    // Skipped entirely, not written as null — a null frame means "session ended"
                    // to the replay and would slam the panel shut mid-recording.
                    payload = null;
                    UnscrubbedFrames++;
                }

                if (payload is not null)
                {
                    // One Write for the whole line: with AutoFlush, five separate writes could
                    // leave a torn line behind if the process dies mid-frame — breaking replay.
                    _writer.WriteLine($"{{\"offsetMs\":{offset},\"session\":{payload}}}");
                    FrameCount++;
                }
            }
        }

        SessionJson?.Invoke(json);
    }

    /// <summary>JSONL is line-oriented; a payload containing raw line breaks would split a frame.</summary>
    private static string Sanitize(string payload)
        => payload.Contains('\n') ? payload.Replace("\r", string.Empty).Replace('\n', ' ') : payload;

    private void OnStatusChanged(ClientStatus status) => StatusChanged?.Invoke(status);

    private void OnGameflowPhase(string phase)
    {
        // Recorded too, so a replay can drive the in-game build view — without this the whole
        // game-start path was untestable from a recording.
        lock (_writeLock)
        {
            if (!_closed && phase.Length > 0)
            {
                var offset = (long)_clock.Elapsed.TotalMilliseconds;

                // Properly JSON-escaped: a stray quote or backslash in the phase would write a
                // syntactically torn line that the replay then counts as unreadable.
                var encoded = System.Text.Json.JsonEncodedText.Encode(phase);
                _writer.WriteLine($"{{\"offsetMs\":{offset},\"phase\":\"{encoded}\"}}");
            }
        }

        GameflowPhase?.Invoke(phase);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_writeLock)
        {
            _closed = true;
        }

        _inner.SessionJson -= OnSessionJson;
        _inner.StatusChanged -= OnStatusChanged;
        _inner.GameflowPhase -= OnGameflowPhase;
        await _inner.DisposeAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
