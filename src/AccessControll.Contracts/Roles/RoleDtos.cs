namespace AccessControll.Contracts.Roles;

public record RoleDto(string Id, string Name, int UsersCount);

public record CreateRoleRequest(string Name);

public record AssignRoleRequest(string UserId, string RoleName);

public record RolePanelsDto(string RoleName, List<string> Panels);

public record SetRolePanelsRequest(List<string> Panels);
