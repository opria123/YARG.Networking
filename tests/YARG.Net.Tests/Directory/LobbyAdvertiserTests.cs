using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YARG.Net.Directory;

namespace YARG.Net.Tests.Directory;

public class LobbyAdvertiserTests
{
    private readonly Uri _lobbyServerUri = new("http://localhost:8080/api/lobbies");

    [Fact]
    public void Constructor_WithNullUri_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LobbyAdvertiser(null!));
    }

    [Fact]
    public void IsAdvertising_WhenNotStarted_ReturnsFalse()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);

        // Act & Assert
        Assert.False(sut.IsAdvertising);
    }

    [Fact]
    public void StartAdvertising_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.StartAdvertising(null!, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void StartAdvertising_WhenCalled_SetsIsAdvertisingTrue()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);
        var request = CreateSampleRequest();

        // Act
        sut.StartAdvertising(request, TimeSpan.FromMinutes(5));

        // Assert
        Assert.True(sut.IsAdvertising);
    }

    [Fact]
    public void StartAdvertising_WhenAlreadyAdvertising_ThrowsInvalidOperationException()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);
        var request = CreateSampleRequest();
        sut.StartAdvertising(request, TimeSpan.FromMinutes(5));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => sut.StartAdvertising(request, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task StopAdvertisingAsync_WhenNotAdvertising_DoesNotThrow()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);

        // Act - should not throw
        await sut.StopAdvertisingAsync();

        // Assert
        Assert.False(sut.IsAdvertising);
    }

    [Fact]
    public async Task StopAdvertisingAsync_WhenAdvertising_SetsIsAdvertisingFalse()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);
        var request = CreateSampleRequest();
        sut.StartAdvertising(request, TimeSpan.FromMinutes(5));

        // Act
        await sut.StopAdvertisingAsync();

        // Assert
        Assert.False(sut.IsAdvertising);
    }

    [Fact]
    public void UpdateAdvertisement_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.UpdateAdvertisement(null!));
    }

    [Fact]
    public void UpdateAdvertisement_WhenNotAdvertising_DoesNotThrow()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);
        var request = CreateSampleRequest();

        // Act - should not throw
        sut.UpdateAdvertisement(request);

        // Assert - no exception means success
        Assert.False(sut.IsAdvertising);
    }

    [Fact]
    public void Dispose_WhenAdvertising_StopsAdvertising()
    {
        // Arrange
        var sut = new LobbyAdvertiser(_lobbyServerUri);
        var request = CreateSampleRequest();
        sut.StartAdvertising(request, TimeSpan.FromMinutes(5));

        // Act
        sut.Dispose();

        // Assert
        Assert.False(sut.IsAdvertising);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var sut = new LobbyAdvertiser(_lobbyServerUri);

        // Act - should not throw
        sut.Dispose();
        sut.Dispose();

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task StopAdvertisingAsync_WithCancelledToken_StopsSuccessfullyIfNotAdvertising()
    {
        // Arrange
        using var sut = new LobbyAdvertiser(_lobbyServerUri);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - should not throw when not advertising (no HTTP call to cancel)
        await sut.StopAdvertisingAsync(cts.Token);

        // Assert
        Assert.False(sut.IsAdvertising);
    }

    private static LobbyAdvertisementRequest CreateSampleRequest()
    {
        return new LobbyAdvertisementRequest(
            LobbyId: Guid.NewGuid(),
            LobbyName: "Test Lobby",
            HostName: "TestHost",
            Address: "192.168.1.100",
            Port: 7777,
            CurrentPlayers: 1,
            MaxPlayers: 8,
            HasPassword: false,
            Version: "1.0.0");
    }
}
