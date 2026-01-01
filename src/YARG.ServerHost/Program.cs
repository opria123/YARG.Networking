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

/// <summary>
/// Standalone YARG dedicated server host.
/// Uses ServerNetworkingBootstrapper to initialize the server stack.
/// </summary>
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

        Console.WriteLine($"Starting YARG Dedicated Server on port {options.Port} (max players: {options.MaxPlayers}).");
        if (!string.IsNullOrEmpty(options.Password))
        {
            Console.WriteLine("Lobby password is enabled.");
        }
        if (!string.IsNullOrEmpty(options.LobbyName))
        {
            Console.WriteLine($"Lobby name: {options.LobbyName}");
        }

        // Create transport and serializer
        using var transport = new LiteNetLibTransport();
        var serializer = new NewtonsoftNetSerializer();

        // Use ServerNetworkingBootstrapper to create the full server stack
        // Pass password during initialization
        var server = ServerNetworkingBootstrapper.InitializeDedicatedServer(
            transport,
            serializer,
            options.Port,
            options.MaxPlayers,
            password: options.Password);

        // Set lobby name if provided
        if (!string.IsNullOrEmpty(options.LobbyName))
        {
            server.LobbyName = options.LobbyName;
        }

        // Hook up connection events for logging and discovery updates
        server.ConnectionManager.ClientAuthenticated += (_, args) =>
        {
            var identity = args.Client.Identity;
            Console.WriteLine($"Player authenticated: {identity?.DisplayName ?? "Unknown"} ({args.Client.Connection.EndPoint})");
            Console.WriteLine($"Players: {server.CurrentPlayerCount}/{options.MaxPlayers}");
            server.UpdateDiscoveryPlayerCount();
        };

        server.ConnectionManager.ClientDisconnected += (_, args) =>
        {
            Console.WriteLine($"Player disconnected: {args.Identity?.DisplayName ?? args.ConnectionId.ToString()}");
            Console.WriteLine($"Players: {server.CurrentPlayerCount}/{options.MaxPlayers}");
            server.UpdateDiscoveryPlayerCount();
        };

        // Start the server
        await server.Runtime.StartAsync(shutdownCts.Token);
        
        // Start discovery advertising so clients can find this server
        server.StartDiscovery();
        
        Console.WriteLine("Server running. Press Ctrl+C to shut down.");
        Console.WriteLine($"Players: {server.CurrentPlayerCount}/{options.MaxPlayers}");
        Console.WriteLine("Discovery is active - clients can now find this server.");

        try
        {
            await Task.Delay(Timeout.Infinite, shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Shutdown requested, stopping server...");
        }

        server.StopDiscovery();
        await server.Runtime.StopAsync();
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
                case "--max-players" when i + 1 < args.Length && int.TryParse(args[i + 1], out var maxPlayers):
                    options = options with { MaxPlayers = Math.Clamp(maxPlayers, 1, 64) };
                    i++;
                    break;
                case "--password" when i + 1 < args.Length:
                    options = options with { Password = args[i + 1] };
                    i++;
                    break;
                case "--name" when i + 1 < args.Length:
                    options = options with { LobbyName = args[i + 1] };
                    i++;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return options;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("YARG Dedicated Server");
        Console.WriteLine();
        Console.WriteLine("Usage: YARG.ServerHost [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --port <port>         Server port (default: 7777)");
        Console.WriteLine("  --max-players <num>   Maximum players (default: 8, range: 1-64)");
        Console.WriteLine("  --password <pass>     Lobby password (optional)");
        Console.WriteLine("  --name <name>         Lobby name (default: YARG Server)");
        Console.WriteLine("  --help, -h            Show this help message");
    }

    private sealed record HostOptions(int Port = 7777, int MaxPlayers = 8, string? Password = null, string? LobbyName = null);
}
