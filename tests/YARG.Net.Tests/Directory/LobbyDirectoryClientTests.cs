using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YARG.Net.Directory;

namespace YARG.Net.Tests.Directory;

public sealed class LobbyDirectoryClientTests
{
    private static readonly Uri TestUri = new("http://localhost:8080/lobbies");

    [Fact]
    public async Task RefreshAsync_ParsesLobbiesFromResponse()
    {
        // Arrange
        var entries = new List<LobbyDirectoryEntry>
        {
            new(
                Guid.NewGuid(),
                "Test Lobby",
                "Host1",
                "192.168.1.1",
                7777,
                2,
                8,
                false,
                "1.0.0",
                DateTimeOffset.UtcNow)
        };

        var handler = new MockHttpHandler(JsonSerializer.Serialize(entries));
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);

        // Act
        await client.RefreshAsync();

        // Assert
        Assert.Single(client.Lobbies);
        Assert.Equal("Test Lobby", client.Lobbies[0].LobbyName);
        Assert.Equal("Host1", client.Lobbies[0].HostName);
    }

    [Fact]
    public async Task RefreshAsync_FiltersStaleLobbies()
    {
        // Arrange
        var staleTime = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        var freshTime = DateTimeOffset.UtcNow;

        var entries = new List<LobbyDirectoryEntry>
        {
            new(Guid.NewGuid(), "Stale", "Host", "192.168.1.1", 7777, 1, 4, false, "1.0.0", staleTime),
            new(Guid.NewGuid(), "Fresh", "Host", "192.168.1.2", 7777, 1, 4, false, "1.0.0", freshTime),
        };

        var handler = new MockHttpHandler(JsonSerializer.Serialize(entries));
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);

        // Act
        await client.RefreshAsync();

        // Assert
        Assert.Single(client.Lobbies);
        Assert.Equal("Fresh", client.Lobbies[0].LobbyName);
    }

    [Fact]
    public async Task RefreshAsync_RaisesLobbiesChangedEvent()
    {
        // Arrange
        var entries = new List<LobbyDirectoryEntry>
        {
            new(Guid.NewGuid(), "Test", "Host", "192.168.1.1", 7777, 1, 4, false, "1.0.0", DateTimeOffset.UtcNow)
        };

        var handler = new MockHttpHandler(JsonSerializer.Serialize(entries));
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);

        IReadOnlyList<LobbyDirectoryEntry>? eventLobbies = null;
        client.LobbiesChanged += (_, e) => eventLobbies = e.Lobbies;

        // Act
        await client.RefreshAsync();

        // Assert
        Assert.NotNull(eventLobbies);
        Assert.Single(eventLobbies);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotRaiseEventWhenListUnchanged()
    {
        // Arrange
        var lobbyId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var entries = new List<LobbyDirectoryEntry>
        {
            new(lobbyId, "Test", "Host", "192.168.1.1", 7777, 1, 4, false, "1.0.0", timestamp)
        };

        var handler = new MockHttpHandler(JsonSerializer.Serialize(entries));
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);

        int invocationCount = 0;
        client.LobbiesChanged += (_, _) => invocationCount++;

        // Act
        await client.RefreshAsync(); // First call
        await client.RefreshAsync(); // Second call with same data

        // Assert
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void StartPolling_CanBeStopped()
    {
        // Arrange
        var handler = new MockHttpHandler("[]");
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);

        // Act
        client.StartPolling(TimeSpan.FromMilliseconds(100));
        client.StopPolling();

        // Assert - no exception means success
        Assert.Empty(client.Lobbies);
    }

    [Fact]
    public async Task RefreshAsync_HandlesHttpErrors()
    {
        // Arrange
        var handler = new MockHttpHandler(HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);

        // Act & Assert - should not throw
        await client.RefreshAsync();
        Assert.Empty(client.Lobbies);
    }

    [Fact]
    public async Task RefreshAsync_HandlesCancellation()
    {
        // Arrange
        var handler = new MockHttpHandler("[]", delayMs: 5000);
        using var httpClient = new HttpClient(handler);
        using var client = new LobbyDirectoryClient(TestUri, TimeSpan.FromSeconds(30), httpClient);
        using var cts = new CancellationTokenSource();

        // Act
        cts.Cancel();

        // Assert - TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.RefreshAsync(cts.Token));
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly string? _response;
        private readonly HttpStatusCode _statusCode;
        private readonly int _delayMs;

        public MockHttpHandler(string response, int delayMs = 0)
        {
            _response = response;
            _statusCode = HttpStatusCode.OK;
            _delayMs = delayMs;
        }

        public MockHttpHandler(HttpStatusCode statusCode)
        {
            _response = null;
            _statusCode = statusCode;
            _delayMs = 0;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }

            var response = new HttpResponseMessage(_statusCode);

            if (_response is not null)
            {
                response.Content = new StringContent(_response);
            }

            return response;
        }
    }
}
