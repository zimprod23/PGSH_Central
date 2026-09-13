using PGSH.Application.Abstractions.Authentication;

namespace PGSH.API.Middleware
{
    public class SyncUserMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context, IUserContext userContext)
        {
            if (context.User.Identity is { IsAuthenticated: true })
            {
                // ⚠ Une exception d'ici sort par GlobalExceptionHandler, qui distingue une base
                // injoignable (503, et une phrase) d'un défaut (500). Voir DatabaseOutage : c'est ce
                // middleware, traversé par *chaque* requête authentifiée, qui a fait lire une panne
                // d'infrastructure comme une panne de l'application le 13/09/2026.
                await userContext.SyncAsync(context.RequestAborted);
            }
            await next(context);
        }
    }
}


