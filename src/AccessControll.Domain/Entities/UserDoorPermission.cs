namespace AccessControll.Domain.Entities;

public class UserDoorPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public Guid DoorId { get; set; }
    public bool CanOpen { get; set; }
    public bool CanLock { get; set; }
    public TimeSpan? AllowedFromTime { get; set; }
    public TimeSpan? AllowedToTime { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string GrantedByUserId { get; set; } = string.Empty;

    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Door Door { get; set; } = null!;
}
