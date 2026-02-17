using MediatR;
using Microsoft.AspNetCore.Identity;
using AccessControll.Domain.Entities;
using OtpNet;
using System.Text;

namespace AccessControll.Application.Auth;

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
            return new LoginResponse(false, null, false, "نام کاربری یا رمز عبور اشتباه است");

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return new LoginResponse(false, null, false, "حساب کاربری قفل شده است");

        if (!result.Succeeded)
            return new LoginResponse(false, null, false, "نام کاربری یا رمز عبور اشتباه است");

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(request.TwoFactorCode))
                return new LoginResponse(false, null, true, null);

            if (!VerifyTotp(user.TwoFactorSecretKey!, request.TwoFactorCode))
                return new LoginResponse(false, null, false, "کد تأیید دو مرحله‌ای نامعتبر است");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtService.GenerateToken(user, roles);

        return new LoginResponse(true, token, false, null);
    }

    private static bool VerifyTotp(string secretKey, string code)
    {
        var keyBytes = Base32Encoding.ToBytes(secretKey);
        var totp = new Totp(keyBytes);
        return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
    }
}

public class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, TwoFactorSetupResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public EnableTwoFactorCommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<TwoFactorSetupResponse> Handle(EnableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId)
            ?? throw new InvalidOperationException("کاربر یافت نشد");

        var secretKey = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        user.TwoFactorSecretKey = secretKey;
        await _userManager.UpdateAsync(user);

        var issuer = "AccessControll";
        var accountTitle = Uri.EscapeDataString(user.Email ?? user.UserName ?? "user");
        var qrUri = $"otpauth://totp/{issuer}:{accountTitle}?secret={secretKey}&issuer={issuer}&digits=6";

        return new TwoFactorSetupResponse(secretKey, qrUri);
    }
}

public class VerifyTwoFactorCommandHandler : IRequestHandler<VerifyTwoFactorCommand, bool>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public VerifyTwoFactorCommandHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<bool> Handle(VerifyTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user?.TwoFactorSecretKey == null) return false;

        var keyBytes = Base32Encoding.ToBytes(user.TwoFactorSecretKey);
        var totp = new Totp(keyBytes);
        var valid = totp.VerifyTotp(request.Code, out _, new VerificationWindow(1, 1));

        if (valid)
        {
            user.TwoFactorEnabled = true;
            await _userManager.UpdateAsync(user);
        }

        return valid;
    }
}

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

// Interface for JWT service (implemented in Infrastructure)
public interface IJwtService
{
    string GenerateToken(ApplicationUser user, IList<string> roles);
    (bool valid, string? userId) ValidateToken(string token);
}
