using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListTasks;

/// <summary>
/// MCP endpoint for the list_tasks slice: binds the tool's JSON args, delegates to
/// <see cref="ListTasksHandler"/>, and returns the result. Thin by design — no logic here.
/// </summary>
[McpServerToolType]
public static class ListTasksTool
{
    /// <summary>
    /// list_tasks tool entry point. <paramref name="toggl"/> and <paramref name="cancellationToken"/>
    /// are DI/framework-injected; <paramref name="projectId"/> is the only client-facing input.
    /// </summary>
    [McpServerTool(Name = "list_tasks", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description("List the tasks under a project (Toggl 'Project • Task' entries). Requires a project id from list_projects. task_id can only be used together with its project_id.")]
    public static Task<IReadOnlyList<TaskDto>> ListTasks(
        ITogglClient toggl,
        [Description("Project id to list tasks for (from list_projects).")] long projectId,
        CancellationToken cancellationToken)
        => new ListTasksHandler(toggl).HandleAsync(projectId, cancellationToken);
}
