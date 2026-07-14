using System.ComponentModel;
using ModelContextProtocol.Server;
using TogglMcp.Mapping;
using TogglMcp.Toggl;
using TogglMcp.Tokens;

namespace TogglMcp.Features.SuggestProject;

/// <summary>
/// MCP endpoint for the suggest_project slice: binds the tool's JSON args, constructs the
/// handler with DI-injected services, and returns its result. Read-only — it never writes
/// to Toggl, it only recommends a project so the caller can log against it.
/// </summary>
[McpServerToolType]
public static class SuggestProjectTool
{
    /// <summary>
    /// Recommends a Toggl project for a work item from its source calendar/list name and title.
    /// Leading service parameters are DI-injected; the [Description] params form the tool's input schema.
    /// </summary>
    /// <returns>A <see cref="SuggestProjectResult"/>: a silent rule match, or a best-effort guess to confirm.</returns>
    [McpServerTool(Name = "suggest_project", ReadOnly = true, Idempotent = true, OpenWorld = true),
     Description(
        "Suggest a Toggl project for a piece of work given its source calendar/list name and title. If a learned rule matches, Matched=true (use it silently). Otherwise it's a best-effort guess (Matched=false) — confirm with the user, then persist their choice with add_mapping_rule.")]
    public static Task<SuggestProjectResult> SuggestProject(
        ITogglClient toggl,
        IMappingRuleStore rules,
        ITogglTokenProvider tokens,
        [Description("Name of the source calendar / board / list the item came from (may be empty).")] string calendarName,
        [Description("Title/summary of the work item.")] string title,
        CancellationToken cancellationToken)
        => new SuggestProjectHandler(toggl, rules, tokens).HandleAsync(calendarName, title, cancellationToken);
}
