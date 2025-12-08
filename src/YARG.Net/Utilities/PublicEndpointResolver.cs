using System;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Utilities;

/// <summary>
/// Manages asynchronous public endpoint resolution using STUN.
/// Handles cancellation, state tracking, and event notification.
/// </summary>
public sealed class PublicEndpointResolver : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _currentCts;
    private string? _resolvedAddress;
    private int _resolvedPort;
    private bool _isResolving;
    private bool _disposed;

    /// <summary>
    /// Gets the resolved public address, or null if not yet resolved.
    /// </summary>
    public string? PublicAddress
    {
        get
        {
            lock (_gate)
            {
                return _resolvedAddress;
            }
        }
    }

    /// <summary>
    /// Gets the resolved public port.
    /// </summary>
    public int PublicPort
    {
        get
        {
            lock (_gate)
            {
                return _resolvedPort;
            }
        }
    }

    /// <summary>
    /// Gets whether resolution is currently in progress.
    /// </summary>
    public bool IsResolving
    {
        get
        {
            lock (_gate)
            {
                return _isResolving;
            }
        }
    }

    /// <summary>
    /// Gets whether a public endpoint has been successfully resolved.
    /// </summary>
    public bool HasResolved
    {
        get
        {
            lock (_gate)
            {
                return !string.IsNullOrEmpty(_resolvedAddress);
            }
        }
    }

    /// <summary>
    /// Fired when the public endpoint has been successfully resolved.
    /// </summary>
    public event EventHandler<PublicEndpointResolvedEventArgs>? EndpointResolved;

    /// <summary>
    /// Fired when resolution fails.
    /// </summary>
    public event EventHandler<PublicEndpointFailedEventArgs>? ResolutionFailed;

    /// <summary>
    /// Starts asynchronous resolution of the public endpoint.
    /// Cancels any existing resolution in progress.
    /// </summary>
    /// <param name="localPort">The local port being used (will be the public port if NAT preserves it).</param>
    /// <returns>A task that completes when resolution finishes or fails.</returns>
    public Task ResolveAsync(int localPort)
    {
        return ResolveAsync(localPort, CancellationToken.None);
    }

    /// <summary>
    /// Starts asynchronous resolution of the public endpoint.
    /// Cancels any existing resolution in progress.
    /// </summary>
    /// <param name="localPort">The local port being used (will be the public port if NAT preserves it).</param>
    /// <param name="externalToken">External cancellation token.</param>
    /// <returns>A task that completes when resolution finishes or fails.</returns>
    public async Task ResolveAsync(int localPort, CancellationToken externalToken)
    {
        CancellationTokenSource cts;

        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PublicEndpointResolver));
            }

            // Cancel any existing resolution
            CancelLocked();

            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _isResolving = true;
            cts = _currentCts;
        }

        try
        {
            string? address = await StunResolver.ResolvePublicAddressAsync(cts.Token).ConfigureAwait(false);

            lock (_gate)
            {
                if (_currentCts != cts)
                {
                    // Resolution was superseded by another call
                    return;
                }

                _isResolving = false;

                if (string.IsNullOrEmpty(address))
                {
                    ResolutionFailed?.Invoke(this, new PublicEndpointFailedEventArgs("STUN lookup did not return a public address."));
                    return;
                }

                bool changed = !string.Equals(_resolvedAddress, address, StringComparison.OrdinalIgnoreCase) ||
                               _resolvedPort != localPort;

                _resolvedAddress = address;
                _resolvedPort = localPort;

                if (changed)
                {
                    EndpointResolved?.Invoke(this, new PublicEndpointResolvedEventArgs(address, localPort));
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (_currentCts == cts)
                {
                    _isResolving = false;
                }
            }
            // Cancellation is not a failure, just silently complete
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_currentCts == cts)
                {
                    _isResolving = false;
                    ResolutionFailed?.Invoke(this, new PublicEndpointFailedEventArgs(ex.Message));
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_currentCts == cts)
                {
                    _currentCts = null;
                    try
                    {
                        cts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed
                    }
                }
            }
        }
    }

    /// <summary>
    /// Cancels any in-progress resolution.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            CancelLocked();
        }
    }

    /// <summary>
    /// Clears the resolved endpoint state.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            CancelLocked();
            _resolvedAddress = null;
            _resolvedPort = 0;
        }
    }

    /// <summary>
    /// Gets the resolved endpoint as a formatted string (address:port).
    /// </summary>
    public string? GetEndpointString()
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(_resolvedAddress))
            {
                return null;
            }

            return EndpointUtility.FormatEndpoint(_resolvedAddress, _resolvedPort);
        }
    }

    private void CancelLocked()
    {
        var cts = _currentCts;
        if (cts == null)
        {
            return;
        }

        _currentCts = null;
        _isResolving = false;

        try
        {
            cts.Cancel();
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelLocked();
        }
    }
}

/// <summary>
/// Event args for successful public endpoint resolution.
/// </summary>
public sealed class PublicEndpointResolvedEventArgs : EventArgs
{
    public PublicEndpointResolvedEventArgs(string address, int port)
    {
        Address = address;
        Port = port;
    }

    /// <summary>
    /// The resolved public IP address.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// The public port (assumed same as local port).
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the formatted endpoint string (address:port).
    /// </summary>
    public string Endpoint => EndpointUtility.FormatEndpoint(Address, Port);
}

/// <summary>
/// Event args for failed public endpoint resolution.
/// </summary>
public sealed class PublicEndpointFailedEventArgs : EventArgs
{
    public PublicEndpointFailedEventArgs(string reason)
    {
        Reason = reason;
    }

    /// <summary>
    /// The reason resolution failed.
    /// </summary>
    public string Reason { get; }
}
