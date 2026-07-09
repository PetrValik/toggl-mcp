namespace TogglMcp.Mcp;

// Stable, purpose-built shapes returned to the model (decoupled from Toggl wire models).

public sealed record ProjectDto(long Id, string Name, bool Active, long? ClientId);

public sealed record TaskDto(long Id, string Name, long ProjectId, bool Active);

public sealed record EntryDto(
    long Id,
    string? Description,
    string? Start,
    long DurationSeconds,
    long? ProjectId,
    string? ProjectName,
    long? TaskId,
    IReadOnlyList<string> Tags);

/// <summary>Result of <c>log_time</c>. Status is <c>created</c>, <c>duplicate</c>, or <c>error</c>.</summary>
public sealed record LogTimeResult(string Status, string Message, EntryDto? Entry);

/// <summary>
/// Result of <c>suggest_project</c>. <c>Matched=true</c> means a user rule fired (act
/// silently); <c>false</c> means this is a best-effort guess — confirm with the user
/// before logging, then teach it with <c>add_mapping_rule</c>.
/// </summary>
public sealed record SuggestProjectResult(
    bool Matched,
    long? ProjectId,
    string? ProjectName,
    double Confidence,
    string Reason,
    IReadOnlyList<ProjectDto> Candidates);

public sealed record AddMappingRuleResult(bool Ok, string Message, string Keyword, long ProjectId, string? ProjectName);
