namespace LumaCast.Configuration;

public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    public string? Url { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Url) &&
        string.IsNullOrWhiteSpace(ApiKey) &&
        string.IsNullOrWhiteSpace(ApiSecret);

    public bool IsConfigured =>
        HasValidUrl() &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    public bool HasAllValues =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    public bool HasValidUrl() =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "ws" or "wss";
}
