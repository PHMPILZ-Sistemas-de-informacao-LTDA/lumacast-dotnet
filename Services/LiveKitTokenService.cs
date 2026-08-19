using System.Text.Json.Serialization;
using Livekit.Server.Sdk.Dotnet;
using LumaCast.Configuration;
using Microsoft.Extensions.Options;

namespace LumaCast.Services;

public sealed class LiveKitTokenService(IOptions<LiveKitOptions> options)
{
    private readonly LiveKitOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public LiveKitCredential CreateParticipantToken(
        string roomName,
        string identity,
        string participantName,
        bool canPublish)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("LiveKit não está configurado.");
        }

        var token = new AccessToken(_options.ApiKey!, _options.ApiSecret!)
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

        return new LiveKitCredential(_options.Url!, token.ToJwt());
    }
}

public sealed record LiveKitCredential(
    [property: JsonPropertyName("server_url")] string ServerUrl,
    [property: JsonPropertyName("participant_token")] string ParticipantToken);
