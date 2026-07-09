using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Tokens;

/// <summary>
/// Phase 1 token provider: one token from <c>Toggl:ApiToken</c> (env
/// <c>TOGGL__APITOKEN</c>), same user for every request. Replaced by a
/// vault-backed, per-<c>sub</c> provider in Phase 3.
/// </summary>
public sealed class EnvTogglTokenProvider(IOptions<TogglOptions> options) : ITogglTokenProvider
{
    public string UserKey => "default";

    public ValueTask<string> GetApiTokenAsync(CancellationToken ct = default)
    {
        var token = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "No Toggl token configured. Set Toggl:ApiToken (env TOGGL__APITOKEN) — the raw account token, not a toggl_sk_ key.");
        return ValueTask.FromResult(token);
    }
}
