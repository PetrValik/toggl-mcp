using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.AddMappingRule;

internal sealed class AddMappingRuleHandler(
    ITogglClient toggl,
    IMappingRuleStore rules,
    ITogglTokenProvider tokens)
{
    public async Task<AddMappingRuleResult> HandleAsync(string keyword, long projectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new AddMappingRuleResult(false, "keyword must not be empty.", keyword, projectId, null);

        var projects = await toggl.GetProjectsAsync(cancellationToken);
        var project = projects.FirstOrDefault(candidate => candidate.Id == projectId);
        if (project is null)
            return new AddMappingRuleResult(false, $"No active project with id {projectId} in this workspace.", keyword, projectId, null);

        await rules.AddRuleAsync(tokens.UserKey, new MappingRule
        {
            Keyword = keyword.Trim(),
            ProjectId = projectId,
            ProjectName = project.Name,
        }, cancellationToken);

        return new AddMappingRuleResult(true, $"Rule saved: '{keyword.Trim()}' -> {project.Name}.", keyword.Trim(), projectId, project.Name);
    }
}
