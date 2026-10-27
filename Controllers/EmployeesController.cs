using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace EmployeeApi.Controllers;

[Authorize(Roles = "Manager,Employee")]
[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    public class EmployeeWriteRequest : Employee
    {
        public string? Password { get; set; }
    }

    private static void CopyDetails(Employee target, Employee source)
    {
        target.FirstName = source.FirstName.Trim();
        target.LastName = source.LastName.Trim();
        target.Email = source.Email.Trim().ToLowerInvariant();
        target.Phone = source.Phone;
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

    public EmployeesController(AppDbContext context)
    {
        _context = context;
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

    [Authorize(Roles = "Employee")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        var employee = await _context.VisibleEmployees(User).SingleOrDefaultAsync();
        return employee == null ? NotFound(new { message = "Your login is not linked to an employee profile." }) : Ok(employee);
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
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email) ||
            await _context.Employees.AnyAsync(e => e.Email.ToLower() == email))
            return Conflict(new { message = "This email is already used. Choose a different employee login email." });
        var employee = new Employee();
        CopyDetails(employee, request);
        var user = new User { FirstName = employee.FirstName, LastName = employee.LastName,
            Email = employee.Email, Role = "Employee", Employee = employee,
            IsActive = employee.Status == "Active" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, request.Password);
        _context.Employees.Add(employee);
        _context.Users.Add(user);
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
        if (!string.IsNullOrEmpty(updated.Password) && updated.Password.Length < 6)
            return BadRequest(new { message = "New passwords must contain at least 6 characters." });
        var email = updated.Email.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.EmployeeId != id && u.Email.ToLower() == email) ||
            await _context.Employees.AnyAsync(e => e.Id != id && e.Email.ToLower() == email))
            return Conflict(new { message = "This email is already used by another employee or account." });
        var statusChanged = existing.Status != updated.Status;
        CopyDetails(existing, updated);
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
