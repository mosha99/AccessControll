using AccessControll.Hardware;
using Microsoft.AspNetCore.SignalR;

namespace AccessControll.API.Hubs;

public class HardwareHub : Hub
{
    private readonly StationConnectionManager _connectionManager;
    private readonly StationSessionManager _sessionManager;

    public HardwareHub(StationConnectionManager connectionManager, StationSessionManager sessionManager)
    {
        _connectionManager = connectionManager;
        _sessionManager = sessionManager;
    }

    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"[HW] Station connecting: {Context.ConnectionId} — waiting for RegisterStation");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var mac = _connectionManager.GetMac(Context.ConnectionId);
        if (mac != null)
        {
            Console.WriteLine($"[HW] Station disconnected: {mac}");
            _connectionManager.Unregister(Context.ConnectionId);
            _sessionManager.RemoveSession(mac);
        }
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Called by the ESP8266 after the SignalR handshake to identify itself by MAC address.
    /// Creates a dedicated session; the display pipeline was wired in Program.cs.
    /// </summary>
    public Task RegisterStation(string macAddress)
    {
        var mac = macAddress.ToUpperInvariant();
        Console.WriteLine($"[HW] Station registered: {mac} (conn={Context.ConnectionId})");

        _connectionManager.Register(mac, Context.ConnectionId);
        _sessionManager.CreateSession(mac);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Receives a key press from an ESP8266 keypad and routes it to that station's session.
    /// Display update is sent back only to that station via its ConnectionId.
    /// </summary>
    public async Task SendKey(string key)
    {
        var mac = _connectionManager.GetMac(Context.ConnectionId);
        if (mac == null)
        {
            Console.WriteLine($"[HW] Key from unregistered connection: {Context.ConnectionId}");
            return;
        }

        Console.WriteLine($"[HW] Key '{key}' from {mac}");

        if (key.Length > 0)
            await _sessionManager.HandleKey(mac, key[0]);
    }
}
