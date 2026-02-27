using Microsoft.AspNetCore.Identity;
using AccessControll.Domain.Entities;
using AccessControll.Infrastructure.Data;

namespace AccessControll.Infrastructure.Services;

/// <summary>
/// سید اولیه دیتابیس — ادمین پیش‌فرض و داده‌های نمونه
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(bool develoop ,
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        Console.WriteLine("---------Start Seed ----------------------");
        // Seed roles
        var roles = new[] { "Admin" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        Console.WriteLine("✅ Roles seeded successfully");
    }
}
