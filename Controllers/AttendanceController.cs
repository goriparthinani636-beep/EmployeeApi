
using System.Security.Claims;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly Cloudinary _cloudinary;
    private readonly IConfiguration _configuration;

    public AttendanceController(
        AppDbContext context,
        Cloudinary cloudinary,
        IConfiguration configuration)
    {
        _context = context;
        _cloudinary = cloudinary;
        _configuration = configuration;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> MarkAttendance(
        [FromForm] IFormFile selfie,
        [FromForm] double latitude,
        [FromForm] double longitude)
    {
        var userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        if (selfie == null || selfie.Length == 0)
        {
            return BadRequest(new
            {
                message = "Selfie is required."
            });
        }

        if (selfie.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new
            {
                message = "Selfie must be 5 MB or smaller."
            });
        }

        if (!selfie.ContentType.Equals(
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase) &&
            !selfie.ContentType.Equals(
                "image/png",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Only JPEG and PNG selfies are allowed."
            });
        }

        if (!double.IsFinite(latitude) ||
            !double.IsFinite(longitude) ||
            latitude < -90 || latitude > 90 ||
            longitude < -180 || longitude > 180)
        {
            return BadRequest(new
            {
                message = "Invalid GPS coordinates."
            });
        }

        var companyLatitude = _configuration.GetValue<double?>(
            "Attendance:CompanyLatitude");

        var companyLongitude = _configuration.GetValue<double?>(
            "Attendance:CompanyLongitude");

        var allowedRadius = _configuration.GetValue<double?>(
            "Attendance:AllowedRadiusMeters") ?? 100;

        if (companyLatitude == null ||
            companyLongitude == null ||
            allowedRadius <= 0)
        {
            return StatusCode(500, new
            {
                message = "Company attendance location is not configured."
            });
        }

        var distance = GetDistanceInMeters(
            latitude,
            longitude,
            companyLatitude.Value,
            companyLongitude.Value);

        if (distance > allowedRadius)
        {
            return BadRequest(new
            {
                message = "You are outside the permitted attendance area.",
                distanceMeters = Math.Round(distance, 2),
                allowedRadiusMeters = allowedRadius
            });
        }

        var now = DateTime.UtcNow;
        var today = now.Date;

        var alreadyMarked = await _context.Attendances.AnyAsync(a =>
            a.UserId == userId &&
            a.AttendanceDate.Date == today);

        if (alreadyMarked)
        {
            return Conflict(new
            {
                message = "Attendance has already been marked today."
            });
        }

        string? uploadedPublicId = null;

        try
        {
            await using var stream = selfie.OpenReadStream();

            var uploadResult = await _cloudinary.UploadAsync(
                new ImageUploadParams
                {
                    File = new FileDescription(
                        selfie.FileName,
                        stream),
                    Folder = "employee-attendance",
                    PublicId = $"attendance_{userId}_{Guid.NewGuid():N}"
                });

            if (uploadResult.Error != null ||
                uploadResult.SecureUrl == null)
            {
                return StatusCode(502, new
                {
                    message = "Selfie upload failed."
                });
            }

            uploadedPublicId = uploadResult.PublicId;

            var attendance = new Attendance
            {
                UserId = userId,
                AttendanceDate = today,
                CheckInTime = now,
                Latitude = latitude,
                Longitude = longitude,
                DistanceFromCompany = Math.Round(distance, 2),
                SelfieUrl = uploadResult.SecureUrl.ToString(),
                CloudinaryPublicId = uploadResult.PublicId,
                Status = "Present"
            };

            _context.Attendances.Add(attendance);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Attendance marked successfully.",
                attendance = new
                {
                    attendance.Id,
                    attendance.UserId,
                    attendance.AttendanceDate,
                    attendance.CheckInTime,
                    attendance.DistanceFromCompany,
                    attendance.SelfieUrl,
                    attendance.Status
                }
            });
        }
        catch (Exception)
        {
            if (!string.IsNullOrWhiteSpace(uploadedPublicId))
            {
                try
                {
                    await _cloudinary.DestroyAsync(
                        new DeletionParams(uploadedPublicId));
                }
                catch
                {
                    // Keep the original attendance error.
                }
            }

            return StatusCode(500, new
            {
                message = "Unable to save attendance."
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAttendance()
    {
        var userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        var isAdmin = User.IsInRole("Admin");

        var query = _context.Attendances.AsNoTracking();

        if (!isAdmin)
        {
            query = query.Where(a => a.UserId == userId);
        }

        var records = await query
            .OrderByDescending(a => a.CheckInTime)
            .ToListAsync();

        return Ok(records);
    }

    private static double GetDistanceInMeters(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double earthRadius = 6371000;

        static double ToRadians(double degrees) =>
            degrees * Math.PI / 180;

        var dLat = ToRadians(latitude2 - latitude1);
        var dLon = ToRadians(longitude2 - longitude1);

        var a =
            Math.Pow(Math.Sin(dLat / 2), 2) +
            Math.Cos(ToRadians(latitude1)) *
            Math.Cos(ToRadians(latitude2)) *
            Math.Pow(Math.Sin(dLon / 2), 2);

        var boundedA = Math.Clamp(a, 0.0, 1.0);

        return earthRadius *
            2 *
            Math.Atan2(
                Math.Sqrt(boundedA),
                Math.Sqrt(1 - boundedA));
    }
}
