using System.Globalization;
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

    /// <summary>Per-request stash for the Retry-After hint a rejected policy advertises.</summary>
    private const string RetryAfterHintItemKey = "__ratelimit_retry_after_seconds";

    /// <summary>Last-resort Retry-After when no policy stashed a hint (defence in depth).</summary>
    private const int DefaultRetryAfterSeconds = 60;

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
                    StashRetryAfterHint(httpContext, options.Api);
                    return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                        BuildSlidingWindowOptions(options.Api));
                });

            // Api policy: Also registered as named policy so endpoints can
            // explicitly opt into it via RequireRateLimiting("Api").
            limiterOptions.AddPolicy(RateLimitPolicies.Api, httpContext =>
            {
                var key = GetUserOrIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Api);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Api));
            });

            // Auth policy: Strict, per-IP (user isn't authenticated yet).
            limiterOptions.AddPolicy(RateLimitPolicies.Auth, httpContext =>
            {
                var key = GetIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Auth);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Auth));
            });

            // Public policy: Relaxed, per-IP.
            limiterOptions.AddPolicy(RateLimitPolicies.Public, httpContext =>
            {
                var key = GetIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Public);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Public));
            });

            // Upload policy: Tight, per-user.
            limiterOptions.AddPolicy(RateLimitPolicies.Upload, httpContext =>
            {
                var key = GetUserOrIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Upload);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Upload));
            });

            // Custom rejection response with Retry-After header.
            //
            // Retry-After is ALWAYS emitted. Reading it from the lease metadata alone
            // was dead code: every policy here is a sliding window, and
            // SlidingWindowRateLimiter never populates MetadataName.RetryAfter (only
            // the fixed-window and token-bucket limiters do). So this branch never
            // fired and every 429 shipped without the one header that tells a client
            // when to come back. The stashed per-policy hint is the fallback.
            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = ResolveRetryAfterSeconds(context.Lease, context.HttpContext);

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

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
    /// Records how long a caller should wait before retrying the policy that is about
    /// to evaluate this request. For a sliding window the oldest segment expires after
    /// <c>window / segments</c>, which is when a permit next frees up.
    /// </summary>
    /// <remarks>
    /// Called from the POLICY lambda (which runs per request), never from the
    /// partition factory — that only runs the first time a partition key is seen.
    /// </remarks>
    private static void StashRetryAfterHint(HttpContext httpContext, PolicyOptions policy)
    {
        var seconds = policy.SegmentsPerWindow > 0
            ? (int)Math.Ceiling((double)policy.WindowSeconds / policy.SegmentsPerWindow)
            : policy.WindowSeconds;
        httpContext.Items[RetryAfterHintItemKey] = Math.Max(1, seconds);
    }

    /// <summary>
    /// Resolves Retry-After seconds for a rejected request: limiter metadata when a
    /// limiter supplies it, else the stashed per-policy hint, else a safe default.
    /// </summary>
    internal static int ResolveRetryAfterSeconds(RateLimitLease lease, HttpContext httpContext)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) &&
            retryAfter.TotalSeconds >= 1)
        {
            return (int)Math.Ceiling(retryAfter.TotalSeconds);
        }

        if (httpContext.Items.TryGetValue(RetryAfterHintItemKey, out var hint) && hint is int hintSeconds)
        {
            return hintSeconds;
        }

        return DefaultRetryAfterSeconds;
    }

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
