using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using EmployeeApi.Data;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EmployeeApi.Controllers;

public record ApplyLeave(DateOnly StartDate, DateOnly EndDate,
    [Required, MaxLength(30)] string LeaveType, [Required, MaxLength(1000)] string Reason);
public record ReviewLeave([Required] string Status, [MaxLength(500)] string? Comment);

[ApiController]
[Route("api/leaves")]
[Authorize(Roles = "Manager,Employee")]
public class LeavesController(AppDbContext db, LeavePushService notifications) : ControllerBase
{
    private IQueryable<LeaveRequest> Visible() => db.LeaveRequests.AsNoTracking()
        .Where(l => User.IsManager() || l.EmployeeId == User.EmployeeId());

    private static object LeaveResponse(LeaveRequest leave, string? employeeName, string? warning = null) => new {
        leave.Id, leave.EmployeeId, employeeName, leave.StartDate, leave.EndDate,
        days = leave.EndDate.DayNumber - leave.StartDate.DayNumber + 1,
        leave.LeaveType, leave.Reason, leave.Status, leave.ManagerComment,
        leave.ReviewedAt, leave.CreatedAt, notificationWarning = warning
    };

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var records = await Visible().Include(l => l.Employee).OrderByDescending(l => l.Id).ToListAsync();
        return Ok(records.Select(l => LeaveResponse(l, l.Employee!.FirstName + " " + l.Employee.LastName)));
    }

    [HttpPost]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> Apply(ApplyLeave request)
    {
        var employeeId = User.EmployeeId();
        if (employeeId == null) return BadRequest(new { message = "Your login is not linked to an employee." });
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));
        if (request.StartDate < today || request.EndDate < request.StartDate ||
            request.EndDate.DayNumber - request.StartDate.DayNumber > 365)
            return BadRequest(new { message = "Choose today or a future start date and an end date within 366 days." });
        if (request.LeaveType is not ("Casual" or "Sick" or "Annual" or "Unpaid") || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Choose a leave type and enter a reason." });
        try
        {
            // Serialize applications for this employee, including overlapping requests.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            if (!await db.Employees.AnyAsync(e => e.Id == employeeId && e.Status.ToLower() == "active"))
                return BadRequest(new { message = "Your employee profile is inactive." });
            if (await db.LeaveRequests.AnyAsync(l => l.EmployeeId == employeeId && l.Status != "Rejected" &&
                l.StartDate <= request.EndDate && l.EndDate >= request.StartDate))
                return Conflict(new { message = "You already have a pending or approved leave for these dates." });
            var leave = new LeaveRequest { EmployeeId = employeeId.Value, StartDate = request.StartDate,
                EndDate = request.EndDate, LeaveType = request.LeaveType, Reason = request.Reason.Trim() };
            db.LeaveRequests.Add(leave);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return StatusCode(201, LeaveResponse(leave, null));
        }
        catch (Exception ex) when (ex is PostgresException { SqlState: "40001" } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: "40001" } })
        {
            return Conflict(new { message = "Another leave request was saved at the same time. Refresh and try again." });
        }
    }

    [HttpPatch("{id:int}/decision")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Review(int id, ReviewLeave request)
    {
        if (request.Status is not ("Approved" or "Rejected"))
            return BadRequest(new { message = "Choose Approved or Rejected." });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId)) return Unauthorized();
        var now = DateTime.UtcNow;
        // Only one manager can decide a pending request; completed decisions cannot be overwritten.
        var changed = await db.LeaveRequests.Where(l => l.Id == id && l.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.Status, request.Status)
                .SetProperty(l => l.ManagerComment, request.Comment == null ? null : request.Comment.Trim())
                .SetProperty(l => l.ReviewedByUserId, reviewerId).SetProperty(l => l.ReviewedAt, now));
        if (changed == 0)
            return await db.LeaveRequests.AnyAsync(l => l.Id == id)
                ? Conflict(new { message = "This leave request has already been reviewed." }) : NotFound();
        var leave = await db.LeaveRequests.AsNoTracking().Include(l => l.Employee).SingleAsync(l => l.Id == id);
        var warning = await notifications.NotifyDecision(leave);
        return Ok(LeaveResponse(leave, leave.Employee!.FirstName + " " + leave.Employee.LastName, warning));
    }
}

