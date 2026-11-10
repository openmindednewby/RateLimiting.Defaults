using System.Net;
using System.Net.Sockets;

namespace RateLimiting.Defaults.Extensions;

internal static class PublicWritePartitionKey
{
    private const string UnknownKey = "pw:ip:unknown";
    private const int Ipv6AddressBits = 128;
    private const int BitsPerByte = 8;
    private const int FullByteMask = 0xFF;

    internal static string From(IPAddress? address, int ipv6PrefixLength)
    {
        if (address is null)
            return UnknownKey;

        var client = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (client.AddressFamily != AddressFamily.InterNetworkV6)
            return $"pw:ip:{client}";

        var prefixLength = Math.Clamp(ipv6PrefixLength, 0, Ipv6AddressBits);
        return $"pw:ip6:{MaskToPrefix(client, prefixLength)}/{prefixLength}";
    }

    private static IPAddress MaskToPrefix(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var bitsKept = Math.Clamp(prefixLength - (i * BitsPerByte), 0, BitsPerByte);
            bytes[i] = (byte)(bytes[i] & (FullByteMask << (BitsPerByte - bitsKept)));
        }

        return new IPAddress(bytes);
    }
}
