using System;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Handlers;
using YARG.Net.Packets;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Runtime;
using YARG.Net.Serialization;
using YARG.Net.Sessions;
using YARG.Net.Transport;
using YARG.Net.Handlers.Server;

namespace YARG.ServerHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = ParseArguments(args);

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdownCts.Cancel();
        };

        Console.WriteLine($"Starting YARG.ServerHost on port {options.Port} (NAT punch: {options.EnableNatPunchThrough}, max players: {options.MaxPlayers}).");
        if (!string.IsNullOrEmpty(options.Password))
        {
            Console.WriteLine("Lobby password is enabled.");
        }

        using var transport = new LiteNetLibTransport();
        var serializer = new JsonNetSerializer();
        var sessionManager = new SessionManager(options.MaxPlayers);
        var lobbyManager = new LobbyStateManager(sessionManager, new LobbyConfiguration { MaxPlayers = options.MaxPlayers });
        using var lobbyCoordinator = new ServerLobbyCoordinator(sessionManager, lobbyManager, serializer);
        var dispatcher = new PacketDispatcher(serializer);
        var lobbyCommandHandler = new ServerLobbyCommandHandler(sessionManager, lobbyManager);
        lobbyCommandHandler.Register(dispatcher);

        var handshakeHandler = new ServerHandshakeHandler(
            sessionManager,
            serializer,
            new HandshakeServerOptions
            {
                Password = options.Password,
            });

        handshakeHandler.Register(dispatcher);
        handshakeHandler.HandshakeAccepted += (_, session) =>
        {
            Console.WriteLine($"Handshake accepted for {session.PlayerName} ({session.Connection.EndPoint}).");
            lobbyCoordinator.HandleHandshakeAccepted(session);
        };

        handshakeHandler.HandshakeRejected += (_, args) =>
        {
            Console.WriteLine($"Handshake rejected from {args.Context.Connection.EndPoint}: {args.Reason}");
        };

        dispatcher.RegisterHandler<HeartbeatPacket>(PacketType.Heartbeat, (context, envelope, _) =>
        {
            Console.WriteLine($"Heartbeat from {context.Connection.Id} at {envelope.Payload.TimestampUnixMs}.");
            return Task.CompletedTask;
        });

        transport.OnPeerDisconnected += connection =>
        {
            Console.WriteLine($"Peer disconnected: {connection.EndPoint} ({connection.Id}).");
            lobbyCoordinator.HandlePeerDisconnected(connection.Id);
        };

        var runtime = new DefaultServerRuntime();
        runtime.Configure(new ServerRuntimeOptions
        {
            Transport = transport,
            Port = options.Port,
            EnableNatPunchThrough = options.EnableNatPunchThrough,
            PacketDispatcher = dispatcher,
        });

        await runtime.StartAsync(shutdownCts.Token);
        Console.WriteLine("Server running. Press Ctrl+C to shut down.");

        try
        {
            await Task.Delay(Timeout.Infinite, shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Shutdown requested, stopping transport...");
        }

        await runtime.StopAsync();
        return 0;
    }

    private static HostOptions ParseArguments(string[] args)
    {
        var options = new HostOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var port):
                    options = options with { Port = port };
                    i++;
                    break;
                case "--nat":
                    options = options with { EnableNatPunchThrough = true };
                    break;
                case "--max-players" when i + 1 < args.Length && int.TryParse(args[i + 1], out var maxPlayers):
                    options = options with { MaxPlayers = Math.Clamp(maxPlayers, 1, 64) };
                    i++;
                    break;
                case "--password" when i + 1 < args.Length:
                    options = options with { Password = args[i + 1] };
                    i++;
                    break;
            }
        }

        return options;
    }

    private sealed record HostOptions(int Port = 7777, bool EnableNatPunchThrough = false, int MaxPlayers = 8, string? Password = null);
}
