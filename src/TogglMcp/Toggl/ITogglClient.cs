namespace TogglMcp.Toggl;

/// <summary>Thin, per-request-authenticated wrapper over the Toggl Track API v9.</summary>
public interface ITogglClient
{
    /// <summary>Config override, else the user's <c>default_workspace_id</c> (cached).</summary>
    Task<long> GetWorkspaceIdAsync(CancellationToken ct = default);

    Task<TogglMe> GetMeAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TogglProject>> GetProjectsAsync(CancellationToken ct = default);

    /// <summary>Tasks under a project (Starter+ feature). Requires the project id.</summary>
    Task<IReadOnlyList<TogglTask>> GetTasksAsync(long projectId, CancellationToken ct = default);

    /// <summary><paramref name="toExclusive"/> is exclusive (Toggl's end_date semantics).</summary>
    Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken ct = default);

    Task<TogglTimeEntry> CreateTimeEntryAsync(CreateTimeEntryRequest request, CancellationToken ct = default);
}
