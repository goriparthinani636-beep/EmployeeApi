using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using EmployeeApi.Models;

namespace EmployeeApi.Data;

public static class AccessScope
{
    public static async Task ValidateToken(Microsoft.AspNetCore.Authentication.JwtBearer.TokenValidatedContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var id = context.Principal?.UserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null || !user.IsActive ||
            user.Role is not ("Manager" or "Employee") ||
            (user.Role == "Employee" && user.EmployeeId == null) ||
            context.Principal?.FindFirst("sv")?.Value != user.SecurityVersion.ToString())
        {
            context.Fail("Please sign in again.");
            return;
        }
        // Read current permissions from the database, including for existing tokens.
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        foreach (var claim in identity.FindAll(ClaimTypes.Role).Concat(identity.FindAll("employeeId")).ToList())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        if (user.EmployeeId.HasValue)
            identity.AddClaim(new Claim("employeeId", user.EmployeeId.Value.ToString()));
    }

    public static bool IsManager(this ClaimsPrincipal user) => user.IsInRole("Manager");
    public static int? EmployeeId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("employeeId")?.Value, out var id) ? id : null;
    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    public static IQueryable<Employee> VisibleEmployees(this AppDbContext db, ClaimsPrincipal user) =>
        db.Employees.Where(e => user.IsManager() || e.Id == user.EmployeeId());
    public static IQueryable<TaskItem> VisibleTasks(this AppDbContext db, ClaimsPrincipal user) =>
        db.Tasks.Where(t => user.IsManager() || t.EmployeeId == user.EmployeeId());
}
