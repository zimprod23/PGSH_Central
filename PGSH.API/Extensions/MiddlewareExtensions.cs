using PGSH.API.Middleware;
using PGSH.Infrastructure.Authentication;

namespace PGSH.API.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLoggingMiddleware(this IApplicationBuilder app) 
    {
        app.UseMiddleware<RequestContextLoggingMiddleware>();
        app.UseMiddleware<SyncUserMiddleware>();
        return app;
    }
}
