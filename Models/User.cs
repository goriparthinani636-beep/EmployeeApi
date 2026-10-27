
namespace EmployeeApi.Models;

public class User
{
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int SecurityVersion { get; set; }
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Hashed refresh token; raw token database lo save cheyyamu.
    public string? RefreshTokenHash { get; set; }

    // Refresh token expiry date.
    public DateTime? RefreshTokenExpiry { get; set; }
}
