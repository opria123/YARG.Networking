using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SharpOpenNat;

namespace YARG.Net.Utilities.UPnP;

/// <summary>
/// Client for UPnP (Universal Plug and Play) port mapping operations.
/// Uses SharpOpenNat for robust router compatibility.
/// Enables automatic port forwarding on supported routers.
/// </summary>
public sealed class UPnPClient : IDisposable
{
    private readonly TimeSpan _discoveryTimeout;
    private INatDevice? _cachedDevice;
    private readonly object _deviceLock = new();
    private static Action<string>? _logger;

    /// <summary>
    /// Sets a logger action for UPnP discovery diagnostics.
    /// </summary>
    /// <param name="logger">Action that receives log messages.</param>
    public static void SetLogger(Action<string>? logger)
    {
        _logger = logger;
    }

    private static void Log(string message)
    {
        _logger?.Invoke(message);
    }

    /// <summary>
    /// Gets whether UPnP is available (a compatible gateway device was found).
    /// </summary>
    public bool IsAvailable => _cachedDevice != null;

    /// <summary>
    /// Gets the cached device information, if available.
    /// </summary>
    public UPnPDevice? Device
    {
        get
        {
            var device = _cachedDevice;
            if (device == null) return null;
            
            return new UPnPDevice(
                FriendlyName: device.ToString() ?? "NAT Device",
                ServiceType: "SharpOpenNat",
                ControlUrl: "",
                BaseUrl: device.HostEndPoint?.ToString() ?? ""
            );
        }
    }

    /// <summary>
    /// Creates a new UPnP client.
    /// </summary>
    /// <param name="discoveryTimeout">Timeout for device discovery. Default is 5 seconds.</param>
    public UPnPClient(TimeSpan? discoveryTimeout = null)
    {
        _discoveryTimeout = discoveryTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Discovers a UPnP-capable gateway device on the network.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a device was discovered, false otherwise.</returns>
    public async Task<bool> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log("Starting UPnP/NAT-PMP device discovery...");
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_discoveryTimeout);
            
            // Try to discover using both UPnP and NAT-PMP
            var device = await OpenNat.Discoverer.DiscoverDeviceAsync(PortMapper.Upnp | PortMapper.Pmp, cts.Token);
            
            lock (_deviceLock)
            {
                _cachedDevice = device;
            }

            if (device != null)
            {
                Log($"Discovered NAT device: {device}");
                Log($"Device endpoint: {device.HostEndPoint}");
            }

            return device != null;
        }
        catch (NatDeviceNotFoundException)
        {
            Log("No UPnP/NAT-PMP device found on the network");
            return false;
        }
        catch (OperationCanceledException)
        {
            Log("Discovery timed out - no compatible device found");
            return false;
        }
        catch (Exception ex)
        {
            Log($"Discovery error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds a port mapping on the gateway device.
    /// </summary>
    /// <param name="mapping">The port mapping to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the operation.</returns>
    public async Task<UPnPResult> AddPortMappingAsync(
        PortMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var device = _cachedDevice;
        if (device == null)
        {
            return UPnPResult.Failure("No UPnP device discovered. Call DiscoverAsync first.");
        }

        try
        {
            // Get local IP if not specified
            string internalClient = mapping.InternalClient;
            if (string.IsNullOrEmpty(internalClient))
            {
                internalClient = GetLocalIPAddress()?.ToString() ?? "0.0.0.0";
            }

            var protocol = mapping.Protocol == PortMappingProtocol.TCP ? Protocol.Tcp : Protocol.Udp;
            
            var natMapping = new Mapping(
                protocol,
                IPAddress.Parse(internalClient),
                mapping.InternalPort,
                mapping.ExternalPort,
                mapping.LeaseDuration,
                mapping.Description
            );

            Log($"Creating port mapping: {mapping.Protocol} external:{mapping.ExternalPort} -> {internalClient}:{mapping.InternalPort}");
            
            await device.CreatePortMapAsync(natMapping);
            
            Log($"Successfully created port mapping");
            return UPnPResult.Success();
        }
        catch (MappingException ex)
        {
            Log($"Port mapping failed: {ex.Message} (Error: {ex.ErrorCode})");
            return UPnPResult.Failure(ex.Message, (int)ex.ErrorCode);
        }
        catch (Exception ex)
        {
            Log($"Port mapping error: {ex.Message}");
            return UPnPResult.Failure($"Failed to add port mapping: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a port mapping from the gateway device.
    /// </summary>
    /// <param name="externalPort">The external port to unmap.</param>
    /// <param name="protocol">The protocol (TCP or UDP).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the operation.</returns>
    public async Task<UPnPResult> RemovePortMappingAsync(
        int externalPort,
        PortMappingProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        var device = _cachedDevice;
        if (device == null)
        {
            return UPnPResult.Failure("No UPnP device discovered. Call DiscoverAsync first.");
        }

        try
        {
            var natProtocol = protocol == PortMappingProtocol.TCP ? Protocol.Tcp : Protocol.Udp;
            var mapping = new Mapping(natProtocol, externalPort, externalPort);
            
            Log($"Removing port mapping: {protocol} port {externalPort}");
            await device.DeletePortMapAsync(mapping);
            
            Log("Successfully removed port mapping");
            return UPnPResult.Success();
        }
        catch (MappingException ex)
        {
            Log($"Remove mapping failed: {ex.Message}");
            return UPnPResult.Failure(ex.Message, (int)ex.ErrorCode);
        }
        catch (Exception ex)
        {
            Log($"Remove mapping error: {ex.Message}");
            return UPnPResult.Failure($"Failed to remove port mapping: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the external (public) IP address from the gateway device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The external IP address, or null if unavailable.</returns>
    public async Task<string?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default)
    {
        var device = _cachedDevice;
        if (device == null)
        {
            return null;
        }

        try
        {
            var ip = await device.GetExternalIPAsync();
            Log($"External IP: {ip}");
            return ip?.ToString();
        }
        catch (Exception ex)
        {
            Log($"Failed to get external IP: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Lists all port mappings on the gateway device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all port mappings, or empty list on error.</returns>
    public async Task<System.Collections.Generic.List<PortMapping>> GetAllMappingsAsync(CancellationToken cancellationToken = default)
    {
        var result = new System.Collections.Generic.List<PortMapping>();
        var device = _cachedDevice;
        
        if (device == null)
        {
            return result;
        }

        try
        {
            var mappings = await device.GetAllMappingsAsync();
            foreach (var mapping in mappings)
            {
                var protocol = mapping.Protocol == Protocol.Tcp ? PortMappingProtocol.TCP : PortMappingProtocol.UDP;
                result.Add(new PortMapping(
                    ExternalPort: mapping.PublicPort,
                    InternalPort: mapping.PrivatePort,
                    Protocol: protocol,
                    Description: mapping.Description ?? "",
                    InternalClient: mapping.PrivateIP?.ToString() ?? "",
                    LeaseDuration: mapping.Lifetime,
                    Enabled: true
                ));
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to list mappings: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Checks if a specific port mapping exists.
    /// </summary>
    /// <param name="externalPort">The external port to check.</param>
    /// <param name="protocol">The protocol (TCP or UDP).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing mapping if found, null otherwise.</returns>
    public async Task<PortMapping?> GetPortMappingAsync(
        int externalPort,
        PortMappingProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        var device = _cachedDevice;
        if (device == null)
        {
            return null;
        }

        try
        {
            var natProtocol = protocol == PortMappingProtocol.TCP ? Protocol.Tcp : Protocol.Udp;
            var mapping = await device.GetSpecificMappingAsync(natProtocol, externalPort);
            
            if (mapping == null)
                return null;

            return new PortMapping(
                ExternalPort: mapping.PublicPort,
                InternalPort: mapping.PrivatePort,
                Protocol: protocol,
                Description: mapping.Description ?? "",
                InternalClient: mapping.PrivateIP?.ToString() ?? "",
                LeaseDuration: mapping.Lifetime,
                Enabled: true
            );
        }
        catch
        {
            return null;
        }
    }

    private static IPAddress? GetLocalIPAddress()
    {
        try
        {
            // Connect to a public address to determine local IP
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            
            socket.Connect("8.8.8.8", 80);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cachedDevice = null;
    }
}

/// <summary>
/// Result of a UPnP operation.
/// </summary>
public sealed class UPnPResult
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public int? ErrorCode { get; }

    private UPnPResult(bool isSuccess, string? error, int? errorCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static UPnPResult Success() => new(true, null, null);
    public static UPnPResult Failure(string error, int? errorCode = null) => new(false, error, errorCode);
}

/// <summary>
/// Represents a UPnP port mapping.
/// </summary>
public sealed record PortMapping(
    int ExternalPort,
    int InternalPort,
    PortMappingProtocol Protocol,
    string Description,
    string InternalClient = "",
    int LeaseDuration = 0, // 0 = permanent until removed
    bool Enabled = true);

/// <summary>
/// Port mapping protocol.
/// </summary>
public enum PortMappingProtocol
{
    TCP,
    UDP
}

/// <summary>
/// Exception thrown for UPnP-specific errors.
/// </summary>
public sealed class UPnPException : Exception
{
    public int ErrorCode { get; }

    public UPnPException(string message, int errorCode = 0)
        : base(message)
    {
        ErrorCode = errorCode;
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
