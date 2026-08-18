using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Planning;

/// <summary>
/// JSON-file ledger: one file per user under <c>{DataDir}/ledger/</c>, mirroring
/// <see cref="Mapping.FileMappingRuleStore"/> (single gate around every read-modify-write, atomic
/// temp-file rename). Write volume is one small append per committed entry, so a global lock costs
/// nothing.
///
/// MUST live on the persisted volume: losing it doesn't corrupt anything, but the server forgets
/// which entries it wrote and falls back to matching on content alone.
/// </summary>
public sealed class FileLedgerStore : ILedgerStore
{
    /// <summary>
    /// How long a record is kept, counted from its entry's day. Well past the point where a week
    /// could still be reopened and re-planned, so the file stays small without ever dropping a
    /// record dedup might still need.
    /// </summary>
    public const int RetentionDays = 400;

    // Indented on purpose: these files are human-inspectable when diagnosing a dedup surprise.
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    // Single global lock guarding every read-modify-write; the write volume is tiny so contention is a non-issue.
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Absolute path to the per-user ledger directory ({DataDir}/ledger), created at startup.
    private readonly string _dir;
    // Injected clock; the retention horizon is measured against it so pruning is testable.
    private readonly TimeProvider _clock;

    /// <summary>Resolves the ledger directory from <see cref="StorageOptions.DataDir"/> and ensures it exists.</summary>
    public FileLedgerStore(IOptions<StorageOptions> options, TimeProvider clock)
    {
        _dir = Path.Combine(options.Value.DataDir, "ledger");
        _clock = clock;
        Directory.CreateDirectory(_dir); // idempotent: no-op if it already exists
    }

    /// <inheritdoc />
    public async Task RecordAsync(string userKey, string sourceUid, long entryId, DateOnly day, CancellationToken ct = default)
    {
        var path = PathFor(userKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var records = (await ReadAsync(path, ct).ConfigureAwait(false)).ToList();
            // Last write wins per sourceUid: re-logging an entry that was deleted in Toggl must
            // leave one record pointing at the entry that exists now, not two competing ones.
            records.RemoveAll(r => string.Equals(r.SourceUid, sourceUid, StringComparison.Ordinal));
            records.Add(new LedgerRecord(sourceUid, entryId, day, _clock.GetUtcNow()));

            // Prune on write — the only moment the file is open anyway.
            var horizon = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(-RetentionDays);
            records.RemoveAll(r => r.Day < horizon);

            var tmp = path + ".tmp";
            // Write to a sibling temp file, then rename over the target so a crash mid-write can't leave a truncated file.
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(records, Json), ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true); // atomic replace
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerRecord>> GetAsync(string userKey, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default)
    {
        var path = PathFor(userKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var records = await ReadAsync(path, ct).ConfigureAwait(false);
            return records.Where(r => r.Day >= fromInclusive && r.Day <= toInclusive).ToList();
        }
        finally { _gate.Release(); }
    }

    // Deserialises the ledger file; a missing file (new user) is an empty ledger, not an error.
    private static async Task<IReadOnlyList<LedgerRecord>> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var records = await JsonSerializer.DeserializeAsync<List<LedgerRecord>>(stream, Json, ct).ConfigureAwait(false);
        return records ?? [];
    }

    // Maps a userKey to its JSON file path via a SHA-256 hex hash of the raw key — collision-free
    // and traversal-safe, exactly as the mapping store does it.
    private string PathFor(string userKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userKey)));
        return Path.Combine(_dir, hash + ".json");
    }
}
