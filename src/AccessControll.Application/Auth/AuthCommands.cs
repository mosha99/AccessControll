using MediatR;

namespace AccessControll.Application.Auth;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record LoginRequest(string Email, string Password, string? TwoFactorCode);
public record LoginResponse(bool Succeeded, string? Token, bool RequiresTwoFactor, string? Error);
public record RegisterRequest(string Email, string FullName, string Password);
public record TwoFactorSetupResponse(string SecretKey, string QrCodeUri);
public record VerifyTwoFactorRequest(string UserId, string Code);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string UserId, string Token, string NewPassword);

// ── Commands ──────────────────────────────────────────────────────────────────

public record LoginCommand(string Email, string Password, string? TwoFactorCode)
    : IRequest<LoginResponse>;

public record RegisterCommand(string Email, string FullName, string Password)
    : IRequest<(bool Succeeded, IEnumerable<string> Errors)>;

public record EnableTwoFactorCommand(string UserId)
    : IRequest<TwoFactorSetupResponse>;

public record DisableTwoFactorCommand(string UserId)
    : IRequest<bool>;

public record VerifyTwoFactorCommand(string UserId, string Code)
    : IRequest<bool>;

public record LogoutCommand(string UserId)
    : IRequest;

public record RefreshTokenCommand(string Token)
    : IRequest<LoginResponse>;
