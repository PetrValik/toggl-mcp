namespace TogglMcp.Configuration;

/// <summary>
/// OAuth 2.1 resource-server settings. Authentik is the authorization server; this
/// service validates the JWT it issues. Disabled by default so Phase 1 can run
/// locally (single env token, no login). Enable once the connector is wired.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section these options bind from (appsettings / env).</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// When false the MCP endpoint is open (no JWT required) — for local Phase 1
    /// only. MUST be true for any publicly reachable deployment.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Authentik issuer / OIDC authority, e.g.
    /// <c>https://auth.irminsul.uk/application/o/toggl-mcp/</c>. Used as the JWT
    /// <c>iss</c> and to discover JWKS.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// The public MCP resource URL that must appear in the token <c>aud</c>, e.g.
    /// <c>https://toggl.irminsul.uk/mcp</c>. This is both the JWT audience and the
    /// <c>resource</c> in the Protected Resource Metadata — they must byte-match.
    /// </summary>
    public string? ResourceUrl { get; set; }

    /// <summary>Require HTTPS for OIDC metadata. Only turn off for local testing.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Scopes advertised in the Protected Resource Metadata.</summary>
    public string[] Scopes { get; set; } = ["openid"];

    /// <summary>
    /// Optional: require this scope on the access token (checked against the space-delimited
    /// <c>scope</c>/<c>scp</c> claim). Null/empty = any authenticated principal is allowed.
    /// </summary>
    public string? RequiredScope { get; set; }

    /// <summary>
    /// Optional allowlist of Authentik <c>sub</c> values permitted to use the server. Empty =
    /// any authenticated principal is allowed. Set before opening the app to multiple users.
    /// </summary>
    public string[] AllowedSubjects { get; set; } = [];
}
