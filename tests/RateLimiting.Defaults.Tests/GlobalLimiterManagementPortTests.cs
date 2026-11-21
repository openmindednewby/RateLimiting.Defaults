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

    [Fact]
    public void ManagementPort_WithDefaults_Is8081()
    {
        var options = new RateLimitingDefaultsOptions();

        var managementPort = options.ManagementPort;

        Assert.Equal(8081, managementPort);
    }

    [Fact]
    public async Task GlobalLimiter_WhenOverTheLimitOnManagementPort_NeverRejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        var statuses = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add(await SendAsync(pipeline, 8081, ReadyPath));
        }

        var fifth = await SendAsync(pipeline, 8081, "/metrics");

        Assert.All(statuses, status => Assert.Equal(StatusOk, status));
        Assert.Equal(StatusOk, fifth);
    }

    [Fact]
    public async Task GlobalLimiter_WhenOverTheLimitOnAppPort_Rejects()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "2"
        });
        await SendAsync(pipeline, 8080, ReadyPath);
        await SendAsync(pipeline, 8080, ReadyPath);

        var third = await SendAsync(pipeline, 8080, ReadyPath);

        Assert.Equal(StatusTooManyRequests, third);
    }

    [Fact]
    public async Task GlobalLimiter_WithManagementPortFromConfiguration_ExemptsThatPortOnly()
    {
        var pipeline = BuildPipeline(new Dictionary<string, string?>
        {
            ["RateLimiting:Api:PermitLimit"] = "1",
            ["RateLimiting:ManagementPort"] = "9090"
        });
        await SendAsync(pipeline, 9090, ReadyPath);
        await SendAsync(pipeline, 8081, ReadyPath);

        var managementOverLimit = await SendAsync(pipeline, 9090, ReadyPath);
        var oldPortOverLimit = await SendAsync(pipeline, 8081, ReadyPath);

        Assert.Equal(StatusOk, managementOverLimit);
        Assert.Equal(StatusTooManyRequests, oldPortOverLimit);
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

        var third = await SendAsync(pipeline, 8081, "/api/items");

        Assert.Equal(StatusTooManyRequests, third);
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
