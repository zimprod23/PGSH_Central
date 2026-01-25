using PGSH.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace PGSH.Infrastructure.Authorization;

internal sealed class PermissionAuthorizationHandler(IServiceScopeFactory serviceScopeFactory)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // TODO: You definitely want to reject unauthenticated users here.
        if (context.User is { Identity.IsAuthenticated: true } && context.User.IsInRole(requirement.Permission))
        {
            // TODO: Remove this call when you implement the PermissionProvider.GetForUserIdAsync
            context.Succeed(requirement);

            //return;
        }
        return Task.CompletedTask;

        //using IServiceScope scope = serviceScopeFactory.CreateScope();

        //PermissionProvider permissionProvider = scope.ServiceProvider.GetRequiredService<PermissionProvider>();

        //Guid userId = context.User.GetUserId();

        //HashSet<string> permissions = await permissionProvider.GetForUserIdAsync(userId);

        //if (permissions.Contains(requirement.Permission))
        //{
        //    context.Succeed(requirement);
        //}
    }
}
