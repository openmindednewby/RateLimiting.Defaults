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
/// Registers production rate limiting with the Auth, Api, Public, PublicWrite and Upload policies.
/// </summary>
public static class RateLimitingExtensions
{
    private const int StatusTooManyRequests = 429;
    private const string ManagementPartitionKey = "management";
    private const string HealthPathPrefix = "/health";

    private const string RetryAfterHintItemKey = "__ratelimit_retry_after_seconds";

    private const int DefaultRetryAfterSeconds = 60;

    private static readonly PathString MetricsPath = new("/metrics");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Adds a global Api limiter and the named policies, bound from "RateLimiting", then <paramref name="configure"/>.
    /// </summary>
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
        ManagementPortValidator.Validate(options);

        builder.Services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusTooManyRequests;

            limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext =>
                {
                    if (IsManagementRequest(httpContext, options.ManagementPort))
                    {
                        return RateLimitPartition.GetNoLimiter(ManagementPartitionKey);
                    }

                    var key = GetUserOrIpKey(httpContext);
                    StashRetryAfterHint(httpContext, options.Api);
                    return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                        BuildSlidingWindowOptions(options.Api));
                });

            limiterOptions.AddPolicy(RateLimitPolicies.Api, httpContext =>
            {
                var key = GetUserOrIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Api);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Api));
            });

            limiterOptions.AddPolicy(RateLimitPolicies.Auth, httpContext =>
            {
                var key = GetIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Auth);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Auth));
            });

            limiterOptions.AddPolicy(RateLimitPolicies.Public, httpContext =>
            {
                var key = GetIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Public);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Public));
            });

            limiterOptions.AddPolicy(RateLimitPolicies.PublicWrite, httpContext =>
            {
                var key = PublicWritePartitionKey.From(
                    httpContext.Connection.RemoteIpAddress, options.PublicWriteIpv6PrefixLength);
                StashRetryAfterHint(httpContext, options.PublicWrite);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.PublicWrite));
            });

            limiterOptions.AddPolicy(RateLimitPolicies.Upload, httpContext =>
            {
                var key = GetUserOrIpKey(httpContext);
                StashRetryAfterHint(httpContext, options.Upload);
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                    BuildSlidingWindowOptions(options.Upload));
            });

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

    private static SlidingWindowRateLimiterOptions BuildSlidingWindowOptions(PolicyOptions policy) =>
        new()
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            SegmentsPerWindow = policy.SegmentsPerWindow,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        };

    private static void StashRetryAfterHint(HttpContext httpContext, PolicyOptions policy)
    {
        var seconds = policy.SegmentsPerWindow > 0
            ? (int)Math.Ceiling((double)policy.WindowSeconds / policy.SegmentsPerWindow)
            : policy.WindowSeconds;
        httpContext.Items[RetryAfterHintItemKey] = Math.Max(1, seconds);
    }

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

    private static bool IsManagementRequest(HttpContext httpContext, int managementPort) =>
        httpContext.Connection.LocalPort == managementPort && IsManagementPath(httpContext.Request.Path);

    private static bool IsManagementPath(PathString path) =>
        path.Equals(MetricsPath) ||
        (path.StartsWithSegments(HealthPathPrefix, out var remaining) && remaining.HasValue);

    private static string GetUserOrIpKey(HttpContext httpContext)
    {
        var userId = httpContext.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return GetIpKey(httpContext);
    }

    private static string GetIpKey(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}

internal sealed class RateLimitResponse
{
    public int Status { get; init; }

    public string Title { get; init; } = default!;

    public string Detail { get; init; } = default!;

    public int? RetryAfterSeconds { get; init; }
}
