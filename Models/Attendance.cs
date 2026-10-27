
namespace EmployeeApi.Models;

public class Attendance
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime AttendanceDate { get; set; } = DateTime.UtcNow;

    public DateTime CheckInTime { get; set; } = DateTime.UtcNow;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double DistanceFromCompany { get; set; }

    public string SelfieUrl { get; set; } = string.Empty;

    public string? CloudinaryPublicId { get; set; }

    public string Status { get; set; } = "Present";
}
