using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace LumaCast.Tests;

[TestClass]
public sealed class PeerToPeerSignalingTests
{
    [TestMethod]
    public async Task ViewerReceivesOfflineWhenBroadcasterIsMissing()
    {
        await using var factory = CreateFactory();
        using var viewer = await ConnectAsync(factory, "room-offline", "viewer", "viewer-1");

        using var message = await ReceiveJsonAsync(viewer);

        Assert.AreEqual("offline", message.RootElement.GetProperty("type").GetString());
        await WaitForClosedStateAsync(viewer);
    }

    [TestMethod]
    public async Task SignalingRelaysMessagesBetweenBroadcasterAndViewer()
    {
        await using var factory = CreateFactory();
        using var broadcaster = await ConnectAsync(factory, "room-live", "broadcaster", "host-1");
        using var connected = await ReceiveJsonAsync(broadcaster);
        using var initialCount = await ReceiveJsonAsync(broadcaster);
        Assert.AreEqual("connected", connected.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(0, initialCount.RootElement.GetProperty("count").GetInt32());

        using var viewer = await ConnectAsync(factory, "room-live", "viewer", "viewer-1");
        using var viewerConnected = await ReceiveJsonAsync(viewer);
        using var viewerJoined = await ReceiveJsonAsync(broadcaster);
        using var viewerCount = await ReceiveJsonAsync(broadcaster);
        Assert.AreEqual("connected", viewerConnected.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("viewer-joined", viewerJoined.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(1, viewerCount.RootElement.GetProperty("count").GetInt32());

        await SendJsonAsync(viewer, new { type = "offer", sdp = "viewer-offer" });
        using var offer = await ReceiveJsonAsync(broadcaster);
        Assert.AreEqual("viewer-1", offer.RootElement.GetProperty("viewerId").GetString());
        Assert.AreEqual("viewer-offer", offer.RootElement.GetProperty("sdp").GetString());

        await SendJsonAsync(broadcaster, new
        {
            type = "answer",
            viewerId = "viewer-1",
            sdp = "host-answer"
        });
        using var answer = await ReceiveJsonAsync(viewer);
        Assert.AreEqual("host-answer", answer.RootElement.GetProperty("sdp").GetString());

        await viewer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
        using var viewerLeft = await ReceiveJsonAsync(broadcaster);
        using var finalCount = await ReceiveJsonAsync(broadcaster);
        Assert.AreEqual("viewer-left", viewerLeft.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(0, finalCount.RootElement.GetProperty("count").GetInt32());

        broadcaster.Abort();
    }

    [TestMethod]
    public async Task SignalingRejectsHttpAndInvalidWebSocketRequests()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var httpResponse = await client.GetAsync("/signal");
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, httpResponse.StatusCode);

        var webSocketClient = factory.Server.CreateWebSocketClient();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => webSocketClient.ConnectAsync(
            new Uri("ws://localhost/signal?room=invalid!&role=viewer&id=viewer-1"),
            CancellationToken.None));
        await AssertInvalidWebSocketAsync(factory, "/signal?room=room-1&role=invalid&id=viewer-1");
        await AssertInvalidWebSocketAsync(factory, $"/signal?room=room-1&role=viewer&id={new string('a', 81)}");
        await AssertInvalidWebSocketAsync(factory, "/signal?room=&role=viewer&id=viewer-1");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    private static Task<WebSocket> ConnectAsync(
        WebApplicationFactory<Program> factory,
        string room,
        string role,
        string clientId)
    {
        var client = factory.Server.CreateWebSocketClient();
        var query = $"room={Uri.EscapeDataString(room)}&role={role}&id={Uri.EscapeDataString(clientId)}";
        return client.ConnectAsync(new Uri($"ws://localhost/signal?{query}"), CancellationToken.None);
    }

    private static Task<InvalidOperationException> AssertInvalidWebSocketAsync(
        WebApplicationFactory<Program> factory,
        string path)
    {
        var client = factory.Server.CreateWebSocketClient();
        return Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.ConnectAsync(
            new Uri($"ws://localhost{path}"),
            CancellationToken.None));
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("The socket closed before a JSON message was received.");
            }

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray());
    }

    private static Task SendJsonAsync(WebSocket socket, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task WaitForClosedStateAsync(WebSocket socket)
    {
        var buffer = new byte[1];
        while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;
        }
    }
}
