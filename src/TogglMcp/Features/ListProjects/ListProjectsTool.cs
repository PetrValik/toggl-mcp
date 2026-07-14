using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Contracts;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListProjects;

/// <summary>
/// MCP endpoint for the list_projects slice: binds the tool call to ListProjectsHandler.
/// Exposes the workspace's projects so the client can resolve project ids for other tools.
/// </summary>
[McpServerToolType]
public static class ListProjectsTool
{
    // Read-only + idempotent (safe to call repeatedly, mutates nothing); OpenWorld because it reads live Toggl state.
    /// <summary>
    /// Tool entry point: constructs the handler with the DI-injected Toggl client and returns the project list.
    /// </summary>
    /// <param name="toggl">Toggl API client, injected from DI (not part of the tool's JSON input schema).</param>
    [McpServerTool(Name = "list_projects", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description("List the active projects in the shared Toggl workspace (id, name, active, client). Use these ids for plan_week and suggest_project.")]
    public static Task<IReadOnlyList<ProjectDto>> ListProjects(ITogglClient toggl, CancellationToken cancellationToken)
        => new ListProjectsHandler(toggl).HandleAsync(cancellationToken);
}
