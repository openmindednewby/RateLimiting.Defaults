namespace RateLimiting.Defaults.Configuration;

/// <summary>Which proxies may set the client IP through <c>X-Forwarded-For</c>.</summary>
public sealed class ClientIpForwardingOptions
{
    /// <summary>The private cluster and WireGuard range, used when no networks are configured in code.</summary>
    public const string DefaultTrustedNetwork = "10.0.0.0/8";

    /// <summary>CIDR ranges of trusted proxies. Replacing the list drops the default; empty throws.</summary>
    public IReadOnlyList<string> TrustedNetworks { get; set; } = [DefaultTrustedNetwork];
}
