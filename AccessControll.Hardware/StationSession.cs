using AccessControll.Application.Auth;
using AccessControll.Application.Doors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;

namespace AccessControll.Hardware;

/// <summary>
/// Per-station auth + door-code state machine.
///
/// IMPORTANT: HandleKey must always return immediately (no awaiting inside it).
/// If HandleKey awaited async work (TOTP check, door open), SignalR's sequential
/// per-connection dispatch would deadlock — the hub method waiting for door input
/// would block all subsequent SendKey calls from the same client.
///
/// Instead, long-running work (TOTP, door relay) is fired as background tasks
/// and state is tracked with a simple enum.
/// </summary>
public class StationSession
{
    private readonly string _mac;
    private readonly Oled _oled;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<byte[], Task> _sendDisplay;

    private enum State { Auth, DoorCode, Processing }
    private State _state = State.Auth;

    // ── Auth state ───────────────────────────────────────────────────────────
    private string _authBuffer = "";
    private const int AuthCodeLength = 6;

    // ── Door-code state ──────────────────────────────────────────────────────
    private string _doorBuffer = "";
    private const int DoorCodeLength = 2;
    private string? _pendingUserId;

    public StationSession(string mac, Oled oled, IServiceScopeFactory scopeFactory,
        Func<byte[], Task> sendDisplay)
    {
        _mac = mac;
        _oled = oled;
        _scopeFactory = scopeFactory;
        _sendDisplay = sendDisplay;
    }

    /// <summary>Show the initial "enter 2FA code" screen.</summary>
    public void Init() => ShowAuthUI();

    // ── Key dispatch — always returns immediately, never blocks ──────────────

    public Task HandleKey(char key)
    {
        switch (_state)
        {
            case State.Auth:     HandleAuthKey(key);  break;
            case State.DoorCode: HandleDoorKey(key);  break;
            // State.Processing: ignore input while background task is running
        }
        return Task.CompletedTask;
    }

    // ── Auth flow ─────────────────────────────────────────────────────────────

    private void HandleAuthKey(char key)
    {
        if (key == '*' || key == '#') { _authBuffer = ""; ShowAuthUI(); return; }
        if (!char.IsDigit(key)) return;

        _authBuffer += key;
        ShowAuthUI();

        if (_authBuffer.Length >= AuthCodeLength)
        {
            var code = _authBuffer;
            _authBuffer = "";
            _state = State.Processing;          // block further input immediately
            _ = RunTOTPCheck(code);             // background — does not block hub
        }
    }

    private async Task RunTOTPCheck(string code)
    {
        using var scope = _scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new _2FALoginCommand(code));

        if (result.Succeeded)
        {
            _pendingUserId = result.UserId;
            RenderAndSend("در حال پردازش", result.Name!, Color.White);
            await Task.Delay(2000);

            // Transition to door-code entry
            _doorBuffer = "";
            _state = State.DoorCode;
            ShowDoorUI();

            // Auto-cancel after 5 s if no door code entered
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (_state == State.DoorCode)
                {
                    _pendingUserId = null;
                    _doorBuffer = "";
                    _state = State.Auth;
                    ShowAuthUI();
                }
            });
        }
        else
        {
            RenderAndSend("دسترسی غیرمجاز", "✗ کد اشتباه است", Color.White);
            await Task.Delay(3000);
            _state = State.Auth;
            ShowAuthUI();
        }
    }

    // ── Door-code flow ────────────────────────────────────────────────────────

    private void HandleDoorKey(char key)
    {
        if (key == '*' || key == '#') { _doorBuffer = ""; ShowDoorUI(); return; }
        if (!char.IsDigit(key)) return;

        _doorBuffer += key;
        ShowDoorUI();

        if (_doorBuffer.Length >= DoorCodeLength)
        {
            var code = int.Parse(_doorBuffer);
            var userId = _pendingUserId!;
            _doorBuffer = "";
            _pendingUserId = null;
            _state = State.Processing;          // block input during door relay
            _ = RunDoorControl(code, userId);   // background — does not block hub
        }
    }

    private async Task RunDoorControl(int doorCode, string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var doorResult = await mediator.Send(
            new ControlDoorCommand(null, doorCode, userId, false, "0.0.0.0"));

        if (doorResult.Succeeded)
        {
            RenderAndSend("دسترسی مجاز", "✓ خوش آمدید", Color.White);
            await Task.Delay(1000);
            await mediator.Send(
                new ControlDoorCommand(null, doorCode, userId, true, "0.0.0.0"));
        }
        else
        {
            RenderAndSend("خطا", doorResult.Message ?? "خطای ناشناخته", Color.White);
        }

        await Task.Delay(3000);
        _state = State.Auth;
        ShowAuthUI();
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void ShowAuthUI()
    {
        var len = _authBuffer.Length;
        var body = len switch
        {
            0 => "_ _ _   _ _ _",
            1 => $"{_authBuffer[0]} _ _   _ _ _",
            2 => $"{_authBuffer[0]} {_authBuffer[1]} _   _ _ _",
            3 => $"{_authBuffer[0]} {_authBuffer[1]} {_authBuffer[2]}   _ _ _",
            4 => $"{_authBuffer[0]} {_authBuffer[1]} {_authBuffer[2]}   {_authBuffer[3]} _ _",
            5 => $"{_authBuffer[0]} {_authBuffer[1]} {_authBuffer[2]}   {_authBuffer[3]} {_authBuffer[4]} _",
            _ => $"{_authBuffer[0]} {_authBuffer[1]} {_authBuffer[2]}   {_authBuffer[3]} {_authBuffer[4]} {_authBuffer[5]}"
        };
        RenderAndSend("کد 2FA را بزنید:", body, Color.White);
    }

    private void ShowDoorUI()
    {
        var len = _doorBuffer.Length;
        var body = len switch
        {
            0 => "_  _",
            1 => $"{_doorBuffer[0]}  _",
            _ => $"{_doorBuffer[0]}  {_doorBuffer[1]}"
        };
        RenderAndSend("شماره در :", body, Color.White);
    }

    private void RenderAndSend(string title, string body, Color color)
    {
        var buffer = _oled.RenderUI(title, body, color);
        _ = _sendDisplay(buffer);
    }
}
