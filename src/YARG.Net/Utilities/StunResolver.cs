using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Utilities;

/// <summary>
/// Resolves public IP addresses using STUN (Session Traversal Utilities for NAT).
/// </summary>
public static class StunResolver
{
    private const ushort BindingRequestType = 0x0001;
    private const ushort BindingSuccessResponseType = 0x0101;
    private const uint MagicCookie = 0x2112A442;
    private const ushort MappedAddressType = 0x0001;
    private const ushort XorMappedAddressType = 0x0020;
    private const int DefaultTimeoutMs = 3000;

    /// <summary>
    /// Default STUN servers to query.
    /// </summary>
    public static readonly (string Host, int Port)[] DefaultStunServers =
    {
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302),
        ("stun2.l.google.com", 19302),
        ("stun3.l.google.com", 19302),
        ("stun4.l.google.com", 19302)
    };

    /// <summary>
    /// Attempts to resolve the public IP address by querying STUN servers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeoutMs">Timeout per server in milliseconds.</param>
    /// <returns>The public IP address, or null if resolution failed.</returns>
    public static async Task<string?> ResolvePublicAddressAsync(
        CancellationToken cancellationToken = default,
        int timeoutMs = DefaultTimeoutMs)
    {
        return await ResolvePublicAddressAsync(DefaultStunServers, cancellationToken, timeoutMs);
    }

    /// <summary>
    /// Attempts to resolve the public IP address by querying specified STUN servers.
    /// </summary>
    /// <param name="stunServers">STUN servers to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeoutMs">Timeout per server in milliseconds.</param>
    /// <returns>The public IP address, or null if resolution failed.</returns>
    public static async Task<string?> ResolvePublicAddressAsync(
        (string Host, int Port)[] stunServers,
        CancellationToken cancellationToken = default,
        int timeoutMs = DefaultTimeoutMs)
    {
        foreach (var (host, port) in stunServers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string? address = await QueryServerAsync(host, port, timeoutMs, cancellationToken);
                if (!string.IsNullOrEmpty(address))
                    return address;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Try next server
            }
        }

        return null;
    }

    /// <summary>
    /// Queries a single STUN server for the public IP address.
    /// </summary>
    public static async Task<string?> QueryServerAsync(
        string host,
        int port,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        using var udpClient = new UdpClient();
        udpClient.Client.SendTimeout = timeoutMs;
        udpClient.Client.ReceiveTimeout = timeoutMs;

        byte[] request = BuildBindingRequest();

        cancellationToken.ThrowIfCancellationRequested();

        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCts.CancelAfter(timeoutMs);

        try
        {
            await udpClient.SendAsync(request, request.Length, host, port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null; // Timeout
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveCts.CancelAfter(timeoutMs);

        try
        {
            var receiveTask = udpClient.ReceiveAsync();
            var timeoutTask = Task.Delay(timeoutMs, receiveCts.Token);

            var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
            if (completedTask == timeoutTask)
                return null;

            var response = await receiveTask;
            if (TryParsePublicAddress(response.Buffer, out var address))
                return address.ToString();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null; // Timeout
        }

        return null;
    }

    private static byte[] BuildBindingRequest()
    {
        byte[] buffer = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), BindingRequestType);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), 0); // Message length
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), MagicCookie);
        RandomNumberGenerator.Fill(buffer.AsSpan(8, 12)); // Transaction ID
        return buffer;
    }

    private static bool TryParsePublicAddress(ReadOnlySpan<byte> data, out IPAddress address)
    {
        address = IPAddress.None;

        if (data.Length < 20)
            return false;

        ushort messageType = BinaryPrimitives.ReadUInt16BigEndian(data[..2]);
        if (messageType != BindingSuccessResponseType)
            return false;

        uint magicCookie = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
        int offset = 20;

        while (offset + 4 <= data.Length)
        {
            ushort attributeType = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            ushort attributeLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));
            offset += 4;

            if (offset + attributeLength > data.Length)
                break;

            if ((attributeType == XorMappedAddressType || attributeType == MappedAddressType) && attributeLength >= 8)
            {
                byte family = data[offset + 1];
                if (family == 0x01) // IPv4
                {
                    if (attributeType == XorMappedAddressType)
                    {
                        uint xorAddress = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4, 4));
                        uint addressValue = xorAddress ^ magicCookie;
                        Span<byte> bytes = stackalloc byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(bytes, addressValue);
                        address = new IPAddress(bytes);
                        return true;
                    }
                    else
                    {
                        Span<byte> bytes = stackalloc byte[4];
                        data.Slice(offset + 4, 4).CopyTo(bytes);
                        address = new IPAddress(bytes);
                        return true;
                    }
                }
            }

            offset += AlignToWord(attributeLength);
        }

        return false;
    }

    private static int AlignToWord(int length)
    {
        return (length + 3) & ~3;
    }
}
