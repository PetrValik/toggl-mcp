namespace TogglMcp.Toggl;

/// <summary>Thin, per-request-authenticated wrapper over the Toggl Track API v9.</summary>
public interface ITogglClient
{
    /// <summary>Config override, else the user's <c>default_workspace_id</c> (cached).</summary>
    Task<long> GetWorkspaceIdAsync(CancellationToken ct = default);

    /// <summary>Current authenticated user (<c>/me</c>) — source of the default workspace id.</summary>
    Task<TogglMe> GetMeAsync(CancellationToken ct = default);

    /// <summary>Active projects in the resolved workspace; used to resolve mapping rules to project ids.</summary>
    Task<IReadOnlyList<TogglProject>> GetProjectsAsync(CancellationToken ct = default);

    /// <summary>Tasks under a project (Starter+ feature). Requires the project id.</summary>
    Task<IReadOnlyList<TogglTask>> GetTasksAsync(long projectId, CancellationToken ct = default);

    /// <summary>
    /// Existing entries in the half-open range [from, to). <paramref name="toExclusive"/>
    /// is exclusive (Toggl's end_date semantics). Used to detect what a week already holds
    /// before back-filling.
    /// </summary>
    Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken ct = default);

    /// <summary>Persists one completed time entry (the back-fill write). Non-idempotent — see the implementation's retry policy.</summary>
    Task<TogglTimeEntry> CreateTimeEntryAsync(CreateTimeEntryRequest request, CancellationToken ct = default);
}
