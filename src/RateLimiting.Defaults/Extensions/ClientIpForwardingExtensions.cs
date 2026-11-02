using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RateLimiting.Defaults.Configuration;

namespace RateLimiting.Defaults.Extensions;

/// <summary>Resolves the real client IP behind trusted proxies so rate limits partition per client.</summary>
public static class ClientIpForwardingExtensions
{
    private const string EmptyNetworksMessage =
        "TrustedNetworks is empty, which makes ForwardedHeadersMiddleware trust X-Forwarded-For from every peer.";

    /// <summary>Configures <c>UseForwardedHeaders()</c>, which must run before the rate limiter.</summary>
    public static TBuilder AddClientIpForwarding<TBuilder>(
        this TBuilder builder,
        Action<ClientIpForwardingOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var forwarding = new ClientIpForwardingOptions();
        configure?.Invoke(forwarding);

        if (forwarding.TrustedNetworks.Count == 0)
        {
            throw new ArgumentException(EmptyNetworksMessage, nameof(configure));
        }

        var trustedNetworks = forwarding.TrustedNetworks.Select(System.Net.IPNetwork.Parse).ToArray();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = null;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var network in trustedNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return builder;
    }
}
