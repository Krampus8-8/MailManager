using MailManager.Core.Models;

namespace MailManager.Core.Interfaces;

public interface ISecretProtector
{
    byte[] Protect(string value);
    string Unprotect(byte[] value);
}

public interface IMailStore
{
    Task InitializeAsync();
    Task<WorkspaceData> ReadAsync();
    Task<int> AddTaskAsync(RegistrationDraft draft, RegistrationMode mode);
    Task<int> ImportAsync(IReadOnlyList<ImportRow> rows);
    Task FinishAsync(int taskId, RegistrationOutcome outcome);
    Task<int> SaveAccountAsync(int? id, AccountEdit edit);
    Task DeleteAccountAsync(int id);
    Task SaveSettingsAsync(AppSettings settings);
    Task CreateBackupAsync(string destination);
    Task RestoreBackupAsync(string source);
}
