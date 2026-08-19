using System.Threading.RateLimiting;
using LumaCast.Configuration;
using LumaCast.Endpoints;
using LumaCast.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace LumaCast.Infrastructure;

/// <summary>
/// Centraliza o registro dos serviços, opções e políticas de resiliência da aplicação.
/// </summary>
public static class ApplicationServicesExtensions
{
    /// <summary>
    /// Adiciona Razor Pages, opções tipadas do LiveKit, health checks, serviços de streaming
    /// e políticas de limitação de requisições ao contêiner de dependências.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configuration">Configuração composta por arquivos, segredos e ambiente.</param>
    /// <returns>A mesma coleção para permitir encadeamento.</returns>
    public static IServiceCollection AddLumaCastServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRazorPages();
        services.AddProblemDetails();
        services.AddHealthChecks();

        services.AddOptions<LiveKitOptions>()
            .Bind(configuration.GetSection(LiveKitOptions.SectionName))
            .PostConfigure(options => ApplyLegacyEnvironmentVariables(options, configuration))
            .Validate(options => options.IsEmpty || options.HasAllValues,
                "A configuração LiveKit deve informar URL, API key e API secret em conjunto.")
            .Validate(options => options.IsEmpty || options.HasValidUrl(),
                "A URL do LiveKit deve ser absoluta e usar ws:// ou wss://.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<StreamingSocketManager>();
        services.AddSingleton<LiveKitRoomRegistry>();
        services.AddSingleton<LiveKitTokenService>();
        services.AddLumaCastRateLimiting();

        return services;
    }

    private static void AddLumaCastRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.RoomCreation, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(RateLimitPolicies.TokenIssuance, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetClientKey(context),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(RateLimitPolicies.Signaling, context =>
                RateLimitPartition.GetConcurrencyLimiter(
                    GetClientKey(context),
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 30,
                        QueueLimit = 0
                    }));
        });
    }

    private static string GetClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static void ApplyLegacyEnvironmentVariables(
        LiveKitOptions options,
        IConfiguration configuration)
    {
        options.Url = PreferEnvironmentValue(configuration["LIVEKIT_URL"], options.Url);
        options.ApiKey = PreferEnvironmentValue(configuration["LIVEKIT_API_KEY"], options.ApiKey);
        options.ApiSecret = PreferEnvironmentValue(configuration["LIVEKIT_API_SECRET"], options.ApiSecret);
    }

    private static string? PreferEnvironmentValue(string? environmentValue, string? configuredValue) =>
        !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue.Trim() : configuredValue?.Trim();
}
