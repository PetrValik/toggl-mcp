using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.AddMappingRule;

/// <summary>
/// Sole logic layer for the add_mapping_rule slice: validates the keyword, verifies the
/// target project actually exists live in the workspace, then persists the rule per-user.
/// </summary>
internal sealed class AddMappingRuleHandler(
    ITogglClient toggl,
    IMappingRuleStore rules,
    ITogglTokenProvider tokens)
{
    /// <summary>
    /// Validates and stores a keyword->project mapping. The project id is checked against the
    /// live workspace project list so we never persist a rule pointing at a stale/deleted project.
    /// </summary>
    /// <param name="keyword">Text to match (stored trimmed); rejected if empty/whitespace.</param>
    /// <param name="projectId">Toggl project id the keyword should resolve to.</param>
    /// <returns>Ok result carrying the trimmed keyword + resolved name, or a failure result explaining why.</returns>
    public async Task<AddMappingRuleResult> HandleAsync(string keyword, long projectId, CancellationToken cancellationToken)
    {
        // Guard: an empty keyword would never match anything, so reject before touching Toggl.
        if (string.IsNullOrWhiteSpace(keyword))
            return new AddMappingRuleResult(false, "keyword must not be empty.", keyword, projectId, null);

        // Confirm the project exists in the live workspace (GetProjects returns active projects only).
        var projects = await toggl.GetProjectsAsync(cancellationToken);
        var project = projects.FirstOrDefault(candidate => candidate.Id == projectId);
        if (project is null)
            return new AddMappingRuleResult(false, $"No active project with id {projectId} in this workspace.", keyword, projectId, null);

        // Persist under the current user's key; cache the project name for display (see MappingRule.ProjectName).
        await rules.AddRuleAsync(tokens.UserKey, new MappingRule
        {
            Keyword = keyword.Trim(),
            ProjectId = projectId,
            ProjectName = project.Name,
        }, cancellationToken);

        return new AddMappingRuleResult(true, $"Rule saved: '{keyword.Trim()}' -> {project.Name}.", keyword.Trim(), projectId, project.Name);
    }
}
