using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AccessControll.Application.Doors;
using AccessControll.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using AccessControll.Domain.Interfaces;
using AccessControll.Domain.Entities;
using AccessControll.Contracts.Doors;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DoorsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHubContext<DoorHub> _hub;
    private readonly IUserDoorPermissionRepository _permRepo;

    public DoorsController(IMediator mediator, IHubContext<DoorHub> hub, IUserDoorPermissionRepository permRepo)
    {
        _mediator = mediator;
        _hub = hub;
        _permRepo = permRepo;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>لیست تمام درها</summary>
    [HttpGet]
    [Authorize(Policy = "panel:doors")]
    public async Task<IActionResult> GetAll()
    {
        var doors = await _mediator.Send(new GetAllDoorsQuery());
        return Ok(doors);
    }

    /// <summary>اطلاعات یک در</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "panel:doors")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var door = await _mediator.Send(new GetDoorByIdQuery(id));
        return door == null ? NotFound() : Ok(door);
    }

    /// <summary>ایجاد در جدید</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateDoorCommand command)
    {
        var door = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = door.Id }, door);
    }

    /// <summary>ویرایش در</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDoorCommand command)
    {
        if (id != command.Id) return BadRequest();
        var door = await _mediator.Send(command);
        await _hub.Clients.All.SendAsync("DoorUpdated", door);
        return Ok(door);
    }

    /// <summary>حذف در</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteDoorCommand(id));
        return NoContent();
    }

    /// <summary>قفل یا باز کردن در</summary>
    [HttpPost("{id:guid}/control")]
    public async Task<IActionResult> Control(Guid id, [FromBody] ControlDoorRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (succeeded, message) = await _mediator.Send(
            new ControlDoorCommand(id, null, UserId, request.Lock, ipAddress));

        if (succeeded)
        {
            await _hub.Clients.All.SendAsync("DoorStatusChanged", new
            {
                DoorId = id,
                IsLocked = request.Lock,
                ChangedBy = User.FindFirstValue(ClaimTypes.Name),
                At = DateTime.UtcNow
            });
        }

        return succeeded ? Ok(new { message }) : BadRequest(new { message });
    }

    /// <summary>لاگ دسترسی‌های درها</summary>
    [HttpGet("logs")]
    [Authorize(Policy = "panel:logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? doorId,
        [FromQuery] string? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var (items, total) = await _mediator.Send(new GetDoorLogsQuery(doorId, userId, from, to, page, pageSize));
        return Ok(new { items, total, page, pageSize });
    }

    /// <summary>لیست مجوزهای یک در</summary>
    [HttpGet("{doorId:guid}/permissions")]
    [Authorize(Policy = "panel:doors")]
    public async Task<IActionResult> GetPermissions(Guid doorId)
    {
        var perms = await _mediator.Send(new GetDoorPermissionsQuery(doorId));
        return Ok(perms);
    }

    /// <summary>اعطای مجوز دسترسی به کاربر</summary>
    [HttpPost("{doorId:guid}/permissions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GrantPermission(Guid doorId, [FromBody] GrantPermissionRequest request)
    {
        var permission = new UserDoorPermission
        {
            UserId = request.UserId,
            DoorId = doorId,
            CanOpen = request.CanOpen,
            CanLock = request.CanLock,
            AllowedFromTime = request.AllowedFromTime,
            AllowedToTime = request.AllowedToTime,
            GrantedByUserId = UserId
        };

        await _permRepo.GrantPermissionAsync(permission);
        return Ok(new { message = "دسترسی با موفقیت اعطا شد" });
    }

    /// <summary>لغو مجوز دسترسی کاربر</summary>
    [HttpDelete("{doorId:guid}/permissions/{targetUserId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevokePermission(Guid doorId, string targetUserId)
    {
        await _permRepo.RevokePermissionAsync(targetUserId, doorId);
        return Ok(new { message = "دسترسی لغو شد" });
    }
}
