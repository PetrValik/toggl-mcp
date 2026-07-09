using System.Net;

namespace TogglMcp.Toggl;

/// <summary>
/// Raised when the Toggl API returns a non-success status the client cannot
/// recover from (after retries). Never carries the API token.
/// </summary>
public sealed class TogglException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public bool IsAuth => Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
    public bool IsRateLimited => Status is HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired;
}
