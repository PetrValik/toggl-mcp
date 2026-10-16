using System.Net;
using System.Net.Sockets;

namespace TogglMcp.Tests.Support;

/// <summary>
/// A stand-in Toggl API on a loopback port that answers every request with 401, the way the real
/// one answers a revoked or mistyped token. Lets an end-to-end test see what the model is told.
/// </summary>
public sealed class RejectingToggl : IDisposable
{
    private readonly HttpListener _listener = new();

    /// <summary>Base URL to hand the server as <c>TOGGL__BASEURL</c>.</summary>
    public string Url { get; }

    /// <summary>Starts answering on a free loopback port.</summary>
    public RejectingToggl()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    // Every request gets the body Toggl sends for a bad token.
    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }

            ctx.Response.StatusCode = 401;
            await using (var body = ctx.Response.OutputStream)
                await body.WriteAsync("Incorrect username and/or password"u8.ToArray());
        }
    }

    /// <summary>Stops answering.</summary>
    public void Dispose() => _listener.Close();
}
