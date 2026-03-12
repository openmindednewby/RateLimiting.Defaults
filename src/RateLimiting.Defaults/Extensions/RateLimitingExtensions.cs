using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RateLimiting.Defaults.Configuration;
using RateLimiting.Defaults.Policies;

namespace RateLimiting.Defaults.Extensions;

/// <summary>
/// Extension methods for configuring production-ready rate limiting with
/// pre-defined policies for Auth, Api, Public, and Upload scenarios.
/// </summary>
public static class RateLimitingExtensions
{
    private const int StatusTooManyRequests = 429;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Adds rate limiting services with pre-configured policies.
    /// Policies are configurable via the "RateLimiting" section in appsettings.json.
    /// <para>
    /// Registers a global limiter (Api policy) that applies to all requests by default.
    /// Endpoints can opt into stricter policies via <c>RequireRateLimiting("Auth")</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="TBuilder">The host builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Optional action to override configuration after binding from appsettings.</param>
    /// <remarks>
    /// When deployed behind a reverse proxy (e.g., Nginx), callers must configure
    /// <c>ForwardedHeadersOptions</c> and call <c>app.UseForwardedHeaders()</c>
    /// before <c>app.UseRateLimiter()</c> to ensure correct client IP extraction.
    /// Without this, all requests will share a single rate-limit bucket keyed to
    /// the proxy's IP address.
    /// </remarks>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddRateLimitingDefaults<TBuilder>(
        this TBuilder builder,
        Action<RateLimitingDefaultsOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new RateLimitingDefaultsOptions();
        builder.Configuration
            .GetSection(RateLimitingDefaultsOptions.SectionName)
            .Bind(options);
        configure?.Invoke(options);

        builder.Services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusTooManyRequests;

            // Global limiter: Api policy for all requests by default.
            // Partitioned by user ID (authenticated) or IP (anonymous).
            limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext =>
                {
                    var key = GetUserOrIpKey(httpContext);
                    return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                        BuildSlidingWindowOptions(options.Api));
                });

            // Api policy: Also registered as named policy so endpoints can
            // explicitly opt into it via RequireRateLimiting("Api").
            limiterOptions.AddPolicy(RateLimitPolicies.Api, httpContext =>
            {
                var key = GetUserOrIpKey(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Api));
            });

            // Auth policy: Strict, per-IP (user isn't authenticated yet).
            limiterOptions.AddPolicy(RateLimitPolicies.Auth, httpContext =>
            {
                var key = GetIpKey(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Auth));
            });

            // Public policy: Relaxed, per-IP.
            limiterOptions.AddPolicy(RateLimitPolicies.Public, httpContext =>
            {
                var key = GetIpKey(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Public));
            });

            // Upload policy: Tight, per-user.
            limiterOptions.AddPolicy(RateLimitPolicies.Upload, httpContext =>
            {
                var key = GetUserOrIpKey(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Upload));
            });

            // Custom rejection response with Retry-After header.
            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter, out var retryAfter)
                    ? (int)retryAfter.TotalSeconds
                    : (int?)null;

                if (retryAfterSeconds.HasValue)
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        retryAfterSeconds.Value.ToString();
                }

                var response = new RateLimitResponse
                {
                    Status = StatusTooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "Rate limit exceeded. Please try again later.",
                    RetryAfterSeconds = retryAfterSeconds
                };

                await context.HttpContext.Response.WriteAsJsonAsync(
                    response, JsonOptions, cancellationToken);
            };
        });

        return builder;
    }

    /// <summary>
    /// Creates a <see cref="SlidingWindowRateLimiterOptions"/> from the given policy configuration.
    /// </summary>
    private static SlidingWindowRateLimiterOptions BuildSlidingWindowOptions(PolicyOptions policy) =>
        new()
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            SegmentsPerWindow = policy.SegmentsPerWindow,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        };

    /// <summary>
    /// Gets a partition key based on authenticated user ID or client IP.
    /// Used for Api and Upload policies where authenticated users get per-user limits.
    /// </summary>
    private static string GetUserOrIpKey(HttpContext httpContext)
    {
        var userId = httpContext.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return GetIpKey(httpContext);
    }

    /// <summary>
    /// Gets a partition key based on client IP address.
    /// Used for Auth and Public policies where requests are anonymous.
    /// </summary>
    private static string GetIpKey(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}

/// <summary>
/// Response model for rate limit rejection responses.
/// </summary>
internal sealed class RateLimitResponse
{
    /// <summary>
    /// HTTP status code (always 429).
    /// </summary>
    public int Status { get; init; }

    /// <summary>
    /// Error title.
    /// </summary>
    public string Title { get; init; } = default!;

    /// <summary>
    /// Human-readable error detail.
    /// </summary>
    public string Detail { get; init; } = default!;

    /// <summary>
    /// Seconds until the client can retry, if available.
    /// </summary>
    public int? RetryAfterSeconds { get; init; }
}
