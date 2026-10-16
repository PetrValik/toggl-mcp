namespace TogglMcp.Configuration;

/// <summary>
/// OAuth 2.1 resource-server settings for the hosted HTTP mode. The OIDC provider (Authentik in
/// the tested setup) is the authorization server; this service validates the JWT it issues.
/// Enabled by default; the app refuses to start with it off outside the Development environment.
/// Not used in <c>--stdio</c> mode, where the process boundary is the trust boundary.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section these options bind from (appsettings / env).</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// When false the MCP endpoint is open (no JWT required) — for local development
    /// only. MUST be true for any publicly reachable deployment.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// OIDC issuer / authority of your provider, e.g.
    /// <c>https://auth.example.com/application/o/toggl-mcp/</c> (Authentik style). Used as the JWT
    /// <c>iss</c> and to discover JWKS.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// The public MCP resource URL that must appear in the token <c>aud</c>, e.g.
    /// <c>https://toggl-mcp.example.com/mcp</c>. This is both the JWT audience and the
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
    /// Optional allowlist of OIDC <c>sub</c> values permitted to use the server. Empty =
    /// any authenticated principal is allowed. Set before opening the app to multiple users.
    /// </summary>
    public string[] AllowedSubjects { get; set; } = [];
}
