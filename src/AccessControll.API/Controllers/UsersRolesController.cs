using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AccessControll.Application.Users;
using AccessControll.Application.Roles;
using AccessControll.Application.Doors;
using AccessControll.Contracts.Users;
using AccessControll.Contracts.Roles;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "panel:users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (users, total) = await _mediator.Send(new GetAllUsersQuery(page, pageSize));
        return Ok(new { users, total, page, pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _mediator.Send(new GetUserByIdQuery(id));
        return user == null ? NotFound() : Ok(user);
    }

    [HttpGet("{id}/permissions")]
    public async Task<IActionResult> GetPermissions(string id)
    {
        var perms = await _mediator.Send(new GetUserPermissionsForDoorsQuery(id));
        return Ok(perms);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var (succeeded, errors) = await _mediator.Send(
            new CreateUserCommand(request.Email, request.FullName, request.Password, request.Roles));
        return succeeded ? Ok(new { message = "کاربر با موفقیت ایجاد شد" }) : BadRequest(new { errors });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request)
    {
        var success = await _mediator.Send(new UpdateUserCommand(id, request.FullName, request.IsActive, request.Roles));
        return success ? Ok() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var success = await _mediator.Send(new DeleteUserCommand(id));
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var isNowActive = await _mediator.Send(new ToggleUserActiveCommand(id));
        return Ok(new { isActive = isNowActive, message = isNowActive ? "کاربر فعال شد" : "کاربر غیرفعال شد" });
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "panel:roles")]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;
    public RolesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _mediator.Send(new GetAllRolesQuery());
        return Ok(roles);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        var (succeeded, error) = await _mediator.Send(new CreateRoleCommand(request.Name));
        return succeeded ? Ok(new { message = "نقش با موفقیت ایجاد شد" }) : BadRequest(new { error });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var (succeeded, error) = await _mediator.Send(new DeleteRoleCommand(id));
        return succeeded ? NoContent() : BadRequest(new { error });
    }

    [HttpPost("assign")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest request)
    {
        var success = await _mediator.Send(new AssignRoleToUserCommand(request.UserId, request.RoleName));
        return success ? Ok() : BadRequest();
    }

    [HttpPost("remove")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveRole([FromBody] AssignRoleRequest request)
    {
        var success = await _mediator.Send(new RemoveRoleFromUserCommand(request.UserId, request.RoleName));
        return success ? Ok() : BadRequest();
    }

    [HttpGet("{roleName}/panels")]
    public async Task<IActionResult> GetPanels(string roleName)
    {
        var panels = await _mediator.Send(new GetRolePanelsQuery(roleName));
        return Ok(new RolePanelsDto(roleName, panels));
    }

    [HttpPut("{roleName}/panels")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetPanels(string roleName, [FromBody] SetRolePanelsRequest request)
    {
        var success = await _mediator.Send(new SetRolePanelsCommand(roleName, request.Panels));
        return success ? Ok() : BadRequest(new { message = "پنل‌های ارسالی معتبر نیستند" });
    }
}
