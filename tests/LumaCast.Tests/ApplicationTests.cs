using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LumaCast.Tests;

[TestClass]
public sealed class ApplicationTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    private static WebApplicationFactory<Program> CreateLiveKitFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LiveKit:Url"] = "wss://example.livekit.cloud",
                    ["LiveKit:ApiKey"] = "api-key",
                    ["LiveKit:ApiSecret"] = "api-secret-with-at-least-32-characters"
                }));
        });

    [TestMethod]
    public async Task LiveKitStatusReturnsPeerToPeerWhenCredentialsAreEmpty()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/livekit/status");
        var status = await response.Content.ReadFromJsonAsync<LiveKitStatusContract>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(status);
        Assert.IsFalse(status.Configured);
        Assert.AreEqual("peer-to-peer", status.Provider);
        AssertSecurityHeaders(response);
    }

    [TestMethod]
    public async Task LiveKitStatusUsesValuesFromAppSettingsSection()
    {
        await using var factory = CreateLiveKitFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/livekit/status");
        var status = await response.Content.ReadFromJsonAsync<LiveKitStatusContract>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(status);
        Assert.IsTrue(status.Configured);
        Assert.AreEqual("livekit", status.Provider);
    }

    [TestMethod]
    public async Task LiveKitEndpointsManageCompleteRoomLifecycle()
    {
        await using var factory = CreateLiveKitFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var createResponse = await client.PostAsync("/api/livekit/rooms", content: null);
        var room = await createResponse.Content.ReadFromJsonAsync<LiveKitRoomContract>();
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.IsNotNull(room);
        Assert.StartsWith("lumacast-", room.RoomName);
        Assert.HasCount(48, room.BroadcastKey);

        using var missingRoomResponse = await client.PostAsJsonAsync("/api/livekit/token", new
        {
            roomName = "missing-room",
            role = "viewer",
            participantName = "Visitante"
        });
        Assert.AreEqual(HttpStatusCode.NotFound, missingRoomResponse.StatusCode);

        using var invalidHostResponse = await client.PostAsJsonAsync("/api/livekit/token", new
        {
            roomName = room.RoomName,
            role = "broadcaster",
            participantName = "Apresentador",
            broadcastKey = "invalid"
        });
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidHostResponse.StatusCode);

        using var viewerResponse = await client.PostAsJsonAsync("/api/livekit/token", new
        {
            roomName = room.RoomName,
            role = "viewer",
            participantName = "   "
        });
        var viewerCredential = await viewerResponse.Content.ReadFromJsonAsync<LiveKitCredentialContract>();
        Assert.AreEqual(HttpStatusCode.Created, viewerResponse.StatusCode);
        Assert.IsNotNull(viewerCredential);
        Assert.AreEqual("wss://example.livekit.cloud", viewerCredential.ServerUrl);
        Assert.IsFalse(string.IsNullOrWhiteSpace(viewerCredential.ParticipantToken));

        using var hostResponse = await client.PostAsJsonAsync("/api/livekit/token", new
        {
            roomName = room.RoomName,
            role = "broadcaster",
            participantName = new string('A', 80),
            broadcastKey = room.BroadcastKey
        });
        Assert.AreEqual(HttpStatusCode.Created, hostResponse.StatusCode);

        using var invalidEndResponse = await client.PostAsJsonAsync(
            $"/api/livekit/rooms/{room.RoomName}/end",
            new { broadcastKey = "invalid" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidEndResponse.StatusCode);

        using var endResponse = await client.PostAsJsonAsync(
            $"/api/livekit/rooms/{room.RoomName}/end",
            new { room.BroadcastKey });
        Assert.AreEqual(HttpStatusCode.NoContent, endResponse.StatusCode);
    }

    [TestMethod]
    public async Task LiveKitEndpointsFailSafelyWithoutConfiguration()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var roomResponse = await client.PostAsync("/api/livekit/rooms", content: null);
        using var tokenResponse = await client.PostAsJsonAsync("/api/livekit/token", new
        {
            roomName = "room",
            role = "viewer"
        });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, roomResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, tokenResponse.StatusCode);
    }

    [TestMethod]
    public async Task RazorPagesRenderSuccessfully()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var studioResponse = await client.GetAsync("/");
        using var viewerResponse = await client.GetAsync("/Assistir?room=room-test");
        using var errorResponse = await client.GetAsync("/Error");

        Assert.AreEqual(HttpStatusCode.OK, studioResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, viewerResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, errorResponse.StatusCode);
        Assert.Contains("LumaCast", await studioResponse.Content.ReadAsStringAsync());
        Assert.Contains("Transmissão LumaCast", await viewerResponse.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task HealthCheckReturnsHealthy()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/healthz");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("Healthy", await response.Content.ReadAsStringAsync());
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.IsTrue(response.Headers.Contains("Content-Security-Policy"));
        Assert.IsTrue(response.Headers.Contains("Permissions-Policy"));
    }

    private sealed record LiveKitStatusContract(bool Configured, string Provider);
    private sealed record LiveKitRoomContract(string RoomName, string BroadcastKey);
    private sealed record LiveKitCredentialContract(
        [property: JsonPropertyName("server_url")] string ServerUrl,
        [property: JsonPropertyName("participant_token")] string ParticipantToken);
}
