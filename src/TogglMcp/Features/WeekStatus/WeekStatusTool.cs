using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Lock;
using TogglMcp.Toggl;

namespace TogglMcp.Features.WeekStatus;

[McpServerToolType]
public static class WeekStatusTool
{
    [McpServerTool(Name = "week_status", ReadOnly = true, OpenWorld = true),
     Description(
        "Show, for a week (default the current week), which working days are empty vs already filled in Toggl, plus the lock deadline (when the week closes permanently). Use this first to see what still needs back-filling before proposing entries with plan_week.")]
    public static Task<WeekStatusResult> WeekStatus(
        ITogglClient toggl,
        WeekLockPolicy locks,
        [Description("Any date in the target week (YYYY-MM-DD). Defaults to the current week.")] string? weekOf = null,
        CancellationToken cancellationToken = default)
        => new WeekStatusHandler(toggl, locks).HandleAsync(weekOf, cancellationToken);
}
