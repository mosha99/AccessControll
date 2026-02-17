using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AccessControll.Application.Auth;

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

    /// <summary>خروج از سیستم</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _mediator.Send(new LogoutCommand(userId));
        return Ok(new { message = "خروج موفق" });
    }

    /// <summary>تنظیم احراز هویت دو مرحله‌ای</summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    public async Task<IActionResult> Setup2FA()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _mediator.Send(new EnableTwoFactorCommand(userId));
        return Ok(result);
    }

    /// <summary>تأیید و فعال‌سازی 2FA</summary>
    [HttpPost("2fa/verify")]
    [Authorize]
    public async Task<IActionResult> Verify2FA([FromBody] VerifyTwoFactorRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var success = await _mediator.Send(new VerifyTwoFactorCommand(userId, request.Code));
        return success ? Ok(new { message = "2FA با موفقیت فعال شد" }) : BadRequest(new { message = "کد نامعتبر" });
    }

    /// <summary>غیرفعال‌سازی 2FA</summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _mediator.Send(new DisableTwoFactorCommand(userId));
        return Ok(new { message = "2FA غیرفعال شد" });
    }
}
