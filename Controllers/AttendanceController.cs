using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize(Roles = "Manager,Employee")]
public class AttendanceController(AppDbContext db, IConfiguration configuration) : ControllerBase
{
    private bool Office(out double latitude, out double longitude, out double radius)
    {
        latitude = configuration.GetValue<double?>("Attendance:Latitude") ?? double.NaN;
        longitude = configuration.GetValue<double?>("Attendance:Longitude") ?? double.NaN;
        radius = configuration.GetValue<double?>("Attendance:RadiusMeters") ?? 500;
        return double.IsFinite(latitude) && Math.Abs(latitude) <= 90 &&
            double.IsFinite(longitude) && Math.Abs(longitude) <= 180 && double.IsFinite(radius) && radius > 0;
    }

    [HttpGet("office")]
    public IActionResult GetOffice()
    {
        if (!Office(out var latitude, out var longitude, out var radiusMeters))
            return StatusCode(503, new { message = "Office map pin has not been configured. Contact your manager." });
        return Ok(new { name = configuration["Attendance:Name"] ?? "Appzon Technologies", latitude, longitude, radiusMeters });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var records = await db.Attendance.AsNoTracking()
            .Where(a => User.IsManager() || a.EmployeeId == User.EmployeeId())
            .OrderByDescending(a => a.CheckedInAt)
            .Select(a => new { a.Id, a.EmployeeId, employeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                a.WorkDate, a.CheckedInAt, a.Latitude, a.Longitude }).ToListAsync();
        return Ok(records);
    }

    [HttpGet("{id:int}/selfie")]
    public async Task<IActionResult> Selfie(int id)
    {
        var record = await db.Attendance.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && (User.IsManager() || a.EmployeeId == User.EmployeeId()));
        return record == null ? NotFound() : Ok(new { dataUri = "data:image/jpeg;base64," + Convert.ToBase64String(record.Selfie) });
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> CheckIn([FromForm] IFormFile selfie,
        [FromForm, BindRequired] double latitude, [FromForm, BindRequired] double longitude)
    {
        var employeeId = User.EmployeeId();
        if (employeeId == null || !await db.Employees.AnyAsync(e => e.Id == employeeId && e.Status.ToLower() == "active"))
            return BadRequest(new { message = "Your account must be linked to an active employee." });
        if (!Office(out var officeLat, out var officeLon, out var radius))
            return StatusCode(503, new { message = "Office map pin has not been configured." });
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            return BadRequest(new { message = "Valid location coordinates are required." });
        if (Distance(latitude, longitude, officeLat, officeLon) > radius)
            return BadRequest(new { message = "You are outside the office attendance area." });
        if (selfie == null || selfie.Length < 4 || selfie.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Provide a JPEG selfie up to 5 MB." });
        await using var memory = new MemoryStream();
        await selfie.CopyToAsync(memory);
        var bytes = memory.ToArray();
        if (bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            return BadRequest(new { message = "The selfie must be a JPEG image." });
        var now = DateTime.UtcNow;
        var workDate = DateOnly.FromDateTime(now.AddHours(5.5));
        if (await db.Attendance.AnyAsync(a => a.EmployeeId == employeeId && a.WorkDate == workDate))
            return Conflict(new { message = "Attendance is already recorded for today." });
        var record = new Attendance { EmployeeId = employeeId.Value, WorkDate = workDate,
            CheckedInAt = now, Latitude = latitude, Longitude = longitude, Selfie = bytes };
        db.Attendance.Add(record);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Conflict(new { message = "Attendance is already recorded for today." }); }
        return Ok(new { record.Id, record.WorkDate, record.CheckedInAt });
    }

    public static double Distance(double lat, double lon, double otherLat, double otherLon)
    {
        double Rad(double value) => value * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(otherLat - lat) / 2), 2) +
            Math.Cos(Rad(lat)) * Math.Cos(Rad(otherLat)) * Math.Pow(Math.Sin(Rad(otherLon - lon) / 2), 2);
        return 6371000 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
    }
}
