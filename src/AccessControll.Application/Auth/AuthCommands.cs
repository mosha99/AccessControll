using AccessControll.Contracts.Auth;
using AccessControll.Contracts.Users;
using AccessControll.Domain.Entities;
using MediatR;

namespace AccessControll.Application.Auth;

// ── Internal response (used by Hardware layer via MediatR) ────────────────────
public record _2FALoginResponse(bool Succeeded, string? UserId = null, string? Name = null);

// ── Commands ──────────────────────────────────────────────────────────────────

/// <summary>ورود با ایمیل و رمز عبور (+ کد 2FA اختیاری)</summary>
public record LoginCommand(string Email, string Password, string? TwoFactorCode)
    : IRequest<LoginResponse>;

/// <summary>ورود فقط با کد TOTP — برای وب (توکن برمی‌گرداند)</summary>
public record Login2FAOnlyCommand(string TwoFactorCode)
    : IRequest<LoginResponse>;

/// <summary>ورود فقط با TOTP — برای سخت‌افزار (فقط UserId برمی‌گرداند)</summary>
public record _2FALoginCommand(string TwoFactorCode)
    : IRequest<_2FALoginResponse>;

public record EnableTwoFactorCommand(string UserId)
    : IRequest<TwoFactorSetupResponse>;

public record DisableTwoFactorCommand(string UserId)
    : IRequest<bool>;

public record VerifyTwoFactorCommand(string UserId, string Code)
    : IRequest<bool>;

public record LogoutCommand(string UserId)
    : IRequest<bool>;

public record UpdateProfileCommand(string UserId, string FullName)
    : IRequest<ProfileDto?>;

public record ChangePasswordCommand(string UserId, string CurrentPassword, string NewPassword)
    : IRequest<(bool Succeeded, string? Error)>;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IJwtService
{
    string GenerateToken(ApplicationUser user, IList<string> roles);
    (bool valid, string? userId) ValidateToken(string token);
}
