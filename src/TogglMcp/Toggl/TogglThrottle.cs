using System.Diagnostics;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Toggl;

/// <summary>
/// Process-wide request spacer. Toggl enforces a ~1 req/s leaky bucket per token;
/// holding the gate across the spacing delay serialises outbound calls so they
/// leave at most one per <see cref="TogglOptions.MinRequestIntervalMs"/>.
/// Singleton. (A single global bucket is conservative once multiple users share
/// the server — each Toggl token has its own bucket — but it can never *over*-send.
/// Per-token buckets are a Phase 3 refinement.)
/// </summary>
public sealed class TogglThrottle(IOptions<TogglOptions> options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastTimestamp;

    public async Task WaitTurnAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var min = TimeSpan.FromMilliseconds(options.Value.MinRequestIntervalMs);
            if (_lastTimestamp != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp);
                if (elapsed < min)
                    await Task.Delay(min - elapsed, ct).ConfigureAwait(false);
            }
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
        finally
        {
            _gate.Release();
        }
    }
}
