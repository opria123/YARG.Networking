using System;
using YARG.Net.Runtime;
using Xunit;

namespace YARG.Net.Tests.Runtime;

public sealed class ClientSessionContextTests
{
    [Fact]
    public void TrySetSession_StoresValueAndRaisesEvent()
    {
        var context = new ClientSessionContext();
        ClientSessionChangedEventArgs? args = null;
        context.SessionChanged += (_, eventArgs) => args = eventArgs;

        var sessionId = Guid.NewGuid();
        Assert.True(context.TrySetSession(sessionId));
        Assert.Equal(sessionId, context.SessionId);
        Assert.True(context.HasSession);
        Assert.NotNull(args);
        Assert.Null(args!.PreviousSessionId);
        Assert.Equal(sessionId, args.CurrentSessionId);
    }

    [Fact]
    public void ClearSession_ResetsState()
    {
        var context = new ClientSessionContext();
        var sessionId = Guid.NewGuid();
        context.TrySetSession(sessionId);

        ClientSessionChangedEventArgs? args = null;
        context.SessionChanged += (_, eventArgs) => args = eventArgs;

        Assert.True(context.ClearSession());
        Assert.False(context.HasSession);
        Assert.Null(context.SessionId);
        Assert.NotNull(args);
        Assert.Equal(sessionId, args!.PreviousSessionId);
        Assert.Null(args.CurrentSessionId);
    }
}
