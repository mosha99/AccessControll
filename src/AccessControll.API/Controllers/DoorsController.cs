using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AccessControll.Application.Doors;
using AccessControll.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DoorsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHubContext<DoorHub> _hub;

    public DoorsController(IMediator mediator, IHubContext<DoorHub> hub)
    {
        _mediator = mediator;
        _hub = hub;
    }

    /// <summary>لیست تمام درها</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,DoorManager")]
    public async Task<IActionResult> GetAll()
    {
        var doors = await _mediator.Send(new GetAllDoorsQuery());
        return Ok(doors);
    }

    /// <summary>اطلاعات یک در</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,DoorManager")]
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

    /// <summary>قفل یا باز کردن در — کنترل اصلی</summary>
    [HttpPost("{id:guid}/control")]
    public async Task<IActionResult> Control(Guid id, [FromBody] ControlDoorRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var (succeeded, message) = await _mediator.Send(
            new ControlDoorCommand(id, userId, request.Lock, ipAddress));

        if (succeeded)
        {
            // Real-time notification to all connected clients
            await _hub.Clients.All.SendAsync("DoorStatusChanged", new
            {
                DoorId = id,
                IsLocked = request.Lock,
                ChangedBy = User.FindFirstValue(ClaimTypes.Name),
                At = DateTime.UtcNow
            });
        }

        return succeeded ? Ok(new { message }) : Forbid();
    }

    /// <summary>لاگ دسترسی‌های در</summary>
    [HttpGet("logs")]
    [Authorize(Roles = "Admin,DoorManager")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? doorId,
        [FromQuery] string? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, total) = await _mediator.Send(new GetDoorLogsQuery(doorId, userId, from, to, page, pageSize));
        return Ok(new { items, total, page, pageSize });
    }

    /// <summary>مدیریت دسترسی کاربران به در</summary>
    [HttpPost("{doorId:guid}/permissions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GrantPermission(Guid doorId, [FromBody] GrantPermissionRequest request)
    {
        var permission = new Domain.Entities.UserDoorPermission
        {
            UserId = request.UserId,
            DoorId = doorId,
            CanOpen = request.CanOpen,
            CanLock = request.CanLock,
            AllowedFromTime = request.AllowedFromTime,
            AllowedToTime = request.AllowedToTime,
            GrantedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!
        };

        var repo = HttpContext.RequestServices.GetRequiredService<Domain.Interfaces.IUserDoorPermissionRepository>();
        await repo.GrantPermissionAsync(permission);
        return Ok(new { message = "دسترسی اعطا شد" });
    }

    [HttpDelete("{doorId:guid}/permissions/{userId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevokePermission(Guid doorId, string userId)
    {
        var repo = HttpContext.RequestServices.GetRequiredService<Domain.Interfaces.IUserDoorPermissionRepository>();
        await repo.RevokePermissionAsync(userId, doorId);
        return Ok(new { message = "دسترسی لغو شد" });
    }
}

public record GrantPermissionRequest(string UserId, bool CanOpen, bool CanLock, TimeSpan? AllowedFromTime, TimeSpan? AllowedToTime);
