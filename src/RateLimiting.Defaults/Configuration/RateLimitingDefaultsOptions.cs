namespace RateLimiting.Defaults.Configuration;

/// <summary>
/// Configuration options for rate limiting defaults.
/// Bind from the "RateLimiting" section in appsettings.json.
/// </summary>
public sealed class RateLimitingDefaultsOptions
{
    /// <summary>
    /// The configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Auth policy options (login, OTP, password reset). Default: 5/min per IP.
    /// </summary>
    public PolicyOptions Auth { get; set; } = new() { PermitLimit = 5 };

    /// <summary>
    /// Api policy options (standard CRUD endpoints). Default: 100/min per user.
    /// </summary>
    public PolicyOptions Api { get; set; } = new() { PermitLimit = 100 };

    /// <summary>
    /// Public policy options (public-facing pages). Default: 300/min per IP.
    /// </summary>
    public PolicyOptions Public { get; set; } = new() { PermitLimit = 300 };

    /// <summary>
    /// Upload policy options (file uploads). Default: 10/min per user.
    /// </summary>
    public PolicyOptions Upload { get; set; } = new() { PermitLimit = 10 };
}

/// <summary>
/// Options for a single rate limiting policy.
/// </summary>
public sealed class PolicyOptions
{
    private const int DefaultSegments = 4;

    /// <summary>
    /// Maximum number of requests allowed in the time window.
    /// </summary>
    public int PermitLimit { get; set; }

    /// <summary>
    /// Time window in seconds for the sliding window limiter.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Number of segments per window for the sliding window algorithm.
    /// More segments = smoother distribution. Default: 4.
    /// </summary>
    public int SegmentsPerWindow { get; set; } = DefaultSegments;
}
