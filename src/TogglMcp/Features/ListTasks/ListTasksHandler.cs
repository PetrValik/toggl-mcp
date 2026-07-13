using TogglMcp.Toggl;

namespace TogglMcp.Features.ListTasks;

internal sealed class ListTasksHandler(ITogglClient toggl)
{
    public async Task<IReadOnlyList<TaskDto>> HandleAsync(long projectId, CancellationToken cancellationToken)
    {
        var tasks = await toggl.GetTasksAsync(projectId, cancellationToken);
        return tasks.Select(task => new TaskDto(task.Id, task.Name, task.ProjectId, task.Active)).ToList();
    }
}
