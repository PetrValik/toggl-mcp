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
// token provider, clock), and stands up the MCP transport that auto-discovers the *Tool slices:
// stdio (`--stdio`) for one person's local server that the MCP client launches itself, or
// Streamable HTTP for a hosted one, optionally gated behind an OIDC resource server.

// ── Local server (stdio) ─────────────────────────────────────────────────────────
// No HTTP and no login: the MCP client owns the process, and the token comes from the env block
// of its server config. The process boundary is the trust boundary.
if (args.Contains("--stdio"))
{
    // The client picks the working directory (Claude Desktop uses "/"), so config is read from
    // next to the binary, not from wherever the process happens to start.
    var local = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args.Where(a => a != "--stdio").ToArray(),
        ContentRootPath = AppContext.BaseDirectory,
    });

    // stdout is the protocol channel: a single log line written there breaks the client.
    local.Logging.ClearProviders();
    local.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AddTogglMcp(local);

    // For the same reason a relative data dir is anchored in the user's local app-data folder.
    local.Services.PostConfigure<StorageOptions>(o =>
    {
        if (!Path.IsPathRooted(o.DataDir))
            o.DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "toggl-mcp", o.DataDir);
    });

    local.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(ListProjectsTool).Assembly); // scans all Features/*/*Tool slices

    await local.Build().RunAsync();
    return;
}

// ── Hosted server (Streamable HTTP) ──────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
AddTogglMcp(builder);
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
const string mcpAuthPolicy = "mcp"; // authz policy applied to /mcp (+ /settings) when auth is enabled

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(ListProjectsTool).Assembly); // scans all Features/*/*Tool slices

// ── Auth: OAuth resource server (OIDC provider; Authentik in the tested setup) ─
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
    // optional required scope and/or an allowlisted `sub` (both no-ops when unset, so single-user
    // use is unaffected while multi-user gating is one config change away).
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

// Liveness probe (used by the Docker healthcheck and any uptime monitor).
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "toggl-mcp" }));

// Stub. Roadmap Phase 3 (not built) would register each user's Toggl token here — behind the same
// login, NEVER via chat. The route exists to document intent.
// Gated the moment auth is on (RequireAuthorization) so a token-registration surface
// that might live here is authenticated by construction, not retrofitted later.
var settings = app.MapGet("/settings", () => Results.Content(
    """
    <!doctype html><meta charset="utf-8"><title>toggl-mcp settings</title>
    <h1>toggl-mcp</h1>
    <p>Per-user token registration is not built yet. The Toggl token comes from the
    <code>TOGGL__APITOKEN</code> environment variable.</p>
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

// Options and the shared infra both transports run on.
static void AddTogglMcp(IHostApplicationBuilder builder)
{
    // ── Configuration ─────────────────────────────────────────────────────────
    builder.Services.Configure<TogglOptions>(builder.Configuration.GetSection(TogglOptions.SectionName));
    builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
    builder.Services.Configure<LockOptions>(builder.Configuration.GetSection(LockOptions.SectionName));

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();                                  // no HttpContext under stdio → UserKey "default"
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TogglThrottle>();
    builder.Services.AddSingleton<WeekLockPolicy>();
    builder.Services.AddSingleton<IPlanStore, InMemoryPlanStore>();             // ephemeral frozen plans (plan_week → commit_plan)
    builder.Services.AddSingleton<ILedgerStore, FileLedgerStore>();            // durable "we already wrote this" record — replaces the old in-description marker
    builder.Services.AddScoped<ITogglTokenProvider, EnvTogglTokenProvider>();   // a per-user vault-backed provider would replace this (unbuilt)
    builder.Services.AddSingleton<IMappingRuleStore, FileMappingRuleStore>();
    builder.Services.AddHttpClient<ITogglClient, TogglClient>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("toggl-mcp/0.1");
    })
    // Never let the IHttpClientFactory logging handlers emit the Basic-auth Toggl token,
    // even if HttpClient logging is raised to Trace/Debug.
    .RedactLoggedHeaders(["Authorization"]);
}

// Scope check: the OIDC provider grants scopes in a space-delimited "scope" (or "scp") claim.
static bool HasScope(ClaimsPrincipal user, string required) =>
    user.FindAll("scope").Concat(user.FindAll("scp"))
        .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Contains(required);

// Stable subject id (JwtBearer remaps "sub" onto NameIdentifier by default).
static string? SubjectOf(ClaimsPrincipal user) =>
    user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
