using Microsoft.AspNetCore.Identity;
using AccessControll.Domain.Entities;
using AccessControll.Infrastructure.Data;

namespace AccessControll.Infrastructure.Services;

/// <summary>
/// سید اولیه دیتابیس — ادمین پیش‌فرض و داده‌های نمونه
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        await context.Database.EnsureCreatedAsync();

        // Seed roles
        var roles = new[] { "Admin", "DoorManager", "User" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed admin user
        const string adminEmail = "admin@accesscontroll.local";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "مدیر سیستم",
                IsActive = true,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, "Admin@1234!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Admin");
        }

        // Seed sample doors
        if (!context.Doors.Any())
        {
            var doors = new[]
            {
                new Domain.Entities.Door { Name = "در ورودی اصلی", Description = "ورودی ساختمان اصلی", Location = "طبقه همکف — لابی", IsLocked = true, IsEnabled = true, HardwareId = "DOOR-001" },
                new Domain.Entities.Door { Name = "سالن کنفرانس", Description = "اتاق جلسات بزرگ", Location = "طبقه اول", IsLocked = true, IsEnabled = true, HardwareId = "DOOR-002" },
                new Domain.Entities.Door { Name = "اتاق سرور", Description = "دسترسی محدود", Location = "طبقه منفی یک", IsLocked = true, IsEnabled = true, HardwareId = "DOOR-003" },
                new Domain.Entities.Door { Name = "پارکینگ", Description = "در پارکینگ B1", Location = "زیرزمین", IsLocked = false, IsEnabled = true, HardwareId = "DOOR-004" }
            };
            context.Doors.AddRange(doors);
            await context.SaveChangesAsync();
        }

        Console.WriteLine("✅ Database seeded successfully");
        Console.WriteLine("   Admin: admin@accesscontroll.local / Admin@1234!");
    }
}
