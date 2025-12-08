using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace YARG.Net.Utilities;

/// <summary>
/// Utility methods for detecting local network addresses.
/// </summary>
public static class NetworkAddressUtility
{
    /// <summary>
    /// Attempts to detect the local LAN IP address.
    /// Prefers addresses with gateways (routable) and private IP ranges.
    /// </summary>
    /// <returns>The best candidate LAN address, or "127.0.0.1" if none found.</returns>
    public static string GetLocalLanAddress()
    {
        try
        {
            var candidates = new List<(IPAddress address, bool hasGateway, int preference)>(8);

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                // Skip virtual/VPN adapters
                string nicName = nic.Name.ToLowerInvariant();
                string nicDesc = nic.Description.ToLowerInvariant();
                if (nicName.Contains("virtual") || nicName.Contains("vmware") || nicName.Contains("vbox") ||
                    nicDesc.Contains("virtual") || nicDesc.Contains("vmware") || nicDesc.Contains("virtualbox"))
                    continue;

                var properties = nic.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(g =>
                    g?.Address != null &&
                    !g.Address.Equals(IPAddress.Any) &&
                    !g.Address.Equals(IPAddress.None));

                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any))
                        continue;

                    // Skip link-local addresses (169.254.x.x)
                    byte[] bytes = address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254)
                        continue;

                    // Calculate preference score (higher is better)
                    int preference = GetPrivateAddressPreference(bytes);
                    candidates.Add((address, hasGateway, preference));
                }
            }

            if (candidates.Count > 0)
            {
                // Sort by: hasGateway (desc), preference (desc)
                var selected = candidates
                    .OrderByDescending(c => c.hasGateway ? 1 : 0)
                    .ThenByDescending(c => c.preference)
                    .First();

                return selected.address.ToString();
            }
        }
        catch
        {
            // Fall through to DNS fallback
        }

        // Fallback to DNS lookup
        return GetAddressViaDns();
    }

    /// <summary>
    /// Checks if an IP address is in a private (RFC 1918) range.
    /// </summary>
    public static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        byte[] bytes = address.GetAddressBytes();
        return GetPrivateAddressPreference(bytes) > 0;
    }

    /// <summary>
    /// Checks if an IP address is a link-local address (169.254.x.x).
    /// </summary>
    public static bool IsLinkLocalAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static int GetPrivateAddressPreference(byte[] bytes)
    {
        // 192.168.x.x - most common home/office network
        if (bytes[0] == 192 && bytes[1] == 168)
            return 40;

        // 10.x.x.x - common in larger networks
        if (bytes[0] == 10)
            return 30;

        // 172.16-31.x.x - less common private range
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return 20;

        return 0;
    }

    private static string GetAddressViaDns()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    byte[] bytes = ip.GetAddressBytes();
                    // Skip link-local
                    if (bytes[0] != 169 || bytes[1] != 254)
                        return ip.ToString();
                }
            }
        }
        catch
        {
            // Ignore DNS failures
        }

        return "127.0.0.1";
    }
}
