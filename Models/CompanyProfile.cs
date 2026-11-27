using System.ComponentModel.DataAnnotations;

namespace EmployeeApi.Models;

public class CompanyProfile
{
    public int Id { get; set; } = 1;

    [Required, MaxLength(120)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string ManagerName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string ManagerEmail { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? ManagerPhone { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int UpdatedByUserId { get; set; }
}
