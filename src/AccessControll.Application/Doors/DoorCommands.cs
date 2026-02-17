using MediatR;
using AccessControll.Domain.Entities;
using AccessControll.Domain.Enums;
using AccessControll.Domain.Interfaces;

namespace AccessControll.Application.Doors;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record DoorDto(Guid Id, string Name, string Description, string Location, bool IsLocked, bool IsEnabled, string? HardwareId, DateTime CreatedAt);
public record DoorAccessLogDto(Guid Id, string DoorName, string UserFullName, string UserEmail, DateTime AccessedAt, string Action, string Result, string? IpAddress, string? Notes);
public record ControlDoorRequest(Guid DoorId, string UserId, bool Lock);

// ── Commands & Queries ────────────────────────────────────────────────────────

public record GetAllDoorsQuery : IRequest<IEnumerable<DoorDto>>;
public record GetDoorByIdQuery(Guid Id) : IRequest<DoorDto?>;

public record CreateDoorCommand(string Name, string Description, string Location, string? HardwareId)
    : IRequest<DoorDto>;

public record UpdateDoorCommand(Guid Id, string Name, string Description, string Location, bool IsEnabled, string? HardwareId)
    : IRequest<DoorDto>;

public record DeleteDoorCommand(Guid Id) : IRequest;

public record ControlDoorCommand(Guid DoorId, string UserId, bool Lock, string? IpAddress)
    : IRequest<(bool Succeeded, string Message)>;

public record GetDoorLogsQuery(Guid? DoorId, string? UserId, DateTime? From, DateTime? To, int Page, int PageSize)
    : IRequest<(IEnumerable<DoorAccessLogDto> Items, int Total)>;

// ── Handlers ──────────────────────────────────────────────────────────────────

public class GetAllDoorsQueryHandler : IRequestHandler<GetAllDoorsQuery, IEnumerable<DoorDto>>
{
    private readonly IDoorRepository _repo;
    public GetAllDoorsQueryHandler(IDoorRepository repo) => _repo = repo;

    public async Task<IEnumerable<DoorDto>> Handle(GetAllDoorsQuery request, CancellationToken ct)
    {
        var doors = await _repo.GetAllAsync();
        return doors.Select(d => new DoorDto(d.Id, d.Name, d.Description, d.Location, d.IsLocked, d.IsEnabled, d.HardwareId, d.CreatedAt));
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
            Name = request.Name,
            Description = request.Description,
            Location = request.Location,
            HardwareId = request.HardwareId,
            IsLocked = true,
            IsEnabled = true
        };
        var created = await _repo.CreateAsync(door);
        return new DoorDto(created.Id, created.Name, created.Description, created.Location, created.IsLocked, created.IsEnabled, created.HardwareId, created.CreatedAt);
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

        door.Name = request.Name;
        door.Description = request.Description;
        door.Location = request.Location;
        door.IsEnabled = request.IsEnabled;
        door.HardwareId = request.HardwareId;
        door.LastModifiedAt = DateTime.UtcNow;

        var updated = await _repo.UpdateAsync(door);
        return new DoorDto(updated.Id, updated.Name, updated.Description, updated.Location, updated.IsLocked, updated.IsEnabled, updated.HardwareId, updated.CreatedAt);
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

    public ControlDoorCommandHandler(IDoorRepository doorRepo, IDoorAccessLogRepository logRepo, IUserDoorPermissionRepository permRepo)
    {
        _doorRepo = doorRepo;
        _logRepo = logRepo;
        _permRepo = permRepo;
    }

    public async Task<(bool Succeeded, string Message)> Handle(ControlDoorCommand request, CancellationToken ct)
    {
        var door = await _doorRepo.GetByIdAsync(request.DoorId);
        if (door == null)
            return (false, "در یافت نشد");

        if (!door.IsEnabled)
        {
            await _logRepo.LogAccessAsync(request.DoorId, request.UserId,
                request.Lock ? DoorAction.Lock : DoorAction.Unlock,
                AccessResult.DoorDisabled, request.IpAddress);
            return (false, "در غیرفعال است");
        }

        var perm = await _permRepo.GetPermissionAsync(request.UserId, request.DoorId);
        if (perm == null)
        {
            await _logRepo.LogAccessAsync(request.DoorId, request.UserId,
                request.Lock ? DoorAction.Lock : DoorAction.Unlock,
                AccessResult.NoPermission, request.IpAddress);
            return (false, "دسترسی ندارید");
        }

        if (!request.Lock && !perm.CanOpen)
        {
            await _logRepo.LogAccessAsync(request.DoorId, request.UserId,
                DoorAction.Unlock, AccessResult.NoPermission, request.IpAddress);
            return (false, "مجاز به باز کردن در نیستید");
        }

        // Check allowed time window
        if (perm.AllowedFromTime.HasValue && perm.AllowedToTime.HasValue)
        {
            var now = DateTime.UtcNow.TimeOfDay;
            if (now < perm.AllowedFromTime || now > perm.AllowedToTime)
            {
                await _logRepo.LogAccessAsync(request.DoorId, request.UserId,
                    request.Lock ? DoorAction.Lock : DoorAction.Unlock,
                    AccessResult.OutsideAllowedHours, request.IpAddress);
                return (false, "خارج از ساعت مجاز دسترسی");
            }
        }

        door.IsLocked = request.Lock;
        door.LastModifiedAt = DateTime.UtcNow;
        await _doorRepo.UpdateAsync(door);

        var action = request.Lock ? DoorAction.Lock : DoorAction.Unlock;
        await _logRepo.LogAccessAsync(request.DoorId, request.UserId, action, AccessResult.Success, request.IpAddress);

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
