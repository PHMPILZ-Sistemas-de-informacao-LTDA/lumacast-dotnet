namespace LumaCast.Infrastructure;

public static class SecurityHeadersExtensions
{
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
