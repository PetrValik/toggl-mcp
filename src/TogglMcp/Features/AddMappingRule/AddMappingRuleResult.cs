namespace TogglMcp.Features.AddMappingRule;

public sealed record AddMappingRuleResult(bool Ok, string Message, string Keyword, long ProjectId, string? ProjectName);
