namespace AccessControll.Contracts.Doors;

public record DoorDto(
    Guid Id,
    string Name,
    int Code,
    string Description,
    string Location,
    bool IsLocked,
    bool IsEnabled,
    string? HardwareId,
    DateTime CreatedAt);

public record DoorAccessLogDto(
    Guid Id,
    string DoorName,
    string UserFullName,
    string UserEmail,
    DateTime AccessedAt,
    string Action,
    string Result,
    string? IpAddress,
    string? Notes);

public record ControlDoorRequest(bool Lock);

public record GrantPermissionRequest(
    string UserId,
    bool CanOpen,
    bool CanLock,
    TimeSpan? AllowedFromTime,
    TimeSpan? AllowedToTime);

public record UserPermissionDto(
    Guid PermissionId,
    string UserId,
    string UserFullName,
    string UserEmail,
    Guid DoorId,
    string DoorName,
    bool CanOpen,
    bool CanLock,
    TimeSpan? AllowedFromTime,
    TimeSpan? AllowedToTime,
    DateTime GrantedAt);
