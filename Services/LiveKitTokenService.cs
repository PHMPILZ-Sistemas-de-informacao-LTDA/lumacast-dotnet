using System.Text.Json.Serialization;
using Livekit.Server.Sdk.Dotnet;
using LumaCast.Configuration;
using Microsoft.Extensions.Options;

namespace LumaCast.Services;

/// <summary>
/// Gera credenciais de curta duração para participantes do LiveKit sem expor o segredo da API
/// ao navegador.
/// </summary>
/// <param name="options">Configuração validada da integração LiveKit.</param>
public sealed class LiveKitTokenService(IOptions<LiveKitOptions> options)
{
    private readonly LiveKitOptions _options = options.Value;

    /// <summary>Indica se a integração LiveKit está pronta para emitir tokens.</summary>
    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// Cria um JWT com permissões adequadas ao apresentador ou ao espectador de uma sala.
    /// </summary>
    /// <param name="roomName">Nome interno da sala LiveKit.</param>
    /// <param name="identity">Identidade única do participante.</param>
    /// <param name="participantName">Nome exibido na sessão.</param>
    /// <param name="canPublish">Define se o participante pode publicar áudio e vídeo.</param>
    /// <returns>URL do servidor e token que serão enviados ao cliente.</returns>
    /// <exception cref="InvalidOperationException">A integração LiveKit não está configurada.</exception>
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

/// <summary>Transporta ao navegador os dados temporários necessários para entrar no LiveKit.</summary>
/// <param name="ServerUrl">URL WebSocket do servidor LiveKit.</param>
/// <param name="ParticipantToken">JWT de curta duração do participante.</param>
public sealed record LiveKitCredential(
    [property: JsonPropertyName("server_url")] string ServerUrl,
    [property: JsonPropertyName("participant_token")] string ParticipantToken);
