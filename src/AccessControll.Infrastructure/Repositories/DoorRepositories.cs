using Microsoft.EntityFrameworkCore;
using AccessControll.Domain.Entities;
using AccessControll.Domain.Enums;
using AccessControll.Domain.Interfaces;
using AccessControll.Infrastructure.Data;

namespace AccessControll.Infrastructure.Repositories;

public class DoorRepository : IDoorRepository
{
    private readonly ApplicationDbContext _context;
    public DoorRepository(ApplicationDbContext context) => _context = context;

    public async Task<IEnumerable<Door>> GetAllAsync()
        => await _context.Doors.OrderBy(d => d.Name).ToListAsync();

    public async Task<Door?> GetByIdAsync(Guid id)
        => await _context.Doors.FindAsync(id);

    public async Task<Door?> GetByCodeAsync(int code)
        => await _context.Doors.FirstOrDefaultAsync(x=>x.Code == code);

    public async Task<Door> CreateAsync(Door door)
    {
        _context.Doors.Add(door);
        await _context.SaveChangesAsync();
        return door;
    }

    public async Task<Door> UpdateAsync(Door door)
    {
        _context.Doors.Update(door);
        await _context.SaveChangesAsync();
        return door;
    }

    public async Task DeleteAsync(Guid id)
    {
        var door = await _context.Doors.FindAsync(id);
        if (door != null)
        {
            _context.Doors.Remove(door);
            await _context.SaveChangesAsync();
        }
    }
}

public class DoorAccessLogRepository : IDoorAccessLogRepository
{
    private readonly ApplicationDbContext _context;
    public DoorAccessLogRepository(ApplicationDbContext context) => _context = context;

    public async Task<IEnumerable<DoorAccessLog>> GetLogsAsync(
        Guid? doorId = null, string? userId = null, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.DoorAccessLogs
            .Include(l => l.Door)
            .Include(l => l.User)
            .AsQueryable();

        if (doorId.HasValue)    query = query.Where(l => l.DoorId == doorId.Value);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(l => l.UserId == userId);
        if (from.HasValue)      query = query.Where(l => l.AccessedAt >= from.Value);
        if (to.HasValue)        query = query.Where(l => l.AccessedAt <= to.Value);

        return await query.OrderByDescending(l => l.AccessedAt).ToListAsync();
    }

    public async Task<DoorAccessLog> LogAccessAsync(
        Guid doorId, string userId, DoorAction action, AccessResult result,
        string? ipAddress = null, string? notes = null)
    {
        var log = new DoorAccessLog
        {
            DoorId = doorId,
            UserId = userId,
            Action = action,
            Result = result,
            IpAddress = ipAddress,
            Notes = notes,
            AccessedAt = DateTime.UtcNow
        };
        _context.DoorAccessLogs.Add(log);
        await _context.SaveChangesAsync();
        return log;
    }

    public async Task<int> GetTotalCountAsync(Guid? doorId = null, string? userId = null)
    {
        var query = _context.DoorAccessLogs.AsQueryable();
        if (doorId.HasValue)    query = query.Where(l => l.DoorId == doorId.Value);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(l => l.UserId == userId);
        return await query.CountAsync();
    }
}

public class UserDoorPermissionRepository : IUserDoorPermissionRepository
{
    private readonly ApplicationDbContext _context;
    public UserDoorPermissionRepository(ApplicationDbContext context) => _context = context;

    public async Task<IEnumerable<UserDoorPermission>> GetUserPermissionsAsync(string userId)
        => await _context.UserDoorPermissions
            .Include(p => p.Door)
            .Include(p => p.User)
            .Where(p => p.UserId == userId)
            .ToListAsync();

    public async Task<IEnumerable<UserDoorPermission>> GetDoorPermissionsAsync(Guid doorId)
        => await _context.UserDoorPermissions
            .Include(p => p.User)
            .Include(p => p.Door)
            .Where(p => p.DoorId == doorId)
            .ToListAsync();

    public async Task<UserDoorPermission?> GetPermissionAsync(string userId, Guid doorId)
        => await _context.UserDoorPermissions
            .FirstOrDefaultAsync(p => p.UserId == userId && p.DoorId == doorId);

    public async Task<UserDoorPermission> GrantPermissionAsync(UserDoorPermission permission)
    {
        var existing = await GetPermissionAsync(permission.UserId, permission.DoorId);
        if (existing != null)
        {
            existing.CanOpen = permission.CanOpen;
            existing.CanLock = permission.CanLock;
            existing.AllowedFromTime = permission.AllowedFromTime;
            existing.AllowedToTime = permission.AllowedToTime;
            _context.UserDoorPermissions.Update(existing);
        }
        else
        {
            _context.UserDoorPermissions.Add(permission);
        }
        await _context.SaveChangesAsync();
        return existing ?? permission;
    }

    public async Task RevokePermissionAsync(string userId, Guid doorId)
    {
        var perm = await GetPermissionAsync(userId, doorId);
        if (perm != null)
        {
            _context.UserDoorPermissions.Remove(perm);
            await _context.SaveChangesAsync();
        }
    }
}
