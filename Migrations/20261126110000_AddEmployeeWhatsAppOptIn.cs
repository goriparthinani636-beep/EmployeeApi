using EmployeeApi.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeApi.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126110000_AddEmployeeWhatsAppOptIn")]
public partial class AddEmployeeWhatsAppOptIn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "WhatsAppOptIn",
            table: "Employees",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WhatsAppOptIn", table: "Employees");
    }
}
