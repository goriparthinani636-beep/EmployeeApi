using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using EmployeeApi.Services;

namespace EmployeeApi.Controllers;

[Authorize(Roles = "Manager,Employee")]
[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    public class EmployeeWriteRequest : Employee
    {
        public string? Password { get; set; }
        public string? AadhaarNumber { get; set; }
        public bool AadhaarConsentConfirmed { get; set; }
    }

    public class AadhaarLastFourRequest
    {
        public string LastFour { get; set; } = string.Empty;
    }

    private static void CopyDetails(Employee target, Employee source)
    {
        target.FirstName = source.FirstName.Trim();
        target.LastName = source.LastName.Trim();
        target.Email = source.Email.Trim().ToLowerInvariant();
        target.Phone = source.Phone;
        target.WhatsAppOptIn = source.WhatsAppOptIn;
        target.Department = source.Department.Trim();
        target.Designation = source.Designation.Trim();
        target.Status = source.Status;
        target.ResumeUrl = source.ResumeUrl;
        target.ResumeFileName = source.ResumeFileName;
    }

    private static bool ValidDetails(Employee request) =>
        !string.IsNullOrWhiteSpace(request.FirstName) &&
        !string.IsNullOrWhiteSpace(request.LastName) &&
        !string.IsNullOrWhiteSpace(request.Department) &&
        !string.IsNullOrWhiteSpace(request.Designation) &&
        System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out _) &&
        request.Status is "Active" or "Inactive";

    private readonly AppDbContext _context;
    private readonly AadhaarNumberProtector _aadhaarProtector;

    public EmployeesController(AppDbContext context, AadhaarNumberProtector aadhaarProtector)
    {
        _context = context;
        _aadhaarProtector = aadhaarProtector;
    }

    // 1. GET: /api/employees
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employees = await _context.VisibleEmployees(User)
            .OrderByDescending(e => e.Id)
            .ToListAsync();
        return Ok(employees);
    }

    // Paged directory endpoint. Keep /api/employees as a full-list endpoint for
    // existing callers that need all employees (for example, task assignment).
    [HttpGet("page")]
    public async Task<IActionResult> GetPage(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.VisibleEmployees(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                e.FirstName.Contains(term) ||
                e.LastName.Contains(term) ||
                e.Email.Contains(term) ||
                e.Department.Contains(term) ||
                e.Designation.Contains(term));
        }

        if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(e => e.Status == "Active");
        else if (string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase))
            query = query.Where(e => e.Status == "Inactive");

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { items, total, page, pageSize, hasMore = page * pageSize < total });
    }

    [Authorize(Roles = "Employee")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        var employee = await _context.VisibleEmployees(User).SingleOrDefaultAsync();
        return employee == null ? NotFound(new { message = "Your login is not linked to an employee profile." }) : Ok(employee);
    }

    [Authorize(Roles = "Employee")]
    [HttpGet("me/aadhaar-status")]
    public async Task<IActionResult> GetMyAadhaarStatus()
    {
        var employeeId = User.EmployeeId();
        if (employeeId == null) return BadRequest(new { message = "Your login is not linked to an employee." });
        var enrollment = await _context.EmployeeAadhaarVerifications.AsNoTracking()
            .SingleOrDefaultAsync(a => a.EmployeeId == employeeId.Value);
        var isEnrolled = !string.IsNullOrWhiteSpace(enrollment?.EncryptedAadhaar);
        return Ok(new { isEnrolled, isVerified = isEnrolled && (enrollment?.IsVerified ?? false) });
    }

    [Authorize(Roles = "Employee")]
    [HttpPost("me/aadhaar-verification")]
    public async Task<IActionResult> VerifyMyAadhaarLastFour([FromBody] AadhaarLastFourRequest request)
    {
        var employeeId = User.EmployeeId();
        if (employeeId == null) return BadRequest(new { message = "Your login is not linked to an employee." });
        var digits = request.LastFour?.Trim() ?? string.Empty;
        if (digits.Length != 4 || digits.Any(c => c is < '0' or > '9'))
            return BadRequest(new { message = "Enter exactly the last 4 Aadhaar digits." });

        var enrollment = await _context.EmployeeAadhaarVerifications
            .SingleOrDefaultAsync(a => a.EmployeeId == employeeId.Value);
        if (enrollment == null || string.IsNullOrWhiteSpace(enrollment.EncryptedAadhaar))
            return Conflict(new { message = "Your manager has not pre-enrolled your verification digits yet." });
        if (enrollment.IsLocked)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Verification is locked after several incorrect attempts. Ask your manager to reset the pre-enrolled digits." });
        if (enrollment.IsVerified)
            return Ok(new { isVerified = true, message = "Aadhaar last-four verification is already complete." });

        if (!_aadhaarProtector.MatchesLastFour(digits, enrollment.PreEnrolledLastFourHash))
        {
            enrollment.FailedAttempts++;
            if (enrollment.FailedAttempts >= 5) enrollment.IsLocked = true;
            await _context.SaveChangesAsync();
            return BadRequest(new { message = "Those digits do not match the pre-enrolled details. Check with your manager if needed." });
        }

        enrollment.IsVerified = true;
        enrollment.VerifiedAt = DateTime.UtcNow;
        enrollment.FailedAttempts = 0;
        await _context.SaveChangesAsync();
        return Ok(new { isVerified = true, message = "Aadhaar last-four verification complete." });
    }

    // 2. GET: /api/employees/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employee = await _context.VisibleEmployees(User).FirstOrDefaultAsync(e => e.Id == id);
        if (employee == null)
            return NotFound(new { message = $"Employee with ID {id} not found." });

        return Ok(employee);
    }

    // 3. POST: /api/employees
    [Authorize(Roles = "Manager")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeWriteRequest request)
    {
        if (!ValidDetails(request))
            return BadRequest(new { message = "Name, email, department, designation and a valid status are required." });
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6)
            return BadRequest(new { message = "An employee login password with at least 6 characters is required." });
        if (!ValidOptionalAadhaarNumber(request.AadhaarNumber))
            return BadRequest(new { message = "Aadhaar number must contain exactly 12 digits." });
        if (!string.IsNullOrWhiteSpace(request.AadhaarNumber) && !request.AadhaarConsentConfirmed)
            return BadRequest(new { message = "Confirm the employee has consented before saving their Aadhaar number." });
        if (request.WhatsAppOptIn && string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new { message = "An employee phone number is required when WhatsApp updates are enabled." });
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email) ||
            await _context.Employees.AnyAsync(e => e.Email.ToLower() == email))
            return Conflict(new { message = "This email is already used. Choose a different employee login email." });
        var employee = new Employee();
        CopyDetails(employee, request);
        EmployeeAadhaarVerification? aadhaarEnrollment = null;
        if (!string.IsNullOrWhiteSpace(request.AadhaarNumber))
        {
            var protectedValue = _aadhaarProtector.Protect(request.AadhaarNumber.Trim());
            aadhaarEnrollment = new EmployeeAadhaarVerification
            {
                Employee = employee,
                EncryptedAadhaar = protectedValue.EncryptedAadhaar,
                AadhaarNonce = protectedValue.AadhaarNonce,
                AadhaarTag = protectedValue.AadhaarTag,
                PreEnrolledLastFourHash = protectedValue.LastFourHash
            };
        }
        var user = new User { FirstName = employee.FirstName, LastName = employee.LastName,
            Email = employee.Email, Role = "Employee", Employee = employee,
            IsActive = employee.Status == "Active" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, request.Password);
        _context.Employees.Add(employee);
        _context.Users.Add(user);
        if (aadhaarEnrollment != null) _context.EmployeeAadhaarVerifications.Add(aadhaarEnrollment);
        // One SaveChanges transaction creates both profile and linked login, or neither.
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Conflict(new { message = "This email is already used by a login account." }); }

        return CreatedAtAction(nameof(GetById), new { id = employee.Id }, employee);
    }

    // 4. PUT: /api/employees/{id}
    [Authorize(Roles = "Manager")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] EmployeeWriteRequest updated)
    {
        var existing = await _context.Employees.FindAsync(id);
        if (existing == null)
            return NotFound(new { message = $"Employee with ID {id} not found." });

        if (!ValidDetails(updated))
            return BadRequest(new { message = "Name, email, department, designation and a valid status are required." });
        if (!ValidOptionalAadhaarNumber(updated.AadhaarNumber))
            return BadRequest(new { message = "Aadhaar number must contain exactly 12 digits." });
        if (!string.IsNullOrWhiteSpace(updated.AadhaarNumber) && !updated.AadhaarConsentConfirmed)
            return BadRequest(new { message = "Confirm the employee has consented before saving their Aadhaar number." });
        if (updated.WhatsAppOptIn && string.IsNullOrWhiteSpace(updated.Phone))
            return BadRequest(new { message = "An employee phone number is required when WhatsApp updates are enabled." });
        if (!string.IsNullOrEmpty(updated.Password) && updated.Password.Length < 6)
            return BadRequest(new { message = "New passwords must contain at least 6 characters." });
        var email = updated.Email.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.EmployeeId != id && u.Email.ToLower() == email) ||
            await _context.Employees.AnyAsync(e => e.Id != id && e.Email.ToLower() == email))
            return Conflict(new { message = "This email is already used by another employee or account." });
        var statusChanged = existing.Status != updated.Status;
        CopyDetails(existing, updated);
        if (!string.IsNullOrWhiteSpace(updated.AadhaarNumber))
        {
            var protectedValue = _aadhaarProtector.Protect(updated.AadhaarNumber.Trim());
            var enrollment = await _context.EmployeeAadhaarVerifications.FindAsync(id);
            if (enrollment == null)
            {
                _context.EmployeeAadhaarVerifications.Add(new EmployeeAadhaarVerification
                {
                    EmployeeId = id,
                    EncryptedAadhaar = protectedValue.EncryptedAadhaar,
                    AadhaarNonce = protectedValue.AadhaarNonce,
                    AadhaarTag = protectedValue.AadhaarTag,
                    PreEnrolledLastFourHash = protectedValue.LastFourHash
                });
            }
            else
            {
                enrollment.EncryptedAadhaar = protectedValue.EncryptedAadhaar;
                enrollment.AadhaarNonce = protectedValue.AadhaarNonce;
                enrollment.AadhaarTag = protectedValue.AadhaarTag;
                enrollment.PreEnrolledLastFourHash = protectedValue.LastFourHash;
                enrollment.IsVerified = false;
                enrollment.VerifiedAt = null;
                enrollment.FailedAttempts = 0;
                enrollment.IsLocked = false;
            }
        }
        var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == id);
        if (user == null && !string.IsNullOrEmpty(updated.Password))
        {
            user = new User { Role = "Employee", Employee = existing, IsActive = existing.Status == "Active" };
            _context.Users.Add(user);
        }
        if (user != null)
        {
            var invalidateSession = user.Email != email ||
                !string.IsNullOrEmpty(updated.Password) ||
                (user.Role == "Employee" && statusChanged);
            user.FirstName = existing.FirstName;
            user.LastName = existing.LastName;
            user.Email = email;
            if (user.Role == "Employee" && statusChanged) user.IsActive = existing.Status == "Active";
            user.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(updated.Password))
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, updated.Password);
            if (invalidateSession)
            {
                _context.DevicePushTokens.RemoveRange(await _context.DevicePushTokens.Where(d => d.UserId == user.Id).ToListAsync());
                user.SecurityVersion++;
                user.RefreshTokenHash = null;
                user.RefreshTokenExpiry = null;
            }
        }
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Conflict(new { message = "This email is already used by a login account." }); }
        return Ok(existing);
    }

    private static bool ValidOptionalAadhaarNumber(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (value.Trim().Length == 12 && value.Trim().All(c => c is >= '0' and <= '9'));

    // Remove only the attachment; do not overwrite employee profile fields.
    [Authorize(Roles = "Manager")]
    [HttpDelete("{id}/resume")]
    public async Task<IActionResult> RemoveResume(int id, CancellationToken cancellationToken)
    {
        var employee = await _context.Employees.FindAsync(new object[] { id }, cancellationToken);
        if (employee == null)
            return NotFound(new { message = "Employee not found." });

        employee.ResumeUrl = null;
        employee.ResumeFileName = null;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // 5. DELETE: /api/employees/{id}
    [Authorize(Roles = "Manager")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _context.Employees.FindAsync(id);
        if (existing == null)
            return NotFound(new { message = $"Employee with ID {id} not found." });

        if (await _context.Users.AnyAsync(u => u.EmployeeId == id) || await _context.Attendance.AnyAsync(a => a.EmployeeId == id))
            return Conflict(new { message = "This employee has an account or attendance history. Set the employee inactive instead." });

        _context.Employees.Remove(existing);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
