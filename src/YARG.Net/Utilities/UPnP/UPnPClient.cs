using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Utilities.UPnP;

/// <summary>
/// Client for UPnP (Universal Plug and Play) port mapping operations.
/// Enables automatic port forwarding on supported routers.
/// </summary>
public sealed class UPnPClient : IDisposable
{
    private readonly TimeSpan _discoveryTimeout;
    private UPnPDevice? _cachedDevice;
    private readonly object _deviceLock = new();

    /// <summary>
    /// Gets whether UPnP is available (a compatible gateway device was found).
    /// </summary>
    public bool IsAvailable => _cachedDevice != null;

    /// <summary>
    /// Gets the cached device information, if available.
    /// </summary>
    public UPnPDevice? Device => _cachedDevice;

    /// <summary>
    /// Creates a new UPnP client.
    /// </summary>
    /// <param name="discoveryTimeout">Timeout for device discovery. Default is 3 seconds.</param>
    public UPnPClient(TimeSpan? discoveryTimeout = null)
    {
        _discoveryTimeout = discoveryTimeout ?? TimeSpan.FromSeconds(3);
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
            var device = await UPnPDiscovery.DiscoverAsync(_discoveryTimeout, cancellationToken);
            
            lock (_deviceLock)
            {
                _cachedDevice = device;
            }

            return device != null;
        }
        catch
        {
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

            var actualMapping = mapping with { InternalClient = internalClient };
            
            await UPnPSoap.AddPortMappingAsync(device, actualMapping, cancellationToken);
            return UPnPResult.Success();
        }
        catch (UPnPException ex)
        {
            return UPnPResult.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
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
            await UPnPSoap.DeletePortMappingAsync(device, externalPort, protocol, cancellationToken);
            return UPnPResult.Success();
        }
        catch (UPnPException ex)
        {
            return UPnPResult.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
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
            return await UPnPSoap.GetExternalIPAddressAsync(device, cancellationToken);
        }
        catch
        {
            return null;
        }
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
            return await UPnPSoap.GetSpecificPortMappingAsync(device, externalPort, protocol, cancellationToken);
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
