using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using RateLimiting.Defaults.Extensions;

namespace RateLimiting.Defaults.Tests;

public class ManagementPortValidationTests
{
    [Fact]
    public void AddRateLimitingDefaults_WithZeroManagementPort_Throws()
    {
        var builder = BuilderWith(new Dictionary<string, string?> { ["RateLimiting:ManagementPort"] = "0" });

        var exception = Record.Exception(() => builder.AddRateLimitingDefaults());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void AddRateLimitingDefaults_WithManagementPortEqualToPublicPort_Throws()
    {
        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["RateLimiting:ManagementPort"] = "8080",
            ["RateLimiting:PublicPort"] = "8080"
        });

        var exception = Record.Exception(() => builder.AddRateLimitingDefaults());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void AddRateLimitingDefaults_WithDistinctPositivePorts_DoesNotThrow()
    {
        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["RateLimiting:ManagementPort"] = "9090",
            ["RateLimiting:PublicPort"] = "8080"
        });

        var exception = Record.Exception(() => builder.AddRateLimitingDefaults());

        Assert.Null(exception);
    }

    private static WebApplicationBuilder BuilderWith(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}
