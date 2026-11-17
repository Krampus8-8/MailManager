using MailManager.Core.Services;
using MailManager.Core.Models;
using MailManager.Data.Database;
using MailManager.Data.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MailManager.Data.Repositories;

public sealed partial class WorkspaceStore
{
    private const string InvalidBackup = "Файл повреждён или имеет неподдерживаемую схему Mail Manager.";

    public Task CreateBackupAsync(string destination) => Run(async db =>
    {
        destination = ExternalPath(destination);
        var staging = TemporaryPath(Path.GetDirectoryName(destination)!);
        try
        {
            CopyDatabase(_path, staging, SqliteOpenMode.ReadWriteCreate);
            await ValidateBackup(staging);
            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            File.Delete(staging);
        }

        AddHistory(db, HistoryAction.BackupCreated);
        await db.SaveChangesAsync();
    });

    public Task RestoreBackupAsync(string source) => Run(async db =>
    {
        source = ExternalPath(source);
        if (!File.Exists(source))
            throw new InputException("Файл резервной копии не найден.");

        var staging = TemporaryPath(Path.GetDirectoryName(_path)!);
        try
        {
            // Снимок исключает замену исходного файла между проверкой и восстановлением.
            try
            {
                CopyDatabase(source, staging, SqliteOpenMode.ReadWriteCreate);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
            {
                throw new InputException(InvalidBackup);
            }
            await ValidateBackup(staging);
            await using (var restored = new MailDbContext(staging))
            {
                AddHistory(restored, HistoryAction.BackupRestored);
                await restored.SaveChangesAsync();
            }

            // SQLite записывает снимок в целевую базу транзакционно.
            CopyDatabase(staging, _path, SqliteOpenMode.ReadWrite);
        }
        finally
        {
            File.Delete(staging);
        }
    });

    private string ExternalPath(string file)
    {
        var fullPath = Path.GetFullPath(file);
        string[] internalFiles = [_path, _path + "-wal", _path + "-shm", _path + "-journal"];
        if (internalFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            throw new InputException("Выберите отдельный файл резервной копии.");
        return fullPath;
    }

    private static string TemporaryPath(string directory) => Path.Combine(directory, $".mailmanager-{Guid.NewGuid():N}.tmp");

    private static void CopyDatabase(string source, string destination, SqliteOpenMode mode)
    {
        using var from = new SqliteConnection(MailDbContext.ConnectionString(source, SqliteOpenMode.ReadOnly));
        using var to = new SqliteConnection(MailDbContext.ConnectionString(destination, mode));
        from.Open();
        to.Open();
        from.BackupDatabase(to);
        if (mode == SqliteOpenMode.ReadWriteCreate)
        {
            using var command = to.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE";
            command.ExecuteScalar();
        }
    }

    private static async Task ValidateBackup(string file)
    {
        try
        {
            using var connection = new SqliteConnection(MailDbContext.ConnectionString(file, SqliteOpenMode.ReadOnly));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA trusted_schema=OFF";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA application_id";
            if (Convert.ToInt64(command.ExecuteScalar()) != Initial.ApplicationId)
                throw new InputException(InvalidBackup);
            command.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                throw new InputException(InvalidBackup);
            command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE type IN ('trigger', 'view') OR sql LIKE 'CREATE VIRTUAL TABLE%'";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw new InputException(InvalidBackup);
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
            using (var rows = command.ExecuteReader())
            {
                if (!rows.Read() || rows.GetString(0) != Initial.Id || rows.Read())
                    throw new InputException(InvalidBackup);
            }

            await using var probe = new MailDbContext(file);
            var settings = await probe.Settings.AsNoTracking().SingleAsync();
            if (settings.Id != 1)
                throw new InputException(InvalidBackup);
            RegistrationRules.ValidateSettings(settings);
            // Проверяем доступность всех сопоставленных столбцов, в том числе в пустых таблицах.
            await probe.Accounts.AsNoTracking().Take(1).ToListAsync();
            await probe.Tasks.AsNoTracking().Take(1).ToListAsync();
            await probe.History.AsNoTracking().Take(1).ToListAsync();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or InputException)
        {
            throw new InputException(InvalidBackup);
        }
    }
}
