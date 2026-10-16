using ModelContextProtocol;
using ModelContextProtocol.Client;
using TogglMcp.Features.ListProjects;
using TogglMcp.Tests.Support;

namespace TogglMcp.Tests.Hosting;

/// <summary>
/// The server started with <c>--stdio</c>, the way Claude Desktop or Claude Code launch a local
/// MCP server: a real child process driven by a real MCP client over its stdin/stdout. Anything
/// that writes to stdout besides the protocol (a log line, a banner) breaks the handshake here.
/// </summary>
public sealed class StdioHostTests
{
    // The built server sits next to the test assembly (ProjectReference copies its runtimeconfig too).
    private static readonly string ServerDll = typeof(ListProjectsTool).Assembly.Location;

    // Nothing in these tests may reach the real Toggl API: port 9 (discard) refuses at once.
    private const string UnreachableToggl = "http://127.0.0.1:9";

    // A server that never answers must fail the test, not hang the run.
    private static CancellationToken Deadline() => new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

    [Fact]
    public async Task A_stdio_client_sees_every_tool()
    {
        using var data = new TempDataDir();
        await using var client = await StartAsync(new() { ["STORAGE__DATADIR"] = data.Path });

        var tools = await client.ListToolsAsync(cancellationToken: Deadline());

        Assert.Equal(
            ["add_mapping_rule", "commit_plan", "list_entries", "list_projects", "list_tasks", "plan_week", "suggest_project", "week_status"],
            tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task A_relative_data_dir_lands_in_the_user_data_folder_not_the_working_directory()
    {
        // The MCP client picks the working directory (Claude Desktop uses "/"), so a relative
        // data dir must not be resolved against it. A unique name keeps the test clear of any
        // real local install's data, which lives in the same user folder.
        var relative = $"test-{Guid.NewGuid():N}";
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "toggl-mcp");
        var appDataExisted = Directory.Exists(appData);
        using var cwd = new TempDataDir();
        try
        {
            await using (var client = await StartAsync(new() { ["STORAGE__DATADIR"] = relative }, workingDirectory: cwd.Path))
            {
                // Resolving the tool's services creates the mapping store's directory before the
                // (unreachable) Toggl call fails; the failure itself is not what this test is about.
                try
                {
                    await client.CallToolAsync("add_mapping_rule",
                        new Dictionary<string, object?> { ["keyword"] = "standup", ["projectId"] = 1 },
                        cancellationToken: Deadline());
                }
                catch (McpException) { }
            }

            Assert.True(Directory.Exists(Path.Combine(appData, relative, "mappings")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(cwd.Path));
        }
        finally
        {
            if (Directory.Exists(Path.Combine(appData, relative))) Directory.Delete(Path.Combine(appData, relative), recursive: true);
            if (!appDataExisted && Directory.Exists(appData) && !Directory.EnumerateFileSystemEntries(appData).Any()) Directory.Delete(appData);
        }
    }

    private static Task<McpClient> StartAsync(Dictionary<string, string?> env, string? workingDirectory = null)
    {
        env["TOGGL__APITOKEN"] = "not-a-real-token";
        env["TOGGL__BASEURL"] = UnreachableToggl;
        env["TOGGL__WORKSPACEID"] = "1";
        env["TOGGL__MAXRETRIES"] = "0";

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "toggl-mcp",
            Command = "dotnet",
            Arguments = [ServerDll, "--stdio"],
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = env,
        });
        return McpClient.CreateAsync(transport, cancellationToken: Deadline());
    }
}
