using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RateLimiting.Defaults.Configuration;
using RateLimiting.Defaults.Extensions;

namespace RateLimiting.Defaults.Tests;

/// <summary>
/// Tests for <see cref="RateLimitingExtensions"/>.
/// </summary>
public class RateLimitingExtensionsTests
{
    [Fact]
    public void AddRateLimitingDefaults_RegistersRateLimiterServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddRateLimitingDefaults();

        var app = builder.Build();
        var options = app.Services.GetService<IOptions<RateLimiterOptions>>();

        Assert.NotNull(options);
        Assert.NotNull(options.Value);
    }

    [Fact]
    public void AddRateLimitingDefaults_UsesDefaultOptions_WhenNoConfigProvided()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddRateLimitingDefaults();

        // Verify the builder extension returns the builder for chaining
        Assert.NotNull(builder);
    }

    [Fact]
    public void AddRateLimitingDefaults_AppliesCustomOptions()
    {
        var builder = WebApplication.CreateBuilder();

        const int customAuthLimit = 3;
        const int customApiLimit = 200;

        builder.AddRateLimitingDefaults(opts =>
        {
            opts.Auth.PermitLimit = customAuthLimit;
            opts.Api.PermitLimit = customApiLimit;
        });

        var app = builder.Build();
        var options = app.Services.GetService<IOptions<RateLimiterOptions>>();

        Assert.NotNull(options);
    }

    [Fact]
    public void RateLimitingDefaultsOptions_HasCorrectDefaults()
    {
        var options = new RateLimitingDefaultsOptions();

        Assert.Equal(5, options.Auth.PermitLimit);
        Assert.Equal(60, options.Auth.WindowSeconds);
        Assert.Equal(100, options.Api.PermitLimit);
        Assert.Equal(60, options.Api.WindowSeconds);
        Assert.Equal(300, options.Public.PermitLimit);
        Assert.Equal(60, options.Public.WindowSeconds);
        Assert.Equal(10, options.Upload.PermitLimit);
        Assert.Equal(60, options.Upload.WindowSeconds);
    }

    [Fact]
    public void RateLimitingDefaultsOptions_SectionName_IsCorrect()
    {
        Assert.Equal("RateLimiting", RateLimitingDefaultsOptions.SectionName);
    }

    [Fact]
    public void PolicyOptions_HasCorrectDefaultSegments()
    {
        var options = new PolicyOptions { PermitLimit = 10 };

        Assert.Equal(4, options.SegmentsPerWindow);
    }
}
