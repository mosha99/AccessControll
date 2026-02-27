using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using AccessControll.Application.Auth;
using AccessControll.Application.Roles;
using AccessControll.Contracts.Auth;
using AccessControll.Domain.Entities;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(IMediator mediator, UserManager<ApplicationUser> userManager)
    {
        _mediator = mediator;
        _userManager = userManager;
    }

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

    /// <summary>بررسی اینکه آیا سیستم نیاز به راه‌اندازی اولیه دارد (جدول یوزر خالی است)</summary>
    [HttpGet("setup-required")]
    [AllowAnonymous]
    public async Task<IActionResult> SetupRequired()
    {
        var hasUsers = await _userManager.Users.AnyAsync();
        return Ok(new { required = !hasUsers });
    }

    /// <summary>پنل‌هایی که کاربر جاری به آن‌ها دسترسی دارد</summary>
    [HttpGet("my-panels")]
    [Authorize]
    public async Task<IActionResult> MyPanels()
    {
        var roles = User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value);
        var panels = await _mediator.Send(new GetCurrentUserPanelsQuery(roles));
        return Ok(panels);
    }

    /// <summary>ثبت اولین ادمین — فقط وقتی هیچ کاربری در سیستم نیست</summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        var hasUsers = await _userManager.Users.AnyAsync();
        if (hasUsers)
            return BadRequest(new { message = "سیستم از قبل راه‌اندازی شده است" });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, "Admin");

        // ورود خودکار بعد از ثبت‌نام
        var loginResult = await _mediator.Send(new LoginCommand(request.Email, request.Password, null));
        if (loginResult.Succeeded)
            return Ok(new { token = loginResult.Token });

        return Ok(new { message = "ادمین ایجاد شد. لطفاً وارد شوید." });
    }
}
