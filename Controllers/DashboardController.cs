using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EmployeeApi.Data;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalEmployees = await _context.Employees.CountAsync();
        
        // Matches your database "Status" column
        var activeEmployees = await _context.Employees
            .CountAsync(e => e.Status.ToLower() == "active");
            
        var inactiveEmployees = totalEmployees - activeEmployees;

        var departmentCounts = await _context.Employees
            .GroupBy(e => e.Department)
            .Select(g => new { department = g.Key, count = g.Count() })
            .ToListAsync();

        return Ok(new
        {
            totalEmployees,
            activeEmployees,
            inactiveEmployees,
            departments = departmentCounts
        });
    }
}