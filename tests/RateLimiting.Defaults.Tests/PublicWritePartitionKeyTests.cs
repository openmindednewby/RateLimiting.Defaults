using System.Net;
using RateLimiting.Defaults.Extensions;

namespace RateLimiting.Defaults.Tests;

public class PublicWritePartitionKeyTests
{
    private const int Slash64 = 64;
    private const int FullAddress = 128;

    [Fact]
    public void From_WithIpv4MappedIpv6Address_ReturnsTheIpv4Key()
    {
        var ipv4Key = PublicWritePartitionKey.From(IPAddress.Parse("203.0.113.9"), Slash64);

        var mappedKey = PublicWritePartitionKey.From(IPAddress.Parse("::ffff:203.0.113.9"), Slash64);

        Assert.Equal(ipv4Key, mappedKey);
    }

    [Fact]
    public void From_WithTwoIpv6AddressesInOnePrefix_ReturnsOneKey()
    {
        var firstKey = PublicWritePartitionKey.From(IPAddress.Parse("2001:db8:1:2::1"), Slash64);

        var secondKey = PublicWritePartitionKey.From(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff"), Slash64);

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void From_WithIpv6AddressesInDifferentPrefixes_ReturnsDifferentKeys()
    {
        var firstKey = PublicWritePartitionKey.From(IPAddress.Parse("2001:db8:1:2::1"), Slash64);

        var secondKey = PublicWritePartitionKey.From(IPAddress.Parse("2001:db8:1:3::1"), Slash64);

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void From_WithPrefixLengthOf128_KeepsEachIpv6AddressApart()
    {
        var firstKey = PublicWritePartitionKey.From(IPAddress.Parse("2001:db8:1:2::1"), FullAddress);

        var secondKey = PublicWritePartitionKey.From(IPAddress.Parse("2001:db8:1:2::2"), FullAddress);

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void From_WithoutAddress_ReturnsOneSharedUnknownKey()
    {
        var key = PublicWritePartitionKey.From(null, Slash64);

        Assert.Equal("pw:ip:unknown", key);
    }
}
