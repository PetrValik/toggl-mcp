using TogglMcp.Contracts;
using TogglMcp.Toggl;

namespace TogglMcp.Features.ListProjects;

internal sealed class ListProjectsHandler(ITogglClient toggl)
{
    public async Task<IReadOnlyList<ProjectDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        return projects.Select(project => project.ToDto()).ToList();
    }
}
