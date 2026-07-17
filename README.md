# toggl-mcp

A hosted **MCP server** that lets Claude **back-fill Toggl Track time entries** from
whatever record of "what I did" you have (calendar, git, notes, second brain):
**read → propose → you confirm → write.**

It is **one always-on web service**, not an app anyone installs. Claude's cloud calls
it over HTTPS; the server holds the Toggl token(s) and calls the Toggl API v9. Users add
a connector URL in Claude once and log in — nothing runs on their machine.

```
Claude cloud ──HTTPS──▶ https://toggl.irminsul.uk/mcp   (this service, always on)
                          holds Toggl token(s), calls Toggl API v9
You + colleagues: add that URL in Claude once + OAuth login. No install.
```

**Source-agnostic by design:** the server has zero knowledge of where the work record
comes from. Claude reads the source via *its own* connectors and just proposes entries (`plan_week` → `commit_plan`).

---

## Docs

- **[docs/USAGE.md](docs/USAGE.md)** — day-to-day usage: the read → propose → confirm → write flow, a tool reference with examples, mapping rules, the lock, and troubleshooting.
- **[docs/DAILY-PLANNING.md](docs/DAILY-PLANNING.md)** — turning your activity notes into a reviewed Toggl day: the honesty guard rails, one-day-back default, and ready-to-use prompts.

---

## Status

- **Design:** complete (see the project note in the Obsidian vault).
- **Phase 1 scaffold:** ✅ built and verified — server boots, Streamable HTTP transport
  works, all eight tools register, argument binding + validation confirmed via a real MCP
  client. Not yet run against a live Toggl account or deployed.

---

## Stack

- **ASP.NET Core (.NET 10) + `ModelContextProtocol.AspNetCore` 1.4.1**, Streamable HTTP transport.
- One **Docker** container, config via env, behind **Caddy** (cloud/Hetzner) or ALB/App Runner (AWS).
  Same image both places → Hetzner → AWS is a redeploy, not a rewrite.
- Toggl called server-side; token from env (Phase 1) or per-user vault (Phase 3).

## Tools exposed

| Tool | Kind | Purpose |
|---|---|---|
| `week_status(weekOf?)` | read | Which working days of a week are empty vs filled, + when the week locks. |
| `plan_week(entries[])` | read (stages) | Prepare a back-fill proposal — validate, resolve, dedup, lock-check. **Writes nothing**; returns a `planId`. |
| `commit_plan(planId, exclude?)` | **write** | The **only** write path — replays exactly the approved plan into Toggl. |
| `list_projects` | read | Active projects in the shared workspace. |
| `list_tasks(projectId)` | read | Tasks under a project (Starter+ feature). |
| `list_entries(from?, to?)` | read | Entries in a date range (default last 7 days). |
| `suggest_project(calendarName, title)` | read | Rule match (silent) or fuzzy guess (confirm). |
| `add_mapping_rule(keyword, projectId)` | write | Teach a keyword→project rule. |

### The write flow (enforced: propose → approve → commit)
Back-fill is deliberately two-phase so a write can only ever be a replay of something you approved:

1. **`week_status`** → shows the gaps and the lock deadline.
2. **`plan_week(entries[])`** → the model proposes entries for the empty working days (weekends and days off simply omitted). The server validates each, resolves projects, dedups against Toggl, checks the lock, and **freezes** the resolved plan under a `planId`. **Nothing is written.**
3. You **review** the preview → approve, drop rows, or ask for changes (re-plan).
4. **`commit_plan(planId, exclude?)`** → writes exactly the frozen "new" entries. No re-interpretation; the model can't commit something different from the preview.

There is **no direct `log_time`** — every write goes through an approved plan.

### Timesheet lock (hard rule)
Weeks lock **permanently at Friday 23:59 (Europe/Prague)**: each Friday the *previous* week closes and can never be edited again — only the workspace admin (**Josef Širůčka**) can reopen it. The tools compute this (`Lock` config), stay inside the editable window (current + last week), and **refuse locked days** with a "contact the admin" message instead of failing. Configurable via the `Lock` section.

Adding a capability later = another `[McpServerTool]` method → instantly live on all devices.

---

## Run Phase 1 locally (just you)

Requires the **.NET 10 SDK**.

```bash
cd src/TogglMcp

# Your raw Toggl token (Toggl → Profile → API Token). NOT a toggl_sk_ key.
export TOGGL__APITOKEN="<your-toggl-api-token>"
# Local dev (dotnet run = Development env) runs with auth OFF → /mcp is open. Local use ONLY.
export AUTH__ENABLED=false

dotnet run
```

Then point any MCP client at `http://localhost:<port>/mcp` (Streamable HTTP). Health
check: `curl http://localhost:<port>/healthz`.

> **Security:** the Toggl token is only ever read from configuration/env or (Phase 3)
> the encrypted vault. It is **never** accepted as a tool argument and never logged —
> MCP forbids token passthrough because tool args transit the model.

### Configuration

All keys bind from `appsettings.json` or env (`__` = nesting). See `.env.example`.

| Env | Default | Meaning |
|---|---|---|
| `TOGGL__APITOKEN` | — | Phase 1 account token (Basic-auth username; password is literal `api_token`). |
| `TOGGL__WORKSPACEID` | *(from `/me`)* | Pin the workspace; else discovered once and cached. |
| `AUTH__ENABLED` | `true` | JWT validation. **Secure by default**; auto-off under the Development env for local `dotnet run`. The app **refuses to start** with auth off outside Development. |
| `AUTH__AUTHORITY` | — | Authentik OIDC issuer. |
| `AUTH__RESOURCEURL` | — | Public MCP URL; must match JWT `aud` and PRM `resource`. |
| `STORAGE__DATADIR` | `data` | Durable dir (mapping rules; Phase 3 vault + key ring). |

---

## Deploy to `cloud` (Phase 1/3)

1. Copy `.env.example` → `.env`, set `TOGGL__APITOKEN`; for a public deploy also set
   `AUTH__ENABLED=true`, `AUTH__AUTHORITY`, `AUTH__RESOURCEURL=https://toggl.irminsul.uk/mcp`.
2. `docker compose up -d --build` (joins the shared external `web` network).
3. Add `deploy/Caddyfile.snippet` to the cloud Caddyfile and reload Caddy.
4. Verify: `curl https://toggl.irminsul.uk/healthz`.

### Authentik setup checklist (required before `AUTH__ENABLED=true`)

- Authentik **≥ 2025.8** (RFC 8414 metadata).
- An OAuth2/OpenID **Provider** for this app; note its issuer → `AUTH__AUTHORITY`.
- **Scope Mapping bound to the `openid` scope** returning `{"aud": "https://toggl.irminsul.uk/mcp"}`.
  The default `aud` is the client_id, which fails the MCP audience check.
- Assign an **RSA signing key** to the provider (else it signs HS256, not JWKS-verifiable).
- Phase 3 team login: federate Authentik → company **Google Workspace** (Directory →
  Federation → Google source; "Link on unique identifier"; enrollment flow
  `default-source-enrollment`; gate the domain via Google "Internal" consent + the `hd` claim).

> **Why Authentik and not Google directly:** Google ignores the RFC 8707 `resource`
> param, issues opaque tokens with `aud = client_id`, and has no Dynamic Client
> Registration — so the MCP-mandated audience check is impossible. Brokering through
> Authentik is the correct path.

---

## Connect in Claude

Add a **custom connector** with URL `https://toggl.irminsul.uk/mcp` and complete the
OAuth login. The C# SDK auto-serves the RFC 9728 Protected Resource Metadata at
`/.well-known/oauth-protected-resource` and the `401 WWW-Authenticate` challenge, so
Claude discovers the Authentik authorization server automatically.

---

## Phased plan

1. **Phase 1 — cloud, just me:** env token, `AUTH__ENABLED=false` locally then `true`
   with Authentik locked to me. Validates the whole Toggl + Authentik + connector chain.
2. **Phase 2 — identity spike:** 2 devs connect on Claude Team; the server logs the `sub`
   it sees. **Distinct → per-user vault is safe. Same → stop before building it.** This is
   the one live risk: org-admin-added connectors have credible reports of collapsing to a
   single shared token ("last authenticator wins").
3. **Phase 3 — team on AWS:** switch on the per-user encrypted vault (registered via
   `/settings`, keyed by Authentik `sub`) + Google-federated login; redeploy the same image
   to company AWS + subdomain; publish self-host docs.

## Gotchas baked into the code (don't undo them)

- **Toggl Basic auth field order:** token is the *username*, literal `api_token` the password.
- **Idempotency:** Toggl has no idempotency key — entries embed `⟨sourceUid⟩` in the
  description; `plan_week` and `commit_plan` both dedup against it (and re-check at commit).
  `sourceUid` is required, so re-running a plan never duplicates.
- **Lock:** never write past Friday 23:59 (Europe/Prague) — locked weeks are refused, not
  attempted. Only the admin can reopen a week.
- **DST:** `start` is passed through verbatim as RFC3339 with the source's own offset —
  never hardcode `+02:00`; the day/week an entry counts for is computed in Europe/Prague.
- **JWT audience:** Authentik's default `aud` is the client_id → add the `aud` scope mapping.
- **Data Protection key ring (Phase 3):** must be persisted to `STORAGE__DATADIR` with a
  fixed application name, or a restart makes every stored token undecryptable.
- **`MapMcp` path:** pinned to `/mcp` so it byte-matches the PRM `resource`.

---

## Layout — Vertical Slice Architecture (adapted for MCP)

One folder per MCP tool under `Features/`. Each slice is a thin `*Tool` ("endpoint": bind
args → call handler → return) + an `internal sealed *Handler` (the only logic layer) +
slice-local request/response records. Slices never reference each other; they share only the
module infrastructure below. (VSA adapted: no MediatR / FluentValidation / DbContext /
`Result<T>` — this is an MCP tool server, not an HTTP/EF web API. See `.claude/kit.profile.yaml`.)

```
src/TogglMcp/
  Program.cs              MCP + HTTP transport + auth toggle wiring
  Features/               one vertical slice per tool (Tool + Handler + slice DTOs)
    ListProjects/ · ListTasks/ · ListEntries/ · SuggestProject/ · AddMappingRule/
    WeekStatus/ · PlanWeek/ · CommitPlan/
  Contracts/              ProjectDto (module-shared response) + mapping
  Toggl/                  ITogglClient/TogglClient (v9), throttle, models, exception   ┐
  Tokens/                 ITogglTokenProvider (env now; vault in Phase 3)              │ module-
  Mapping/                keyword→project rule store (JSON per user)                   │ shared
  Lock/                   WeekLockPolicy (Fri-23:59 permanent lock, editable window)   │ infra
  Planning/               WeekPlan + IPlanStore + SourceMarker (plan_week → commit_plan)┘
  Configuration/          TogglOptions, AuthOptions, StorageOptions, LockOptions
Dockerfile · docker-compose.yml · deploy/Caddyfile.snippet · deploy/DEPLOY.md · .env.example · .claude/kit.profile.yaml
```
