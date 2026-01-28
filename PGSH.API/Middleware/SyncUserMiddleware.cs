using PGSH.Application.Abstractions.Authentication;

namespace PGSH.API.Middleware
{
    public class SyncUserMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context, IUserContext userContext)
        {
            if (context.User.Identity is { IsAuthenticated: true })
            {
                //throw new Exception("TEST: This should stop the app!");
                await userContext.SyncAsync(context.RequestAborted);
            }
            await next(context);
        }
    }
}


