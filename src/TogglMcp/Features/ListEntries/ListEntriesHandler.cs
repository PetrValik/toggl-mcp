using TogglMcp.Contracts;
using TogglMcp.Lock;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListEntries;

/// <summary>
/// The only logic layer for the list_entries slice: resolves the query window (lock-time-zone
/// anchored), fetches raw Toggl entries, resolves project names, and maps them to <see cref="EntryDto"/>.
/// </summary>
internal sealed class ListEntriesHandler(ITogglClient toggl, WeekLockPolicy locks)
{
    /// <summary>
    /// Lists entries between <paramref name="from"/> and <paramref name="to"/> (both inclusive, YYYY-MM-DD),
    /// defaulting to the last 7 days, with project names resolved.
    /// </summary>
    /// <param name="from">Inclusive start date; null defaults to 7 days before today (lock time zone).</param>
    /// <param name="to">Inclusive end date; null defaults to today (lock time zone).</param>
    public async Task<IReadOnlyList<EntryDto>> HandleAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        // Anchor the default window on "today" in the configured lock time zone, so list_entries
        // agrees with week_status/plan_week near local midnight instead of drifting a day in UTC.
        var today = locks.Today();
        var fromDate = DateInput.Parse(from) ?? today.AddDays(-7);
        var toDate = DateInput.Parse(to) ?? today;
        // Toggl end_date is exclusive; make 'to' inclusive.
        var entries = await toggl.GetTimeEntriesAsync(fromDate, toDate.AddDays(1), cancellationToken);

        var names = await ProjectNamesAsync(entries.Select(entry => entry.ProjectId), cancellationToken);
        return entries.Select(entry => ToDto(entry, names)).ToList();
    }

    // Builds an id->name map for resolving entry projects. Skips the Toggl /projects round-trip
    // entirely when none of the entries carry a project, so a plain time list stays a single call.
    private async Task<IReadOnlyDictionary<long, string>> ProjectNamesAsync(IEnumerable<long?> projectIds, CancellationToken cancellationToken)
    {
        if (!projectIds.Any(id => id is not null)) return new Dictionary<long, string>();
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        return projects.ToDictionary(project => project.Id, project => project.Name);
    }

    // Flattens a raw Toggl entry to a DTO, filling ProjectName from the lookup (null if unassigned
    // or the id isn't in the map); Tags coalesces null -> empty list to keep the DTO non-null.
    private static EntryDto ToDto(TogglTimeEntry entry, IReadOnlyDictionary<long, string> names) => new(
        entry.Id, entry.Description, entry.Start, entry.Duration, entry.ProjectId,
        entry.ProjectId is { } pid && names.TryGetValue(pid, out var name) ? name : null,
        entry.TaskId, entry.Tags ?? []);
}
