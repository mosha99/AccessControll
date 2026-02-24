namespace AccessControll.Domain.Entities;

public class Door
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsLocked { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedAt { get; set; }
    public string? HardwareId { get; set; }

    public string? StationMacAddress { get; set; }
    public string I2cAddress { get; set; } = "0x20";
    public string I2cPin { get; set; } = "0";
    public int DurationMs { get; set; } = 5000;
    public bool IsMomentary { get; set; } = true;

    /// <summary>
    /// When true (default): relay module is active-low (PCF8574 LOW = relay ON, 0xFF = all off).
    /// When false: relay module is active-high (PCF8574 HIGH = relay ON, 0x00 = all off).
    /// NOTE: active-high boards require external pull-down resistors on PCF8574 outputs
    /// to prevent a brief startup pulse while the ESP8266 boots (PCF8574 resets to 0xFF).
    /// </summary>
    public bool IsActiveLow { get; set; } = true;

    public virtual ICollection<DoorAccessLog> AccessLogs { get; set; } = new List<DoorAccessLog>();
    public virtual ICollection<UserDoorPermission> UserPermissions { get; set; } = new List<UserDoorPermission>();
    public int Code { get; set; }
}
