using AccessControll.Domain.Entities;
using AccessControll.Domain.Interfaces;
using MediatR;

namespace AccessControll.Application.Roles;

// ── Queries ──────────────────────────────────────────────────────────────────

public record GetRolePanelsQuery(string RoleName) : IRequest<List<string>>;

/// <summary>Returns union of panels for all given roles. Admin gets every panel.</summary>
public record GetCurrentUserPanelsQuery(IEnumerable<string> UserRoles) : IRequest<List<string>>;

// ── Commands ─────────────────────────────────────────────────────────────────

public record SetRolePanelsCommand(string RoleName, List<string> Panels) : IRequest<bool>;

// ── Handlers ─────────────────────────────────────────────────────────────────

public class GetRolePanelsQueryHandler(IRolePanelPermissionRepository repo)
    : IRequestHandler<GetRolePanelsQuery, List<string>>
{
    public Task<List<string>> Handle(GetRolePanelsQuery request, CancellationToken ct)
        => repo.GetPanelsForRoleAsync(request.RoleName);
}

public class GetCurrentUserPanelsQueryHandler(IRolePanelPermissionRepository repo)
    : IRequestHandler<GetCurrentUserPanelsQuery, List<string>>
{
    public async Task<List<string>> Handle(GetCurrentUserPanelsQuery request, CancellationToken ct)
    {
        var roles = request.UserRoles.ToList();

        // Admin always gets full access — no DB lookup needed
        if (roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            return AppPanels.All.ToList();

        return await repo.GetPanelsForRolesAsync(roles);
    }
}

public class SetRolePanelsCommandHandler(IRolePanelPermissionRepository repo)
    : IRequestHandler<SetRolePanelsCommand, bool>
{
    public async Task<bool> Handle(SetRolePanelsCommand request, CancellationToken ct)
    {
        // Validate that all requested panels are known
        var valid = request.Panels.All(p => AppPanels.All.Contains(p));
        if (!valid) return false;

        await repo.SetPanelsForRoleAsync(request.RoleName, request.Panels);
        return true;
    }
}
