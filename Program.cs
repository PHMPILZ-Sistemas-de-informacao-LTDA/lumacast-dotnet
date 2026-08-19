using LumaCast.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton<StreamingSocketManager>();
builder.Services.AddSingleton<LiveKitRoomRegistry>();
builder.Services.AddSingleton<LiveKitTokenService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapGet("/api/livekit/status", (LiveKitTokenService tokens) => Results.Ok(new
{
    configured = tokens.IsConfigured,
    provider = tokens.IsConfigured ? "livekit" : "peer-to-peer"
}));

app.MapPost("/api/livekit/rooms", (LiveKitTokenService tokens, LiveKitRoomRegistry rooms) =>
{
    if (!tokens.IsConfigured)
    {
        return Results.Problem(
            "Configure LIVEKIT_URL, LIVEKIT_API_KEY e LIVEKIT_API_SECRET para ativar o modo LiveKit.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var room = rooms.Create();
    return Results.Created($"/api/livekit/rooms/{room.RoomName}", new
    {
        roomName = room.RoomName,
        broadcastKey = room.BroadcastKey
    });
});

app.MapPost("/api/livekit/token", (LiveKitTokenRequest request, LiveKitTokenService tokens, LiveKitRoomRegistry rooms) =>
{
    if (!tokens.IsConfigured)
    {
        return Results.Problem("LiveKit não está configurado.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (string.IsNullOrWhiteSpace(request.RoomName) || !rooms.IsActive(request.RoomName))
    {
        return Results.NotFound(new { message = "A transmissão não está disponível." });
    }

    var isBroadcaster = string.Equals(request.Role, "broadcaster", StringComparison.OrdinalIgnoreCase);
    if (isBroadcaster && !rooms.ValidateBroadcaster(request.RoomName, request.BroadcastKey))
    {
        return Results.Unauthorized();
    }

    var participantName = string.IsNullOrWhiteSpace(request.ParticipantName)
        ? (isBroadcaster ? "Apresentador" : "Espectador")
        : request.ParticipantName.Trim()[..Math.Min(request.ParticipantName.Trim().Length, 60)];
    var prefix = isBroadcaster ? "host" : "viewer";
    var identity = $"{prefix}-{Guid.NewGuid():N}";
    var credential = tokens.CreateParticipantToken(request.RoomName, identity, participantName, isBroadcaster);

    return Results.Json(credential, statusCode: StatusCodes.Status201Created);
});

app.MapPost("/api/livekit/rooms/{roomName}/end", (string roomName, EndLiveKitRoomRequest request, LiveKitRoomRegistry rooms) =>
{
    return rooms.End(roomName, request.BroadcastKey)
        ? Results.NoContent()
        : Results.Unauthorized();
});

app.Map("/signal", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Uma conexão WebSocket é necessária.");
        return;
    }

    var roomId = context.Request.Query["room"].ToString();
    var role = context.Request.Query["role"].ToString();
    var clientId = context.Request.Query["id"].ToString();

    if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(clientId) ||
        (role != "broadcaster" && role != "viewer"))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Parâmetros da sala inválidos.");
        return;
    }

    var manager = context.RequestServices.GetRequiredService<StreamingSocketManager>();
    await manager.HandleAsync(context, roomId, role, clientId);
});

app.MapRazorPages();
app.Run();

public sealed record LiveKitTokenRequest(
    string RoomName,
    string Role,
    string? ParticipantName,
    string? BroadcastKey);

public sealed record EndLiveKitRoomRequest(string? BroadcastKey);
