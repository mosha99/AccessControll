namespace AccessControll.Hardware;

/// <summary>
/// Routes keypad input from ESP8266 clients (via HardwareHub SignalR) to subscribers.
/// Replaces the physical Keypad class — same OnPress pattern so existing
/// PhysicalAuthService / PhysicalDoorService work unchanged.
/// </summary>
public class KeyboardRouter
{
    public Action<char>? OnPress;

    public void TriggerKey(char key) => OnPress?.Invoke(key);
}
