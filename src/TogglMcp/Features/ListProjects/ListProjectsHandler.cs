using TogglMcp.Contracts;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListProjects;

/// <summary>
/// Logic layer for the list_projects slice: fetches the workspace's projects from Toggl
/// and maps them to the client-facing ProjectDto shape. Read-only, no locking involved.
/// </summary>
internal sealed class ListProjectsHandler(ITogglClient toggl)
{
    /// <summary>
    /// Pulls the projects from the shared Toggl workspace and projects each onto ProjectDto.
    /// </summary>
    /// <returns>The workspace projects as DTOs, in the order Toggl returns them.</returns>
    public async Task<IReadOnlyList<ProjectDto>> HandleAsync(CancellationToken cancellationToken)
    {
        // Toggl returns its own project model; ToDto() strips it down to the fields the client contract exposes.
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        return projects.Select(project => project.ToDto()).ToList();
    }
}
