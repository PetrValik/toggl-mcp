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
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dir;

    public FileMappingRuleStore(IOptions<StorageOptions> options)
    {
        _dir = Path.Combine(options.Value.DataDir, "mappings");
        Directory.CreateDirectory(_dir);
    }

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

    public async Task AddRuleAsync(string userKey, MappingRule rule, CancellationToken ct = default)
    {
        var path = PathFor(userKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rules = (await ReadAsync(path, ct).ConfigureAwait(false)).ToList();
            rules.RemoveAll(r => r.Keyword.Equals(rule.Keyword, StringComparison.OrdinalIgnoreCase));
            rules.Add(rule);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(rules, Json), ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true); // atomic replace
        }
        finally { _gate.Release(); }
    }

    private static async Task<IReadOnlyList<MappingRule>> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var rules = await JsonSerializer.DeserializeAsync<List<MappingRule>>(stream, Json, ct).ConfigureAwait(false);
        return rules ?? [];
    }

    private string PathFor(string userKey)
    {
        var safe = string.Concat(userKey.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        if (safe.Length == 0) safe = "user";
        return Path.Combine(_dir, safe + ".json");
    }
}
