using TogglMcp.Contracts;

namespace TogglMcp.Features.SuggestProject;

/// <summary>
/// <c>Matched=true</c> means a learned rule fired (act silently); <c>false</c> is a
/// best-effort guess — confirm with the user, then persist their choice via add_mapping_rule.
/// </summary>
public sealed record SuggestProjectResult(
    bool Matched,                             // true only when a learned rule fired; a fuzzy guess is always false
    long? ProjectId,                          // best project's id; null when no candidate cleared the confidence floor
    string? ProjectName,                      // best project's name; null in the same no-match case
    double Confidence,                        // 0..1 — 0.95 for a rule hit, otherwise the rounded similarity score
    string Reason,                            // human-readable rationale for the caller/user (why this guess, what to do next)
    IReadOnlyList<ProjectDto> Candidates);    // up to top-3 projects by similarity, to offer the user when unsure
