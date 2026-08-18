using TogglMcp.Tokens;

namespace TogglMcp.Tests.Support;

/// <summary>Supplies a test identity; the token itself is never used because Toggl is faked at the client boundary.</summary>
public sealed class FakeTokenProvider(string userKey) : ITogglTokenProvider
{
    /// <inheritdoc />
    public string UserKey { get; } = userKey;

    /// <inheritdoc />
    public ValueTask<string> GetApiTokenAsync(CancellationToken ct = default) => ValueTask.FromResult("not-a-real-token");
}
