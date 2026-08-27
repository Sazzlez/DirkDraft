using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DraftPilot.Core.Lcu;

/// <summary>One push notification from the client.</summary>
/// <param name="Uri">The LCU resource that changed, e.g. <c>/lol-champ-select/v1/session</c>.</param>
/// <param name="EventType"><c>Create</c>, <c>Update</c> or <c>Delete</c>.</param>
/// <param name="Data">The payload as raw JSON, or <see langword="null"/> for a delete.</param>
public sealed record LcuEvent(string Uri, string EventType, string? Data);

/// <summary>
/// Subscribes to the client event socket. This is what keeps the tool at roughly zero idle CPU:
/// the client pushes champion select changes instead of us polling for them.
/// </summary>
public sealed class LcuEventSocket : IAsyncDisposable
{
    /// <summary>WAMP opcode the client uses for subscribing to an event.</summary>
    private const int SubscribeOpcode = 5;

    /// <summary>WAMP opcode the client uses when delivering an event.</summary>
    private const int EventOpcode = 8;

    private static readonly string[] Subscriptions =
    [
        "OnJsonApiEvent_lol-champ-select_v1_session",
        "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",
    ];

    private readonly LcuCredentials _credentials;
    private ClientWebSocket? _socket;

    /// <summary>Consecutive subscriber failures; only the receive loop's thread touches this.</summary>
    private int _handlerFailures;

    /// <summary>Whether at least one frame was handed to subscribers on the current link.</summary>
    private bool _frameHandled;

    public LcuEventSocket(LcuCredentials credentials) => _credentials = credentials;

    /// <summary>Raised for every subscribed event, on the receive loop's thread.</summary>
    public event Action<LcuEvent>? EventReceived;

    /// <summary>Raised with <see langword="true"/> once subscribed, <see langword="false"/> when the link drops.</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>
    /// Connects, subscribes and pumps events until cancelled, reconnecting with backoff if the
    /// client drops the link. Returns only when <paramref name="ct"/> fires.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // A link has to live this long before the backoff resets — a client that accepts and
        // immediately drops the connection must not be hammered twice a second forever.
        const long StableLinkMilliseconds = 5_000;

        var backoff = TimeSpan.FromMilliseconds(500);

        while (!ct.IsCancellationRequested)
        {
            var connectedAt = 0L;
            _frameHandled = false;

            try
            {
                await ConnectAndPumpAsync(ct, () => connectedAt = Environment.TickCount64).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown or teardown by the owner: no drop announcement — the owner already
                // knows, and a late "disconnected" only made the status flap back after Offline.
                return;
            }
            catch (Exception)
            {
                // Deliberately everything, not just the network types: an exception escaping a
                // subscriber used to end this loop for good while the status kept saying
                // "connected". Any failure now reads as a dropped link and reconnects.
            }

            // One place announces the drop, whatever path got us here — the pump returning, a
            // network error, or a subscriber giving up.
            if (connectedAt != 0)
                ConnectionChanged?.Invoke(false);

            if (ct.IsCancellationRequested)
                return;

            // The backoff resets only when the link both lived a while AND delivered a frame the
            // subscribers digested — a deterministic handler failure must not turn into a
            // half-second reconnect-and-reseed loop.
            if (connectedAt != 0 && _frameHandled && Environment.TickCount64 - connectedAt >= StableLinkMilliseconds)
                backoff = TimeSpan.FromMilliseconds(500);

            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled during the backoff; a caller awaiting RunAsync must not see a fault.
                return;
            }

            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 5_000));
        }
    }

    private async Task ConnectAndPumpAsync(CancellationToken ct, Action onConnected)
    {
        using var socket = new ClientWebSocket();
        Volatile.Write(ref _socket, socket);

        try
        {
            socket.Options.AddSubProtocol("wamp");
            socket.Options.RemoteCertificateValidationCallback = RiotCertificate.ValidateSocket;

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_credentials.Password}"));
            socket.Options.SetRequestHeader("Authorization", $"Basic {token}");

            await socket.ConnectAsync(_credentials.WebSocketUri, ct).ConfigureAwait(false);

            foreach (var subscription in Subscriptions)
            {
                var frame = Encoding.UTF8.GetBytes($"[{SubscribeOpcode},\"{subscription}\"]");
                await socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
            }

            onConnected();
            _handlerFailures = 0;
            ConnectionChanged?.Invoke(true);

            await PumpAsync(socket, ct).ConfigureAwait(false);
        }
        finally
        {
            // Otherwise the field keeps pointing at this disposed socket — and a reconnect racing
            // DisposeAsync could have its brand-new socket torn down by mistake.
            Interlocked.CompareExchange(ref _socket, null, socket);
        }
    }

    private async Task PumpAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var rent = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            var message = new ArrayBufferWriter<byte>(16 * 1024);

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                message.Clear();

                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(rent), ct).ConfigureAwait(false);

                    // The reconnect loop announces the drop; announcing it here too would double
                    // every "getrennt" downstream.
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    message.Write(rent.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                // The client sends an empty frame as an ack for each subscription.
                if (message.WrittenCount == 0)
                    continue;

                Dispatch(message.WrittenSpan);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rent);
        }
    }

    /// <summary>
    /// Parses the WAMP envelope <c>[8, "OnJsonApiEvent_...", { uri, eventType, data }]</c>.
    /// Anything that does not match is ignored rather than throwing: the socket carries frames we
    /// never subscribed to, and a malformed one must not kill the loop mid-draft.
    /// </summary>
    private void Dispatch(ReadOnlySpan<byte> payload)
    {
        LcuEvent? parsed;

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
                return;

            if (root[0].ValueKind != JsonValueKind.Number || root[0].GetInt32() != EventOpcode)
                return;

            var body = root[2];
            if (body.ValueKind != JsonValueKind.Object)
                return;

            var uri = body.TryGetProperty("uri", out var uriElement) ? uriElement.GetString() : null;
            if (string.IsNullOrEmpty(uri))
                return;

            var eventType = body.TryGetProperty("eventType", out var typeElement)
                ? typeElement.GetString() ?? "Update"
                : "Update";

            var data = body.TryGetProperty("data", out var dataElement) && dataElement.ValueKind is not JsonValueKind.Null
                ? dataElement.GetRawText()
                : null;

            parsed = new LcuEvent(uri, eventType, data);
        }
        catch (JsonException)
        {
            // Not JSON we understand; drop the frame.
            return;
        }

        // Outside the parse-try on purpose: a JsonException thrown by a SUBSCRIBER must not be
        // mistaken for a malformed frame and silently dropped.
        try
        {
            EventReceived?.Invoke(parsed);
            _handlerFailures = 0;
            _frameHandled = true;
        }
        catch (Exception) when (++_handlerFailures < 10)
        {
            // One bad payload must not tear down a healthy link (that turned into a reconnect
            // loop). Ten failures in a row mean the subscriber is broken for good — then the
            // exception escapes to the reconnect loop, which treats it as a dropped link.
        }
    }

    public async ValueTask DisposeAsync()
    {
        var socket = Interlocked.Exchange(ref _socket, null);

        if (socket is null)
            return;

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Best effort; the process is going away anyway.
            }
        }

        socket.Dispose();
    }
}
