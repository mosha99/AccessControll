using AccessControll.Domain.Enums;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace AccessControll.Hardware;

/// <summary>
/// Manages one <see cref="StationSession"/> per connected ESP8266 station.
/// Singleton — created once at startup, sessions created/destroyed as stations connect.
///
/// Call <see cref="SetDisplaySender"/> once at startup (from Program.cs) to wire the
/// SignalR send pipeline — this avoids injecting IHubContext inside the hub class itself.
/// </summary>
public class StationSessionManager
{
    private readonly ConcurrentDictionary<string, StationSession> _sessions = new();
    private readonly Oled _oled;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Set once at startup. Receives (mac, buffer) and sends to the correct ESP8266 connection.
    /// </summary>
    private Func<string, byte[], Task>? _displaySender;

    public StationSessionManager(Oled oled, IServiceScopeFactory scopeFactory)
    {
        _oled = oled;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Wire the display send pipeline. Call from Program.cs after the app is built,
    /// using app.Services.GetRequiredService&lt;IHubContext&lt;HardwareHub&gt;&gt;().
    /// </summary>
    public void SetDisplaySender(Func<string, byte[], Task> sender) => _displaySender = sender;

    /// <summary>Create (or replace) a session for the given MAC address.</summary>
    public StationSession CreateSession(string mac, StationType type = StationType.General)
    {
        _sessions.TryRemove(mac, out _);

        // Wrap the global sender so this session sends only to its own station
        var capturedMac = mac;
        Func<byte[], Task> sendDisplay = buffer =>
            _displaySender != null ? _displaySender(capturedMac, buffer) : Task.CompletedTask;

        var session = new StationSession(mac, type, _oled, _scopeFactory, sendDisplay);
        _sessions[mac] = session;
        session.Init();
        return session;
    }

    /// <summary>Destroy the session when a station disconnects.</summary>
    public void RemoveSession(string mac) => _sessions.TryRemove(mac, out _);

    /// <summary>Route a key press to the correct station's session.</summary>
    public async Task HandleKey(string mac, char key)
    {
        if (_sessions.TryGetValue(mac, out var session))
            await session.HandleKey(key);
        else
            Console.WriteLine($"[SessionMgr] No session found for MAC {mac}");
    }
}
