using MailManager.Data.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MailManager.Data.Migrations;

[DbContext(typeof(MailDbContext))]
[Migration("20261117000200_RegistrationNames")]
public sealed class RegistrationNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FirstName", table: "Tasks", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "LastName", table: "Tasks", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE Tasks DROP COLUMN LastName;");
        migrationBuilder.Sql("ALTER TABLE Tasks DROP COLUMN FirstName;");
    }
}
