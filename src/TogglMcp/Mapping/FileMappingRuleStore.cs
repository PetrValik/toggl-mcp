using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Mapping;

/// <summary>
/// JSON-file mapping store: one file per user under <c>{DataDir}/mappings/</c>.
/// A single gate serialises read-modify-write; volume here is tiny (one write when
/// a new rule is learned), so a global lock is fine.
/// </summary>
public sealed class FileMappingRuleStore : IMappingRuleStore
{
    // Indented on purpose: these files are human-inspectable / hand-editable.
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    // Single global lock guarding every read-modify-write; the write volume is tiny so contention is a non-issue.
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Absolute path to the per-user mappings directory ({DataDir}/mappings), created at startup.
    private readonly string _dir;

    /// <summary>Resolves the mappings directory from <see cref="StorageOptions.DataDir"/> and ensures it exists.</summary>
    public FileMappingRuleStore(IOptions<StorageOptions> options)
    {
        _dir = Path.Combine(options.Value.DataDir, "mappings");
        Directory.CreateDirectory(_dir); // idempotent: no-op if it already exists
    }

    /// <summary>Returns all rules for a user (empty list if the user has no file yet), read under the gate.</summary>
    public async Task<IReadOnlyList<MappingRule>> GetRulesAsync(string userKey, CancellationToken ct = default)
    {
        var path = PathFor(userKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadAsync(path, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Upserts a rule under the gate: an existing rule for the same keyword (case-insensitive) is dropped
    /// first so a keyword always maps to exactly one project, then the whole list is rewritten atomically.
    /// </summary>
    public async Task AddRuleAsync(string userKey, MappingRule rule, CancellationToken ct = default)
    {
        var path = PathFor(userKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rules = (await ReadAsync(path, ct).ConfigureAwait(false)).ToList();
            rules.RemoveAll(r => r.Keyword.Equals(rule.Keyword, StringComparison.OrdinalIgnoreCase)); // last write wins per keyword
            rules.Add(rule);
            var tmp = path + ".tmp";
            // Write to a sibling temp file, then rename over the target so a crash mid-write can't leave a truncated file.
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(rules, Json), ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true); // atomic replace
        }
        finally { _gate.Release(); }
    }

    // Deserialises the rule file; a missing file (new user) is treated as an empty rule set, not an error.
    private static async Task<IReadOnlyList<MappingRule>> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var rules = await JsonSerializer.DeserializeAsync<List<MappingRule>>(stream, Json, ct).ConfigureAwait(false);
        return rules ?? [];
    }

    // Maps a userKey to its JSON file path via a SHA-256 hex hash of the raw key. Collision-free
    // (distinct keys never share a file — a lossy char-substitution could collapse 'a/b' and 'a_b'
    // together) and traversal-safe (hex is always a valid, fixed-length filename inside _dir).
    private string PathFor(string userKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userKey)));
        return Path.Combine(_dir, hash + ".json");
    }
}
