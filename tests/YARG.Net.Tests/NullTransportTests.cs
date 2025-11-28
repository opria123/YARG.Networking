using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests;

public sealed class NullTransportTests
{
    [Fact]
    public void StartThenShutdown_TogglesIsRunning()
    {
        using var transport = new NullTransport();
        Assert.False(transport.IsRunning);

        transport.Start(new TransportStartOptions { Port = 1234, IsServer = true });
        Assert.True(transport.IsRunning);

        transport.Shutdown();
        Assert.False(transport.IsRunning);
    }
}
