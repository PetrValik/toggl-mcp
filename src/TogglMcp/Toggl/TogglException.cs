using System.Net;

namespace TogglMcp.Toggl;

/// <summary>
/// Raised when the Toggl API returns a non-success status the client cannot
/// recover from (after retries). Never carries the API token.
/// </summary>
public sealed class TogglException(HttpStatusCode status, string message) : Exception(message)
{
    /// <summary>The HTTP status the API returned; lets handlers branch without parsing the message.</summary>
    public HttpStatusCode Status { get; } = status;

    /// <summary>True for auth failures (401/403) — surface a "re-check token" hint rather than a retry.</summary>
    public bool IsAuth => Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
    /// <summary>True when throttled or over the plan's hourly quota (429/402) — a transient, back-off condition.</summary>
    public bool IsRateLimited => Status is HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired;
}
