using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;

namespace AccessControll.Hardware;

public class PhysicalAuthService
{
    private readonly Oled _oled;
    private readonly IServiceScopeFactory _scopeFactory;

    private static string _inputBuffer = "";
    private const int CodeLength = 6;

    public PhysicalAuthService(Oled oled, IServiceScopeFactory scopeFactory)
    {
        _oled = oled;
        _scopeFactory = scopeFactory;
    }

    public void Init()
    {
        UpdateUI();
    }

    public async void OnKeyPress(char key)
    {
        // پاک کردن با * یا #
        if (key == '*' || key == '#')
        {
            _inputBuffer = "";
            UpdateUI();
            return;
        }

        // فقط اعداد قبول می‌شن
        if (!char.IsDigit(key)) return;

        _inputBuffer += key;
        UpdateUI();

        if (_inputBuffer.Length == CodeLength && _inputBuffer.Length >= CodeLength)
        {
            await CheckTOTP();
        }
    }

    private async Task CheckTOTP()
    {
        string code = _inputBuffer;
        _inputBuffer = "";

        using IServiceScope scope = _scopeFactory.CreateScope();
        // TODO: سرویس verify رو از scope بگیر و کد رو چک کن
        // var totpService = scope.ServiceProvider.GetRequiredService<ITotpService>();
        // bool isValid = await totpService.VerifyAsync(code);

        bool isValid = false; // placeholder

        if (isValid)
        {
            _oled.RenderUI("دسترسی مجاز", "✓ خوش آمدید", Color.White);
        }
        else
        {
            _oled.RenderUI("دسترسی غیرمجاز", "✗ کد اشتباه است", Color.White);
        }

        await Task.Delay(2000);
        UpdateUI();
    }

    private void UpdateUI()
    {
        int length = _inputBuffer.Length;

        string body = length switch
        {
            0 => "_ _ _   _ _ _",
            1 => $"{_inputBuffer[0]} _ _   _ _ _",
            2 => $"{_inputBuffer[0]} {_inputBuffer[1]} _   _ _ _",
            3 => $"{_inputBuffer[0]} {_inputBuffer[1]} {_inputBuffer[2]}   _ _ _",
            4 => $"{_inputBuffer[0]} {_inputBuffer[1]} {_inputBuffer[2]}   {_inputBuffer[3]} _ _",
            5 => $"{_inputBuffer[0]} {_inputBuffer[1]} {_inputBuffer[2]}   {_inputBuffer[3]} {_inputBuffer[4]} _",
            _ => $"{_inputBuffer[0]} {_inputBuffer[1]} {_inputBuffer[2]}   {_inputBuffer[3]} {_inputBuffer[4]} {_inputBuffer[5]}"
        };

        _oled.RenderUI("کد 2FA را بزنید:", body, Color.White);
    }
}
