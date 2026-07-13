using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Contracts;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListProjects;

[McpServerToolType]
public static class ListProjectsTool
{
    [McpServerTool(Name = "list_projects", ReadOnly = true, OpenWorld = true),
     Description("List the active projects in the shared Toggl workspace (id, name, active, client). Use these ids for plan_week and suggest_project.")]
    public static Task<IReadOnlyList<ProjectDto>> ListProjects(ITogglClient toggl, CancellationToken cancellationToken)
        => new ListProjectsHandler(toggl).HandleAsync(cancellationToken);
}
