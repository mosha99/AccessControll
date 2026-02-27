using Microsoft.AspNetCore.Authorization;
using AccessControll.Domain.Interfaces;

namespace AccessControll.Infrastructure.Authorization;

public class PanelAccessRequirement(string panel) : IAuthorizationRequirement
{
    public string Panel { get; } = panel;
}

public class PanelAccessHandler(IRolePanelPermissionRepository repo)
    : AuthorizationHandler<PanelAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PanelAccessRequirement requirement)
    {
        // Admin always succeeds — no DB lookup needed
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
            return;
        }

        var userRoles = context.User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        if (userRoles.Count == 0)
            return;

        var panels = await repo.GetPanelsForRolesAsync(userRoles);

        if (panels.Contains(requirement.Panel, StringComparer.OrdinalIgnoreCase))
            context.Succeed(requirement);
    }
}
