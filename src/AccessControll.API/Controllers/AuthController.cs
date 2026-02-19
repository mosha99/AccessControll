using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AccessControll.Application.Auth;
using AccessControll.Contracts.Auth;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>ورود با ایمیل و رمز عبور (+ 2FA در صورت فعال بودن)</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password, request.TwoFactorCode));

        if (!result.Succeeded)
        {
            if (result.RequiresTwoFactor)
                return Ok(new { requiresTwoFactor = true });
            return Unauthorized(new { message = result.Error });
        }

        return Ok(new { token = result.Token });
    }

    /// <summary>ورود فقط با کد 2FA — بدون ایمیل/پسورد</summary>
    [HttpPost("login/2fa")]
    [AllowAnonymous]
    public async Task<IActionResult> Login2FA([FromBody] Login2FARequest request)
    {
        var result = await _mediator.Send(new Login2FAOnlyCommand(request.TwoFactorCode));

        if (!result.Succeeded)
            return Unauthorized(new { message = result.Error });

        return Ok(new { token = result.Token });
    }

    /// <summary>خروج از سیستم</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _mediator.Send(new LogoutCommand(userId));
        return Ok(new { message = "خروج موفق" });
    }
}
