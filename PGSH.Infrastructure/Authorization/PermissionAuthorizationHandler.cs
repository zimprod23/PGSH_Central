using Microsoft.AspNetCore.Authorization;

namespace PGSH.Infrastructure.Authorization;

internal sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User is { Identity.IsAuthenticated: true } &&
            context.User.IsInRole(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
