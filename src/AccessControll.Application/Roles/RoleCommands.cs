using MediatR;
using Microsoft.AspNetCore.Identity;
using AccessControll.Contracts.Roles;

namespace AccessControll.Application.Roles;

// ── Queries & Commands ────────────────────────────────────────────────────────

public record GetAllRolesQuery : IRequest<IEnumerable<RoleDto>>;
public record CreateRoleCommand(string Name) : IRequest<(bool Succeeded, string? Error)>;
public record DeleteRoleCommand(string Id) : IRequest<(bool Succeeded, string? Error)>;
public record AssignRoleToUserCommand(string UserId, string RoleName) : IRequest<bool>;
public record RemoveRoleFromUserCommand(string UserId, string RoleName) : IRequest<bool>;

// ── Handlers ──────────────────────────────────────────────────────────────────

public class GetAllRolesQueryHandler : IRequestHandler<GetAllRolesQuery, IEnumerable<RoleDto>>
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> _userManager;

    public GetAllRolesQueryHandler(
        RoleManager<IdentityRole> roleManager,
        Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task<IEnumerable<RoleDto>> Handle(GetAllRolesQuery request, CancellationToken ct)
    {
        var roles = _roleManager.Roles.ToList();
        var dtos = new List<RoleDto>();
        foreach (var role in roles)
        {
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            dtos.Add(new RoleDto(role.Id, role.Name!, usersInRole.Count));
        }
        return dtos;
    }
}

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, (bool Succeeded, string? Error)>
{
    private readonly RoleManager<IdentityRole> _roleManager;
    public CreateRoleCommandHandler(RoleManager<IdentityRole> roleManager) => _roleManager = roleManager;

    public async Task<(bool Succeeded, string? Error)> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        if (await _roleManager.RoleExistsAsync(request.Name))
            return (false, "این نقش از قبل وجود دارد");

        var result = await _roleManager.CreateAsync(new IdentityRole(request.Name));
        return result.Succeeded
            ? (true, null)
            : (false, string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, (bool Succeeded, string? Error)>
{
    private readonly RoleManager<IdentityRole> _roleManager;
    public DeleteRoleCommandHandler(RoleManager<IdentityRole> roleManager) => _roleManager = roleManager;

    public async Task<(bool Succeeded, string? Error)> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await _roleManager.FindByIdAsync(request.Id);
        if (role == null) return (false, "نقش یافت نشد");

        var result = await _roleManager.DeleteAsync(role);
        return result.Succeeded
            ? (true, null)
            : (false, string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}

public class AssignRoleToUserCommandHandler : IRequestHandler<AssignRoleToUserCommand, bool>
{
    private readonly Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> _userManager;
    public AssignRoleToUserCommandHandler(Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<bool> Handle(AssignRoleToUserCommand request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return false;
        var result = await _userManager.AddToRoleAsync(user, request.RoleName);
        return result.Succeeded;
    }
}

public class RemoveRoleFromUserCommandHandler : IRequestHandler<RemoveRoleFromUserCommand, bool>
{
    private readonly Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> _userManager;
    public RemoveRoleFromUserCommandHandler(Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<bool> Handle(RemoveRoleFromUserCommand request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return false;
        var result = await _userManager.RemoveFromRoleAsync(user, request.RoleName);
        return result.Succeeded;
    }
}
