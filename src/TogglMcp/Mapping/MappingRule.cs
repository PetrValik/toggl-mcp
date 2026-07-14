namespace TogglMcp.Mapping;

/// <summary>
/// A learned "if the source text contains <see cref="Keyword"/>, it's this project"
/// rule. Per-user; the project list itself is shared (pulled live from the workspace).
/// </summary>
public sealed record MappingRule
{
    // Substring matched (case-insensitively) against the source text; unique per user — the key of the rule.
    public required string Keyword { get; init; }
    // Toggl project the keyword resolves to; the authoritative target (names can drift, ids don't).
    public required long ProjectId { get; init; }
    /// <summary>Cached project name at rule-creation time, for display only.</summary>
    public string? ProjectName { get; init; }
}
