using PGSH.API.Middleware;

namespace PGSH.API.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLoggingMiddleware(this IApplicationBuilder app) 
    {
        app.UseMiddleware<RequestContextLoggingMiddleware>();
        return app;
    }
}
