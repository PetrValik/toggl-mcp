using System.Globalization;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListEntries;

internal sealed class ListEntriesHandler(ITogglClient toggl)
{
    public async Task<IReadOnlyList<EntryDto>> HandleAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = ParseDate(from) ?? today.AddDays(-7);
        var toDate = ParseDate(to) ?? today;
        // Toggl end_date is exclusive; make 'to' inclusive.
        var entries = await toggl.GetTimeEntriesAsync(fromDate, toDate.AddDays(1), cancellationToken);

        var names = await ProjectNamesAsync(entries.Select(entry => entry.ProjectId), cancellationToken);
        return entries.Select(entry => ToDto(entry, names)).ToList();
    }

    private async Task<IReadOnlyDictionary<long, string>> ProjectNamesAsync(IEnumerable<long?> projectIds, CancellationToken cancellationToken)
    {
        if (!projectIds.Any(id => id is not null)) return new Dictionary<long, string>();
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        return projects.ToDictionary(project => project.Id, project => project.Name);
    }

    private static EntryDto ToDto(TogglTimeEntry entry, IReadOnlyDictionary<long, string> names) => new(
        entry.Id, entry.Description, entry.Start, entry.Duration, entry.ProjectId,
        entry.ProjectId is { } pid && names.TryGetValue(pid, out var name) ? name : null,
        entry.TaskId, entry.Tags ?? []);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
