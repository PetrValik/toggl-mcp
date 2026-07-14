using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Lock;
using TogglMcp.Planning;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.CommitPlan;

/// <summary>
/// MCP endpoint for the commit_plan tool — the slice's only write path to Toggl. Thin: binds
/// tool args and delegates all logic to <see cref="CommitPlanHandler"/>.
/// </summary>
[McpServerToolType]
public static class CommitPlanTool
{
    // Idempotent = true because a re-commit is safe: the handler re-checks the dedup marker so
    // replaying the same plan won't double-write. OpenWorld = true because it hits the Toggl API.
    [McpServerTool(Name = "commit_plan", Idempotent = true, OpenWorld = true),
     Description(
        "Write a previously prepared plan to Toggl — the ONLY write path. Pass the planId from plan_week. Writes exactly the frozen 'new' entries (a verbatim replay of what the user approved), re-checking the lock and dedup at write time. Optionally exclude some entries by sourceUid (days the user decided to skip).")]
    // Leading service-typed params (toggl/tokens/locks/plans) + cancellationToken are DI-injected
    // by the MCP framework; planId and exclude (the [Description] params) form the JSON input schema.
    public static Task<CommitResult> CommitPlan(
        ITogglClient toggl,
        ITogglTokenProvider tokens,
        WeekLockPolicy locks,
        IPlanStore plans,
        [Description("planId returned by plan_week.")] string planId,
        [Description("Optional sourceUids to skip (e.g. days the user decided not to log).")] string[]? exclude = null,
        CancellationToken cancellationToken = default)
        => new CommitPlanHandler(toggl, tokens, locks, plans).HandleAsync(planId, exclude, cancellationToken);
}
