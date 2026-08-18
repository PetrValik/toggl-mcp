using Microsoft.Extensions.Options;
using TogglMcp.Configuration;

namespace TogglMcp.Tests.Support;

/// <summary>
/// A throwaway <see cref="StorageOptions.DataDir"/> on the real filesystem, removed when the test
/// finishes. The file-backed stores are tested against real files on purpose — their whole job is
/// surviving a restart, which an in-memory substitute would never prove.
/// </summary>
public sealed class TempDataDir : IDisposable
{
    /// <summary>Absolute path of the directory; unique per instance.</summary>
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "toggl-mcp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the directory.</summary>
    public TempDataDir() => Directory.CreateDirectory(Path);

    /// <summary>The bound options a store would receive from DI, pointed at this directory.</summary>
    public IOptions<StorageOptions> Options => Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDir = Path });

    /// <summary>Deletes the directory and everything under it; a failure to clean up never fails a test.</summary>
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* best effort — a leftover temp dir is not a test failure */ }
        catch (UnauthorizedAccessException) { /* ditto */ }
    }
}
