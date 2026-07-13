using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.AddMappingRule;

[McpServerToolType]
public static class AddMappingRuleTool
{
    [McpServerTool(Name = "add_mapping_rule", Idempotent = true, OpenWorld = true),
     Description("Teach a rule: when the source text contains 'keyword', map it to this Toggl project id. Replaces any existing rule for the same keyword. Makes future suggest_project calls silent for this kind of work.")]
    public static Task<AddMappingRuleResult> AddMappingRule(
        ITogglClient toggl,
        IMappingRuleStore rules,
        ITogglTokenProvider tokens,
        [Description("Keyword to match (case-insensitive) in the calendar name or title.")] string keyword,
        [Description("Toggl project id to map to (from list_projects).")] long projectId,
        CancellationToken cancellationToken)
        => new AddMappingRuleHandler(toggl, rules, tokens).HandleAsync(keyword, projectId, cancellationToken);
}
