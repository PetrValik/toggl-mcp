using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Lock;
using TogglMcp.Toggl;

namespace TogglMcp.Features.WeekStatus;

/// <summary>MCP endpoint slice for week_status: the read-only entry point that reports a week's back-fill state.</summary>
[McpServerToolType]
public static class WeekStatusTool
{
    /// <summary>
    /// Read-only tool: reports which working days in a week are still empty vs filled, plus the lock deadline.
    /// Thin binding only — delegates all logic to <see cref="WeekStatusHandler"/>. <paramref name="toggl"/> and
    /// <paramref name="locks"/> are DI-injected; only <paramref name="weekOf"/> forms the tool's JSON input schema.
    /// </summary>
    /// <param name="weekOf">Any date in the target week (YYYY-MM-DD); null defaults to the current week.</param>
    [McpServerTool(Name = "week_status", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description(
        "Show, for a week (default the current week), which working days are empty vs already filled in Toggl, plus the lock deadline (when the week closes permanently) if the workspace locks weeks; locksAt is null when it does not. Use this first to see what still needs back-filling before proposing entries with plan_week.")]
    public static Task<WeekStatusResult> WeekStatus(
        ITogglClient toggl,
        WeekLockPolicy locks,
        [Description("Any date in the target week (YYYY-MM-DD). Defaults to the current week.")] string? weekOf = null,
        CancellationToken cancellationToken = default)
        => new WeekStatusHandler(toggl, locks).HandleAsync(weekOf, cancellationToken);
}
