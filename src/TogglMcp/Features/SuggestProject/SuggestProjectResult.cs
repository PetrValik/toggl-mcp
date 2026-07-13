using TogglMcp.Contracts;

namespace TogglMcp.Features.SuggestProject;

/// <summary>
/// <c>Matched=true</c> means a learned rule fired (act silently); <c>false</c> is a
/// best-effort guess — confirm with the user, then persist their choice via add_mapping_rule.
/// </summary>
public sealed record SuggestProjectResult(
    bool Matched,
    long? ProjectId,
    string? ProjectName,
    double Confidence,
    string Reason,
    IReadOnlyList<ProjectDto> Candidates);
