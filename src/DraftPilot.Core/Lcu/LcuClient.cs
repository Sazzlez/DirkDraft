using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// HTTP access to the local League client. Almost everything is a read; the one deliberate
/// exception is the rune-page import, whose writes live at the bottom of this class and follow
/// one rule: DraftPilot only ever deletes pages it created itself or the user agreed to lose.
/// </summary>
public sealed class LcuClient : IDisposable, IRunePageClient
{
    private readonly HttpClient _http;

    public LcuClient(LcuCredentials credentials)
    {
        Credentials = credentials;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = RiotCertificate.ValidateHttp,
            // Loopback only; a proxy would break the connection and leak the request off-machine.
            UseProxy = false,
            AllowAutoRedirect = false,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(credentials.HttpBase),
            Timeout = TimeSpan.FromSeconds(5),
        };

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{credentials.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public LcuCredentials Credentials { get; }

    /// <summary>
    /// Where unexpected answers go (auth failures, 5xx). Without this sink a stale password looks
    /// exactly like "no champion select right now": empty panel, status still "connected".
    /// </summary>
    public Action<string>? Diagnostic { get; set; }

    /// <summary>
    /// The session as raw JSON. Used to seed state on connect and to record fixtures verbatim.
    /// </summary>
    public Task<string?> GetChampSelectSessionRawAsync(CancellationToken ct = default)
        => GetRawAsync("/lol-champ-select/v1/session", ct);

    /// <summary>Champions the local player may pick this game (ownership and rotation applied).</summary>
    public async Task<HashSet<int>> GetPickableChampionIdsAsync(CancellationToken ct = default)
        => [.. await GetAsync("/lol-champ-select/v1/pickable-champion-ids", LcuJson.Default.ListInt32, ct) ?? []];

    /// <summary>Raw gameflow phase, e.g. <c>ChampSelect</c>, <c>Lobby</c>, <c>InProgress</c>.</summary>
    public async Task<string?> GetGameflowPhaseAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync("/lol-gameflow/v1/gameflow-phase", ct);
        return raw?.Trim('"');
    }

    // ----- Rune pages (the import feature's read and write surface) --------------------------

    /// <inheritdoc />
    public Task<List<LcuRunePage>?> GetRunePagesAsync(CancellationToken ct = default)
        => GetAsync("/lol-perks/v1/pages", LcuPerksJson.Default.ListLcuRunePage, ct);

    /// <inheritdoc />
    public async Task<int?> GetOwnedPageCountAsync(CancellationToken ct = default)
        => (await GetAsync("/lol-perks/v1/inventory", LcuPerksJson.Default.LcuPerksInventory, ct))?.OwnedPageCount;

    /// <inheritdoc />
    public async Task<(bool Ok, string? Error)> CreateRunePageAsync(LcuRunePageRequest page, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(page, LcuPerksJson.Default.LcuRunePageRequest);
        return await SendAsync(HttpMethod.Post, "/lol-perks/v1/pages", body, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<(bool Ok, string? Error)> DeleteRunePageAsync(long pageId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/lol-perks/v1/pages/{pageId}", body: null, ct);

    private async Task<(bool Ok, string? Error)> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return (true, null);

            // The client's error bodies name the actual reason ("Max pages reached", …).
            var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (false, $"{(int)response.StatusCode}: {Truncate(detail)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A caller-requested abort must stay an abort. Turned into (false, "task canceled")
            // it looked like the client rejecting the page — mid-import, after a delete.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ObjectDisposedException)
        {
            return (false, ex.Message);
        }
    }

    private static string Truncate(string text)
    {
        if (text.Length <= 200)
            return text;

        // Never cut through a surrogate pair; a half character makes the message unreadable.
        var cut = char.IsHighSurrogate(text[199]) ? 199 : 200;
        return text[..cut];
    }

    private async Task<T?> GetAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        var body = await GetRawAsync(path, ct);
        if (string.IsNullOrWhiteSpace(body))
            return default;

        try
        {
            return JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException)
        {
            // A shape change on Riot's side must not take the tool down mid-draft.
            return default;
        }
    }

    private async Task<string?> GetRawAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

            // 404 is the normal answer for "no champion select right now".
            if (response.StatusCode is HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                // 401 (stale password), 5xx: without a trace this is indistinguishable from
                // "nothing going on", and the panel just stays silently empty.
                Diagnostic?.Invoke($"LCU {path} → {(int)response.StatusCode}");
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Client shutting down, the port went away between lockfile read and request, or this
            // instance was disposed by a reconnect while a read was still in flight.
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
