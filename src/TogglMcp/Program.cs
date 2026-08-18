using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using TogglMcp.Configuration;
using TogglMcp.Features.ListProjects;
using TogglMcp.Lock;
using TogglMcp.Mapping;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

// Composition root: the only place slices are wired together. Binds options, registers the
// shared infra every slice couples through (Toggl client, lock policy, plan store, mapping rules,
// token provider, clock), stands up the MCP HTTP transport that auto-discovers the *Tool slices,
// and optionally gates /mcp behind the Authentik OAuth resource server.
var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<TogglOptions>(builder.Configuration.GetSection(TogglOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<LockOptions>(builder.Configuration.GetSection(LockOptions.SectionName));
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
const string mcpAuthPolicy = "mcp"; // authz policy applied to /mcp (+ /settings) when auth is enabled

// ── Application services ────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TogglThrottle>();
builder.Services.AddSingleton<WeekLockPolicy>();
builder.Services.AddSingleton<IPlanStore, InMemoryPlanStore>();             // ephemeral frozen plans (plan_week → commit_plan)
builder.Services.AddSingleton<ILedgerStore, FileLedgerStore>();            // durable "we already wrote this" record — replaces the old in-description marker
builder.Services.AddScoped<ITogglTokenProvider, EnvTogglTokenProvider>();   // Phase 3: swap for the vault-backed provider
builder.Services.AddSingleton<IMappingRuleStore, FileMappingRuleStore>();
builder.Services.AddHttpClient<ITogglClient, TogglClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("toggl-mcp/0.1");
})
// Never let the IHttpClientFactory logging handlers emit the Basic-auth Toggl token,
// even if HttpClient logging is raised to Trace/Debug.
.RedactLoggedHeaders(["Authorization"]);

// ── MCP server (Streamable HTTP) ─────────────────────────────────────────────────
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(ListProjectsTool).Assembly); // scans all Features/*/*Tool slices

// ── Auth: Authentik OAuth resource server (Phase 1 may run with Auth:Enabled=false) ─
if (auth.Enabled)
{
    if (string.IsNullOrWhiteSpace(auth.Authority) || string.IsNullOrWhiteSpace(auth.ResourceUrl))
        throw new InvalidOperationException("Auth:Enabled=true requires both Auth:Authority and Auth:ResourceUrl.");

    var resource = auth.ResourceUrl!;

    builder.Services.AddAuthentication(o =>
    {
        // McpAuthentication serves RFC 9728 Protected Resource Metadata + the 401 challenge;
        // JwtBearer validates the access token Claude presents.
        o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.Authority = auth.Authority;
        o.RequireHttpsMetadata = auth.RequireHttpsMetadata;
        o.TokenValidationParameters.ValidateAudience = true;
        // MCP audience must byte-match the resource; be tolerant of a trailing slash.
        o.TokenValidationParameters.ValidAudiences = [resource, resource.TrimEnd('/'), resource.TrimEnd('/') + "/"];
    })
    .AddMcp(o =>
    {
        var prm = new ProtectedResourceMetadata { Resource = resource };
        prm.AuthorizationServers.Add(auth.Authority!);
        prm.BearerMethodsSupported.Add("header");
        foreach (var scope in auth.Scopes) prm.ScopesSupported.Add(scope);
        o.ResourceMetadata = prm;
    });

    // Authorization policy for the MCP surface: always an authenticated principal, plus an
    // optional required scope and/or an allowlisted `sub` (both no-ops when unset, so Phase-1
    // single-user is unaffected while multi-user gating is one config change away).
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(mcpAuthPolicy, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (!string.IsNullOrWhiteSpace(auth.RequiredScope))
                policy.RequireAssertion(ctx => HasScope(ctx.User, auth.RequiredScope!));
            if (auth.AllowedSubjects.Length > 0)
                policy.RequireAssertion(ctx => SubjectOf(ctx.User) is { } sub && auth.AllowedSubjects.Contains(sub));
        });
}

var app = builder.Build();

// Liveness probe (used by Docker healthcheck / Uptime Kuma).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "toggl-mcp" }));

// Phase 3 registers each user's Toggl token here — behind the same login, NEVER via chat.
// Phase 1 stub so the route exists and documents intent.
// Gated the moment auth is on (RequireAuthorization) so the Phase-3 token-registration surface
// that will live here is authenticated by construction, not retrofitted later.
var settings = app.MapGet("/settings", () => Results.Content(
    """
    <!doctype html><meta charset="utf-8"><title>toggl-mcp settings</title>
    <h1>toggl-mcp</h1>
    <p>Per-user Toggl token registration lands here in Phase 3 (behind Authentik login).
    In Phase 1 the token is supplied via the <code>TOGGL__APITOKEN</code> environment variable.</p>
    """, "text/html"));
if (auth.Enabled) settings.RequireAuthorization(mcpAuthPolicy);

if (auth.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMcp("/mcp").RequireAuthorization(mcpAuthPolicy);
}
else
{
    // Fail closed off the local box: an unauthenticated /mcp is backed by a real Toggl token, so
    // refuse to start with auth off anywhere but Development (the default env for `dotnet run`).
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Auth:Enabled=false is only permitted in the Development environment. Refusing to expose an " +
            "unauthenticated /mcp backed by a live Toggl token — set Auth:Enabled=true (with Auth:Authority " +
            "and Auth:ResourceUrl) for any non-local deployment.");

    app.Logger.LogWarning("Auth is DISABLED — /mcp is open. Development/local use only; never expose this publicly.");
    app.MapMcp("/mcp");
}

app.Run();

// Scope check: Authentik grants scopes in a space-delimited "scope" (or "scp") claim.
static bool HasScope(ClaimsPrincipal user, string required) =>
    user.FindAll("scope").Concat(user.FindAll("scp"))
        .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Contains(required);

// Stable subject id (JwtBearer remaps "sub" onto NameIdentifier by default).
static string? SubjectOf(ClaimsPrincipal user) =>
    user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
