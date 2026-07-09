using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;
using TogglMcp.Tokens;

namespace TogglMcp.Toggl;

public sealed class TogglClient : ITogglClient
{
    // Toggl JSON is snake_case in both directions. Null fields omitted so "No Project" stays valid.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // token/user -> default workspace id, single-flight. Static so it survives the transient
    // typed-client lifetime; a Lazy<Task<>> ensures one cold-miss issues /me while the rest
    // await the same call (conserves the 30/hr /me quota).
    private static readonly ConcurrentDictionary<string, Lazy<Task<long>>> WsCache = new();

    private readonly HttpClient _http;
    private readonly ITogglTokenProvider _tokens;
    private readonly TogglThrottle _throttle;
    private readonly TogglOptions _options;
    private readonly ILogger<TogglClient> _logger;
    private readonly string _baseUrl;

    public TogglClient(
        HttpClient http,
        ITogglTokenProvider tokens,
        TogglThrottle throttle,
        IOptions<TogglOptions> options,
        ILogger<TogglClient> logger)
    {
        _http = http;
        _tokens = tokens;
        _throttle = throttle;
        _options = options.Value;
        _logger = logger;
        _baseUrl = _options.BaseUrl.TrimEnd('/');
    }

    public Task<long> GetWorkspaceIdAsync(CancellationToken ct = default)
    {
        if (_options.WorkspaceId is { } configured) return Task.FromResult(configured);
        var key = _tokens.UserKey;
        // Single-flight: first caller runs the fetch; concurrent callers await the same task.
        return WsCache.GetOrAdd(key, _ => new Lazy<Task<long>>(() => FetchWorkspaceIdAsync(key))).Value;
    }

    private async Task<long> FetchWorkspaceIdAsync(string key)
    {
        try
        {
            // Don't thread a caller's CancellationToken into the shared fetch — one caller
            // cancelling must not fail the others awaiting the same task.
            var me = await GetMeAsync(CancellationToken.None).ConfigureAwait(false);
            return me.DefaultWorkspaceId;
        }
        catch
        {
            WsCache.TryRemove(key, out _); // evict the faulted task so the next call retries
            throw;
        }
    }

    public Task<TogglMe> GetMeAsync(CancellationToken ct = default)
        => GetJsonAsync<TogglMe>($"{_baseUrl}/api/v9/me", ct);

    public async Task<IReadOnlyList<TogglProject>> GetProjectsAsync(CancellationToken ct = default)
    {
        var wid = await GetWorkspaceIdAsync(ct).ConfigureAwait(false);
        return await GetJsonAsync<List<TogglProject>>($"{_baseUrl}/api/v9/workspaces/{wid}/projects?active=true", ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TogglTask>> GetTasksAsync(long projectId, CancellationToken ct = default)
    {
        var wid = await GetWorkspaceIdAsync(ct).ConfigureAwait(false);
        return await GetJsonAsync<List<TogglTask>>($"{_baseUrl}/api/v9/workspaces/{wid}/projects/{projectId}/tasks", ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/v9/me/time_entries" +
                  $"?start_date={fromInclusive:yyyy-MM-dd}&end_date={toExclusive:yyyy-MM-dd}";
        return await GetJsonAsync<List<TogglTimeEntry>>(url, ct).ConfigureAwait(false);
    }

    public async Task<TogglTimeEntry> CreateTimeEntryAsync(CreateTimeEntryRequest request, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/v9/workspaces/{request.WorkspaceId}/time_entries";
        // Toggl has no idempotency key: a 5xx/timeout may mean the entry WAS persisted.
        // Retrying then would duplicate it, so retry the POST only on 429/402 (request
        // rejected before processing), never on server errors.
        using var resp = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(request, options: Json) },
            ct, retryOnServerErrors: false).ConfigureAwait(false);
        var entry = await resp.Content.ReadFromJsonAsync<TogglTimeEntry>(Json, ct).ConfigureAwait(false);
        return entry ?? throw new TogglException(resp.StatusCode, "Toggl returned an empty body for the created entry.");
    }

    // Helpers ------------------------------------------------------------------

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);
        var result = await resp.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        return result ?? throw new TogglException(resp.StatusCode, $"Toggl returned an empty body for {url}.");
    }

    /// <summary>
    /// Sends with the current user's Basic-auth header, spacing requests via the
    /// throttle and retrying with exponential backoff. 429/402 (request rejected before
    /// processing) are always retried; 408/5xx are retried only when
    /// <paramref name="retryOnServerErrors"/> is true — set it false for non-idempotent
    /// writes, where a persisted-then-failed response must not be re-sent. The request is
    /// rebuilt from <paramref name="factory"/> each attempt so POST content is resendable.
    /// Caller owns/disposes the returned response.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct, bool retryOnServerErrors = true)
    {
        var token = await _tokens.GetApiTokenAsync(ct).ConfigureAwait(false);
        // Basic base64("<TOKEN>:api_token") — token is the USERNAME, literal "api_token" the password.
        var auth = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:api_token")));

        for (var attempt = 0; ; attempt++)
        {
            await _throttle.WaitTurnAsync(ct).ConfigureAwait(false);
            var req = factory();
            req.Headers.Authorization = auth;
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            finally
            {
                req.Dispose(); // release the request (and its content) even if the send throws
            }

            if (resp.IsSuccessStatusCode) return resp;

            var status = resp.StatusCode;
            var rateOrQuota = status is HttpStatusCode.TooManyRequests   // 429 leaky bucket
                or HttpStatusCode.PaymentRequired;                       // 402 hourly plan quota
            var serverError = status is HttpStatusCode.RequestTimeout || (int)status >= 500;
            var retryable = rateOrQuota || (retryOnServerErrors && serverError);

            if (retryable && attempt < _options.MaxRetries)
            {
                resp.Dispose();
                // Toggl sends no Retry-After → exponential backoff, capped at 30s.
                var ms = Math.Min(_options.RetryBaseDelayMs * Math.Pow(2, attempt), 30_000);
                _logger.LogWarning("Toggl {Status}; backing off {Delay:n0}ms (retry {Attempt}/{Max}).",
                    (int)status, ms, attempt + 1, _options.MaxRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(ms), ct).ConfigureAwait(false);
                continue;
            }

            var body = await SafeReadAsync(resp, ct).ConfigureAwait(false);
            resp.Dispose();
            throw new TogglException(status, $"Toggl API returned {(int)status} {status}. {body}");
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return body.Length > 500 ? body[..500] : body;
        }
        catch
        {
            return "(no body)";
        }
    }
}
