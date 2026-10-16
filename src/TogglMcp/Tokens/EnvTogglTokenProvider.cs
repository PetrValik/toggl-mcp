using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Tokens;

/// <summary>
/// Token provider: one token from <c>Toggl:ApiToken</c> (env <c>TOGGL__APITOKEN</c>),
/// shared by every request. The per-user <see cref="UserKey"/> is still derived from the
/// authenticated <c>sub</c>, so plan + mapping-rule isolation is real the moment auth is on.
/// A vault-backed, per-<c>sub</c> provider is the unbuilt roadmap Phase 3.
/// </summary>
public sealed class EnvTogglTokenProvider(IOptions<TogglOptions> options, IHttpContextAccessor http) : ITogglTokenProvider
{
    /// <summary>
    /// Per-user identity: the authenticated OIDC <c>sub</c> when auth is on, else <c>"default"</c>
    /// (auth off / local). Isolates plans and mapping rules per principal; the Toggl account token
    /// itself is still the single shared env token.
    /// </summary>
    public string UserKey =>
        http.HttpContext?.User.FindFirst("sub")?.Value
        ?? http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "default";

    /// <summary>
    /// Returns the configured account token, or throws if none is set — the message steers the operator
    /// to the raw account token rather than a <c>toggl_sk_</c> API key (which Basic auth rejects).
    /// </summary>
    public ValueTask<string> GetApiTokenAsync(CancellationToken ct = default)
    {
        var token = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "No Toggl token configured. Set Toggl:ApiToken (env TOGGL__APITOKEN) — the raw account token, not a toggl_sk_ key.");
        return ValueTask.FromResult(token);
    }
}
