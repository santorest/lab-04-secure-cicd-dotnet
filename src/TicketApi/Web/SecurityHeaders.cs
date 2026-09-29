namespace TicketApi.Web;

public static class SecurityHeaders
{
    /// <summary>Largest request body accepted (the biggest valid ticket is well under this).</summary>
    public const long MaxRequestBodyBytes = 64 * 1024;

    /// <summary>
    /// Headers for a JSON API that is never rendered as a page: nothing may load, frame or sniff it, and
    /// responses under /api are never cached.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers.Remove("Server");
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });
        await next(context);
    });

    /// <summary>
    /// Rejects declared bodies over the limit with 413 before anything reads them. Kestrel enforces the same limit
    /// on streamed (chunked) bodies in real deployments.
    /// </summary>
    public static IApplicationBuilder UseRequestSizeLimit(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.ContentLength > MaxRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        await next(context);
    });
}
