using MediatR;
using AccessControll.Domain.Entities;
using AccessControll.Domain.Enums;
using AccessControll.Domain.Interfaces;
using AccessControll.Contracts.Doors;

namespace AccessControll.Application.Doors;

// ── Queries & Commands ────────────────────────────────────────────────────────

public record GetAllDoorsQuery : IRequest<IEnumerable<DoorDto>>;
public record GetDoorByIdQuery(Guid Id) : IRequest<DoorDto?>;

public record CreateDoorCommand(string Name, int Code, string Description, string Location, string? HardwareId,
    string? StationMacAddress, string I2cAddress, string I2cPin, int DurationMs, bool IsMomentary, bool IsActiveLow = true)
    : IRequest<DoorDto>;

public record UpdateDoorCommand(Guid Id, string Name, int Code, string Description, string Location, bool IsEnabled,
    string? HardwareId, string? StationMacAddress, string I2cAddress, string I2cPin, int DurationMs, bool IsMomentary, bool IsActiveLow = true)
    : IRequest<DoorDto>;

public record DeleteDoorCommand(Guid Id) : IRequest;

public record ControlDoorCommand(Guid? DoorId, int? DoorCode, string UserId, bool Lock, string? IpAddress, string? StationMac = null)
    : IRequest<(bool Succeeded, string Message)>;

public record GetDoorLogsQuery(Guid? DoorId, string? UserId, DateTime? From, DateTime? To, int Page, int PageSize)
    : IRequest<(IEnumerable<DoorAccessLogDto> Items, int Total)>;

public record GetDoorPermissionsQuery(Guid DoorId)
    : IRequest<IEnumerable<UserPermissionDto>>;

public record GetUserPermissionsForDoorsQuery(string UserId)
    : IRequest<IEnumerable<UserPermissionDto>>;

// ── Handlers ──────────────────────────────────────────────────────────────────

public class GetAllDoorsQueryHandler : IRequestHandler<GetAllDoorsQuery, IEnumerable<DoorDto>>
{
    private readonly IDoorRepository _repo;
    public GetAllDoorsQueryHandler(IDoorRepository repo) => _repo = repo;

    public async Task<IEnumerable<DoorDto>> Handle(GetAllDoorsQuery request, CancellationToken ct)
    {
        var doors = await _repo.GetAllAsync();
        return doors.Select(d => d.ToDto());
    }
}

public class GetDoorByIdQueryHandler : IRequestHandler<GetDoorByIdQuery, DoorDto?>
{
    private readonly IDoorRepository _repo;
    public GetDoorByIdQueryHandler(IDoorRepository repo) => _repo = repo;

    public async Task<DoorDto?> Handle(GetDoorByIdQuery request, CancellationToken ct)
    {
        var door = await _repo.GetByIdAsync(request.Id);
        return door?.ToDto();
    }
}

public class CreateDoorCommandHandler : IRequestHandler<CreateDoorCommand, DoorDto>
{
    private readonly IDoorRepository _repo;
    public CreateDoorCommandHandler(IDoorRepository repo) => _repo = repo;

    public async Task<DoorDto> Handle(CreateDoorCommand request, CancellationToken ct)
    {
        var door = new Door
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            Location = request.Location,
            HardwareId = request.HardwareId,
            StationMacAddress = request.StationMacAddress,
            I2cAddress = request.I2cAddress,
            I2cPin = request.I2cPin,
            DurationMs = request.DurationMs,
            IsMomentary = request.IsMomentary,
            IsActiveLow = request.IsActiveLow,
            IsLocked = true,
            IsEnabled = true
        };
        var created = await _repo.CreateAsync(door);
        return created.ToDto();
    }
}

public class UpdateDoorCommandHandler : IRequestHandler<UpdateDoorCommand, DoorDto>
{
    private readonly IDoorRepository _repo;
    public UpdateDoorCommandHandler(IDoorRepository repo) => _repo = repo;

    public async Task<DoorDto> Handle(UpdateDoorCommand request, CancellationToken ct)
    {
        var door = await _repo.GetByIdAsync(request.Id)
            ?? throw new KeyNotFoundException("در یافت نشد");

        door.Code = request.Code;
        door.Name = request.Name;
        door.Description = request.Description;
        door.Location = request.Location;
        door.IsEnabled = request.IsEnabled;
        door.HardwareId = request.HardwareId;
        door.StationMacAddress = request.StationMacAddress;
        door.I2cAddress = request.I2cAddress;
        door.I2cPin = request.I2cPin;
        door.DurationMs = request.DurationMs;
        door.IsMomentary = request.IsMomentary;
        door.IsActiveLow = request.IsActiveLow;
        door.LastModifiedAt = DateTime.UtcNow;

        var updated = await _repo.UpdateAsync(door);
        return updated.ToDto();
    }
}

public class DeleteDoorCommandHandler : IRequestHandler<DeleteDoorCommand>
{
    private readonly IDoorRepository _repo;
    public DeleteDoorCommandHandler(IDoorRepository repo) => _repo = repo;
    public async Task Handle(DeleteDoorCommand request, CancellationToken ct) => await _repo.DeleteAsync(request.Id);
}

public class ControlDoorCommandHandler : IRequestHandler<ControlDoorCommand, (bool Succeeded, string Message)>
{
    private readonly IDoorRepository _doorRepo;
    private readonly IDoorAccessLogRepository _logRepo;
    private readonly IUserDoorPermissionRepository _permRepo;
    private readonly IPhysicalPortService _portService;
    private readonly IStationRelayService _relayService;

    public ControlDoorCommandHandler(IDoorRepository doorRepo, IDoorAccessLogRepository logRepo,
        IUserDoorPermissionRepository permRepo, IPhysicalPortService portService,
        IStationRelayService relayService)
    {
        _doorRepo = doorRepo;
        _logRepo = logRepo;
        _permRepo = permRepo;
        _portService = portService;
        _relayService = relayService;
    }

    public async Task<(bool Succeeded, string Message)> Handle(ControlDoorCommand request, CancellationToken ct)
    {
        var doorId = request.DoorId ?? default;

        if (request.DoorId == null)
        {
            if (request.DoorCode == null) return (false, "در یافت نشد");

            Door? byCode;
            if (request.StationMac != null)
                byCode = await _doorRepo.GetByCodeAndStationAsync(request.DoorCode.Value, request.StationMac);
            else
                byCode = await _doorRepo.GetByCodeAsync(request.DoorCode.Value);

            if (byCode == null) return (false, "در یافت نشد");
            doorId = byCode.Id;
        }

        var door = await _doorRepo.GetByIdAsync(doorId);
        if (door == null) return (false, "در یافت نشد");

        if (!door.IsEnabled)
        {
            await _logRepo.LogAccessAsync(doorId, request.UserId,
                request.Lock ? DoorAction.Lock : DoorAction.Unlock,
                AccessResult.DoorDisabled, request.IpAddress);
            return (false, "در غیرفعال است");
        }

        var perm = await _permRepo.GetPermissionAsync(request.UserId, doorId);
        if (perm == null)
        {
            await _logRepo.LogAccessAsync(doorId, request.UserId,
                request.Lock ? DoorAction.Lock : DoorAction.Unlock,
                AccessResult.NoPermission, request.IpAddress);
            return (false, "دسترسی ندارید");
        }

        if (!request.Lock && !perm.CanOpen)
        {
            await _logRepo.LogAccessAsync(doorId, request.UserId,
                DoorAction.Unlock, AccessResult.NoPermission, request.IpAddress);
            return (false, "مجاز به باز کردن در نیستید");
        }

        if (request.Lock && !perm.CanLock)
        {
            await _logRepo.LogAccessAsync(doorId, request.UserId,
                DoorAction.Lock, AccessResult.NoPermission, request.IpAddress);
            return (false, "مجاز به قفل کردن در نیستید");
        }

        if (perm.AllowedFromTime.HasValue && perm.AllowedToTime.HasValue)
        {
            var now = DateTime.UtcNow.TimeOfDay;
            if (now < perm.AllowedFromTime || now > perm.AllowedToTime)
            {
                await _logRepo.LogAccessAsync(doorId, request.UserId,
                    request.Lock ? DoorAction.Lock : DoorAction.Unlock,
                    AccessResult.OutsideAllowedHours, request.IpAddress);
                return (false, "خارج از ساعت مجاز دسترسی");
            }
        }

        door.IsLocked = request.Lock;
        door.LastModifiedAt = DateTime.UtcNow;
        await _doorRepo.UpdateAsync(door);

        var action = request.Lock ? DoorAction.Lock : DoorAction.Unlock;
        await _logRepo.LogAccessAsync(doorId, request.UserId, action, AccessResult.Success, request.IpAddress);

        // I2C relay on station
        if (door.StationMacAddress != null)
        {
            if (!request.Lock)
                // Unlock: activate relay (momentary = auto-off after DurationMs; toggle = stay on)
                await _relayService.OpenDoorAsync(door.StationMacAddress, door.I2cAddress, door.I2cPin,
                    door.IsMomentary ? door.DurationMs : 0, door.IsActiveLow);
            else if (!door.IsMomentary)
                // Lock: for toggle outputs, explicitly turn relay off
                await _relayService.CloseDoorAsync(door.StationMacAddress, door.I2cAddress, door.I2cPin, door.IsActiveLow);
        }
        else if (door.HardwareId is not null)
        {
            // Fallback: legacy GPIO relay on server
            _portService.SetPortState(door.HardwareId, !request.Lock);
        }

        return (true, request.Lock ? "در قفل شد" : "در باز شد");
    }
}

public class GetDoorLogsQueryHandler : IRequestHandler<GetDoorLogsQuery, (IEnumerable<DoorAccessLogDto> Items, int Total)>
{
    private readonly IDoorAccessLogRepository _logRepo;
    public GetDoorLogsQueryHandler(IDoorAccessLogRepository logRepo) => _logRepo = logRepo;

    public async Task<(IEnumerable<DoorAccessLogDto> Items, int Total)> Handle(GetDoorLogsQuery request, CancellationToken ct)
    {
        var logs = await _logRepo.GetLogsAsync(request.DoorId, request.UserId, request.From, request.To);
        var total = await _logRepo.GetTotalCountAsync(request.DoorId, request.UserId);
        var paged = logs.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
        var dtos = paged.Select(l => new DoorAccessLogDto(
            l.Id, l.Door?.Name ?? "", l.User?.FullName ?? "", l.User?.Email ?? "",
            l.AccessedAt, l.Action.ToString(), l.Result.ToString(), l.IpAddress, l.Notes));
        return (dtos, total);
    }
}

public class GetDoorPermissionsQueryHandler : IRequestHandler<GetDoorPermissionsQuery, IEnumerable<UserPermissionDto>>
{
    private readonly IUserDoorPermissionRepository _permRepo;
    public GetDoorPermissionsQueryHandler(IUserDoorPermissionRepository permRepo) => _permRepo = permRepo;

    public async Task<IEnumerable<UserPermissionDto>> Handle(GetDoorPermissionsQuery request, CancellationToken ct)
    {
        var perms = await _permRepo.GetDoorPermissionsAsync(request.DoorId);
        return perms.Select(p => p.ToDto());
    }
}

public class GetUserPermissionsForDoorsQueryHandler : IRequestHandler<GetUserPermissionsForDoorsQuery, IEnumerable<UserPermissionDto>>
{
    private readonly IUserDoorPermissionRepository _permRepo;
    public GetUserPermissionsForDoorsQueryHandler(IUserDoorPermissionRepository permRepo) => _permRepo = permRepo;

    public async Task<IEnumerable<UserPermissionDto>> Handle(GetUserPermissionsForDoorsQuery request, CancellationToken ct)
    {
        var perms = await _permRepo.GetUserPermissionsAsync(request.UserId);
        return perms.Select(p => p.ToDto());
    }
}

// ── Mapping helpers ───────────────────────────────────────────────────────────

internal static class DoorMappingExtensions
{
    public static DoorDto ToDto(this Door d) =>
    new(d.Id, d.Name, d.Code, d.Description, d.Location, d.IsLocked, d.IsEnabled, d.HardwareId, d.CreatedAt,
        d.StationMacAddress, d.I2cAddress, d.I2cPin, d.DurationMs, d.IsMomentary, d.IsActiveLow);

    public static UserPermissionDto ToDto(this UserDoorPermission p) =>
    new(p.Id, p.UserId, p.User?.FullName ?? "", p.User?.Email ?? "",
        p.DoorId, p.Door?.Name ?? "",
        p.CanOpen, p.CanLock,
        p.AllowedFromTime, p.AllowedToTime,
        p.GrantedAt);
}
