using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DraftPilot.Meta.OpGg;

/// <summary>The endpoint answered, but with an error.</summary>
public sealed class OpGgApiException(string message) : Exception(message)
{
    /// <summary>
    /// The HTTP status, when the transport itself failed. Null means the response arrived and was
    /// rejected on content — an unknown champion name, no data for this combination. The difference
    /// decides whether a caller may quietly try the next spelling or has to report an outage.
    /// </summary>
    public HttpStatusCode? Status { get; init; }
}

/// <summary>
/// Talks to OP.GG's public MCP endpoint over plain JSON-RPC. No MCP SDK: the exchange is an
/// initialize handshake followed by <c>tools/call</c> posts, which is less code than a dependency.
/// <para>
/// This is the only type in the whole tool that reaches the internet, and it is only ever
/// constructed from the update action.
/// </para>
/// </summary>
public sealed class OpGgMcpClient : IDisposable
{
    private const string Endpoint = "https://mcp-api.op.gg/mcp";
    private const string ProtocolVersion = "2025-06-18";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _handshakeGate = new(1, 1);

    // Not volatile: written under the handshake gate or via Interlocked; readers only ever use
    // it as an advisory value that the server double-checks anyway.
    private string? _sessionId;
    private volatile bool _initialized;
    private int _nextId;
    private int _callCount;

    /// <param name="timeout">
    /// How long one call may take. Thirty seconds suits the update, which runs unattended for
    /// minutes; during a draft the same number means one slow call eats a whole pick phase, so that
    /// caller passes its own. Ignored when an <see cref="HttpClient"/> is supplied.
    /// </param>
    public OpGgMcpClient(HttpClient? http = null, TimeSpan? timeout = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DraftPilot/0.1 (+local champion select assistant)");
    }

    /// <summary>How many calls have been sent; surfaced in the update progress display.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>Calls a tool and returns its parsed payload.</summary>
    public async Task<OpGgNode> CallToolAsync(string tool, JsonObject arguments, CancellationToken ct)
        => OpGgResponseParser.Parse(await CallToolRawAsync(tool, arguments, ct).ConfigureAwait(false));

    /// <summary>
    /// Calls a tool and returns the payload text untouched. Most OP.GG tools answer in a compact
    /// typed-tuple format, but a few — the matchup guide among them — return plain JSON, which the
    /// tuple parser must not be pointed at.
    /// </summary>
    public async Task<string> CallToolRawAsync(string tool, JsonObject arguments, CancellationToken ct)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);

        var parameters = new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        };

        var result = await SendAsync("tools/call", parameters, ct).ConfigureAwait(false);
        return ExtractText(result);
    }

    /// <summary>
    /// The endpoint's own tool catalogue, straight from <c>tools/list</c>: names, descriptions and
    /// the JSON schema of every argument. Nothing in the app calls this — the diagnostic command
    /// does, because "does OP.GG offer X" cannot be answered by reading our own request code,
    /// which only ever shows what we already ask for.
    /// <para>
    /// Follows the cursor: a catalogue that answers in pages would otherwise look like a short one.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<JsonNode>> ListToolsAsync(CancellationToken ct)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);

        var tools = new List<JsonNode>();
        string? cursor = null;

        do
        {
            var parameters = cursor is null ? null : new JsonObject { ["cursor"] = cursor };
            var result = await SendAsync("tools/list", parameters, ct).ConfigureAwait(false);

            if (result["tools"] is JsonArray array)
            {
                foreach (var tool in array)
                {
                    if (tool is not null)
                        tools.Add(tool.DeepClone());
                }
            }

            cursor = result["nextCursor"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(cursor));

        return tools;
    }

    /// <summary>Convenience for the string-array shape every OP.GG tool uses for field selection.</summary>
    public static JsonArray Fields(params string[] names)
    {
        var array = new JsonArray();
        foreach (var name in names)
            array.Add(name);

        return array;
    }

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await _handshakeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            var initialize = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "DraftPilot", ["version"] = "0.1" },
            };

            using var response = await PostAsync("initialize", initialize, isNotification: false, ct).ConfigureAwait(false);
            EnsureSuccess(response);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ReadResult(body, WasEventStream(response));

            // Only a COMPLETE handshake counts. Setting the session id before ReadResult could
            // throw left the client believing it was initialised while the server never got the
            // initialized notification — after which every tools/call failed for good.
            var sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
                ? values.FirstOrDefault()
                : null;

            using var ack = await PostAsyncWithSession("notifications/initialized", null, isNotification: true, sessionId, ct).ConfigureAwait(false);
            EnsureSuccess(ack);

            _sessionId = sessionId;
            _initialized = true;
        }
        finally
        {
            _handshakeGate.Release();
        }
    }

    private async Task<JsonNode> SendAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        // Retries cover the endpoint throttling a long update run, not application errors.
        var delay = TimeSpan.FromMilliseconds(400);
        var sessionRetried = false;

        for (var attempt = 0; ; attempt++)
        {
            // Captured per attempt: with up to ten calls in flight during an update, the
            // invalidation below must be able to tell "MY session died" from "somebody already
            // replaced it".
            var usedSession = _sessionId;
            using var response = await PostAsyncWithSession(method, parameters, isNotification: false, usedSession, ct).ConfigureAwait(false);

            if (IsTransient(response.StatusCode) && attempt < 3)
            {
                // The server's own Retry-After wins over the blind backoff — at the update run's
                // ten calls in parallel, respecting the throttle is what keeps that safe.
                await Task.Delay(RetryAfterOrDefault(response, delay), ct).ConfigureAwait(false);
                delay *= 2;
                continue;
            }

            // A session can expire mid-run; the server answers 404/400 then. One fresh handshake
            // rescues the remaining calls instead of failing all of them. Invalidate only when
            // OUR session is still the current one — a parallel call's stale 404 arriving after
            // the re-handshake must not tear down the freshly created session.
            if (!sessionRetried
                && _initialized
                && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                sessionRetried = true;

                if (usedSession is not null
                    && Interlocked.CompareExchange(ref _sessionId, null, usedSession) == usedSession)
                {
                    _initialized = false;
                }

                await EnsureSessionAsync(ct).ConfigureAwait(false);
                continue;
            }

            EnsureSuccess(response);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ReadResult(body, WasEventStream(response));
        }
    }

    private Task<HttpResponseMessage> PostAsync(string method, JsonObject? parameters, bool isNotification, CancellationToken ct)
        => PostAsyncWithSession(method, parameters, isNotification, _sessionId, ct);

    private async Task<HttpResponseMessage> PostAsyncWithSession(
        string method, JsonObject? parameters, bool isNotification, string? sessionId, CancellationToken ct)
    {
        var envelope = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };

        if (!isNotification)
        {
            // Interlocked: the snapshot builder runs up to three calls in parallel, and duplicate
            // JSON-RPC ids let a server pair answers with the wrong request.
            envelope["id"] = Interlocked.Increment(ref _nextId);
        }

        if (parameters is not null)
            envelope["params"] = parameters;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        if (sessionId is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);

        if (!isNotification)
            Interlocked.Increment(ref _callCount);

        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>SSE detection by the header, not by sniffing the body's first bytes.</summary>
    private static bool WasEventStream(HttpResponseMessage response)
        => string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

    /// <summary>The server's requested wait when it sent one (capped at 30 s), else the fallback.</summary>
    private static TimeSpan RetryAfterOrDefault(HttpResponseMessage response, TimeSpan fallback)
    {
        var retryAfter = response.Headers.RetryAfter;

        var wait = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (wait is not { TotalSeconds: > 0 } positive)
            return fallback;

        return positive > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : positive;
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new OpGgApiException($"OP.GG antwortete mit HTTP {(int)response.StatusCode} {response.ReasonPhrase}.")
            {
                Status = response.StatusCode,
            };
        }
    }

    /// <summary>
    /// Reads the JSON-RPC result out of either a plain JSON body or a server-sent-event stream.
    /// The endpoint currently answers with JSON, but it advertises both.
    /// </summary>
    private static JsonNode ReadResult(string body, bool isEventStream)
    {
        var json = isEventStream || LooksLikeEventStream(body) ? ExtractEventStreamPayload(body) : body;

        var node = JsonNode.Parse(json)
            ?? throw new OpGgApiException("Antwort war kein JSON.");

        if (node["error"] is { } error)
            throw new OpGgApiException($"OP.GG lehnte die Anfrage ab: {error["message"]?.ToString() ?? error.ToJsonString()}");

        return node["result"] ?? throw new OpGgApiException("Antwort enthielt kein result.");
    }

    private static bool LooksLikeEventStream(string body)
        => body.StartsWith("event:", StringComparison.Ordinal) || body.StartsWith("data:", StringComparison.Ordinal);

    private static string ExtractEventStreamPayload(string body)
    {
        var payload = new StringBuilder();

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
                payload.Append(line["data:".Length..].Trim());
        }

        if (payload.Length == 0)
            throw new OpGgApiException("Ereignisstrom enthielt keine Daten.");

        return payload.ToString();
    }

    /// <summary>Concatenates the text parts of an MCP tool result.</summary>
    private static string ExtractText(JsonNode result)
    {
        if (result["content"] is not JsonArray content)
            throw new OpGgApiException("Werkzeugantwort enthielt keinen Inhalt.");

        var text = new StringBuilder();

        foreach (var part in content)
        {
            if (part?["type"]?.GetValue<string>() != "text")
                continue;

            text.Append(part["text"]?.GetValue<string>());
        }

        if (text.Length == 0)
            throw new OpGgApiException("Werkzeugantwort enthielt keinen Text.");

        return text.ToString();
    }

    public void Dispose()
    {
        // Only what we created: disposing an injected HttpClient would break its other users
        // (the snapshot builder shares one with the asset downloaders).
        if (_ownsHttp)
            _http.Dispose();

        _handshakeGate.Dispose();
    }
}
