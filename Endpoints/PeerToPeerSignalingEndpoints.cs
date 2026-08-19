using LumaCast.Services;

namespace LumaCast.Endpoints;

/// <summary>
/// Mapeia o canal WebSocket usado como sinalização WebRTC no modo ponto a ponto.
/// </summary>
public static class PeerToPeerSignalingEndpoints
{
    /// <summary>
    /// Registra o endpoint <c>/signal</c>, valida os identificadores da sala e encaminha
    /// a conexão ao gerenciador de sockets.
    /// </summary>
    /// <param name="endpoints">Construtor de rotas da aplicação.</param>
    /// <returns>O mesmo construtor de rotas para permitir encadeamento.</returns>
    public static IEndpointRouteBuilder MapPeerToPeerSignaling(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/signal", async (HttpContext context, StreamingSocketManager manager) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest(new ApiMessage("Uma conexão WebSocket é necessária."));
            }

            var roomId = context.Request.Query["room"].ToString();
            var role = context.Request.Query["role"].ToString();
            var clientId = context.Request.Query["id"].ToString();

            if (!IsValidIdentifier(roomId, 64) ||
                !IsValidIdentifier(clientId, 80) ||
                role is not ("broadcaster" or "viewer"))
            {
                return Results.BadRequest(new ApiMessage("Parâmetros da sala inválidos."));
            }

            try
            {
                await manager.HandleAsync(context, roomId, role, clientId);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The browser closed the connection.
            }

            return Results.Empty;
        }).RequireRateLimiting(RateLimitPolicies.Signaling);

        return endpoints;
    }

    private static bool IsValidIdentifier(string value, int maximumLength) =>
        value.Length is > 0 && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
