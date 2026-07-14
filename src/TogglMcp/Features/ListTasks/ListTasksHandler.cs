using TogglMcp.Toggl;

namespace TogglMcp.Features.ListTasks;

/// <summary>
/// The only logic layer for the list_tasks slice: fetches a project's tasks from Toggl
/// and projects them to the slice-local <see cref="TaskDto"/>. Read-only lookup — no
/// lock or planning concerns apply.
/// </summary>
internal sealed class ListTasksHandler(ITogglClient toggl)
{
    /// <summary>
    /// Fetches all tasks under <paramref name="projectId"/> and maps them to <see cref="TaskDto"/>.
    /// </summary>
    /// <param name="projectId">Toggl project whose tasks to list.</param>
    /// <returns>Every task on the project (active and inactive alike — no filtering here).</returns>
    public async Task<IReadOnlyList<TaskDto>> HandleAsync(long projectId, CancellationToken cancellationToken)
    {
        var tasks = await toggl.GetTasksAsync(projectId, cancellationToken);
        // Drop unused Toggl fields; keep only what the client needs to target an entry.
        return tasks.Select(task => new TaskDto(task.Id, task.Name, task.ProjectId, task.Active)).ToList();
    }
}
