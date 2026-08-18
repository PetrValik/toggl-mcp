using System.Globalization;
using TogglMcp.Toggl;

namespace TogglMcp.Tests.Support;

/// <summary>
/// An in-memory stand-in for Toggl. It is a substitute only for the HTTP boundary — it stores
/// real entries and answers real queries about them, so a test can commit a plan and then ask
/// what Toggl now holds, exactly as the live server would.
/// </summary>
public sealed class FakeTogglClient : ITogglClient
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    private readonly List<TogglTimeEntry> _entries = [];
    private long _nextId = 900;

    /// <summary>Projects the handlers resolve names/ids against.</summary>
    public List<TogglProject> Projects { get; } =
    [
        new() { Id = 123, WorkspaceId = 1, Name = "Platform", Active = true },
        new() { Id = 456, WorkspaceId = 1, Name = "Website", Active = true },
    ];

    /// <summary>Everything currently "in Toggl", newest last.</summary>
    public IReadOnlyList<TogglTimeEntry> Entries => _entries;

    /// <summary>Seeds an entry as if it had been logged outside this server (by hand, or before the ledger existed).</summary>
    public TogglTimeEntry Seed(string start, long duration, long? projectId, string? description)
    {
        var entry = new TogglTimeEntry
        {
            Id = ++_nextId,
            WorkspaceId = 1,
            Start = start,
            Duration = duration,
            ProjectId = projectId,
            Description = description,
        };
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Removes an entry, as the user deleting it in the Toggl UI would.</summary>
    public void Delete(long entryId) => _entries.RemoveAll(e => e.Id == entryId);

    /// <inheritdoc />
    public Task<long> GetWorkspaceIdAsync(CancellationToken ct = default) => Task.FromResult(1L);

    /// <inheritdoc />
    public Task<TogglMe> GetMeAsync(CancellationToken ct = default) =>
        Task.FromResult(new TogglMe { Id = 7, DefaultWorkspaceId = 1, Fullname = "Test User" });

    /// <inheritdoc />
    public Task<IReadOnlyList<TogglProject>> GetProjectsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TogglProject>>(Projects);

    /// <inheritdoc />
    public Task<IReadOnlyList<TogglTask>> GetTasksAsync(long projectId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TogglTask>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken ct = default)
    {
        var hits = _entries.Where(e => LocalDayOf(e.Start) is { } day && day >= fromInclusive && day < toExclusive).ToList();
        return Task.FromResult<IReadOnlyList<TogglTimeEntry>>(hits);
    }

    /// <inheritdoc />
    public Task<TogglTimeEntry> CreateTimeEntryAsync(CreateTimeEntryRequest request, CancellationToken ct = default)
    {
        var entry = new TogglTimeEntry
        {
            Id = ++_nextId,
            WorkspaceId = request.WorkspaceId,
            Start = request.Start,
            Duration = request.Duration,
            ProjectId = request.ProjectId,
            TaskId = request.TaskId,
            Description = request.Description,
            Tags = request.Tags,
        };
        _entries.Add(entry);
        return Task.FromResult(entry);
    }

    // Which Prague day an entry counts for — the same rule the lock policy applies.
    private static DateOnly? LocalDayOf(string? start) =>
        DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(dto, Prague).DateTime)
            : null;
}
