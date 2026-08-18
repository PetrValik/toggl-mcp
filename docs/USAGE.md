# Using toggl-mcp

A practical guide to actually **using** the server day-to-day: how to back-fill your
Toggl week through Claude, what each tool does, and the rules that keep it safe.

For install / deploy / architecture, see the top-level [`README.md`](../README.md).

---

## The one thing to understand

Back-fill is a **four-step, review-gated loop**. The model can never write straight to
Toggl — it can only *replay a plan you approved*:

```
  week_status          plan_week            (you review)         commit_plan
  ───────────▶         ──────────▶          ──────────▶          ──────────▶
  where are the        propose entries      approve / drop       write EXACTLY
  gaps? when does      for the gaps         rows / re-plan       the frozen plan
  the week lock?       (writes NOTHING)                          (the ONLY write)
```

There is **no `log_time`**. Every write goes through `plan_week` → `commit_plan`, so what
lands in Toggl is always exactly what you saw in the preview.

---

## Prerequisites

Either:

- **Connected in Claude** — a custom connector at `https://toggl.irminsul.uk/mcp`, OAuth
  logged-in. Nothing runs on your machine. (See README → *Connect in Claude*.) — or —
- **Running locally** — `dotnet run` in `src/TogglMcp` with `TOGGL__APITOKEN` set and
  `AUTH__ENABLED=false`, then point an MCP client at `http://localhost:<port>/mcp`.
  (See README → *Run Phase 1 locally*.)

Once connected, you just talk to Claude in natural language — Claude picks the tools.
The examples below show the underlying calls so you know what to expect.

---

## Walkthrough: back-fill last week

### 1. See the gaps — `week_status`

> **You:** "What's still missing in my Toggl for last week?"

Claude calls `week_status(weekOf: "2026-07-13")` (any date in the target week; omit for
the current week). You get back the per-day picture and, crucially, the **lock deadline**:

```jsonc
{
  "weekStart": "2026-07-13", "weekEnd": "2026-07-19",
  "locked": false,
  "locksAt": "2026-07-24 23:59 +02:00",          // when this week closes forever
  "days": [
    { "date": "2026-07-13", "dayOfWeek": "Monday",  "working": true, "hoursLogged": 6.5, "empty": false, "editable": true },
    { "date": "2026-07-14", "dayOfWeek": "Tuesday", "working": true, "hoursLogged": 0.0, "empty": true,  "editable": true },
    // …Wed–Sun…
  ],
  "summary": "Week Jul 13–Jul 19: 2 empty working day(s): Tuesday, Wednesday. Locks 2026-07-24 23:59."
}
```

`empty` + `working` = a day worth filling. Weekends (`working: false`) and days you took
off are simply left alone.

### 2. Propose entries — `plan_week` (nothing is written)

Claude reads *what you did* from **its own** sources (your calendar, git, notes — the
server never sees them) and proposes one entry per gap. Each entry needs a stable
**`sourceUid`** (that's what makes re-runs safe — see [Idempotency](#idempotency-safe-re-runs)):

```jsonc
plan_week(entries: [
  {
    "description": "Design review + API spec",
    "start": "2026-07-14T09:00:00+02:00",   // RFC3339 WITH offset — its date picks the day/week
    "durationSeconds": 7200,                 // 2h, must be positive
    "sourceUid": "calevt:AB12",              // REQUIRED stable id of the source record
    "projectId": 178,                        // preferred; from list_projects
    "tags": ["meeting"]
  },
  { "description": "Bugfix batch", "start": "2026-07-15T13:00:00+02:00",
    "durationSeconds": 10800, "sourceUid": "gitsha:9f3c", "projectName": "Platform" }
])
```

You can pass `projectId` (preferred) **or** `projectName` (resolved by exact-then-contains
match). The server validates each entry, resolves the project, dedups against what's
already in Toggl, checks the lock, then **freezes** the resolved plan under a `planId`:

```jsonc
{
  "planId": "3f9a…",           // pass this to commit_plan
  "weekStart": "2026-07-13", "weekLocked": false, "locksAt": "2026-07-24 23:59 +02:00",
  "newCount": 2, "duplicateCount": 0, "blockedCount": 0,
  "entries": [
    { "day": "2026-07-14", "sourceUid": "calevt:AB12", "projectName": "Website",  "status": "new" },
    { "day": "2026-07-15", "sourceUid": "gitsha:9f3c", "projectName": "Platform", "status": "new" }
  ],
  "summary": "2 entries ready — review, then commit_plan(\"3f9a…\"). 0 duplicate, 0 blocked."
}
```

Per-entry `status` tells you what will happen:

| status | meaning |
|---|---|
| `new` | will be written on commit |
| `duplicate` | already in Toggl (matched on `sourceUid` via the ledger, or on the entry's own content), or repeated within this batch — will be skipped |
| `locked` | its week is already locked — will be refused |
| `invalid` | failed validation (missing `sourceUid`, non-positive duration, unknown project, `start` without an offset, …) — never written |

### 3. Review

Look at the preview. Ask Claude to change descriptions/projects (it re-plans → new
`planId`), or note which rows to drop. **Nothing has touched Toggl yet.**

### 4. Write it — `commit_plan`

> **You:** "Looks right — log it, but skip the bugfix one."

```jsonc
commit_plan(planId: "3f9a…", exclude: ["gitsha:9f3c"])   // exclude by sourceUid, optional
```

It writes **exactly** the frozen `new` entries (minus any you excluded), re-checking the
lock and dedup at write time:

```jsonc
{
  "planId": "3f9a…", "created": 1, "skipped": 0, "failed": 0,
  "results": [
    { "sourceUid": "calevt:AB12", "status": "created", "message": "entry 33e1 on 2026-07-14", "entryId": 33e1 }
  ],
  "summary": "Committed 1, skipped 0, failed 0; 1 excluded by you."
}
```

Done — the entry is in Toggl. `commit_plan` is the **only** tool that writes time entries.

---

## Project mapping: stop repeating yourself

The first time Claude sees a kind of work it may not know the project. Two tools turn that
into a one-time decision.

**`suggest_project(calendarName, title)`** — asks the server for a project guess:

```jsonc
suggest_project(calendarName: "Team Standup", title: "Daily sync")
// → { "matched": false, "projectId": 178, "projectName": "Website", "confidence": 0.42,
//     "reason": "Best guess by name similarity (42%); confirm with the user before logging.",
//     "candidates": [ …top 3… ] }
```

- `matched: true` → a **learned rule** fired (confidence 0.95); use it silently.
- `matched: false` → a fuzzy guess; **confirm with the user first**, then teach it:

**`add_mapping_rule(keyword, projectId)`** — persists the decision so it's silent next time:

```jsonc
add_mapping_rule(keyword: "standup", projectId: 178)
// → { "ok": true, "message": "Rule saved: 'standup' -> Website.", … }
```

Now any future source text containing "standup" resolves to *Website* automatically. Rules
are per-user and stored as JSON under `STORAGE__DATADIR`; a new rule for an existing
keyword replaces it.

---

## Read-only helpers

| Tool | Use it to… | Returns |
|---|---|---|
| `list_projects` | get project **ids** for `plan_week` / mapping rules | `[{ id, name, active, clientId }]` |
| `list_tasks(projectId)` | pick a `taskId` under a project (Starter+ plans) | `[{ id, name, projectId, active }]` |
| `list_entries(from?, to?)` | see what's already logged before proposing | `[{ id, description, start, durationSeconds, projectId, projectName, taskId, tags }]` |

`list_entries` dates are inclusive `YYYY-MM-DD` and default to the **last 7 days**,
anchored on *today in the lock time zone* (Europe/Prague) — the same "today" the
planning tools use, so the windows always agree.

---

## The lock rule (why a day gets refused)

Weeks lock **permanently at Friday 23:59 Europe/Prague**: each Friday the *previous* week
closes and can never be edited again. Only the workspace admin can reopen a week.

- The editable window is **the current week + last week**; older weeks are locked.
- The tools compute this themselves. A locked day is **refused, never attempted** — you'll
  see `status: "locked"` in a plan, or a `skipped` result at commit, with a message telling
  you to contact the admin. It is never a silent failure.
- `week_status.locksAt` / `plan_week.locksAt` tell you exactly when your window closes, so
  you know how long you have to back-fill.

Lock timing is configurable via the `Lock` section (`LOCK__…` env) — see README.

---

## Idempotency: safe re-runs

Toggl has no idempotency key. **Nothing is written into the description to compensate** — the
entry reads exactly as you approved it. Instead the server recognises an already-logged entry
three ways, most precise first:

1. **The ledger.** On commit, `sourceUid → Toggl entry id` is recorded in a durable per-user
   file under `STORAGE__DATADIR`. A record is only trusted while that entry still exists in
   Toggl — delete an entry and it can be written again.
2. **The entry's own content** — same start instant, same duration, same project. This catches
   work you logged by hand in the Toggl UI, and re-runs where the `sourceUid` changed.
3. **The legacy `⟨sourceUid⟩` marker**, for entries written before 2026-08-18, when the marker
   was still embedded in descriptions. Read only; never written.

`plan_week` and `commit_plan` share one dedup implementation, so the preview and the write can't
disagree; `commit_plan` re-checks at write time and serializes concurrent commits of the same
plan. Practical consequences:

- **Re-running the same plan never double-logs.** A second `plan_week` with the same
  `sourceUid`s marks them `duplicate`; a re-`commit_plan` skips what already landed.
- **Keep `STORAGE__DATADIR` on a persisted volume.** Losing it is not fatal — content matching
  still catches unedited entries — but the exact `sourceUid` memory goes with it.
- **Always give each source record a stable `sourceUid`** (`calevt:<id>`, `gitsha:<sha>`,
  `note:<uid>`…). It's required precisely so this guarantee holds.
- **`start` must be RFC3339 with a real offset** (`…+02:00`, not bare local time). The
  offset is passed through verbatim; the day/week it counts for is computed in Europe/Prague.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Entry shows `status: "invalid"` — "start must be RFC3339 with an explicit offset" | `start` lacks an offset. Use `2026-07-14T09:00:00+02:00`, not `2026-07-14T09:00:00`. |
| `status: "invalid"` — "sourceUid is required" | Every proposed entry needs a stable `sourceUid`. |
| `status: "locked"` / commit `skipped` — "ask the admin to reopen" | The week is past the Friday 23:59 lock. Only the workspace admin can reopen it. |
| `plan_week` says `duplicate` for something not in Toggl | Two proposed entries share a `sourceUid`, or share the same start/duration/project. Give distinct ids and non-overlapping times. |
| "project '…' not found" | `projectName` didn't match. Call `list_projects` and pass the exact `projectId`. |
| Nothing writes, `planId` is null | The plan had no `new` entries (all duplicate/blocked/invalid). Nothing to commit. |
| Connector won't authenticate | Auth/Authentik setup — see README → *Authentik setup checklist*. |

**Never** paste your Toggl token into chat: the token is only ever read from
env/config (or the Phase 3 vault) and is **never** accepted as a tool argument — MCP
forbids token passthrough because tool args transit the model.
