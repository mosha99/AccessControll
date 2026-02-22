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

    public virtual ICollection<DoorAccessLog> AccessLogs { get; set; } = new List<DoorAccessLog>();
    public virtual ICollection<UserDoorPermission> UserPermissions { get; set; } = new List<UserDoorPermission>();
    public int Code { get; set; }
}
