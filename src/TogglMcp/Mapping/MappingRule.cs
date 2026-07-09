namespace TogglMcp.Mapping;

/// <summary>
/// A learned "if the source text contains <see cref="Keyword"/>, it's this project"
/// rule. Per-user; the project list itself is shared (pulled live from the workspace).
/// </summary>
public sealed record MappingRule
{
    public required string Keyword { get; init; }
    public required long ProjectId { get; init; }
    /// <summary>Cached project name at rule-creation time, for display only.</summary>
    public string? ProjectName { get; init; }
}
