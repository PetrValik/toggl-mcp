namespace TogglMcp.Configuration;

/// <summary>
/// Durable local storage. Holds per-user mapping rules and the commit ledger (the record of
/// which sourceUid became which Toggl entry), and later the encrypted token vault + Data
/// Protection key ring (Phase 3). MUST be a persisted volume in production — losing the Data
/// Protection key ring makes every stored token undecryptable, and losing the ledger costs the
/// server its exact memory of what it has already written (dedup degrades to content matching).
/// </summary>
public sealed class StorageOptions
{
    /// <summary>Configuration section these options bind from (appsettings / env).</summary>
    public const string SectionName = "Storage";

    /// <summary>Directory for durable state. Mounted as a Docker volume in prod.</summary>
    public string DataDir { get; set; } = "data";
}
