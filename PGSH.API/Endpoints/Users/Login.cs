
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Users.Login;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Users
{
    public sealed class Login : IEndpoint
    {
        public sealed record Request(string email, string password);
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("users/login", async (Request request, ISender sender, CancellationToken cancellationToken) => 
            { 
                //var command = new LoginUserCommand(request.email, request.password);
                //Result<string> result = await sender.Send(command, cancellationToken);

                return Results.Ok();
            })
            .WithTags(Tags.Users);
        }
    }
}
