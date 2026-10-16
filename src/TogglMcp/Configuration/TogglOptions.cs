namespace TogglMcp.Configuration;

/// <summary>
/// Toggl Track API v9 settings. The API token comes from the
/// environment via <see cref="ApiToken"/> (<c>TOGGL__APITOKEN</c>). A per-user encrypted token
/// vault (roadmap Phase 3, never built) would resolve it per user instead and ignore this value.
/// </summary>
public sealed class TogglOptions
{
    /// <summary>Configuration section these options bind from (appsettings / env).</summary>
    public const string SectionName = "Toggl";

    /// <summary>Base URL of the Toggl Track API. No trailing slash.</summary>
    public string BaseUrl { get; set; } = "https://api.track.toggl.com";

    /// <summary>
    /// Single-user API token (the raw account token, NOT a <c>toggl_sk_</c>
    /// key — that belongs to a different product). Used as the Basic-auth *username*
    /// with the literal password <c>api_token</c>. Required: this is the only token source that exists.
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Optional workspace id override. When null it is discovered once from
    /// <c>/api/v9/me</c> (default_workspace_id) and cached per token to stay under
    /// the 30/hr <c>/me</c> quota.
    /// </summary>
    public long? WorkspaceId { get; set; }

    /// <summary>Value sent as <c>created_with</c> on every created time entry.</summary>
    public string CreatedWith { get; set; } = "toggl-mcp";

    /// <summary>
    /// Minimum spacing between outbound Toggl requests. Toggl runs a ~1 req/s
    /// leaky bucket; a little over one second keeps us clear of 429s.
    /// </summary>
    public int MinRequestIntervalMs { get; set; } = 1100;

    /// <summary>Max retry attempts on 429/5xx before giving up (402 fails fast).</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Base backoff (doubled each retry, capped at 30s). Toggl sends no Retry-After.</summary>
    public int RetryBaseDelayMs { get; set; } = 1500;
}
