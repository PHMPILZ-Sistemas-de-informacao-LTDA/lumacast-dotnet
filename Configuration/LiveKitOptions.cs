namespace LumaCast.Configuration;

/// <summary>
/// Representa as credenciais e o endereço usados para conectar o backend ao LiveKit.
/// Os valores são vinculados à seção <c>LiveKit</c> da configuração da aplicação.
/// </summary>
public sealed class LiveKitOptions
{
    /// <summary>Nome da seção utilizada em <c>appsettings.json</c>.</summary>
    public const string SectionName = "LiveKit";

    /// <summary>Obtém ou define a URL WebSocket do servidor LiveKit.</summary>
    public string? Url { get; set; }

    /// <summary>Obtém ou define a chave pública da API LiveKit.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Obtém ou define o segredo usado exclusivamente pelo backend para assinar tokens.</summary>
    public string? ApiSecret { get; set; }

    /// <summary>Indica se nenhum valor LiveKit foi informado e o fallback P2P deve ser utilizado.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Url) &&
        string.IsNullOrWhiteSpace(ApiKey) &&
        string.IsNullOrWhiteSpace(ApiSecret);

    /// <summary>Indica se todos os valores necessários estão presentes e a URL é válida.</summary>
    public bool IsConfigured =>
        HasValidUrl() &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    /// <summary>Indica se URL, chave e segredo foram informados em conjunto.</summary>
    public bool HasAllValues =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    /// <summary>Valida se a URL é absoluta e usa o protocolo <c>ws</c> ou <c>wss</c>.</summary>
    /// <returns><see langword="true"/> quando a URL pode ser usada pelo cliente LiveKit.</returns>
    public bool HasValidUrl() =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "ws" or "wss";
}
