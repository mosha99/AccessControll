using AccessControll.Domain.Enums;

namespace AccessControll.Domain.Entities;

public class Station
{
    public Guid Id { get; set; }

    /// <summary>MAC address of the ESP8266, e.g. "AA:BB:CC:DD:EE:FF"</summary>
    public string MacAddress { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeen { get; set; }

    /// <summary>Last IP where this station was seen on the network (used for reconnect).</summary>
    public string? LastKnownIp { get; set; }

    /// <summary>
    /// Determines the station's operating mode.
    /// General (default): keyboard + display, global output control after 2FA.
    /// Door: keyboard + display, auto-opens its assigned outputs after 2FA.
    /// RemoteControl: no keyboard or display, pure relay output controller.
    /// </summary>
    public StationType Type { get; set; } = StationType.General;
}
