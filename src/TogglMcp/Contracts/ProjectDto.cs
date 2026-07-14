using TogglMcp.Toggl;

namespace TogglMcp.Contracts;

/// <summary>
/// Module-shared projection of a Toggl project. Shared because more than one slice
/// (ListProjects, SuggestProject) returns it — analogous to a module-shared response.
/// </summary>
/// <param name="Active">Toggl's archived flag inverted: false means the project is archived and should not receive new entries.</param>
/// <param name="ClientId">Owning Toggl client id, or null when the project is not attached to a client.</param>
public sealed record ProjectDto(long Id, string Name, bool Active, long? ClientId);

/// <summary>Maps the Toggl API model to the slice-facing <see cref="ProjectDto"/>, keeping the wire model out of tool responses.</summary>
public static class ProjectMappings
{
    // Projects the raw Toggl project down to the fields slices expose.
    public static ProjectDto ToDto(this TogglProject p) => new(p.Id, p.Name, p.Active, p.ClientId);
}
