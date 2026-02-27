namespace AccessControll.Domain.Entities;

public class RolePanelPermission
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string Panel    { get; set; } = string.Empty;
}
