namespace EmployeeApi.Models;

public class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string? ResumeUrl { get; set; }
    public string? ResumeFileName { get; set; }
    public DateTime JoiningDate { get; set; } = DateTime.UtcNow;
}
