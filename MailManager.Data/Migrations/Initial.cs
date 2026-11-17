using MailManager.Data.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MailManager.Data.Migrations;

[DbContext(typeof(MailDbContext))]
[Migration(Id)]
public sealed class Initial : Migration
{
    public const string Id = "20261117000100_Initial";
    internal const int ApplicationId = 0x4D4D4752;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"PRAGMA application_id={ApplicationId};");
        migrationBuilder.Sql("""
            CREATE TABLE Accounts (
                Id INTEGER NOT NULL CONSTRAINT PK_Accounts PRIMARY KEY AUTOINCREMENT,
                Email TEXT NOT NULL,
                Provider INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                Status INTEGER NOT NULL,
                PhoneCheck INTEGER NOT NULL,
                Note TEXT NOT NULL,
                ProtectedPassword BLOB
            );
            CREATE UNIQUE INDEX IX_Accounts_Email ON Accounts (Email);

            CREATE TABLE Tasks (
                Id INTEGER NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY AUTOINCREMENT,
                Provider INTEGER NOT NULL,
                Mode INTEGER NOT NULL,
                Login TEXT NOT NULL,
                Status INTEGER NOT NULL,
                PhoneCheck INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                FinishedUtc TEXT,
                Note TEXT NOT NULL,
                ProtectedPassword BLOB
            );
            CREATE INDEX IX_Tasks_Status_Provider ON Tasks (Status, Provider);

            CREATE TABLE History (
                Id INTEGER NOT NULL CONSTRAINT PK_History PRIMARY KEY AUTOINCREMENT,
                OccurredUtc TEXT NOT NULL,
                Action INTEGER NOT NULL,
                Subject TEXT NOT NULL
            );
            CREATE INDEX IX_History_OccurredUtc ON History (OccurredUtc);

            CREATE TABLE Settings (
                Id INTEGER NOT NULL CONSTRAINT PK_Settings PRIMARY KEY,
                LoginLength INTEGER NOT NULL,
                PasswordLength INTEGER NOT NULL,
                ClipboardSeconds INTEGER NOT NULL
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("History");
        migrationBuilder.DropTable("Tasks");
        migrationBuilder.DropTable("Accounts");
        migrationBuilder.DropTable("Settings");
        migrationBuilder.Sql("PRAGMA application_id=0;");
    }
}
