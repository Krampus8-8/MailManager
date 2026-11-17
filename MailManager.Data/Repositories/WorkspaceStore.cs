using MailManager.Core.Interfaces;
using MailManager.Core.Models;
using MailManager.Core.Services;
using MailManager.Data.Database;
using Microsoft.EntityFrameworkCore;

namespace MailManager.Data.Repositories;

public sealed partial class WorkspaceStore(string path, ISecretProtector protector) : IMailStore
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CredentialGenerator _generator = new();

    public Task InitializeAsync() => Run(async db =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await db.Database.MigrateAsync();
        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(new AppSettings());
            await db.SaveChangesAsync();
        }
    });

    public Task<WorkspaceData> ReadAsync() => Run(async db => new WorkspaceData(
        await db.Accounts.AsNoTracking().OrderByDescending(x => x.Id).ToListAsync(),
        await db.Tasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        await db.History.AsNoTracking().OrderByDescending(x => x.Id).Take(1000).ToListAsync(),
        await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1)));

    public Task<int> AddTaskAsync(RegistrationDraft draft, RegistrationMode mode) => Run(async db =>
    {
        var login = RegistrationRules.Login(draft.Login, draft.Provider);
        var used = await UsedLogins(db);
        if (used.Contains(Key(draft.Provider, login)))
            throw new InputException("Этот логин уже есть среди аккаунтов или активных задач.");
        RegistrationRules.ValidatePassword(draft.Password, draft.Provider);

        var record = NewTask(draft.Provider, login, draft.Password, mode);
        record.FirstName = RegistrationRules.ProfileName(draft.FirstName, "Имя");
        record.LastName = RegistrationRules.ProfileName(draft.LastName, "Фамилия");
        db.Tasks.Add(record);
        AddHistory(db, HistoryAction.TaskAdded, login + " · " + RegistrationRules.Provider(draft.Provider).Name);
        await db.SaveChangesAsync();
        return record.Id;
    });

    public Task<int> ImportAsync(IReadOnlyList<ImportRow> rows) => Run(async db =>
    {
        if (rows.Count is < 1 or > QueueImport.MaximumRows)
            throw new InputException($"Импортируйте от 1 до {QueueImport.MaximumRows} задач.");

        var settings = await db.Settings.SingleAsync(x => x.Id == 1);
        var used = await UsedLogins(db);
        // Резервируем заданные логины до генерации, чтобы порядок строк не менял результат проверки.
        foreach (var row in rows.Where(x => x.Login.Length > 0))
        {
            var login = RegistrationRules.Login(row.Login, row.Provider);
            if (!used.Add(Key(row.Provider, login)))
                throw new InputException($"Логин «{login}» повторяется или уже занят в локальных записях.");
        }

        foreach (var row in rows)
        {
            var login = row.Login.Length > 0 ? RegistrationRules.Login(row.Login, row.Provider)
                : _generator.Login(row.Provider, settings.LoginLength, LoginPattern.Name, value => used.Contains(Key(row.Provider, value)));
            used.Add(Key(row.Provider, login));
            db.Tasks.Add(NewTask(row.Provider, login, _generator.Password(new(settings.PasswordLength)), RegistrationMode.Queue));
        }

        AddHistory(db, HistoryAction.TasksImported, rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await db.SaveChangesAsync();
        return rows.Count;
    });

    public Task FinishAsync(int taskId, RegistrationOutcome outcome) => Run(async db =>
    {
        RegistrationRules.ValidateStatus(outcome.Status, outcome.PhoneCheck);
        if (outcome.Status == RegistrationStatus.Pending)
            throw new InputException("Укажите итог регистрации.");

        var record = await db.Tasks.FindAsync(taskId);
        if (record is null || record.Status != RegistrationStatus.Pending)
            throw new InputException("Эта задача уже закрыта или отсутствует.");

        var note = RegistrationRules.Note(outcome.Note);
        var now = DateTime.UtcNow;
        var subject = record.Login + " · " + RegistrationRules.Provider(record.Provider).Name;
        if (outcome.Status == RegistrationStatus.Succeeded)
        {
            var email = RegistrationRules.Email(outcome.Email, record.Provider);
            await RequireUniqueEmail(db, null, email, record.Provider);
            var password = outcome.KeepPassword ? record.ProtectedPassword : null;
            if (outcome.KeepPassword && outcome.ReplacementPassword is { Length: > 0 } replacement)
            {
                RegistrationRules.ValidatePassword(replacement, record.Provider);
                password = protector.Protect(replacement);
            }
            db.Accounts.Add(new EmailAccount
            {
                Email = email,
                Provider = record.Provider,
                CreatedUtc = now,
                Status = outcome.Status,
                PhoneCheck = outcome.PhoneCheck,
                Note = note,
                ProtectedPassword = password
            });
            subject = email;
        }

        record.Status = outcome.Status;
        record.PhoneCheck = outcome.PhoneCheck;
        record.Note = note;
        record.FinishedUtc = now;
        record.ProtectedPassword = null;
        AddHistory(db, outcome.Status == RegistrationStatus.Succeeded ? HistoryAction.Registered : HistoryAction.RegistrationFailed, subject);
        // EF сохраняет задачу, аккаунт и историю одной транзакцией.
        await db.SaveChangesAsync();
    });

    public Task<int> SaveAccountAsync(int? id, AccountEdit edit) => Run(async db =>
    {
        var email = RegistrationRules.Email(edit.Email, edit.Provider);
        var note = RegistrationRules.Note(edit.Note);
        RegistrationRules.ValidateStatus(edit.Status, edit.PhoneCheck);
        await RequireUniqueEmail(db, id, email, edit.Provider);

        var account = id is int number
            ? await db.Accounts.FindAsync(number) ?? throw new InputException("Аккаунт больше не существует.")
            : new EmailAccount { CreatedUtc = DateTime.UtcNow };

        if (id is null)
            db.Accounts.Add(account);
        if (account.Email != email || account.Provider != edit.Provider)
            account.ProtectedPassword = null;

        account.Email = email;
        account.Provider = edit.Provider;
        account.Status = edit.Status;
        account.PhoneCheck = edit.PhoneCheck;
        account.Note = note;
        AddHistory(db, id is null ? HistoryAction.AccountAdded : HistoryAction.AccountEdited, email);
        await db.SaveChangesAsync();
        return account.Id;
    });

    public Task DeleteAccountAsync(int id) => Run(async db =>
    {
        var record = await db.Accounts.FindAsync(id) ?? throw new InputException("Аккаунт больше не существует.");
        db.Accounts.Remove(record);
        AddHistory(db, HistoryAction.AccountDeleted, record.Email);
        await db.SaveChangesAsync();
    });

    public Task SaveSettingsAsync(AppSettings settings) => Run(async db =>
    {
        RegistrationRules.ValidateSettings(settings);
        var current = await db.Settings.SingleAsync(x => x.Id == 1);
        current.LoginLength = settings.LoginLength;
        current.PasswordLength = settings.PasswordLength;
        current.ClipboardSeconds = settings.ClipboardSeconds;
        AddHistory(db, HistoryAction.SettingsChanged);
        await db.SaveChangesAsync();
    });

    private RegistrationTask NewTask(MailProvider provider, string login, string password, RegistrationMode mode) =>
        new()
        {
            Provider = provider,
            Mode = mode,
            Login = login,
            ProtectedPassword = protector.Protect(password),
            CreatedUtc = DateTime.UtcNow
        };

    private static async Task RequireUniqueEmail(MailDbContext db, int? id, string email, MailProvider provider)
    {
        var addresses = await db.Accounts.Where(x => x.Provider == provider && x.Id != id)
            .Select(x => x.Email).ToListAsync();
        var key = Key(provider, LocalPart(email));
        if (addresses.Any(value => value == email ||
            (provider == MailProvider.Gmail && Key(provider, LocalPart(value)) == key)))
        {
            throw new InputException("Такой почтовый адрес уже сохранён.");
        }
    }

    private static async Task<HashSet<(MailProvider, string)>> UsedLogins(MailDbContext db)
    {
        var accounts = await db.Accounts.Select(x => new { x.Provider, x.Email }).ToListAsync();
        var pending = await db.Tasks.Where(x => x.Status == RegistrationStatus.Pending)
            .Select(x => new { x.Provider, x.Login }).ToListAsync();
        return accounts.Select(x => Key(x.Provider, LocalPart(x.Email)))
            .Concat(pending.Select(x => Key(x.Provider, x.Login))).ToHashSet();
    }

    private static (MailProvider, string) Key(MailProvider provider, string login) =>
        (provider, RegistrationRules.LoginKey(provider, login));

    private static string LocalPart(string email) => email.Split('@', 2)[0];

    private static void AddHistory(MailDbContext db, HistoryAction action, string subject = "") =>
        db.History.Add(new HistoryEntry { Action = action, Subject = subject, OccurredUtc = DateTime.UtcNow });

    private Task Run(Func<MailDbContext, Task> work) => Run(async db =>
    {
        await work(db);
        return true;
    });

    // SQLite выполняет операции синхронно даже через асинхронный API провайдера.
    private Task<T> Run<T>(Func<MailDbContext, Task<T>> work) => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new MailDbContext(_path);
            return await work(db);
        }
        finally
        {
            _gate.Release();
        }
    });
}
