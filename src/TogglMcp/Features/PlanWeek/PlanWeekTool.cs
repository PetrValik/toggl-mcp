using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.PlanWeek;

/// <summary>
/// MCP endpoint for the PlanWeek slice: binds the tool args and DI-injected services, then
/// delegates to <see cref="PlanWeekHandler"/> (the only logic layer). Read-only — it never
/// writes to Toggl; it produces a frozen plan that commit_plan later executes.
/// </summary>
[McpServerToolType]
public static class PlanWeekTool
{
    /// <summary>
    /// Builds a back-fill preview for a week without writing anything. Leading service-typed
    /// parameters (toggl, tokens, locks, plans, clock) plus <paramref name="cancellationToken"/>
    /// are injected by the MCP framework; only <paramref name="entries"/> is part of the tool's
    /// JSON input schema.
    /// </summary>
    [McpServerTool(Name = "plan_week", ReadOnly = true, OpenWorld = true),
     Description(
        "Prepare (but DO NOT write) a back-fill proposal for a week. Pass one entry per piece of work for the empty working days you want to fill — simply omit weekends and any days off (sick/vacation). Each entry is validated, its project resolved, deduped against what's already in Toggl, and checked against the lock. Returns a preview with a planId and per-entry status (new/duplicate/locked/invalid). Show it to the user; on approval call commit_plan(planId) to actually write it.")]
    public static Task<PlanResult> PlanWeek(
        ITogglClient toggl,
        ITogglTokenProvider tokens,
        WeekLockPolicy locks,
        IPlanStore plans,
        TimeProvider clock,
        [Description("Proposed entries for the week's empty working days.")] PlanEntryInput[] entries,
        CancellationToken cancellationToken = default)
        => new PlanWeekHandler(toggl, tokens, locks, plans, clock).HandleAsync(entries, cancellationToken);
}
