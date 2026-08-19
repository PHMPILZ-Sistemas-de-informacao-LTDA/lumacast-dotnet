using System.Text.Json.Serialization;
using Livekit.Server.Sdk.Dotnet;

namespace LumaCast.Services;

public sealed class LiveKitTokenService
{
    private readonly string? _serverUrl;
    private readonly string? _apiKey;
    private readonly string? _apiSecret;

    public LiveKitTokenService(IConfiguration configuration)
    {
        _serverUrl = FirstValue(configuration["LIVEKIT_URL"], configuration["LiveKit:Url"]);
        _apiKey = FirstValue(configuration["LIVEKIT_API_KEY"], configuration["LiveKit:ApiKey"]);
        _apiSecret = FirstValue(configuration["LIVEKIT_API_SECRET"], configuration["LiveKit:ApiSecret"]);
    }

    public bool IsConfigured =>
        Uri.TryCreate(_serverUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "ws" || uri.Scheme == "wss") &&
        !string.IsNullOrWhiteSpace(_apiKey) &&
        !string.IsNullOrWhiteSpace(_apiSecret);

    public LiveKitCredential CreateParticipantToken(
        string roomName,
        string identity,
        string participantName,
        bool canPublish)
    {
        if (!IsConfigured) throw new InvalidOperationException("LiveKit não está configurado.");

        var token = new AccessToken(_apiKey!, _apiSecret!)
            .WithIdentity(identity)
            .WithName(participantName)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = roomName,
                CanPublish = canPublish,
                CanPublishData = false,
                CanSubscribe = !canPublish
            })
            .WithTtl(TimeSpan.FromHours(canPublish ? 6 : 3));

        return new LiveKitCredential(_serverUrl!, token.ToJwt());
    }

    private static string? FirstValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}

public sealed record LiveKitCredential(
    [property: JsonPropertyName("server_url")] string ServerUrl,
    [property: JsonPropertyName("participant_token")] string ParticipantToken);
