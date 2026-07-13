using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListTasks;

[McpServerToolType]
public static class ListTasksTool
{
    [McpServerTool(Name = "list_tasks", ReadOnly = true, OpenWorld = true),
     Description("List the tasks under a project (Toggl 'Project • Task' entries). Requires a project id from list_projects. A task_id can only be used together with its project_id.")]
    public static Task<IReadOnlyList<TaskDto>> ListTasks(
        ITogglClient toggl,
        [Description("Project id to list tasks for (from list_projects).")] long projectId,
        CancellationToken cancellationToken)
        => new ListTasksHandler(toggl).HandleAsync(projectId, cancellationToken);
}
