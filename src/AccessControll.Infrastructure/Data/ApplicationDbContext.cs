using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AccessControll.Domain.Entities;

namespace AccessControll.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public ApplicationDbContext()
    {
        
    }
    public DbSet<Door> Doors => Set<Door>();
    public DbSet<DoorAccessLog> DoorAccessLogs => Set<DoorAccessLog>();
    public DbSet<UserDoorPermission> UserDoorPermissions => Set<UserDoorPermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ApplicationUser
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        });

        // Door
        builder.Entity<Door>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(200).IsRequired();
            e.Property(d => d.Location).HasMaxLength(300);
            e.HasMany(d => d.AccessLogs)
                .WithOne(l => l.Door)
                .HasForeignKey(l => l.DoorId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(d => d.UserPermissions)
                .WithOne(p => p.Door)
                .HasForeignKey(p => p.DoorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DoorAccessLog
        builder.Entity<DoorAccessLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasOne(l => l.User)
                .WithMany(u => u.DoorAccessLogs)
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => l.AccessedAt);
            e.HasIndex(l => new { l.DoorId, l.AccessedAt });
            e.HasIndex(l => new { l.UserId, l.AccessedAt });
        });

        // UserDoorPermission
        builder.Entity<UserDoorPermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.User)
                .WithMany(u => u.DoorPermissions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.UserId, p.DoorId }).IsUnique();
        });

        // Seed default roles
        builder.Entity<IdentityRole>()/*.HasData(
            new IdentityRole { Id = "1", Name = "Admin", NormalizedName = "ADMIN" },
            new IdentityRole { Id = "2", Name = "DoorManager", NormalizedName = "DOORMANAGER" },
            new IdentityRole { Id = "3", Name = "User", NormalizedName = "USER" }
        )*/;
    }
}
