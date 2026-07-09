namespace TogglMcp.Tokens;

/// <summary>
/// Resolves the Toggl API token for the current request's user, and a stable key
/// identifying that user (for per-user mapping rules / vault lookups).
///
/// The token is NEVER accepted as a tool argument (MCP forbids token passthrough;
/// tool args transit the model). Phase 1 = single token from env. Phase 3 = the
/// encrypted vault keyed by the Authentik <c>sub</c>, registered out-of-band via
/// the /settings page.
/// </summary>
public interface ITogglTokenProvider
{
    /// <summary>Stable per-user identity key. <c>"default"</c> in Phase 1 (env token).</summary>
    string UserKey { get; }

    /// <summary>The raw Toggl account token (Basic-auth username). Throws if unset.</summary>
    ValueTask<string> GetApiTokenAsync(CancellationToken ct = default);
}
