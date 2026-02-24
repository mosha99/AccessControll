using AccessControll.Domain.Interfaces;

namespace AccessControll.Hardware;

/// <summary>
/// Sends OpenDoor / CloseDoor commands to the correct ESP8266 station via SignalR.
/// The actual SignalR senders are wired from Program.cs to avoid a circular dependency
/// (Hardware → API.Hubs), using the same callback pattern as the display pipeline.
///
/// I2C address and pin are stored as strings (e.g. "0x21", "33", "P3", "3") and
/// parsed to byte here before sending numeric values to the ESP.
/// </summary>
public class StationRelayService : IStationRelayService
{
    // (stationMac, i2cAddr, pin, durationMs, isActiveLow)
    private Func<string, byte, byte, int, bool, Task>? _openSender;

    // (stationMac, i2cAddr, pin, isActiveLow)
    private Func<string, byte, byte, bool, Task>? _closeSender;

    public void SetOpenSender(Func<string, byte, byte, int, bool, Task> sender) => _openSender = sender;
    public void SetCloseSender(Func<string, byte, byte, bool, Task> sender)     => _closeSender = sender;

    public async Task OpenDoorAsync(string stationMac, string i2cAddress, string pin, int durationMs, bool isActiveLow = true)
    {
        if (_openSender is null) { Console.WriteLine("[Relay] OpenSender not wired"); return; }
        await _openSender(stationMac, ParseAddr(i2cAddress), ParsePin(pin), durationMs, isActiveLow);
    }

    public async Task CloseDoorAsync(string stationMac, string i2cAddress, string pin, bool isActiveLow = true)
    {
        if (_closeSender is null) { Console.WriteLine("[Relay] CloseSender not wired"); return; }
        await _closeSender(stationMac, ParseAddr(i2cAddress), ParsePin(pin), isActiveLow);
    }

    // ── Parsing helpers ────────────────────────────────────────────────────────

    private static byte ParseAddr(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToByte(s[2..], 16);
        if (byte.TryParse(s, out var b)) return b;
        return Convert.ToByte(s, 16); // bare hex without prefix
    }

    private static byte ParsePin(string s)
    {
        s = s.Trim().TrimStart('p', 'P'); // strip optional 'P' prefix (e.g. "P3" → "3")
        return byte.TryParse(s, out var b) ? b : (byte)0;
    }
}
