using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Roles = "Manager")]
public class AccountsController(AppDbContext db) : ControllerBase
{
    public class AccountRequest
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = "Employee";
        public int? EmployeeId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.Users.OrderBy(u => u.Id)
        .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Role, u.EmployeeId, u.IsActive }).ToListAsync());

    [HttpPost]
    public Task<IActionResult> Create(AccountRequest request) => Save(null, request);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, AccountRequest request) => Save(id, request);

    private async Task<IActionResult> Save(int? id, AccountRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || request.Role is not ("Manager" or "Employee"))
            return BadRequest(new { message = "A valid email and Manager or Employee role are required." });
        if ((id == null || request.Password.Length > 0) && request.Password.Length < 6)
            return BadRequest(new { message = "New passwords must contain at least 6 characters." });
        if (request.Role == "Employee" && request.EmployeeId == null)
            return BadRequest(new { message = "Link an employee account to an employee record." });
        if (request.EmployeeId.HasValue && !await db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
            return BadRequest(new { message = "Employee record does not exist." });
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email.ToLower() == email))
            return Conflict(new { message = "Email is already used by an account." });
        if (request.EmployeeId.HasValue && await db.Users.AnyAsync(u => u.Id != id && u.EmployeeId == request.EmployeeId))
            return Conflict(new { message = "This employee already has an account." });
        if (request.EmployeeId.HasValue && await db.Employees.AnyAsync(e => e.Id != request.EmployeeId && e.Email.ToLower() == email))
            return Conflict(new { message = "This email belongs to another employee profile." });
        var user = id.HasValue ? await db.Users.FindAsync(id.Value) : new User();
        if (user == null) return NotFound();
        if (user.Id == User.UserId() && (!request.IsActive || request.Role != "Manager"))
            return BadRequest(new { message = "You cannot disable or demote your own manager account." });
        user.Email = email;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        if (request.EmployeeId.HasValue)
        {
            var employee = (await db.Employees.FindAsync(request.EmployeeId.Value))!;
            employee.Email = email;
            if (string.IsNullOrWhiteSpace(user.FirstName)) user.FirstName = employee.FirstName;
            if (string.IsNullOrWhiteSpace(user.LastName)) user.LastName = employee.LastName;
        }
        user.Role = request.Role;
        user.EmployeeId = request.EmployeeId;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        if (request.Password.Length > 0)
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, request.Password);
        user.SecurityVersion++;
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiry = null;
        if (id == null) db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { message = "Email or employee link is already in use." }); }
        return Ok(new { user.Id, user.Email, user.Role, user.EmployeeId, user.IsActive });
    }
}
