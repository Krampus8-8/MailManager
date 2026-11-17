namespace MailManager.Core.Models;

public enum MailProvider { Gmail, MailRu, Yandex }
public enum RegistrationStatus { Pending, Succeeded, Failed }
public enum RegistrationMode { Single, Queue }
public enum PhoneCheck { Unknown, NotRequested, Verified, NotCompleted }
public enum HistoryAction { TaskAdded, TasksImported, Registered, RegistrationFailed, AccountAdded, AccountEdited, AccountDeleted, BackupCreated, BackupRestored, SettingsChanged }
public enum LoginPattern { Name, Numeric }

public sealed class EmailAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public MailProvider Provider { get; set; }
    public DateTime CreatedUtc { get; set; }
    public RegistrationStatus Status { get; set; }
    public PhoneCheck PhoneCheck { get; set; }
    public string Note { get; set; } = "";
    public byte[]? ProtectedPassword { get; set; }
}

public sealed class RegistrationTask
{
    public int Id { get; set; }
    public MailProvider Provider { get; set; }
    public RegistrationMode Mode { get; set; }
    public string Login { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public RegistrationStatus Status { get; set; }
    public PhoneCheck PhoneCheck { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string Note { get; set; } = "";
    public byte[]? ProtectedPassword { get; set; }
}

public sealed class HistoryEntry
{
    public int Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public HistoryAction Action { get; set; }
    public string Subject { get; set; } = "";
}

public sealed class AppSettings
{
    public int Id { get; set; } = 1;
    public int LoginLength { get; set; } = 12;
    public int PasswordLength { get; set; } = 20;
    public int ClipboardSeconds { get; set; } = 30;
}

public sealed record ProviderInfo(MailProvider Id, string Name, string Domain, Uri Signup, string[] Domains);
public sealed record RegistrationDraft(MailProvider Provider, string Login, string Password, string? FirstName = null, string? LastName = null);
public sealed record RegistrationOutcome(RegistrationStatus Status, string Email, string Note, PhoneCheck PhoneCheck, bool KeepPassword,
    string? ReplacementPassword = null);
public sealed record RegistrationName(string FirstName, string LastName);
public sealed record AccountEdit(string Email, MailProvider Provider, RegistrationStatus Status, PhoneCheck PhoneCheck, string Note);
public sealed record ImportRow(MailProvider Provider, string Login);
public sealed record PasswordOptions(int Length, bool Upper = true, bool Lower = true, bool Digits = true, bool Symbols = true);
public sealed record WorkspaceData(IReadOnlyList<EmailAccount> Accounts, IReadOnlyList<RegistrationTask> Tasks,
    IReadOnlyList<HistoryEntry> History, AppSettings Settings);
