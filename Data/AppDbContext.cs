
using Microsoft.EntityFrameworkCore;
using EmployeeApi.Models;

namespace EmployeeApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;

    public DbSet<Employee> Employees { get; set; } = null!;

    public DbSet<TaskItem> Tasks { get; set; } = null!;

    public DbSet<FileUpload> FileUploads { get; set; } = null!;

    public DbSet<Attendance> Attendances { get; set; } = null!;
}
