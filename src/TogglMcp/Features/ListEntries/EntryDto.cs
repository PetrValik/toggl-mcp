namespace TogglMcp.Features.ListEntries;

public sealed record EntryDto(
    long Id,
    string? Description,
    string? Start,
    long DurationSeconds,
    long? ProjectId,
    string? ProjectName,
    long? TaskId,
    IReadOnlyList<string> Tags);
