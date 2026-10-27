namespace EmployeeApi.Models;

public class Attendance
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public byte[] Selfie { get; set; } = [];
}
