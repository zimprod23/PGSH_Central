using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Students.GetById;

namespace PGSH.API.Endpoints.Students
{
    public sealed class GetCurrent : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/students/me", async (
                IUserContext userContext,
                ISender sender,
                CancellationToken ct) =>
            {
                //// 1. Ensure the IdentityProvider user is synced with our Domain User
                //await userContext.SyncAsync(ct);

                // 2. Send query (The Handler will use userContext.UserId internally)
                var query = new GetCurrentStudentQuery();
                var result = await sender.Send(query, ct);

                // 3. Use your Result pattern matching
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Students)
            .RequireAuthorization(); // Essential for UserContext to work
        }
    }
}