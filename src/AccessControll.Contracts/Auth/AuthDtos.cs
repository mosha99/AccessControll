namespace AccessControll.Contracts.Auth;

public record LoginRequest(string Email, string Password, string? TwoFactorCode = null);

public record Login2FARequest(string TwoFactorCode);

public record LoginResponse(
    bool Succeeded,
    string? Token = null,
    bool RequiresTwoFactor = false,
    string? Error = null);

public record TwoFactorSetupResponse(string SecretKey, string QrCodeUri);

public record VerifyTwoFactorRequest(string Code);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record SetupRequest(string FullName, string Email, string Password);
