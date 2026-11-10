using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RateLimiting.Defaults.Configuration;
using RateLimiting.Defaults.Extensions;
using RateLimiting.Defaults.Policies;

namespace RateLimiting.Defaults.Tests;

public class PublicWritePolicyTests
{
    private const int StatusOk = 200;
    private const int StatusTooManyRequests = 429;

    [Fact]
    public void PublicWriteOptions_WithDefaults_AllowsFivePerDayInHourlySegments()
    {
        var options = new RateLimitingDefaultsOptions();

        var publicWrite = options.PublicWrite;

        Assert.Equal(5, publicWrite.PermitLimit);
        Assert.Equal(86400, publicWrite.WindowSeconds);
        Assert.Equal(24, publicWrite.SegmentsPerWindow);
    }

    [Fact]
    public async Task PublicWrite_WithSixRequestsFromOneIp_AllowsFiveAndRejectsTheSixth()
    {
        const int defaultLimit = 5;
        var pipeline = BuildPipeline(new Dictionary<string, string?>());
        var allowed = new List<int>();
        for (var i = 0; i < defaultLimit; i++)
        {
            allowed.Add(await SendAsync(pipeline, "203.0.113.9"));
        }

        var sixth = await SendAsync(pipeline, "203.0.113.9");

        Assert.All(allowed, status => Assert.Equal(StatusOk, status));
        Assert.Equal(StatusTooManyRequests, sixth);
    }

    [Fact]
    public async Task PublicWrite_WhenOneIpIsExhausted_StillAllowsAnotherIp()
    {
        const int defaultLimit = 5;
        var pipeline = BuildPipeline(new Dictionary<string, string?>());
        for (var i = 0; i <= defaultLimit; i++)
        {
            await SendAsync(pipeline, "203.0.113.9");
        }

        var otherIp = await SendAsync(pipeline, "198.51.100.4");

        Assert.Equal(StatusOk, otherIp);
    }

    [Fact]
    public async Task PublicWrite_WithPermitLimitFromConfiguration_RejectsAfterThatLimit()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:PublicWrite:PermitLimit"] = "2"
        });
        var first = await SendAsync(pipeline, "203.0.113.9");
        var second = await SendAsync(pipeline, "203.0.113.9");

        var third = await SendAsync(pipeline, "203.0.113.9");

        Assert.Equal(StatusOk, first);
        Assert.Equal(StatusOk, second);
        Assert.Equal(StatusTooManyRequests, third);
    }

    [Fact]
    public async Task PublicWrite_WhenRejected_SetsRetryAfterToOneHourlySegment()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:PublicWrite:PermitLimit"] = "1"
        });
        await SendAsync(pipeline, "203.0.113.9");
        var context = CreateContext(pipeline.Services, "203.0.113.9");

        await pipeline.Invoke(context);

        Assert.Equal("3600", context.Response.Headers.RetryAfter.ToString());
    }

    private static Pipeline BuildPipeline(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddRateLimitingDefaults();
        var app = builder.Build();
        var applicationBuilder = new ApplicationBuilder(app.Services);
        applicationBuilder.UseRateLimiter();
        applicationBuilder.Run(context =>
        {
            context.Response.StatusCode = StatusOk;
            return Task.CompletedTask;
        });
        return new Pipeline(applicationBuilder.Build(), app.Services);
    }

    private static async Task<int> SendAsync(Pipeline pipeline, string ip)
    {
        var context = CreateContext(pipeline.Services, ip);
        await pipeline.Invoke(context);
        return context.Response.StatusCode;
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string ip)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        var metadata = new EndpointMetadataCollection(new EnableRateLimitingAttribute(RateLimitPolicies.PublicWrite));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "public-write"));
        return context;
    }

    private sealed record Pipeline(RequestDelegate Invoke, IServiceProvider Services);
}
