using System.ComponentModel.DataAnnotations.Schema;

namespace EmployeeApi.Models;

[Table("Tasks")]
public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int EmployeeId { get; set; }
    public string Priority { get; set; } = "Medium"; // Low, Medium, High
    public string Status { get; set; } = "Pending";   // Pending, InProgress, Completed
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    [ForeignKey("EmployeeId")]
    public Employee? Employee { get; set; }
}

public class UpdateTaskStatusDto
{
    public string Status { get; set; } = string.Empty;
}