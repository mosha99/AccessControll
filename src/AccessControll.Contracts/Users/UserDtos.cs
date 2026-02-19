namespace AccessControll.Contracts.Users;

public record UserDto(
    string Id,
    string Email,
    string FullName,
    bool IsActive,
    bool TwoFactorEnabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    IList<string> Roles);

public record CreateUserRequest(
    string Email,
    string FullName,
    string Password,
    List<string> Roles);

public record UpdateUserRequest(
    string FullName,
    bool IsActive,
    List<string> Roles);

public record ProfileDto(
    string Id,
    string Email,
    string FullName,
    bool TwoFactorEnabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    IList<string> Roles);

public record UpdateProfileRequest(string FullName);
