using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LumaCast.Services;

/// <summary>
/// Coordena a sinalização WebRTC entre um apresentador e espectadores por WebSocket.
/// Este serviço é o fallback local quando o LiveKit não está configurado.
/// </summary>
public sealed class StreamingSocketManager
{
    private const int MaximumSignalMessageSize = 128 * 1024;
    private const int MaximumPeerToPeerViewers = 20;
    private readonly ConcurrentDictionary<string, StreamingRoom> _rooms = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Aceita uma conexão WebSocket validada e executa o fluxo correspondente ao papel informado.
    /// </summary>
    /// <param name="context">Contexto HTTP que contém a solicitação WebSocket.</param>
    /// <param name="roomId">Identificador da sala P2P.</param>
    /// <param name="role">Papel do cliente: <c>broadcaster</c> ou <c>viewer</c>.</param>
    /// <param name="clientId">Identificador único do cliente na sala.</param>
    /// <returns>Uma tarefa concluída quando a conexão é encerrada.</returns>
    public async Task HandleAsync(HttpContext context, string roomId, string role, string clientId)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var room = _rooms.GetOrAdd(roomId, _ => new StreamingRoom());

        if (role == "broadcaster")
        {
            await HandleBroadcasterAsync(roomId, room, socket, context.RequestAborted);
            return;
        }

        await HandleViewerAsync(roomId, room, clientId, socket, context.RequestAborted);
    }

    private async Task HandleBroadcasterAsync(
        string roomId,
        StreamingRoom room,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        room.Broadcaster = socket;
        await SendJsonAsync(socket, new { type = "connected", room = roomId }, cancellationToken);
        await SendViewerCountAsync(room, cancellationToken);

        try
        {
            await ReceiveLoopAsync(socket, async message =>
            {
                var payload = JsonNode.Parse(message)?.AsObject();
                var viewerId = payload?["viewerId"]?.GetValue<string>();
                if (viewerId is null || !room.Viewers.TryGetValue(viewerId, out var viewer)) return;
                await SendTextAsync(viewer.Socket, message, viewer.SendLock, cancellationToken);
            }, cancellationToken);
        }
        finally
        {
            if (ReferenceEquals(room.Broadcaster, socket)) room.Broadcaster = null;
            foreach (var viewer in room.Viewers.Values)
            {
                await CloseQuietlyAsync(viewer.Socket, "Transmissão encerrada", cancellationToken);
            }
            room.Viewers.Clear();
            _rooms.TryRemove(roomId, out _);
        }
    }

    private async Task HandleViewerAsync(
        string roomId,
        StreamingRoom room,
        string clientId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        if (room.Broadcaster?.State != WebSocketState.Open)
        {
            await SendJsonAsync(socket, new { type = "offline" }, cancellationToken);
            await CloseQuietlyAsync(socket, "Transmissão indisponível", cancellationToken);
            return;
        }

        if (room.Viewers.Count >= MaximumPeerToPeerViewers)
        {
            await SendJsonAsync(socket, new { type = "full" }, cancellationToken);
            await CloseQuietlyAsync(socket, "Sala P2P lotada", cancellationToken);
            return;
        }

        var viewer = new ViewerConnection(socket);
        room.Viewers[clientId] = viewer;
        await SendJsonAsync(socket, new { type = "connected", viewerId = clientId }, cancellationToken, viewer.SendLock);
        await SendJsonToBroadcasterAsync(room, new { type = "viewer-joined", viewerId = clientId }, cancellationToken);
        await SendViewerCountAsync(room, cancellationToken);

        try
        {
            await ReceiveLoopAsync(socket, async message =>
            {
                var payload = JsonNode.Parse(message)?.AsObject() ?? new JsonObject();
                payload["viewerId"] = clientId;
                await SendTextToBroadcasterAsync(room, payload.ToJsonString(_jsonOptions), cancellationToken);
            }, cancellationToken);
        }
        finally
        {
            room.Viewers.TryRemove(clientId, out _);
            await SendJsonToBroadcasterAsync(room, new { type = "viewer-left", viewerId = clientId }, cancellationToken);
            await SendViewerCountAsync(room, cancellationToken);
        }
    }

    private static async Task ReceiveLoopAsync(
        WebSocket socket,
        Func<string, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageBuffer = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (messageBuffer.Length + result.Count > MaximumSignalMessageSize)
                {
                    await CloseQuietlyAsync(socket, "Mensagem de sinalização muito grande", cancellationToken);
                    return;
                }
                messageBuffer.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            await onMessage(Encoding.UTF8.GetString(messageBuffer.ToArray()));
        }
    }

    private async Task SendViewerCountAsync(StreamingRoom room, CancellationToken cancellationToken)
    {
        await SendJsonToBroadcasterAsync(room, new { type = "viewer-count", count = room.Viewers.Count }, cancellationToken);
    }

    private async Task SendJsonToBroadcasterAsync(StreamingRoom room, object payload, CancellationToken cancellationToken)
    {
        if (room.Broadcaster?.State != WebSocketState.Open) return;
        var message = JsonSerializer.Serialize(payload, _jsonOptions);
        await SendTextToBroadcasterAsync(room, message, cancellationToken);
    }

    private static async Task SendTextToBroadcasterAsync(StreamingRoom room, string message, CancellationToken cancellationToken)
    {
        if (room.Broadcaster?.State != WebSocketState.Open) return;
        await SendTextAsync(room.Broadcaster, message, room.BroadcasterSendLock, cancellationToken);
    }

    private async Task SendJsonAsync(
        WebSocket socket,
        object payload,
        CancellationToken cancellationToken,
        SemaphoreSlim? sendLock = null)
    {
        var message = JsonSerializer.Serialize(payload, _jsonOptions);
        await SendTextAsync(socket, message, sendLock ?? new SemaphoreSlim(1, 1), cancellationToken);
    }

    private static async Task SendTextAsync(
        WebSocket socket,
        string message,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(
                    Encoding.UTF8.GetBytes(message),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
            }
        }
        catch (WebSocketException)
        {
            // The receive loop handles disconnected clients.
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, string description, CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, description, cancellationToken);
        }
        catch (WebSocketException)
        {
            // Client already disconnected.
        }
    }

    private sealed class StreamingRoom
    {
        public WebSocket? Broadcaster { get; set; }
        public SemaphoreSlim BroadcasterSendLock { get; } = new(1, 1);
        public ConcurrentDictionary<string, ViewerConnection> Viewers { get; } = new();
    }

    private sealed record ViewerConnection(WebSocket Socket)
    {
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}
