using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RateLimiting.Defaults.Configuration;
using RateLimiting.Defaults.Extensions;

namespace RateLimiting.Defaults.Tests;

public class ClientIpForwardingTests
{
    [Fact]
    public async Task ClientIp_WithSpoofedLeftmostEntryThroughDefaultNetwork_IsFirstUntrustedHop()
    {
        var forwardedFor = "6.6.6.6, 203.0.113.9, 10.0.0.1";

        var context = await ResolveAsync("10.42.0.7", forwardedFor);

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task ClientIp_WithForwardedForFromUntrustedPeer_StaysThePeer()
    {
        var forwardedFor = "203.0.113.9";

        var context = await ResolveAsync("198.51.100.4", forwardedFor);

        Assert.Equal(IPAddress.Parse("198.51.100.4"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task ClientIp_WithIpv4MappedPeerInDefaultNetwork_IsForwardedClient()
    {
        var forwardedFor = "203.0.113.9";

        var context = await ResolveAsync("::ffff:10.42.0.7", forwardedFor);

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task ClientIp_WithConfiguredNetworkAndPeerInside_IsForwardedClient()
    {
        var forwardedFor = "203.0.113.9";

        var context = await ResolveAsync(
            "192.168.1.5",
            forwardedFor,
            options => options.TrustedNetworks = ["192.168.0.0/16"]);

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task ClientIp_WithConfiguredNetworkAndPeerInOldDefault_StaysThePeer()
    {
        var forwardedFor = "203.0.113.9";

        var context = await ResolveAsync(
            "10.42.0.7",
            forwardedFor,
            options => options.TrustedNetworks = ["192.168.0.0/16"]);

        Assert.Equal(IPAddress.Parse("10.42.0.7"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Scheme_WithForwardedProtoFromTrustedProxy_IsUnchanged()
    {
        var forwardedFor = "203.0.113.9";

        var context = await ResolveAsync(
            "10.42.0.7",
            forwardedFor,
            configureRequest: http =>
            {
                http.Request.Scheme = "http";
                http.Request.Headers["X-Forwarded-Proto"] = "https";
            });

        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public void AddClientIpForwarding_WithInvalidCidr_ThrowsFormatException()
    {
        var builder = Host.CreateApplicationBuilder();

        Assert.Throws<FormatException>(() =>
            builder.AddClientIpForwarding(options => options.TrustedNetworks = ["not-a-network"]));
    }

    [Fact]
    public void AddClientIpForwarding_WithEmptyTrustedNetworks_ThrowsArgumentException()
    {
        var builder = Host.CreateApplicationBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.AddClientIpForwarding(options => options.TrustedNetworks = []));
    }

    private static async Task<HttpContext> ResolveAsync(
        string peer,
        string forwardedFor,
        Action<ClientIpForwardingOptions>? configure = null,
        Action<HttpContext>? configureRequest = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddClientIpForwarding(configure);
        using var services = builder.Services.BuildServiceProvider();
        var forwardedHeaders = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, forwardedHeaders);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        configureRequest?.Invoke(context);

        await middleware.Invoke(context);

        return context;
    }
}
