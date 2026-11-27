using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/company-profile")]
[Authorize(Roles = "Manager,Employee")]
public class CompanyProfileController(AppDbContext db) : ControllerBase
{
    public sealed class SaveCompanyProfileRequest
    {
        [Required, MaxLength(120)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string ManagerName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string ManagerEmail { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? ManagerPhone { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var profile = await db.CompanyProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == 1);
        if (profile == null) return Ok(null);
        return Ok(ToResponse(profile, User.IsInRole("Manager")));
    }

    [HttpPut]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Save(SaveCompanyProfileRequest request)
    {
        var companyName = request.CompanyName.Trim();
        var managerName = request.ManagerName.Trim();
        var managerEmail = request.ManagerEmail.Trim().ToLowerInvariant();
        var managerPhone = string.IsNullOrWhiteSpace(request.ManagerPhone)
            ? null
            : request.ManagerPhone.Trim();

        if (companyName.Length == 0 || managerName.Length == 0 ||
            !System.Net.Mail.MailAddress.TryCreate(managerEmail, out _))
            return BadRequest(new { message = "Enter a company name and valid manager contact details." });

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var profile = await db.CompanyProfiles.SingleOrDefaultAsync(p => p.Id == 1);
        if (profile == null)
        {
            profile = new CompanyProfile { Id = 1 };
            db.CompanyProfiles.Add(profile);
        }

        profile.CompanyName = companyName;
        profile.ManagerName = managerName;
        profile.ManagerEmail = managerEmail;
        profile.ManagerPhone = managerPhone;
        profile.UpdatedAt = DateTime.UtcNow;
        profile.UpdatedByUserId = userId;
        await db.SaveChangesAsync();

        return Ok(ToResponse(profile, includeManagerDetails: true));
    }

    private static object ToResponse(CompanyProfile profile, bool includeManagerDetails) => new
    {
        profile.CompanyName,
        managerName = includeManagerDetails ? profile.ManagerName : null,
        managerEmail = includeManagerDetails ? profile.ManagerEmail : null,
        managerPhone = includeManagerDetails ? profile.ManagerPhone : null
    };
}
