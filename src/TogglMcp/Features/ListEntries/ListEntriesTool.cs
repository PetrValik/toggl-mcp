using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListEntries;

[McpServerToolType]
public static class ListEntriesTool
{
    [McpServerTool(Name = "list_entries", ReadOnly = true, OpenWorld = true),
     Description("List time entries in a date range (inclusive 'from' to inclusive 'to', YYYY-MM-DD). Defaults to the last 7 days. Use this to see what's already logged before back-filling.")]
    public static Task<IReadOnlyList<EntryDto>> ListEntries(
        ITogglClient toggl,
        [Description("Start date YYYY-MM-DD (inclusive). Defaults to 7 days ago.")] string? from = null,
        [Description("End date YYYY-MM-DD (inclusive). Defaults to today.")] string? to = null,
        CancellationToken cancellationToken = default)
        => new ListEntriesHandler(toggl).HandleAsync(from, to, cancellationToken);
}
