using Microsoft.EntityFrameworkCore;
using AccessControll.Domain.Entities;
using AccessControll.Domain.Interfaces;
using AccessControll.Infrastructure.Data;

namespace AccessControll.Infrastructure.Repositories;

public class RolePanelPermissionRepository(ApplicationDbContext context) : IRolePanelPermissionRepository
{
    public async Task<List<string>> GetPanelsForRoleAsync(string roleName)
        => await context.RolePanelPermissions
            .Where(p => p.RoleName == roleName)
            .Select(p => p.Panel)
            .ToListAsync();

    public async Task<List<string>> GetPanelsForRolesAsync(IEnumerable<string> roleNames)
        => await context.RolePanelPermissions
            .Where(p => roleNames.Contains(p.RoleName))
            .Select(p => p.Panel)
            .Distinct()
            .ToListAsync();

    public async Task SetPanelsForRoleAsync(string roleName, IEnumerable<string> panels)
    {
        var existing = await context.RolePanelPermissions
            .Where(p => p.RoleName == roleName)
            .ToListAsync();

        context.RolePanelPermissions.RemoveRange(existing);

        context.RolePanelPermissions.AddRange(
            panels.Select(panel => new RolePanelPermission { RoleName = roleName, Panel = panel })
        );

        await context.SaveChangesAsync();
    }
}
