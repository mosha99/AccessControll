using MediatR;
using Microsoft.AspNetCore.Identity;
using AccessControll.Domain.Entities;
using AccessControll.Contracts.Users;

namespace AccessControll.Application.Users;

// ── Queries & Commands ────────────────────────────────────────────────────────

public record GetAllUsersQuery(int Page = 1, int PageSize = 20)
    : IRequest<(IEnumerable<UserDto> Users, int Total)>;

public record GetUserByIdQuery(string Id)
    : IRequest<UserDto?>;

public record GetCurrentUserProfileQuery(string UserId)
    : IRequest<ProfileDto?>;

public record CreateUserCommand(string Email, string FullName, string Password, List<string> Roles)
    : IRequest<(bool Succeeded, IEnumerable<string> Errors)>;

public record UpdateUserCommand(string Id, string FullName, bool IsActive, List<string> Roles)
    : IRequest<bool>;

public record DeleteUserCommand(string Id)
    : IRequest<bool>;

public record ToggleUserActiveCommand(string Id)
    : IRequest<bool>;

// ── Handlers ──────────────────────────────────────────────────────────────────

public class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, (IEnumerable<UserDto> Users, int Total)>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public GetAllUsersQueryHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<(IEnumerable<UserDto> Users, int Total)> Handle(GetAllUsersQuery request, CancellationToken ct)
    {
        var allUsers = _userManager.Users.OrderBy(u => u.FullName).ToList();
        var total = allUsers.Count;
        var paged = allUsers.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);

        var dtos = new List<UserDto>();
        foreach (var u in paged)
        {
            var roles = await _userManager.GetRolesAsync(u);
            dtos.Add(u.ToDto(roles));
        }
        return (dtos, total);
    }
}

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public GetUserByIdQueryHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.Id);
        if (user == null) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return user.ToDto(roles);
    }
}

public class GetCurrentUserProfileQueryHandler : IRequestHandler<GetCurrentUserProfileQuery, ProfileDto?>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public GetCurrentUserProfileQueryHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<ProfileDto?> Handle(GetCurrentUserProfileQuery request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return new ProfileDto(user.Id, user.Email ?? "", user.FullName,
            user.TwoFactorEnabled, user.CreatedAt, user.LastLoginAt, roles);
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, (bool Succeeded, IEnumerable<string> Errors)>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public CreateUserCommandHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<(bool Succeeded, IEnumerable<string> Errors)> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return (false, result.Errors.Select(e => e.Description));

        if (request.Roles.Any())
            await _userManager.AddToRolesAsync(user, request.Roles);

        return (true, Enumerable.Empty<string>());
    }
}

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public UpdateUserCommandHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<bool> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.Id);
        if (user == null) return false;

        user.FullName = request.FullName;
        user.IsActive = request.IsActive;
        await _userManager.UpdateAsync(user);

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        if (request.Roles.Any())
            await _userManager.AddToRolesAsync(user, request.Roles);

        return true;
    }
}

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public DeleteUserCommandHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<bool> Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.Id);
        if (user == null) return false;
        var result = await _userManager.DeleteAsync(user);
        return result.Succeeded;
    }
}

public class ToggleUserActiveCommandHandler : IRequestHandler<ToggleUserActiveCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;
    public ToggleUserActiveCommandHandler(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<bool> Handle(ToggleUserActiveCommand request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(request.Id);
        if (user == null) return false;
        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        return user.IsActive;
    }
}

// ── Mapping helpers ───────────────────────────────────────────────────────────

internal static class UserMappingExtensions
{
    public static UserDto ToDto(this ApplicationUser u, IList<string> roles) =>
        new(u.Id, u.Email ?? "", u.FullName, u.IsActive, u.TwoFactorEnabled, u.CreatedAt, u.LastLoginAt, roles);
}
