using System.Text.Json.Serialization;

namespace TogglMcp.Toggl;

// Toggl Track API v9 wire models. JSON is snake_case; a SnakeCaseLower naming
// policy on the serializer maps these PascalCase members automatically, so no
// per-property [JsonPropertyName] is needed.

public sealed record TogglMe
{
    public long Id { get; init; }
    public long DefaultWorkspaceId { get; init; }
    public string? Fullname { get; init; }
    public string? Email { get; init; }
}

public sealed record TogglProject
{
    public long Id { get; init; }
    public long WorkspaceId { get; init; }
    public string Name { get; init; } = "";
    public bool Active { get; init; }
    public string? Color { get; init; }
    public long? ClientId { get; init; }
}

public sealed record TogglTask
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long ProjectId { get; init; }
    public long WorkspaceId { get; init; }
    public bool Active { get; init; }
}

public sealed record TogglTimeEntry
{
    public long Id { get; init; }
    public long WorkspaceId { get; init; }
    public long? ProjectId { get; init; }
    public long? TaskId { get; init; }
    public string? Description { get; init; }
    /// <summary>RFC3339 string as returned by Toggl; offset preserved.</summary>
    public string? Start { get; init; }
    public string? Stop { get; init; }
    /// <summary>Seconds. Negative for a running entry (Toggl encodes -(start epoch)).</summary>
    public long Duration { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// Body for <c>POST /api/v9/workspaces/{wid}/time_entries</c>. Null fields are
/// omitted (serializer configured with WhenWritingNull) so "No Project" stays valid.
/// </summary>
public sealed record CreateTimeEntryRequest
{
    public required string CreatedWith { get; init; }
    public required long WorkspaceId { get; init; }
    /// <summary>RFC3339 with the source event's own offset — never hardcode +02:00/DST.</summary>
    public required string Start { get; init; }
    /// <summary>Positive seconds → a completed entry.</summary>
    public required long Duration { get; init; }
    public string? Description { get; init; }
    public long? ProjectId { get; init; }
    public long? TaskId { get; init; }
    /// <summary>String names auto-create tags in the workspace.</summary>
    public IReadOnlyList<string>? Tags { get; init; }
}
