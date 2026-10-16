---
name: mcp-slice-architecture
description: This skill should be used when the user asks to "add a tool", "přidej nástroj",
  "přidej slice", "where does this handler go", "kam patří ten handler", "review this slice",
  "add an MCP tool", or edits C# under src/TogglMcp/Features/. It carries how a slice is shaped
  in THIS server (tool = thin endpoint, handler = the only logic layer), what the generic VSA
  pack prescribes that does not exist here, and the write-path rules that keep an official
  timesheet safe.
paths:
  - "src/TogglMcp/Features/**"
metadata:
  kit:
    category: pack-delta
    from: kit-vsa-dotnet@0.1.0/vertical-slice-architecture-dotnet
    generated: 2026-08-18
---

# Slices in an MCP tool server

`vertical-slice-architecture-dotnet` is the rule-set for this repo's **shape**: a feature folder
is the unit of work, slices never couple sideways, the endpoint stays thin. Everything it says
about the .NET *web* stack is absent here — this is an MCP tool server, not an HTTP API.

**A slice is one MCP tool.** `src/TogglMcp/Features/{VerbNoun}/` holds the tool, its handler, and
its request/response records. Nothing else.

## Anatomy

```
Features/PlanWeek/
  PlanWeekTool.cs      the MCP "endpoint" — bind args, call handler, return
  PlanWeekHandler.cs   internal sealed, the ONLY logic layer
  PlanEntryInput.cs    slice-local request record
  PlanResult.cs        slice-local response records
```

**The tool** is a `public static class {VerbNoun}Tool` marked `[McpServerToolType]`, holding one
method marked `[McpServerTool(Name = "snake_case", …)]` plus `[Description(…)]`. Its body is one
expression: construct the handler, call `HandleAsync`. If you are writing an `if` in a tool file,
it belongs in the handler.

Two things about that method signature are easy to get wrong:

- **Leading service-typed parameters are dependency injection.** `ITogglClient`, `WeekLockPolicy`,
  `IPlanStore`, `TimeProvider` and friends are supplied by the MCP framework. Handlers take them
  through a primary constructor; tools take them as leading method parameters.
- **Only `[Description]`-annotated parameters become the tool's JSON input schema.** That schema
  and the `[McpServerTool]` description are the *entire* interface the model sees. Treat them as
  API surface: a vague description is a bug, not a style issue.

Set the behaviour flags honestly — `ReadOnly = true` for anything that does not write,
`Idempotent`, `OpenWorld`. `plan_week` is `ReadOnly` precisely because it writes nothing.

**The handler** is `internal sealed class {VerbNoun}Handler(...)` with a primary constructor. It
is internal on purpose: the tool is the only way in. Validation is its first pass — a shape check
that returns a result describing what was rejected. There is no validator class.

**Records** are slice-local `public sealed record`s. Put `[Description]` on input properties; that
text is what the model reads when filling them.

## What the generic pack prescribes that does not exist here

Do not add these looking for parity — their absence is the design:

| The pack says | Here |
|---|---|
| MediatR command/query + handler | A plain handler class, constructed directly by the tool |
| FluentValidation validator per slice | Shape validation as the handler's first pass |
| `DbContext` / EF / migrations | No database. Durable state is JSON files under `StorageOptions.DataDir` |
| `Result<T>` / error envelope | Result records with a `Status` + `Note`/`Summary` the model reads back |
| Minimal-API endpoint + route | `[McpServerTool]`, auto-discovered by `WithToolsFromAssembly` |
| Repository per aggregate | `ITogglClient` — a thin wrapper over someone else's HTTP API |

## Sharing: the only allowed sideways

Slices share **module infrastructure** and nothing else. A slice never references another slice.

`ITogglClient` · `ITogglTokenProvider` · `WeekLockPolicy` · `IPlanStore` · `ILedgerStore` ·
`IMappingRuleStore` · `DuplicateFinder` / `EntryFingerprint` / `SourceMarker` · `TimeProvider` ·
the shared contracts in `Contracts/`.

When two slices need the same logic, it moves **down** into that infrastructure with a comment
saying which slices depend on it — see `Planning/DuplicateFinder.cs`, shared because plan and
commit must reach the same verdict. It never moves sideways.

`Program.cs` is the only composition root. Tools are discovered automatically, so a new slice
needs a DI registration **only** when it introduces new shared infrastructure.

## The write path — do not widen it

This server writes to an official timesheet. One tool writes time entries: `commit_plan`, and only
by replaying a plan the user already approved. `plan_week` freezes a plan and returns a `planId`;
nothing else may create a time entry.

- **Never add a write to a read tool**, and never add a second write path "for convenience". The
  review gate is the product.
- **Never accept a token as a tool argument.** MCP forbids token passthrough — tool arguments
  transit the model. Tokens come from `ITogglTokenProvider` only.
- **Dedup is not optional.** Anything creating an entry goes through `DuplicateFinder`. Entry
  descriptions are written exactly as the user approved them; nothing is ever appended to them.
- **The week lock is re-checked at write time**, not just at plan time. A week can close between
  the two.
- `start` is RFC3339 **with an explicit offset**, passed through verbatim; which day and week an
  entry counts for is computed in the configured time zone
  (`LOCK__TIMEZONE`, blank = the machine's), not from the UTC date.

## Tests

`tests/TogglMcp.Tests/` mirrors the source layout (`Planning/`, `Features/`, `Support/`).
Handlers are `internal`, so the test project is named in `InternalsVisibleTo` in
`src/TogglMcp/TogglMcp.csproj` — drive handlers directly, not through the tool.

Toggl is faked at the HTTP boundary (`Support/FakeTogglClient.cs`) and stores are exercised
against real temp files, because surviving a restart is their whole job. Run
`dotnet test TogglMcp.slnx`; the deploy workflow gates on it.

## Adding a slice

1. `Features/{VerbNoun}/` — tool, handler, records.
2. Name the tool `snake_case`; write the description as if it were public API.
3. Shared infrastructure only if two slices genuinely need it, with the comment saying which.
4. Tests first — the handler is the unit.
5. `Program.cs` only if you added shared infrastructure.

Deeper background: `README.md` (*Gotchas baked into the code*), `docs/USAGE.md` (the tool surface
and the idempotency rules), `docs/DAILY-PLANNING.md` (what the tools are for).
