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
}
