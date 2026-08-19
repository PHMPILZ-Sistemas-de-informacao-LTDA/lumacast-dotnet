using LumaCast.Services;

namespace LumaCast.Endpoints;

/// <summary>Mapeia os endpoints HTTP responsáveis por salas e credenciais LiveKit.</summary>
public static class LiveKitEndpoints
{
    /// <summary>
    /// Registra status, criação e encerramento de salas e emissão de tokens sob <c>/api/livekit</c>.
    /// </summary>
    /// <param name="endpoints">Construtor de rotas da aplicação.</param>
    /// <returns>O mesmo construtor de rotas para permitir encadeamento.</returns>
    public static IEndpointRouteBuilder MapLiveKitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/livekit").WithTags("LiveKit");

        group.MapGet("/status", (LiveKitTokenService tokens) => TypedResults.Ok(new LiveKitStatusResponse(
            tokens.IsConfigured,
            tokens.IsConfigured ? "livekit" : "peer-to-peer")));

        group.MapPost("/rooms", (LiveKitTokenService tokens, LiveKitRoomRegistry rooms) =>
        {
            if (!tokens.IsConfigured)
            {
                return Results.Problem(
                    title: "LiveKit não configurado",
                    detail: "Configure LIVEKIT_URL, LIVEKIT_API_KEY e LIVEKIT_API_SECRET para ativar o modo LiveKit.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var room = rooms.Create();
            return Results.Created($"/api/livekit/rooms/{room.RoomName}", new LiveKitRoomResponse(
                room.RoomName,
                room.BroadcastKey));
        }).RequireRateLimiting(RateLimitPolicies.RoomCreation);

        group.MapPost("/token", (LiveKitTokenRequest request, LiveKitTokenService tokens, LiveKitRoomRegistry rooms) =>
        {
            if (!tokens.IsConfigured)
            {
                return Results.Problem(
                    title: "LiveKit não configurado",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (string.IsNullOrWhiteSpace(request.RoomName) || !rooms.IsActive(request.RoomName))
            {
                return Results.NotFound(new ApiMessage("A transmissão não está disponível."));
            }

            var isBroadcaster = string.Equals(request.Role, "broadcaster", StringComparison.OrdinalIgnoreCase);
            if (isBroadcaster && !rooms.ValidateBroadcaster(request.RoomName, request.BroadcastKey))
            {
                return Results.Unauthorized();
            }

            var defaultName = isBroadcaster ? "Apresentador" : "Espectador";
            var participantName = NormalizeParticipantName(request.ParticipantName, defaultName);
            var prefix = isBroadcaster ? "host" : "viewer";
            var identity = $"{prefix}-{Guid.NewGuid():N}";
            var credential = tokens.CreateParticipantToken(
                request.RoomName,
                identity,
                participantName,
                isBroadcaster);

            return Results.Json(credential, statusCode: StatusCodes.Status201Created);
        }).RequireRateLimiting(RateLimitPolicies.TokenIssuance);

        group.MapPost("/rooms/{roomName}/end", (string roomName, EndLiveKitRoomRequest request, LiveKitRoomRegistry rooms) =>
        {
            return rooms.End(roomName, request.BroadcastKey)
                ? Results.NoContent()
                : Results.Unauthorized();
        }).RequireRateLimiting(RateLimitPolicies.TokenIssuance);

        return endpoints;
    }

    private static string NormalizeParticipantName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, 60)];
    }
}

/// <summary>Define os nomes estáveis das políticas de rate limiting usadas pelos endpoints.</summary>
public static class RateLimitPolicies
{
    /// <summary>Política aplicada à criação de salas.</summary>
    public const string RoomCreation = "livekit-room-creation";

    /// <summary>Política aplicada à emissão de tokens e ao encerramento de salas.</summary>
    public const string TokenIssuance = "livekit-token-issuance";

    /// <summary>Política aplicada às conexões WebSocket de sinalização.</summary>
    public const string Signaling = "p2p-signaling";
}

/// <summary>Solicitação para emitir uma credencial de participante.</summary>
/// <param name="RoomName">Sala que o participante deseja acessar.</param>
/// <param name="Role">Papel solicitado: apresentador ou espectador.</param>
/// <param name="ParticipantName">Nome opcional exibido na sessão.</param>
/// <param name="BroadcastKey">Chave obrigatória somente para o apresentador.</param>
public sealed record LiveKitTokenRequest(
    string RoomName,
    string Role,
    string? ParticipantName,
    string? BroadcastKey);

/// <summary>Solicitação autenticada para encerrar uma sala.</summary>
/// <param name="BroadcastKey">Chave privada do apresentador.</param>
public sealed record EndLiveKitRoomRequest(string? BroadcastKey);

/// <summary>Informa ao cliente qual provedor de mídia está ativo.</summary>
/// <param name="Configured">Indica se o LiveKit está configurado.</param>
/// <param name="Provider">Nome do provedor selecionado.</param>
public sealed record LiveKitStatusResponse(bool Configured, string Provider);

/// <summary>Resposta devolvida ao criar uma sala LiveKit.</summary>
/// <param name="RoomName">Nome público compartilhável da sala.</param>
/// <param name="BroadcastKey">Chave privada que deve permanecer com o apresentador.</param>
public sealed record LiveKitRoomResponse(string RoomName, string BroadcastKey);

/// <summary>Resposta simples usada para comunicar mensagens de API.</summary>
/// <param name="Message">Mensagem destinada ao consumidor da API.</param>
public sealed record ApiMessage(string Message);
