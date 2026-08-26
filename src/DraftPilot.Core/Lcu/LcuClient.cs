using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Read-only HTTP access to the local League client. Every call is a GET; the tool never
/// writes to the client.
/// </summary>
public sealed class LcuClient : IDisposable
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

    /// <summary>The current champion select session, or <see langword="null"/> when there is none.</summary>
    public Task<ChampSelectSession?> GetChampSelectSessionAsync(CancellationToken ct = default)
        => GetAsync("/lol-champ-select/v1/session", LcuJson.Default.ChampSelectSession, ct);

    /// <summary>
    /// The session as raw JSON. Used to seed state on connect and to record fixtures verbatim.
    /// </summary>
    public Task<string?> GetChampSelectSessionRawAsync(CancellationToken ct = default)
        => GetRawAsync("/lol-champ-select/v1/session", ct);

    /// <summary>Champions the local player may pick this game (ownership and rotation applied).</summary>
    public async Task<HashSet<int>> GetPickableChampionIdsAsync(CancellationToken ct = default)
        => [.. await GetAsync("/lol-champ-select/v1/pickable-champion-ids", LcuJson.Default.ListInt32, ct) ?? []];

    /// <summary>Champions the local player may ban this game.</summary>
    public async Task<HashSet<int>> GetBannableChampionIdsAsync(CancellationToken ct = default)
        => [.. await GetAsync("/lol-champ-select/v1/bannable-champion-ids", LcuJson.Default.ListInt32, ct) ?? []];

    /// <summary>Raw gameflow phase, e.g. <c>ChampSelect</c>, <c>Lobby</c>, <c>InProgress</c>.</summary>
    public async Task<string?> GetGameflowPhaseAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync("/lol-gameflow/v1/gameflow-phase", ct);
        return raw?.Trim('"');
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
                return null;

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Client shutting down, or the port went away between lockfile read and request.
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
