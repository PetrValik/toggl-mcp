namespace TogglMcp.Features.AddMappingRule;

/// <summary>
/// Result of teaching a keyword->project mapping rule, returned to the MCP client.
/// </summary>
/// <param name="Ok">True when the rule was persisted; false on validation/lookup failure (see <paramref name="Message"/>).</param>
/// <param name="Message">Human-readable outcome (success confirmation or the reason it was rejected).</param>
/// <param name="Keyword">Echoed keyword — trimmed on success, raw input on failure.</param>
/// <param name="ProjectId">The Toggl project id the rule points at.</param>
/// <param name="ProjectName">Resolved project name; null when the project could not be found or input was empty.</param>
public sealed record AddMappingRuleResult(bool Ok, string Message, string Keyword, long ProjectId, string? ProjectName);
