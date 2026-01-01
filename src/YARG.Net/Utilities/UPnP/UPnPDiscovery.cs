using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Utilities.UPnP;

/// <summary>
/// Handles SSDP (Simple Service Discovery Protocol) discovery of UPnP devices.
/// </summary>
internal static class UPnPDiscovery
{
    private const string SsdpMulticastAddress = "239.255.255.250";
    private const int SsdpPort = 1900;

    // UPnP search targets for Internet Gateway Device
    private static readonly string[] SearchTargets =
    {
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
        "upnp:rootdevice"
    };

    /// <summary>
    /// Discovers a UPnP gateway device on the local network.
    /// </summary>
    public static async Task<UPnPDevice?> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var responses = new List<string>();
        
        // Get the local LAN address to bind to the correct interface
        // This is critical on systems with multiple network adapters (Docker, WSL, VPN, etc.)
        var localAddress = GetLocalLanAddress() ?? IPAddress.Any;
        Console.WriteLine($"[UPnP] Binding to local address: {localAddress}");

        using var udpClient = new UdpClient();
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(localAddress, 0));

        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);
        
        Console.WriteLine($"[UPnP] Starting SSDP discovery (timeout: {timeout.TotalSeconds}s)");

        // Send M-SEARCH requests for each target
        int sentCount = 0;
        foreach (var target in SearchTargets)
        {
            var searchRequest = BuildSearchRequest(target);
            var requestBytes = Encoding.ASCII.GetBytes(searchRequest);
            
            try
            {
                await udpClient.SendAsync(requestBytes, requestBytes.Length, multicastEndpoint);
                sentCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPnP] Failed to send M-SEARCH for {target}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"[UPnP] Sent {sentCount} M-SEARCH requests to {SsdpMulticastAddress}:{SsdpPort}");

        // Collect responses
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var receiveTask = udpClient.ReceiveAsync();
                var delayTask = Task.Delay(100, cts.Token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                
                if (completed == receiveTask && receiveTask.IsCompletedSuccessfully)
                {
                    var result = await receiveTask;
                    var response = Encoding.ASCII.GetString(result.Buffer);
                    responses.Add(response);
                    Console.WriteLine($"[UPnP] Received response from {result.RemoteEndPoint}");

                    // Try to parse the response immediately
                    var location = ParseLocation(response);
                    if (!string.IsNullOrEmpty(location))
                    {
                        Console.WriteLine($"[UPnP] Found device at: {location}");
                        var device = await TryGetDeviceAsync(location, cts.Token);
                        if (device != null)
                        {
                            Console.WriteLine($"[UPnP] Successfully parsed device: {device.FriendlyName}");
                            return device;
                        }
                        else
                        {
                            Console.WriteLine($"[UPnP] Device at {location} is not a compatible gateway");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout - continue to try parsing collected responses
            Console.WriteLine($"[UPnP] Discovery timeout reached. Received {responses.Count} responses.");
        }

        // Try any remaining responses
        foreach (var response in responses)
        {
            var location = ParseLocation(response);
            if (!string.IsNullOrEmpty(location))
            {
                try
                {
                    var device = await TryGetDeviceAsync(location, cancellationToken);
                    if (device != null)
                    {
                        Console.WriteLine($"[UPnP] Found compatible device: {device.FriendlyName}");
                        return device;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UPnP] Error checking device at {location}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"[UPnP] No compatible UPnP gateway found after checking {responses.Count} responses");
        return null;
    }

    private static string BuildSearchRequest(string searchTarget)
    {
        return $"M-SEARCH * HTTP/1.1\r\n" +
               $"HOST: {SsdpMulticastAddress}:{SsdpPort}\r\n" +
               $"MAN: \"ssdp:discover\"\r\n" +
               $"MX: 2\r\n" +
               $"ST: {searchTarget}\r\n" +
               $"\r\n";
    }

    private static string? ParseLocation(string response)
    {
        var match = Regex.Match(response, @"LOCATION:\s*(.+?)[\r\n]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static async Task<UPnPDevice?> TryGetDeviceAsync(string location, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var xml = await httpClient.GetStringAsync(location);
            
            // Parse the device description to find the control URL
            return ParseDeviceDescription(xml, location);
        }
        catch
        {
            return null;
        }
    }

    private static UPnPDevice? ParseDeviceDescription(string xml, string locationUrl)
    {
        // Parse base URL from location
        var locationUri = new Uri(locationUrl);
        var baseUrl = $"{locationUri.Scheme}://{locationUri.Host}:{locationUri.Port}";

        // Look for WANIPConnection or WANPPPConnection service
        var serviceTypes = new[]
        {
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        };

        foreach (var serviceType in serviceTypes)
        {
            var serviceIndex = xml.IndexOf(serviceType, StringComparison.OrdinalIgnoreCase);
            if (serviceIndex < 0)
                continue;

            // Find the service block containing this service type
            var serviceStart = xml.LastIndexOf("<service>", serviceIndex, StringComparison.OrdinalIgnoreCase);
            var serviceEnd = xml.IndexOf("</service>", serviceIndex, StringComparison.OrdinalIgnoreCase);
            
            if (serviceStart < 0 || serviceEnd < 0)
                continue;

            var serviceBlock = xml.Substring(serviceStart, serviceEnd - serviceStart + "</service>".Length);

            // Extract control URL
            var controlUrlMatch = Regex.Match(serviceBlock, @"<controlURL>(.+?)</controlURL>", RegexOptions.IgnoreCase);
            if (!controlUrlMatch.Success)
                continue;

            var controlUrl = controlUrlMatch.Groups[1].Value;
            
            // Make absolute URL if relative
            if (!controlUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                controlUrl = controlUrl.StartsWith("/") ? baseUrl + controlUrl : baseUrl + "/" + controlUrl;
            }

            // Extract friendly name if available
            var nameMatch = Regex.Match(xml, @"<friendlyName>(.+?)</friendlyName>", RegexOptions.IgnoreCase);
            var friendlyName = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Gateway";

            return new UPnPDevice(
                FriendlyName: friendlyName,
                ServiceType: serviceType,
                ControlUrl: controlUrl,
                BaseUrl: baseUrl);
        }

        return null;
    }
    
    /// <summary>
    /// Gets the local LAN IP address by determining which interface would be used
    /// to reach a public address. This ensures we bind to the correct interface
    /// on systems with multiple network adapters.
    /// </summary>
    private static IPAddress? GetLocalLanAddress()
    {
        try
        {
            // Connect to a public address to determine which local interface would be used
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Represents a discovered UPnP device.
/// </summary>
public sealed record UPnPDevice(
    string FriendlyName,
    string ServiceType,
    string ControlUrl,
    string BaseUrl);
