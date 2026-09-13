namespace DashboardService.Models;

public class Role
{
    public int RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public override string ToString() => RoleName;
}
