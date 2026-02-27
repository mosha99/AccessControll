namespace AccessControll.Domain.Entities;

/// <summary>
/// Panel/section keys used for dynamic role-based access control.
/// Dashboard (/) and Profile (/profile) are always accessible.
/// </summary>
public static class AppPanels
{
    public const string Doors    = "doors";
    public const string Logs     = "logs";
    public const string Stations = "stations";
    public const string Users    = "users";
    public const string Roles    = "roles";

    public static readonly string[] All = [Doors, Logs, Stations, Users, Roles];
}
