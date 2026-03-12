namespace RateLimiting.Defaults.Policies;

/// <summary>
/// Pre-defined rate limiting policy names for use with
/// <c>[EnableRateLimiting]</c> or <c>RequireRateLimiting()</c>.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Strict policy for authentication endpoints (login, OTP, password reset).
    /// Partitioned by client IP. Default: 5 requests per 60 seconds.
    /// </summary>
    public const string Auth = "Auth";

    /// <summary>
    /// Standard policy for authenticated API endpoints (CRUD operations).
    /// Partitioned by authenticated user ID, or by IP for anonymous requests.
    /// Default: 100 requests per 60 seconds.
    /// </summary>
    public const string Api = "Api";

    /// <summary>
    /// Relaxed policy for public-facing endpoints (menu viewer, public pages).
    /// Partitioned by client IP. Default: 300 requests per 60 seconds.
    /// </summary>
    public const string Public = "Public";

    /// <summary>
    /// Tight policy for resource-intensive endpoints (file uploads).
    /// Partitioned by authenticated user ID. Default: 10 requests per 60 seconds.
    /// </summary>
    public const string Upload = "Upload";
}
