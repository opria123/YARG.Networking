using System;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Runtime;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Runtime;

public sealed class DefaultServerRuntimeTests
{
    [Fact]
    public async Task StartThenStop_TogglesTransportState()
    {
        var runtime = new DefaultServerRuntime(TimeSpan.FromMilliseconds(1));
        using var transport = new NullTransport();

        runtime.Configure(new ServerRuntimeOptions(transport)
        {
            Port = 9000,
        });

        await runtime.StartAsync();
        Assert.True(transport.IsRunning);

        await runtime.StopAsync(null, CancellationToken.None);
        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task ConfigureWhileRunning_Throws()
    {
        var runtime = new DefaultServerRuntime(TimeSpan.FromMilliseconds(1));
        using var transport = new NullTransport();

        runtime.Configure(new ServerRuntimeOptions(transport)
        {
            Port = 9001,
        });

        await runtime.StartAsync();

        var newTransport = new NullTransport();
        try
        {
            Assert.Throws<InvalidOperationException>(() => runtime.Configure(new ServerRuntimeOptions(newTransport)));
        }
        finally
        {
            await runtime.StopAsync(null, CancellationToken.None);
            newTransport.Dispose();
        }
    }

    [Fact]
    public async Task StartWithoutConfigure_Throws()
    {
        var runtime = new DefaultServerRuntime();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());
    }
}
