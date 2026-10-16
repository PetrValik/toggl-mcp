namespace TogglMcp.Tokens;

/// <summary>
/// Resolves the Toggl API token for the current request's user, and a stable key
/// identifying that user (for per-user mapping rules / vault lookups).
///
/// The token is NEVER accepted as a tool argument (MCP forbids token passthrough;
/// tool args transit the model). Today: a single token from env. Roadmap Phase 3 (not built) =
/// an encrypted vault keyed by the OIDC <c>sub</c>, registered out-of-band via
/// the /settings page.
/// </summary>
public interface ITogglTokenProvider
{
    /// <summary>Stable per-user identity key. <c>"default"</c> with the env token.</summary>
    string UserKey { get; }

    /// <summary>The raw Toggl account token (Basic-auth username). Throws if unset.</summary>
    ValueTask<string> GetApiTokenAsync(CancellationToken ct = default);
}
