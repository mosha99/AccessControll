namespace AccessControll.Hardware;

public class TotpService// : ITotpService
{
    private const string Issuer = "AuthApi";

    public string GenerateSecret()
    {
        byte[] key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    public string GetQrCodeUri(string email, string secret)
    {
        string label = Uri.EscapeDataString(Issuer);
        string account = Uri.EscapeDataString(email);

        return $"otpauth://totp/{label}:{account}?secret={secret}&issuer={label}&digits=6&period=30";
    }

    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            return false;
        }

        byte[] secretKey = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretKey, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

        // VerificationWindow(1, 1) اجازه می‌دهد کد قبلی یا بعدی (تأخیر زمانی کوچک) هم پذیرفته شود
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }
}