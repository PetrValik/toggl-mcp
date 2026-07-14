using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.AddMappingRule;

/// <summary>
/// MCP endpoint for the add_mapping_rule slice: binds tool args and DI services, then delegates
/// to <see cref="AddMappingRuleHandler"/>. Marked Idempotent (re-teaching the same rule is safe —
/// it replaces) and OpenWorld (it talks to the external Toggl workspace).
/// </summary>
[McpServerToolType]
public static class AddMappingRuleTool
{
    /// <summary>
    /// Teaches a keyword->project rule so future project suggestions can resolve this kind of work automatically.
    /// </summary>
    /// <remarks>
    /// <paramref name="toggl"/>, <paramref name="rules"/> and <paramref name="tokens"/> are injected from DI;
    /// only <paramref name="keyword"/> and <paramref name="projectId"/> form the tool's JSON input schema.
    /// </remarks>
    [McpServerTool(Name = "add_mapping_rule", Idempotent = true, OpenWorld = true),
     Description("Teach a rule: when the source text contains 'keyword', map it to this Toggl project id. Replaces any existing rule for the same keyword. Makes future suggest_project calls silent for this kind of work.")]
    public static Task<AddMappingRuleResult> AddMappingRule(
        ITogglClient toggl,
        IMappingRuleStore rules,
        ITogglTokenProvider tokens,
        [Description("Keyword to match (case-insensitive) in the calendar name or title.")] string keyword,
        [Description("Toggl project id to map to (from list_projects).")] long projectId,
        CancellationToken cancellationToken)
        // Thin endpoint: construct the handler with injected services and hand off — no logic here.
        => new AddMappingRuleHandler(toggl, rules, tokens).HandleAsync(keyword, projectId, cancellationToken);
}
