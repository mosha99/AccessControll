using AccessControll.Domain.Enums;

namespace AccessControll.Domain.Entities;

public class DoorAccessLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DoorId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;
    public DoorAction Action { get; set; }
    public AccessResult Result { get; set; }
    public string? IpAddress { get; set; }
    public string? Notes { get; set; }

    public virtual Door Door { get; set; } = null!;
    public virtual ApplicationUser User { get; set; } = null!;
}
