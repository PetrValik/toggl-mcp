using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Lock;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListEntries;

/// <summary>
/// MCP endpoint for the list_entries slice: binds the tool args, then delegates to
/// <see cref="ListEntriesHandler"/>. Thin by design — no logic lives here.
/// </summary>
[McpServerToolType]
public static class ListEntriesTool
{
    // Read-only/idempotent query: fetches entries in a window, resolves project names, returns DTOs.
    // Leading service params (toggl, locks) are DI-injected by the MCP framework; only the
    // [Description]-carrying params form the tool's JSON input schema.
    [McpServerTool(Name = "list_entries", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description("List time entries in a date range (inclusive 'from' to inclusive 'to', YYYY-MM-DD). Defaults to the last 7 days. Use this to see what's already logged before back-filling.")]
    public static Task<IReadOnlyList<EntryDto>> ListEntries(
        ITogglClient toggl,
        WeekLockPolicy locks,
        [Description("Start date YYYY-MM-DD (inclusive). Defaults to 7 days ago.")] string? from = null,
        [Description("End date YYYY-MM-DD (inclusive). Defaults to today.")] string? to = null,
        CancellationToken cancellationToken = default)
        => new ListEntriesHandler(toggl, locks).HandleAsync(from, to, cancellationToken);
}
