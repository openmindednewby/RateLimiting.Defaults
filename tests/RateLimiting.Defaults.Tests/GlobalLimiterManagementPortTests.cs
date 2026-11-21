using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RateLimiting.Defaults.Configuration;
using RateLimiting.Defaults.Extensions;

namespace RateLimiting.Defaults.Tests;

public class GlobalLimiterManagementPortTests
{
    private const int StatusOk = 200;
    private const int StatusTooManyRequests = 429;
    private const string ClientIp = "203.0.113.9";
    private const string ReadyPath = "/health/ready";
    private const string MetricsPath = "/metrics";

    [Fact]
    public void ManagementPort_WithDefaults_Is8081()
    {
        var options = new RateLimitingDefaultsOptions();

        var managementPort = options.ManagementPort;

        Assert.Equal(8081, managementPort);
    }

    [Fact]
    public async Task GlobalLimiter_WhenHealthOverTheLimitOnManagementPort_NeverRejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8081, ReadyPath);
        await SendAsync(pipeline, 8081, ReadyPath);

        var overLimit = await SendAsync(pipeline, 8081, ReadyPath);

        Assert.Equal(StatusOk, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WhenMetricsOverTheLimitOnManagementPort_NeverRejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8081, MetricsPath);
        await SendAsync(pipeline, 8081, MetricsPath);

        var overLimit = await SendAsync(pipeline, 8081, MetricsPath);

        Assert.Equal(StatusOk, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WhenHealthOverTheLimitOnPublicPort_Rejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8080, ReadyPath);
        await SendAsync(pipeline, 8080, ReadyPath);

        var overLimit = await SendAsync(pipeline, 8080, ReadyPath);

        Assert.Equal(StatusTooManyRequests, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WithManagementPortFromConfiguration_ExemptsThatPort()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "1",
            ["RateLimiting:ManagementPort"] = "9090"
        });
        await SendAsync(pipeline, 9090, ReadyPath);

        var overLimit = await SendAsync(pipeline, 9090, ReadyPath);

        Assert.Equal(StatusOk, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WithManagementPortFromConfiguration_LimitsDefaultPort()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "1",
            ["RateLimiting:ManagementPort"] = "9090"
        });
        await SendAsync(pipeline, 8081, ReadyPath);

        var overLimit = await SendAsync(pipeline, 8081, ReadyPath);

        Assert.Equal(StatusTooManyRequests, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WhenOverTheLimitOnManagementPortWithAppPath_Rejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8081, "/api/items");
        await SendAsync(pipeline, 8081, "/api/items");

        var overLimit = await SendAsync(pipeline, 8081, "/api/items");

        Assert.Equal(StatusTooManyRequests, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WhenMetricsSubPathOverTheLimitOnManagementPort_Rejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8081, "/metrics/extra");
        await SendAsync(pipeline, 8081, "/metrics/extra");

        var overLimit = await SendAsync(pipeline, 8081, "/metrics/extra");

        Assert.Equal(StatusTooManyRequests, overLimit);
    }

    [Fact]
    public async Task GlobalLimiter_WhenBareHealthOverTheLimitOnManagementPort_Rejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8081, "/health");
        await SendAsync(pipeline, 8081, "/health");

        var overLimit = await SendAsync(pipeline, 8081, "/health");

        Assert.Equal(StatusTooManyRequests, overLimit);
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

    private static async Task<int> SendAsync(Pipeline pipeline, int localPort, string path)
    {
        var context = new DefaultHttpContext { RequestServices = pipeline.Services };
        context.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
        context.Connection.LocalPort = localPort;
        context.Request.Path = path;
        await pipeline.Invoke(context);
        return context.Response.StatusCode;
    }

    private sealed record Pipeline(RequestDelegate Invoke, IServiceProvider Services);
}
