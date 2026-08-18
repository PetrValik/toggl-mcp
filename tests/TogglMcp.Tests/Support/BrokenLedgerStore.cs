using TogglMcp.Planning;

namespace TogglMcp.Tests.Support;

/// <summary>A ledger whose disk is gone: reads work, every write fails.</summary>
public sealed class BrokenLedgerStore : ILedgerStore
{
    /// <inheritdoc />
    public Task RecordAsync(string userKey, string sourceUid, long entryId, DateOnly day, CancellationToken ct = default) =>
        throw new IOException("no space left on device");

    /// <inheritdoc />
    public Task<IReadOnlyList<LedgerRecord>> GetAsync(string userKey, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LedgerRecord>>([]);
}
