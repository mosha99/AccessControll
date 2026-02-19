using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AccessControll.Application.Auth;
using AccessControll.Application.Users;
using AccessControll.Contracts.Auth;
using AccessControll.Contracts.Users;

namespace AccessControll.API.Controllers;

/// <summary>پروفایل کاربر جاری — اطلاعات شخصی، رمز عبور، مدیریت 2FA</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProfileController(IMediator mediator) => _mediator = mediator;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>دریافت اطلاعات پروفایل کاربر جاری</summary>
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var profile = await _mediator.Send(new GetCurrentUserProfileQuery(UserId));
        return profile == null ? NotFound() : Ok(profile);
    }

    /// <summary>ویرایش نام کامل</summary>
    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var profile = await _mediator.Send(new UpdateProfileCommand(UserId, request.FullName));
        return profile == null ? NotFound() : Ok(profile);
    }

    /// <summary>تغییر رمز عبور</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var (succeeded, error) = await _mediator.Send(
            new ChangePasswordCommand(UserId, request.CurrentPassword, request.NewPassword));

        return succeeded
            ? Ok(new { message = "رمز عبور با موفقیت تغییر یافت" })
            : BadRequest(new { message = error });
    }

    /// <summary>شروع تنظیم 2FA — دریافت QR Code</summary>
    [HttpPost("2fa/setup")]
    public async Task<IActionResult> Setup2FA()
    {
        var result = await _mediator.Send(new EnableTwoFactorCommand(UserId));
        return Ok(result);
    }

    /// <summary>تأیید و فعال‌سازی 2FA با کد TOTP</summary>
    [HttpPost("2fa/verify")]
    public async Task<IActionResult> Verify2FA([FromBody] VerifyTwoFactorRequest request)
    {
        var success = await _mediator.Send(new VerifyTwoFactorCommand(UserId, request.Code));
        return success
            ? Ok(new { message = "احراز هویت دو مرحله‌ای فعال شد" })
            : BadRequest(new { message = "کد نامعتبر است" });
    }

    /// <summary>غیرفعال‌سازی 2FA</summary>
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> Disable2FA()
    {
        var success = await _mediator.Send(new DisableTwoFactorCommand(UserId));
        return success
            ? Ok(new { message = "احراز هویت دو مرحله‌ای غیرفعال شد" })
            : NotFound();
    }
}
