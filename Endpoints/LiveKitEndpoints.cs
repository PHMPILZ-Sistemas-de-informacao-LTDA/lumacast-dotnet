using LumaCast.Services;

namespace LumaCast.Endpoints;

public static class LiveKitEndpoints
{
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

public static class RateLimitPolicies
{
    public const string RoomCreation = "livekit-room-creation";
    public const string TokenIssuance = "livekit-token-issuance";
    public const string Signaling = "p2p-signaling";
}

public sealed record LiveKitTokenRequest(
    string RoomName,
    string Role,
    string? ParticipantName,
    string? BroadcastKey);

public sealed record EndLiveKitRoomRequest(string? BroadcastKey);
public sealed record LiveKitStatusResponse(bool Configured, string Provider);
public sealed record LiveKitRoomResponse(string RoomName, string BroadcastKey);
public sealed record ApiMessage(string Message);
