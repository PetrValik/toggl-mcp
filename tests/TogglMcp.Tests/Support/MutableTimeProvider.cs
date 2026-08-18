namespace TogglMcp.Tests.Support;

/// <summary>
/// A clock the test moves by hand. Everything time-dependent in the server takes an injected
/// <see cref="TimeProvider"/> precisely so retention horizons and week locks can be exercised
/// without waiting for a real calendar.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>The instant every call to <see cref="GetUtcNow"/> returns until it is reassigned.</summary>
    public DateTimeOffset Now { get; set; } = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves the clock forward, for readability at the call site.</summary>
    public void Advance(TimeSpan by) => Now += by;
}
