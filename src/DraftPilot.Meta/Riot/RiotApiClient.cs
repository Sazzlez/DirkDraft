using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace DraftPilot.Meta.Riot;

/// <summary>The Riot API refused in a way waiting does not fix: a bad or expired key, or a broken request.</summary>
public sealed class RiotApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Talks to the Riot API with one key and keeps to its limits by itself. Limits are per host —
/// <c>euw1</c> for the league lists and <c>europe</c> for matches count separately — and come from
/// the answers' own <c>X-App-Rate-Limit</c> and <c>X-Method-Rate-Limit</c> headers, so a key with
/// other limits needs no change here. A 429 is waited out as long as <c>Retry-After</c> says, which
/// Riot asks for explicitly: callers who keep hammering get their key disabled.
/// <para>
/// The key is sent as a header on every request and nowhere else: never in a URL (URLs end up in
/// logs), never in an exception message.
/// </para>
/// </summary>
public sealed class RiotApiClient : IDisposable
{
    /// <summary>A personal key's limits, used until the first answer states its own.</summary>
    private static readonly (int Count, TimeSpan Window)[] DefaultLimits =
        [(20, TimeSpan.FromSeconds(1)), (100, TimeSpan.FromSeconds(120))];

    private const int MaxAttempts = 6;

    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Dictionary<string, Limiter> _limiters = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RiotApiClient(
        string apiKey,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Kein Riot-API-Schlüssel.", nameof(apiKey));

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("X-Riot-Token", apiKey.Trim());
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DirkDraft-Collector", "1.0"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Requests sent, including retries.</summary>
    public int Requests { get; private set; }

    /// <summary>How often a 429 had to be waited out. Should stay near zero; the limiter is meant to prevent them.</summary>
    public int Throttled { get; private set; }

    /// <summary>
    /// GETs <paramref name="pathAndQuery"/> from <paramref name="host"/> (e.g. <c>europe</c>) and
    /// returns the body, or null for a 404 — "no such match" is an answer, not a failure.
    /// </summary>
    public async Task<string?> GetAsync(string host, string pathAndQuery, CancellationToken ct)
    {
        var url = $"https://{host}.api.riotgames.com{pathAndQuery}";

        for (var attempt = 1; ; attempt++)
        {
            await WaitForSlotAsync(host, pathAndQuery, ct).ConfigureAwait(false);

            HttpResponseMessage response;
            try
            {
                Requests++;
                response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested)) && attempt < MaxAttempts)
            {
                await _delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                Learn(host, pathAndQuery, response.Headers);

                switch ((int)response.StatusCode)
                {
                    case 200:
                        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    case 404:
                        return null;

                    case 429 when attempt < MaxAttempts:
                        Throttled++;
                        await _delay(RetryAfter(response) ?? Backoff(attempt), ct).ConfigureAwait(false);
                        continue;

                    case >= 500 when attempt < MaxAttempts:
                        await _delay(Backoff(attempt), ct).ConfigureAwait(false);
                        continue;

                    case 401 or 403:
                        throw new RiotApiException(
                            $"Riot lehnt den Schlüssel ab ({(int)response.StatusCode}) — abgelaufen, gesperrt oder falsch eingetragen.",
                            response.StatusCode);

                    default:
                        throw new RiotApiException($"Riot antwortet {(int)response.StatusCode} auf {Describe(pathAndQuery)}.", response.StatusCode);
                }
            }
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    /// <summary>Waits until every limit of the host — and of this endpoint — has room for one more request.</summary>
    private async Task WaitForSlotAsync(string host, string pathAndQuery, CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var now = _clock();
                var app = LimiterFor(host);
                var method = LimiterFor(MethodKey(host, pathAndQuery), methodScope: true);

                wait = Max(app.WaitTime(now), method.WaitTime(now));
                if (wait <= TimeSpan.Zero)
                {
                    app.Record(now);
                    method.Record(now);
                    return;
                }
            }
            finally
            {
                _gate.Release();
            }

            await _delay(wait, ct).ConfigureAwait(false);
        }
    }

    private Limiter LimiterFor(string key, bool methodScope = false)
    {
        if (!_limiters.TryGetValue(key, out var limiter))
            _limiters[key] = limiter = new Limiter(methodScope ? [] : DefaultLimits);

        return limiter;
    }

    private void Learn(string host, string pathAndQuery, HttpResponseHeaders headers)
    {
        if (ParseLimits(headers, "X-App-Rate-Limit") is { Count: > 0 } app)
            LimiterFor(host).Limits = app;

        if (ParseLimits(headers, "X-Method-Rate-Limit") is { Count: > 0 } method)
            LimiterFor(MethodKey(host, pathAndQuery), methodScope: true).Limits = method;
    }

    /// <summary>"100:120,20:1" — a hundred per two minutes and twenty per second.</summary>
    public static List<(int Count, TimeSpan Window)> ParseLimits(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
            return [];

        var limits = new List<(int, TimeSpan)>();
        foreach (var part in string.Join(',', values).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2
                && int.TryParse(pieces[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                && int.TryParse(pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                && count > 0 && seconds > 0)
            {
                limits.Add((count, TimeSpan.FromSeconds(seconds)));
            }
        }

        return limits;
    }

    /// <summary>
    /// The endpoint a request belongs to, for its method limit: the path with every id replaced, so
    /// all match lookups share one limiter and all league pages another.
    /// </summary>
    public static string MethodKey(string host, string pathAndQuery)
    {
        var path = pathAndQuery.Split('?')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // /lol/match/v5/matches/{id}[/timeline], /lol/match/v5/matches/by-puuid/{puuid}/ids,
        // /lol/league/v4/entries/{queue}/{tier}/{division}
        var shape = segments.Length >= 4 && segments[1] == "league"
            ? string.Join('/', segments.Take(4))
            : string.Join('/', segments.Select((segment, index) => index >= 4 && segment is not ("timeline" or "ids" or "by-puuid") ? "*" : segment));

        return $"{host}:{shape}";
    }

    private static string Describe(string pathAndQuery) => MethodKey(string.Empty, pathAndQuery).TrimStart(':');

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
        => response.Headers.RetryAfter?.Delta is { } delta ? delta + TimeSpan.FromMilliseconds(250) : null;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>A sliding window per limit; the request times of the longest window are kept.</summary>
    private sealed class Limiter(IEnumerable<(int Count, TimeSpan Window)> limits)
    {
        /// <summary>Kept below the stated count: Riot's windows and ours do not start at the same instant.</summary>
        private const int Margin = 1;

        private readonly Queue<DateTimeOffset> _sent = new();

        public List<(int Count, TimeSpan Window)> Limits { get; set; } = [.. limits];

        public TimeSpan WaitTime(DateTimeOffset now)
        {
            var longest = Limits.Count == 0 ? TimeSpan.Zero : Limits.Max(limit => limit.Window);
            while (_sent.Count > 0 && now - _sent.Peek() >= longest)
                _sent.Dequeue();

            var wait = TimeSpan.Zero;
            foreach (var (count, window) in Limits)
            {
                var allowed = Math.Max(1, count - Margin);
                var inWindow = _sent.Where(time => now - time < window).ToList();
                if (inWindow.Count < allowed)
                    continue;

                // The request that has to leave the window before one more fits.
                var oldest = inWindow[inWindow.Count - allowed];
                var until = oldest + window - now + TimeSpan.FromMilliseconds(50);
                if (until > wait)
                    wait = until;
            }

            return wait;
        }

        public void Record(DateTimeOffset now) => _sent.Enqueue(now);
    }
}
