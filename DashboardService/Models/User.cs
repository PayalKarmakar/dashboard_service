namespace DashboardService.Models;

public class User
{
    public long UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime? LastLogin { get; set; }

    public string Status => IsActive ? "Active" : "Inactive";

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";

    public string LastLoginDisplay => LastLogin.HasValue
        ? LastLogin.Value.ToString("dd MMM yyyy hh:mm tt")
        : "-";
}
