using MediatR;
using Microsoft.AspNetCore.Identity;
using AccessControll.Domain.Entities;
using AccessControll.Contracts.Auth;
using AccessControll.Contracts.Users;
using OtpNet;
using Microsoft.EntityFrameworkCore;

namespace AccessControll.Application.Auth;

// ── Login with email + password ───────────────────────────────────────────────

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtService _jwtService;

    public LoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtService jwtService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !user.IsActive)
            return new LoginResponse(false, Error: "نام کاربری یا رمز عبور اشتباه است");

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return new LoginResponse(false, Error: "حساب کاربری قفل شده است. ۱۵ دقیقه دیگر تلاش کنید");

        if (!result.Succeeded)
            return new LoginResponse(false, Error: "نام کاربری یا رمز عبور اشتباه است");

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(request.TwoFactorCode))
                return new LoginResponse(false, RequiresTwoFactor: true);

            if (!VerifyTotp(user.TwoFactorSecretKey!, request.TwoFactorCode))
                return new LoginResponse(false, Error: "کد تأیید دو مرحله‌ای نامعتبر است");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtService.GenerateToken(user, roles);

        return new LoginResponse(true, token);
    }

    public static bool VerifyTotp(string secretKey, string code)
    {
        var keyBytes = Base32Encoding.ToBytes(secretKey);
        var totp = new Totp(keyBytes);
        return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
    }
}

// ── Login with 2FA only (web) ─────────────────────────────────────────────────

public class Login2FAOnlyCommandHandler : IRequestHandler<Login2FAOnlyCommand, LoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtService _jwtService;

    public Login2FAOnlyCommandHandler(UserManager<ApplicationUser> userManager, IJwtService jwtService)
    {
        _userManager = userManager;
        _jwtService = jwtService;
    }

    public async Task<LoginResponse> Handle(Login2FAOnlyCommand request, CancellationToken cancellationToken)
    {
        var users = await _userManager.Users
            .Where(x => x.IsActive && x.TwoFactorEnabled && x.TwoFactorSecretKey != null)
            .ToListAsync(cancellationToken);

        var matched = users.Where(x => LoginCommandHandler.VerifyTotp(x.TwoFactorSecretKey!, request.TwoFactorCode)).ToList();

        if (matched is not [var user])
            return new LoginResponse(false, Error: "کد 2FA نامعتبر است");

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtService.GenerateToken(user, roles);

        return new LoginResponse(true, token);
    }
}

// ── Login with 2FA only (hardware) ───────────────────────────────────────────

public class _2FALoginCommandHandler : IRequestHandler<_2FALoginCommand, _2FALoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public _2FALoginCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<_2FALoginResponse> Handle(_2FALoginCommand request, CancellationToken cancellationToken)
    {
        var users = await _userManager.Users
            .Where(x => x.IsActive && x.TwoFactorEnabled && x.TwoFactorSecretKey != null)
            .ToListAsync(cancellationToken);

        var matched = users.Where(x => LoginCommandHandler.VerifyTotp(x.TwoFactorSecretKey!, request.TwoFactorCode)).ToList();

        if (matched is not [var user])
            return new _2FALoginResponse(false);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return new _2FALoginResponse(true, user.Id, user.FullName);
    }
}

// ── Logout ────────────────────────────────────────────────────────────────────

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public LogoutCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<bool> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // JWT is stateless — token invalidation happens on the client.
        // Future: implement token blacklist here.
        var user = await _userManager.FindByIdAsync(request.UserId);
        return user != null;
    }
}

// ── Enable 2FA ────────────────────────────────────────────────────────────────

public class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, TwoFactorSetupResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public EnableTwoFactorCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<TwoFactorSetupResponse> Handle(EnableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId)
            ?? throw new InvalidOperationException("کاربر یافت نشد");

        var secretKey = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        user.TwoFactorSecretKey = secretKey;
        await _userManager.UpdateAsync(user);

        const string issuer = "AccessControll";
        var accountTitle = Uri.EscapeDataString(user.Email ?? user.UserName ?? "user");
        var qrUri = $"otpauth://totp/{issuer}:{accountTitle}?secret={secretKey}&issuer={issuer}&digits=6";

        return new TwoFactorSetupResponse(secretKey, qrUri);
    }
}

// ── Verify 2FA ────────────────────────────────────────────────────────────────

public class VerifyTwoFactorCommandHandler : IRequestHandler<VerifyTwoFactorCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public VerifyTwoFactorCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<bool> Handle(VerifyTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user?.TwoFactorSecretKey == null) return false;

        var valid = LoginCommandHandler.VerifyTotp(user.TwoFactorSecretKey, request.Code);

        if (valid)
        {
            user.TwoFactorEnabled = true;
            await _userManager.UpdateAsync(user);
        }

        return valid;
    }
}

// ── Disable 2FA ───────────────────────────────────────────────────────────────

public class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public DisableTwoFactorCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<bool> Handle(DisableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return false;

        user.TwoFactorEnabled = false;
        user.TwoFactorSecretKey = null;
        await _userManager.UpdateAsync(user);
        return true;
    }
}

// ── Update Profile ────────────────────────────────────────────────────────────

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, ProfileDto?>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UpdateProfileCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<ProfileDto?> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return null;

        user.FullName = request.FullName;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        return new ProfileDto(user.Id, user.Email ?? "", user.FullName,
            user.TwoFactorEnabled, user.CreatedAt, user.LastLoginAt, roles);
    }
}

// ── Change Password ───────────────────────────────────────────────────────────

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, (bool Succeeded, string? Error)>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ChangePasswordCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<(bool Succeeded, string? Error)> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null) return (false, "کاربر یافت نشد");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return (false, string.Join(" | ", result.Errors.Select(e => e.Description)));

        return (true, null);
    }
}
