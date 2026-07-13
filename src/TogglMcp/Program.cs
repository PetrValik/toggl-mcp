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

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<TogglOptions>(builder.Configuration.GetSection(TogglOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<LockOptions>(builder.Configuration.GetSection(LockOptions.SectionName));
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

// ── Application services ────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TogglThrottle>();
builder.Services.AddSingleton<WeekLockPolicy>();
builder.Services.AddSingleton<IPlanStore, InMemoryPlanStore>();             // ephemeral frozen plans (plan_week → commit_plan)
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

    builder.Services.AddAuthorization();
}

var app = builder.Build();

// Liveness probe (used by Docker healthcheck / Uptime Kuma).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "toggl-mcp" }));

// Phase 3 registers each user's Toggl token here — behind the same login, NEVER via chat.
// Phase 1 stub so the route exists and documents intent.
app.MapGet("/settings", () => Results.Content(
    """
    <!doctype html><meta charset="utf-8"><title>toggl-mcp settings</title>
    <h1>toggl-mcp</h1>
    <p>Per-user Toggl token registration lands here in Phase 3 (behind Authentik login).
    In Phase 1 the token is supplied via the <code>TOGGL__APITOKEN</code> environment variable.</p>
    """, "text/html"));

if (auth.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMcp("/mcp").RequireAuthorization();
}
else
{
    app.Logger.LogWarning("Auth is DISABLED — /mcp is open. Phase 1 local use only; never expose this publicly.");
    app.MapMcp("/mcp");
}

app.Run();
