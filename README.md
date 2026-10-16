# toggl-mcp

> **Archived (October 2026), not maintained, but complete and runnable.** I built this for my own
> time tracking at a previous job, where hours were logged in a shared Toggl Track workspace: at the
> end of a day or week I asked Claude to read my calendar, git history and notes, propose the
> entries, and write them to Toggl once I had confirmed them. It ran as a hosted service on my own
> server. I have left that job, so the service is shut down and the repository is archived. Nothing
> here depends on it: fork it, run it on your own machine, finish it. What is unfinished is listed
> in [CONTRIBUTING.md](CONTRIBUTING.md).

An **MCP server** that lets Claude **back-fill Toggl Track time entries** from whatever record of
"what I did" you have (calendar, git, notes): **read, propose, you confirm, write.**

It is source-agnostic by design. The server knows nothing about where your work record comes from;
Claude reads the sources through its own connectors and only proposes entries (`plan_week`), and
nothing is written until you approve the plan (`commit_plan`).

---

## Quick start (local, stdio)

This is the recommended way to run it. The MCP client launches the process itself with `--stdio`:
no HTTP port, no login, and the process boundary is the trust boundary.

**Prerequisite:** the .NET 10 SDK.

```bash
git clone https://github.com/PetrValik/toggl-mcp.git && cd toggl-mcp
dotnet pack src/TogglMcp -c Release -o ./nupkg
dotnet tool install --global TogglMcp --add-source ./nupkg
```

This installs a command named `toggl-mcp` into `~/.dotnet/tools` (Windows:
`%USERPROFILE%\.dotnet\tools`); if your shell cannot find it, add that folder to `PATH` or use the
absolute path. It is not published to NuGet. After pulling changes, re-pack and run
`dotnet tool update --global TogglMcp --add-source ./nupkg` (bump `<Version>` in
`src/TogglMcp/TogglMcp.csproj`, or uninstall and reinstall).

**Get your Toggl token:** Toggl Track, Profile settings, API Token. Use the raw account token, not a
`toggl_sk_` key.

The token lives only in your MCP client's config (or environment) on your own machine. It is never a
tool argument and never something you type into chat; MCP forbids token passthrough because tool
arguments transit the model.

**Claude Code:**

```bash
claude mcp add toggl -e TOGGL__APITOKEN=<your-token> -- toggl-mcp --stdio
```

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "toggl": {
      "command": "/Users/you/.dotnet/tools/toggl-mcp",
      "args": ["--stdio"],
      "env": { "TOGGL__APITOKEN": "<your-token>" }
    }
  }
}
```

Use the absolute path to the command: GUI apps do not inherit your shell `PATH`.

**Docker instead of the .NET tool** (still stdio):

```bash
docker build -t toggl-mcp .
```

Then set the client's command to:

```
docker run -i --rm -e TOGGL__APITOKEN -e LOCK__TIMEZONE=Europe/Prague -v toggl-mcp-data:/data toggl-mcp --stdio
```

`-e TOGGL__APITOKEN` with no value passes the token through from the `env` block of the client
config, so it still never appears on a command line you type.

---

## Docs

- **[docs/USAGE.md](docs/USAGE.md)**: day-to-day usage, the read, propose, confirm, write flow, a tool reference with examples, mapping rules, the lock, and troubleshooting.
- **[docs/DAILY-PLANNING.md](docs/DAILY-PLANNING.md)**: turning your activity notes into a reviewed Toggl day: the honesty guard rails, one-day-back default, and ready-to-use prompts.
- **[CONTRIBUTING.md](CONTRIBUTING.md)**: what is unfinished and how to pick it up.

---

## Tools exposed

| Tool | Kind | Purpose |
|---|---|---|
| `week_status(weekOf?)` | read | Which working days of a week are empty vs filled, plus when the week locks (if the lock is on). |
| `plan_week(entries[])` | read (stages) | Prepare a back-fill proposal: validate, resolve, dedup, lock-check. **Writes nothing**; returns a `planId`. |
| `commit_plan(planId, exclude?)` | **write** | The **only** write path. Replays exactly the approved plan into Toggl. |
| `list_projects` | read | Active projects in the workspace. |
| `list_tasks(projectId)` | read | Tasks under a project (a Toggl Starter+ feature). |
| `list_entries(from?, to?)` | read | Entries in a date range (default last 7 days). |
| `suggest_project(calendarName, title)` | read | Rule match (silent) or fuzzy guess (confirm). |
| `add_mapping_rule(keyword, projectId)` | write | Teach a keyword to project rule. |

### The write flow (enforced: propose, approve, commit)

Back-fill is deliberately two-phase, so a write can only ever be a replay of something you approved:

1. **`week_status`** shows the gaps.
2. **`plan_week(entries[])`**: the model proposes entries for the empty working days (weekends and days off are simply omitted). The server validates each one, resolves projects, dedups against Toggl, checks the lock, and **freezes** the resolved plan under a `planId`. **Nothing is written.**
3. You **review** the preview: approve, drop rows, or ask for changes (which means re-planning).
4. **`commit_plan(planId, exclude?)`** writes exactly the frozen "new" entries. No re-interpretation; the model cannot commit something different from the preview.

There is **no direct `log_time`**. Every write goes through an approved plan.

### Timesheet lock (opt-in)

Some workspaces have an admin who locks past weeks permanently, after which nothing in them can be
edited. The server can mirror that rule so it refuses locked days up front instead of failing
halfway through a commit. It is **off by default**; turn it on with `LOCK__ENABLED=true`.

When enabled, a week locks on a configurable day and time (`LOCK__CLOSEDAYOFWEEK`, `LOCK__CLOSETIME`)
once the configured number of grace weeks has passed (`LOCK__GRACEWEEKS`), in the zone given by
`LOCK__TIMEZONE`. The tools stay inside the editable window and refuse locked days with a message
that names `LOCK__ADMINCONTACT`. With the defaults, a week locks on the Friday of the following week
at 23:59. A locked week is refused, not retried.

Adding a capability later is another `[McpServerTool]` method.

---

## Configuration

All keys bind from `appsettings.json` or environment variables (`__` means nesting). In stdio mode
`appsettings.json` is read from next to the binary, not from the working directory. See also
`.env.example`.

| Key | Default | Meaning | Mode |
|---|---|---|---|
| `TOGGL__APITOKEN` | none, required | Raw account token. Basic-auth username; the password is the literal `api_token`. | both |
| `TOGGL__WORKSPACEID` | from `/me` | Pin the workspace. Otherwise `default_workspace_id` is discovered once and cached. | both |
| `STORAGE__DATADIR` | `data` | Durable directory for mapping rules and the commit ledger. In stdio mode a relative value resolves under your local app-data folder: macOS `~/Library/Application Support/toggl-mcp/data`, Linux `$XDG_DATA_HOME` or `~/.local/share/toggl-mcp/data`, Windows `%LOCALAPPDATA%\toggl-mcp\data`. In HTTP mode it is relative to the working directory. The Docker image sets `/data` (a volume). | both |
| `LOCK__ENABLED` | `false` | Opt-in permanent weekly timesheet lock. | both |
| `LOCK__TIMEZONE` | blank, the machine's time zone | IANA id, for example `Europe/Prague`. Defines day and week boundaries for `week_status` and `plan_week` even when the lock is off. Set it explicitly in containers, which run in UTC. | both |
| `LOCK__CLOSEDAYOFWEEK` | `Friday` | Day on which a week closes. | both |
| `LOCK__CLOSETIME` | `23:59:00` | Time of day on that day. | both |
| `LOCK__GRACEWEEKS` | `1` | A week locks on the close day of the following week when this is 1. | both |
| `LOCK__ADMINCONTACT` | `your workspace admin` | Named in refusal messages. | both |
| `TOGGL__MINREQUESTINTERVALMS` | `1100` | Minimum gap between Toggl API calls. | both |
| `TOGGL__MAXRETRIES` | `5` | Retries on throttling and transient errors. | both |
| `TOGGL__RETRYBASEDELAYMS` | `1500` | Base delay for retry backoff. | both |
| `TOGGL__CREATEDWITH` | `toggl-mcp` | Value sent as `created_with`. | both |
| `AUTH__ENABLED` | `true` | JWT validation. The app refuses to start with auth off outside the Development environment (`dotnet run` is Development). | HTTP |
| `AUTH__AUTHORITY` | none | OIDC issuer. | HTTP |
| `AUTH__RESOURCEURL` | none | Public MCP URL. Must byte-match the JWT `aud` and the PRM `resource`. The `MapMcp` path is pinned to `/mcp`. | HTTP |
| `AUTH__REQUIREDSCOPE` | none | Optional scope the token must carry. | HTTP |
| `AUTH__ALLOWEDSUBJECTS__0`, `__1`, ... | none | Optional allowlist of `sub` values. | HTTP |
| `AUTH__REQUIREHTTPSMETADATA` | `true` | Require HTTPS for the issuer metadata. | HTTP |
| `ASPNETCORE_URLS` | ASP.NET default | Listen address. | HTTP |

---

## Hosting it for a team (Streamable HTTP + OIDC)

Without `--stdio` the server runs as a Streamable HTTP service at `/mcp`, with `/healthz` and a
`/settings` stub. Authentication (JWT bearer plus RFC 9728 Protected Resource Metadata) is on by
default, and the app refuses to start with auth off outside the Development environment. A client
that gets a `401` discovers the authorization server from
`/.well-known/oauth-protected-resource`.

Any OIDC provider works if it (a) puts the exact MCP resource URL in the access token's `aud` claim
and (b) signs RS256 with a JWKS. I tested it with Authentik 2025.8 or newer. For a worked example
(Docker Compose, reverse proxy, provider setup) see [deploy/DEPLOY.md](deploy/DEPLOY.md); the
examples use `toggl-mcp.example.com` and `auth.example.com`.

> **Why not Google directly:** Google ignores the RFC 8707 `resource` parameter, issues opaque
> tokens with `aud = client_id`, and has no Dynamic Client Registration, so the MCP-mandated
> audience check is impossible. Broker Google through an OIDC provider instead.

Note that a hosted deployment still has a single shared Toggl token (Phase 1). Per-user tokens were
planned but never built; see [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Gotchas baked into the code (don't undo them)

- **Toggl Basic auth field order:** the token is the *username*, the literal `api_token` the password.
- **Idempotency:** Toggl has no idempotency key, and **nothing is ever appended to a description**; an entry reads exactly as the user approved it. Instead the server keeps a per-user ledger (`{STORAGE__DATADIR}/ledger/`) of `sourceUid` to Toggl entry id, and falls back to matching an entry's own content (start instant, duration, project). `plan_week` and `commit_plan` share one `DuplicateFinder` so they cannot disagree, and commit re-checks at write time. A ledger record whose entry no longer exists in Toggl is ignored, so deleting an entry lets it be written again. Entries written by early versions carry a `⟨sourceUid⟩` marker; it is still *read* for dedup, never written.
- **Lock is opt-in:** with `LOCK__ENABLED=false` nothing is refused locally and Toggl is the only authority. When on, locked weeks are refused, not attempted.
- **DST and day bucketing:** `start` is passed through verbatim as RFC 3339 with the source's own offset; never hardcode `+02:00`. The day or week an entry counts for is computed in `LOCK__TIMEZONE`, which is the machine's zone when blank. Containers run in UTC, so set it explicitly there.
- **stdout is the protocol channel in stdio mode:** logs go to stderr. Do not write to stdout from anywhere in the server.
- **JWT audience (HTTP mode):** many providers default `aud` to the client id; add a mapping that returns the exact MCP resource URL.
- **`MapMcp` path:** pinned to `/mcp` so it byte-matches the PRM `resource`.

---

## Layout: Vertical Slice Architecture (adapted for MCP)

One folder per MCP tool under `Features/`. Each slice is a thin `*Tool` (bind args, call handler,
return) plus an `internal sealed *Handler` (the only logic layer) and slice-local request/response
records. Slices never reference each other; they share only the module infrastructure below. (VSA
adapted: no MediatR, FluentValidation, DbContext or `Result<T>`, because this is an MCP tool server,
not an HTTP/EF web API.)

```
src/TogglMcp/
  Program.cs              stdio or HTTP transport + auth wiring
  Features/               one vertical slice per tool (Tool + Handler + slice DTOs)
    ListProjects/ · ListTasks/ · ListEntries/ · SuggestProject/ · AddMappingRule/
    WeekStatus/ · PlanWeek/ · CommitPlan/
  Contracts/              ProjectDto (module-shared response) + mapping
  Toggl/                  ITogglClient/TogglClient (v9), throttle, models, exception   ┐
  Tokens/                 ITogglTokenProvider (env/config)                             │ module-
  Mapping/                keyword to project rule store (JSON per user)                │ shared
  Lock/                   WeekLockPolicy (opt-in permanent lock, editable window)      │ infra
  Planning/               WeekPlan + IPlanStore + SourceMarker (plan_week to commit_plan)┘
  Configuration/          TogglOptions, AuthOptions, StorageOptions, LockOptions
tests/TogglMcp.Tests/     xunit, including a stdio end-to-end test
Dockerfile · docker-compose.yml · deploy/ · .env.example · .claude/ (optional Claude Code config)
```

Run the tests with `dotnet test TogglMcp.slnx`. One of them,
`tests/TogglMcp.Tests/Hosting/StdioHostTests.cs`, launches the built server with `--stdio` and lists
its tools through the MCP C# client.

---

## Status and roadmap

Phase 1 (a single token from configuration) is what exists and works. Phase 2 (an identity spike:
do users of an org-admin-added Claude connector get distinct `sub` claims?) and Phase 3 (a per-user
encrypted token vault keyed by OIDC `sub`, registered via `/settings` behind login) were planned
and never built. The roadmap and the open questions are in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE), copyright (c) 2026 Petr Valik.
