using Microsoft.AspNetCore.Identity;

namespace AccessControll.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string? TwoFactorSecretKey { get; set; }
    public virtual ICollection<DoorAccessLog> DoorAccessLogs { get; set; } = new List<DoorAccessLog>();
    public virtual ICollection<UserDoorPermission> DoorPermissions { get; set; } = new List<UserDoorPermission>();
}
