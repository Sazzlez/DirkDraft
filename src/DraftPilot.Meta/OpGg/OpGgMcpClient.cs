using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DraftPilot.Meta.OpGg;

/// <summary>The endpoint answered, but with an error.</summary>
public sealed class OpGgApiException(string message) : Exception(message);

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
    private readonly SemaphoreSlim _handshakeGate = new(1, 1);
    private string? _sessionId;
    private int _nextId = 1;

    public OpGgMcpClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DraftPilot/0.1 (+local champion select assistant)");
    }

    /// <summary>How many calls have been sent; surfaced in the update progress display.</summary>
    public int CallCount { get; private set; }

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
        if (_sessionId is not null)
            return;

        await _handshakeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_sessionId is not null)
                return;

            var initialize = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "DraftPilot", ["version"] = "0.1" },
            };

            using var response = await PostAsync("initialize", initialize, isNotification: false, ct).ConfigureAwait(false);
            EnsureSuccess(response);

            if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
                _sessionId = values.FirstOrDefault();

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ReadResult(body);

            // The server expects the initialized notification before it accepts tool calls.
            using var ack = await PostAsync("notifications/initialized", null, isNotification: true, ct).ConfigureAwait(false);
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

        for (var attempt = 0; ; attempt++)
        {
            using var response = await PostAsync(method, parameters, isNotification: false, ct).ConfigureAwait(false);

            if (IsTransient(response.StatusCode) && attempt < 3)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay *= 2;
                continue;
            }

            EnsureSuccess(response);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ReadResult(body);
        }
    }

    private Task<HttpResponseMessage> PostAsync(string method, JsonObject? parameters, bool isNotification, CancellationToken ct)
    {
        var envelope = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };

        if (!isNotification)
            envelope["id"] = _nextId++;

        if (parameters is not null)
            envelope["params"] = parameters;

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        if (_sessionId is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

        if (!isNotification)
            CallCount++;

        return _http.SendAsync(request, ct);
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
            throw new OpGgApiException($"OP.GG antwortete mit HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    /// <summary>
    /// Reads the JSON-RPC result out of either a plain JSON body or a server-sent-event stream.
    /// The endpoint currently answers with JSON, but it advertises both.
    /// </summary>
    private static JsonNode ReadResult(string body)
    {
        var json = LooksLikeEventStream(body) ? ExtractEventStreamPayload(body) : body;

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
        _http.Dispose();
        _handshakeGate.Dispose();
    }
}
