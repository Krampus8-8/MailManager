using MailManager.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MailManager.Data.Database;

internal sealed class MailDbContext(string path) : DbContext
{
    internal DbSet<EmailAccount> Accounts => Set<EmailAccount>();
    internal DbSet<RegistrationTask> Tasks => Set<RegistrationTask>();
    internal DbSet<HistoryEntry> History => Set<HistoryEntry>();
    internal DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseSqlite(ConnectionString(path));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<EmailAccount>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasIndex(x => x.Email).IsUnique();
        });
        model.Entity<RegistrationTask>(entity =>
        {
            entity.ToTable("Tasks");
            entity.HasIndex(x => new { x.Status, x.Provider });
        });
        model.Entity<HistoryEntry>().ToTable("History").HasIndex(x => x.OccurredUtc);
        model.Entity<AppSettings>().ToTable("Settings").Property(x => x.Id).ValueGeneratedNever();
    }

    internal static string ConnectionString(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
}
