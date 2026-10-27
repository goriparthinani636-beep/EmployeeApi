using Microsoft.EntityFrameworkCore;
using EmployeeApi.Models;

namespace EmployeeApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Attendance> Attendance { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.EmployeeId).IsUnique();
        modelBuilder.Entity<User>().HasOne(u => u.Employee).WithMany()
            .HasForeignKey(u => u.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Attendance>().HasIndex(a => new { a.EmployeeId, a.WorkDate }).IsUnique();
        modelBuilder.Entity<Attendance>().HasOne(a => a.Employee).WithMany()
            .HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
    public DbSet<Employee> Employees { get; set; }

    public DbSet<TaskItem> Tasks { get; set; }

    public DbSet<FileUpload> FileUploads { get; set; }
        = null!;
}
