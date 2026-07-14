using System.Text.Json.Serialization;

namespace TogglMcp.Toggl;

// Toggl Track API v9 wire models. JSON is snake_case; a SnakeCaseLower naming
// policy on the serializer maps these PascalCase members automatically, so no
// per-property [JsonPropertyName] is needed.

/// <summary><c>/me</c> response — the authenticated user and the workspace to default writes to.</summary>
public sealed record TogglMe
{
    public long Id { get; init; }
    /// <summary>Workspace used when no config override is set (see <c>GetWorkspaceIdAsync</c>).</summary>
    public long DefaultWorkspaceId { get; init; }
    public string? Fullname { get; init; }
    public string? Email { get; init; }
}

/// <summary>A Toggl project; the target a mapping rule resolves a keyword to.</summary>
public sealed record TogglProject
{
    public long Id { get; init; }
    public long WorkspaceId { get; init; }
    public string Name { get; init; } = "";   // defaulted so a missing JSON field never yields null
    public bool Active { get; init; }
    public string? Color { get; init; }   // hex swatch, may be absent
    public long? ClientId { get; init; }   // owning client, null when unassigned
}

/// <summary>A task within a project (Starter+ plans); an optional finer target than the project.</summary>
public sealed record TogglTask
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long ProjectId { get; init; }
    public long WorkspaceId { get; init; }
    public bool Active { get; init; }
}

/// <summary>A logged time entry as returned by Toggl (read side of the back-fill).</summary>
public sealed record TogglTimeEntry
{
    public long Id { get; init; }
    public long WorkspaceId { get; init; }
    public long? ProjectId { get; init; }   // null == "No Project"
    public long? TaskId { get; init; }   // null when the entry is not assigned to a task
    public string? Description { get; init; }
    /// <summary>RFC3339 string as returned by Toggl; offset preserved.</summary>
    public string? Start { get; init; }
    public string? Stop { get; init; }   // null while the entry is still running
    /// <summary>Seconds. Negative for a running entry (Toggl encodes -(start epoch)).</summary>
    public long Duration { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }   // null when the entry has no tags
}

/// <summary>
/// Body for <c>POST /api/v9/workspaces/{wid}/time_entries</c>. Null fields are
/// omitted (serializer configured with WhenWritingNull) so "No Project" stays valid.
/// </summary>
public sealed record CreateTimeEntryRequest
{
    /// <summary>Client identifier Toggl records on the entry (e.g. the app name); required by the API.</summary>
    public required string CreatedWith { get; init; }
    public required long WorkspaceId { get; init; }
    /// <summary>RFC3339 with the source event's own offset — never hardcode +02:00/DST.</summary>
    public required string Start { get; init; }
    /// <summary>Positive seconds → a completed entry.</summary>
    public required long Duration { get; init; }
    public string? Description { get; init; }
    public long? ProjectId { get; init; }   // null → "No Project" (field omitted when null)
    public long? TaskId { get; init; }   // null → no task
    /// <summary>String names auto-create tags in the workspace.</summary>
    public IReadOnlyList<string>? Tags { get; init; }
}
