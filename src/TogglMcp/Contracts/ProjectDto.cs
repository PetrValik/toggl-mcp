using TogglMcp.Toggl;

namespace TogglMcp.Contracts;

/// <summary>
/// Module-shared projection of a Toggl project. Shared because more than one slice
/// (ListProjects, SuggestProject) returns it — analogous to a module-shared response.
/// </summary>
public sealed record ProjectDto(long Id, string Name, bool Active, long? ClientId);

public static class ProjectMappings
{
    public static ProjectDto ToDto(this TogglProject p) => new(p.Id, p.Name, p.Active, p.ClientId);
}
