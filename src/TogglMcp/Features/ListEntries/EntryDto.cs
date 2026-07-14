namespace TogglMcp.Features.ListEntries;

/// <summary>
/// Slice-local, model-facing projection of one Toggl time entry — a flattened, name-resolved
/// view returned by list_entries so the model can see what's already logged before back-filling.
/// </summary>
public sealed record EntryDto(
    long Id,
    string? Description,
    string? Start,                     // Toggl start timestamp as returned (ISO-8601 string), null if absent
    long DurationSeconds,              // entry length in seconds; negative while a timer is still running
    long? ProjectId,                   // null when the entry has no project assigned
    string? ProjectName,               // resolved from ProjectId; null if unassigned or the project lookup missed
    long? TaskId,                      // null when the entry has no task
    IReadOnlyList<string> Tags);       // never null — empty list when the entry is untagged
