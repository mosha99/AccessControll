using AccessControll.Domain.Entities;
using AccessControll.Domain.Enums;

namespace AccessControll.Domain.Interfaces;

public interface IDoorRepository
{
    Task<IEnumerable<Door>> GetAllAsync();
    Task<Door?> GetByIdAsync(Guid id);
    Task<Door?> GetByCodeAsync(int code);
    Task<Door?> GetByCodeAndStationAsync(int code, string stationMac);
    Task<Door> CreateAsync(Door door);
    Task<Door> UpdateAsync(Door door);
    Task DeleteAsync(Guid id);
}

public interface IDoorAccessLogRepository
{
    Task<IEnumerable<DoorAccessLog>> GetLogsAsync(Guid? doorId = null, string? userId = null, DateTime? from = null, DateTime? to = null);
    Task<DoorAccessLog> LogAccessAsync(Guid doorId, string userId, DoorAction action, AccessResult result, string? ipAddress = null, string? notes = null);
    Task<int> GetTotalCountAsync(Guid? doorId = null, string? userId = null);
}

public interface IPhysicalPortService
{
    public void SetPortState(string portAddress , bool high);
}

public interface IStationRelayService
{
    /// <summary>Activate the relay. durationMs=0 means stay on (toggle mode).</summary>
    Task OpenDoorAsync(string stationMac, string i2cAddress, string pin, int durationMs);

    /// <summary>Deactivate the relay (for toggle-mode outputs).</summary>
    Task CloseDoorAsync(string stationMac, string i2cAddress, string pin);
}

public interface IUserDoorPermissionRepository
{
    Task<IEnumerable<UserDoorPermission>> GetUserPermissionsAsync(string userId);
    Task<IEnumerable<UserDoorPermission>> GetDoorPermissionsAsync(Guid doorId);
    Task<UserDoorPermission?> GetPermissionAsync(string userId, Guid doorId);
    Task<UserDoorPermission> GrantPermissionAsync(UserDoorPermission permission);
    Task RevokePermissionAsync(string userId, Guid doorId);
}
