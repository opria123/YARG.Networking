using System;

namespace YARG.Net.Transport;

public sealed record TransportStartOptions
{
    public int Port { get; init; } = 7777;
    public string Address { get; init; } = "0.0.0.0";
    public bool EnableNatPunchThrough { get; init; }
    public bool IsServer { get; init; }
}
