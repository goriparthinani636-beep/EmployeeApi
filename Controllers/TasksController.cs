using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EmployeeApi.Data;
using EmployeeApi.Models;
using EmployeeApi.Services;

namespace EmployeeApi.Controllers;

[Authorize(Roles = "Manager,Employee")]
[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITaskPushService _notifications;
    private readonly IWhatsAppCloudApiService _whatsApp;

    public TasksController(AppDbContext context, ITaskPushService notifications, IWhatsAppCloudApiService whatsApp)
    {
        _context = context;
        _notifications = notifications;
        _whatsApp = whatsApp;
    }

    private static object TaskResponse(TaskItem task, string? warning, string whatsAppStatus) => new {
        task.Id, task.Title, task.Description, task.EmployeeId, task.Priority, task.Status,
        task.DueDate, task.CreatedAt, task.UpdatedAt, task.Employee, notificationWarning = warning,
        whatsAppNotification = whatsAppStatus
    };

    // 1. GET: /api/tasks
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await _context.VisibleTasks(User)
            .Include(t => t.Employee)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        return Ok(tasks);
    }

    // 2. GET: /api/tasks/{id}
    [Authorize(Roles = "Employee")]
    [HttpGet("/api/employees/me/tasks")]
    public async Task<IActionResult> GetMine() => Ok(await _context.VisibleTasks(User)
        .Include(t => t.Employee).OrderByDescending(t => t.Id).ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var task = await _context.VisibleTasks(User)
            .Include(t => t.Employee)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null)
            return NotFound(new { message = $"Task with ID {id} not found." });

        return Ok(task);
    }

    // 3. GET: /api/employees/{employeeId}/tasks
    [HttpGet("/api/employees/{employeeId}/tasks")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var tasks = await _context.VisibleTasks(User)
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        return Ok(tasks);
    }

    // 4. POST: /api/tasks
    [Authorize(Roles = "Manager")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskItem task)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.Title))
            return BadRequest(new { message = "Task title is required." });
        if (task.Priority is not ("Low" or "Medium" or "High") || task.Status is not ("Pending" or "InProgress" or "Completed"))
            return BadRequest(new { message = "Choose a valid task priority and status." });

        var employeeExists = await _context.Employees.AnyAsync(e => e.Id == task.EmployeeId);
        if (!employeeExists)
            return BadRequest(new { message = $"Employee with ID {task.EmployeeId} does not exist." });

        task.Id = 0;
        task.CreatedAt = DateTime.UtcNow;
        task.Employee = null; // Prevent EF Core from re-inserting navigation entity

        _context.Tasks.Add(task);
        await _context.SaveChangesAsync();

        var pushWarning = await _notifications.NotifyAssignment(task);
        var whatsAppResult = await _whatsApp.NotifyTaskAssigned(task.EmployeeId, task.Title);
        var warning = CombineWarnings(pushWarning, whatsAppResult.Warning);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, TaskResponse(task, warning, whatsAppResult.Status));
    }

    // 5. PUT: /api/tasks/{id}
    [Authorize(Roles = "Manager")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskItem updated)
    {
        if (string.IsNullOrWhiteSpace(updated.Title) || updated.Priority is not ("Low" or "Medium" or "High") ||
            updated.Status is not ("Pending" or "InProgress" or "Completed"))
            return BadRequest(new { message = "A task title, valid priority and valid status are required." });
        var existing = await _context.VisibleTasks(User).FirstOrDefaultAsync(t => t.Id == id);
        if (existing == null)
            return NotFound(new { message = $"Task with ID {id} not found." });

        var employeeExists = await _context.Employees.AnyAsync(e => e.Id == updated.EmployeeId);
        if (!employeeExists)
            return BadRequest(new { message = $"Employee with ID {updated.EmployeeId} does not exist." });

        var reassigned = existing.EmployeeId != updated.EmployeeId;
        existing.Title = updated.Title;
        existing.Description = updated.Description;
        existing.EmployeeId = updated.EmployeeId;
        existing.Priority = updated.Priority;
        existing.Status = updated.Status;
        existing.DueDate = updated.DueDate;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        var pushWarning = reassigned ? await _notifications.NotifyAssignment(existing) : null;
        var whatsAppResult = reassigned
            ? await _whatsApp.NotifyTaskAssigned(existing.EmployeeId, existing.Title)
            : new WhatsAppDeliveryResult("skipped");
        var warning = CombineWarnings(pushWarning, whatsAppResult.Warning);
        return Ok(TaskResponse(existing, warning, whatsAppResult.Status));
    }

    // 6. PATCH: /api/tasks/{id}/status
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateTaskStatusDto dto)
    {
        var existing = await _context.VisibleTasks(User).FirstOrDefaultAsync(t => t.Id == id);
        if (existing == null)
            return NotFound(new { message = $"Task with ID {id} not found." });

        if (dto.Status is not ("Pending" or "InProgress" or "Completed"))
            return BadRequest(new { message = "Invalid task status." });
        existing.Status = dto.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    // 7. DELETE: /api/tasks/{id}
    [Authorize(Roles = "Manager")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _context.VisibleTasks(User).FirstOrDefaultAsync(t => t.Id == id);
        if (existing == null)
            return NotFound(new { message = $"Task with ID {id} not found." });

        _context.Tasks.Remove(existing);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private static string? CombineWarnings(params string?[] warnings)
    {
        var messages = warnings.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct().ToArray();
        return messages.Length == 0 ? null : string.Join(" ", messages);
    }
}
