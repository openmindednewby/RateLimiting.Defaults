using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using RateLimiting.Defaults.Extensions;

namespace RateLimiting.Defaults.Tests;

/// <summary>
/// Pins the guarantee that every 429 this package emits carries a
/// <c>Retry-After</c>.
/// <para>
/// Regression origin: the rejection handler read <c>MetadataName.RetryAfter</c>
/// off the lease and set the header only when present. Every policy here is a
/// sliding window, and <see cref="SlidingWindowRateLimiter"/> never populates that
/// metadata — so the branch was dead and every 429 shipped bare, telling clients
/// nothing about when to retry.
/// </para>
/// </summary>
public class RetryAfterResolutionTests
{
    private const string HintKey = "__ratelimit_retry_after_seconds";

    /// <summary>
    /// The framework behaviour the fallback exists for. If a future release starts
    /// populating the metadata, this fails loudly rather than drifting silently.
    /// </summary>
    [Fact]
    public void SlidingWindowLimiter_SuppliesNoRetryAfterMetadata()
    {
        using var lease = RejectedSlidingWindowLease();

        Assert.False(lease.IsAcquired);
        Assert.False(lease.TryGetMetadata(MetadataName.RetryAfter, out _));
    }

    [Fact]
    public void ResolveRetryAfterSeconds_UsesTheStashedHint_WhenMetadataIsAbsent()
    {
        var context = new DefaultHttpContext();
        context.Items[HintKey] = 10;

        using var lease = RejectedSlidingWindowLease();

        Assert.Equal(10, RateLimitingExtensions.ResolveRetryAfterSeconds(lease, context));
    }

    [Fact]
    public void ResolveRetryAfterSeconds_FallsBackToADefault_WhenNoHintWasStashed()
    {
        using var lease = RejectedSlidingWindowLease();

        Assert.Equal(60, RateLimitingExtensions.ResolveRetryAfterSeconds(lease, new DefaultHttpContext()));
    }

    [Fact]
    public void ResolveRetryAfterSeconds_PrefersLimiterMetadata_WhenPresent()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(45),
            QueueLimit = 0
        });
        limiter.AttemptAcquire();
        using var lease = limiter.AttemptAcquire();

        var context = new DefaultHttpContext();
        context.Items[HintKey] = 10;

        Assert.Equal(45, RateLimitingExtensions.ResolveRetryAfterSeconds(lease, context));
    }

    private static RateLimitLease RejectedSlidingWindowLease()
    {
        var limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        limiter.AttemptAcquire();
        return limiter.AttemptAcquire();
    }
}
