namespace LumaCast.Infrastructure;

/// <summary>
/// Fornece o middleware com os cabeçalhos HTTP de proteção adotados pelo LumaCast.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adiciona CSP, Permissions Policy e proteções contra MIME sniffing e incorporação em frames.
    /// Deve ser chamado antes do mapeamento dos endpoints.
    /// </summary>
    /// <param name="app">Pipeline HTTP da aplicação.</param>
    /// <returns>O pipeline configurado.</returns>
    public static IApplicationBuilder UseLumaCastSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' https://cdn.jsdelivr.net; " +
                    "style-src 'self'; " +
                    "img-src 'self' data:; " +
                    "media-src 'self' blob:; " +
                    "connect-src 'self' ws: wss:; " +
                    "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
                headers["Permissions-Policy"] = "camera=(self), microphone=(self), fullscreen=(self), picture-in-picture=(self)";
                return Task.CompletedTask;
            });

            await next(context);
        });
    }
}
