using System.Security.Cryptography;
using System.Text;
using MailManager.Core.Interfaces;
using MailManager.Core.Models;
using MailManager.Core.Services;
using MailManager.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MailManager.Tests;

[TestClass]
public sealed class WorkspaceTests
{
    private const string Secret = "Sample_for_tests_Ft1!";
    private string _folder = null!;
    private WorkspaceStore _store = null!;
    private readonly TestProtector _protector = new();

    [TestInitialize]
    public async Task OpenTemporaryDatabase()
    {
        _folder = Path.Combine(Path.GetTempPath(), "MailManagerTests-" + Guid.NewGuid().ToString("N"));
        _store = new WorkspaceStore(Path.Combine(_folder, "data.sqlite"), _protector);
        await _store.InitializeAsync();
    }

    [TestCleanup]
    public void RemoveTemporaryDatabase()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [TestMethod]
    public async Task SuccessCreatesOneAccountAndMovesOnlyEncryptedPassword()
    {
        var id = await _store.AddTaskAsync(new(MailProvider.Gmail, "example123", Secret), RegistrationMode.Single);
        var draft = await _store.ReadAsync();
        Assert.IsEmpty(draft.Accounts);
        var pending = draft.Tasks.Single();
        Assert.AreEqual(RegistrationStatus.Pending, pending.Status);
        Assert.AreEqual(Secret, _protector.Unprotect(pending.ProtectedPassword!));

        var outcome = new RegistrationOutcome(RegistrationStatus.Succeeded, "actual123@gmail.com", "Личное", PhoneCheck.Verified, true);
        await _store.FinishAsync(id, outcome);
        await Assert.ThrowsExactlyAsync<InputException>(() => _store.FinishAsync(id, outcome));

        var result = await _store.ReadAsync();
        var account = result.Accounts.Single();
        Assert.AreEqual("actual123@gmail.com", account.Email);
        Assert.AreEqual(PhoneCheck.Verified, account.PhoneCheck);
        Assert.AreEqual(Secret, _protector.Unprotect(account.ProtectedPassword!));
        Assert.IsNull(result.Tasks.Single().ProtectedPassword);
        Assert.AreEqual(HistoryAction.Registered, result.History[0].Action);
        Assert.IsFalse(result.History.Any(x => x.Subject.Contains(Secret, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RejectedOutcomeLeavesTaskOpenAndFailureCreatesNoAccount()
    {
        var id = await _store.AddTaskAsync(new(MailProvider.Yandex, "maria24", Secret), RegistrationMode.Queue);

        await Assert.ThrowsExactlyAsync<InputException>(() => _store.FinishAsync(id,
            new(RegistrationStatus.Succeeded, "maria24@ya.ru", "", PhoneCheck.NotCompleted, false)));
        Assert.AreEqual(RegistrationStatus.Pending, (await _store.ReadAsync()).Tasks.Single().Status);

        await _store.FinishAsync(id, new(RegistrationStatus.Failed, "", "Отклонено сервисом", PhoneCheck.NotCompleted, false));
        var data = await _store.ReadAsync();
        Assert.IsEmpty(data.Accounts);
        Assert.AreEqual(RegistrationStatus.Failed, data.Tasks.Single().Status);
        Assert.AreEqual(PhoneCheck.NotCompleted, data.Tasks.Single().PhoneCheck);
        Assert.IsNull(data.Tasks.Single().ProtectedPassword);
    }

    [TestMethod]
    public async Task DuplicateImportIsAtomicAndGeneratedRowsRespectSettings()
    {
        await _store.SaveSettingsAsync(new AppSettings { LoginLength = 8, PasswordLength = 24 });
        await Assert.ThrowsExactlyAsync<InputException>(() => _store.ImportAsync(
            [new(MailProvider.Gmail, "example123"), new(MailProvider.Gmail, "exa.mple123")]));
        Assert.IsEmpty((await _store.ReadAsync()).Tasks);

        var count = await _store.ImportAsync([new(MailProvider.Gmail, "example123"), new(MailProvider.Yandex, "")]);
        var data = await _store.ReadAsync();
        Assert.AreEqual(2, count);
        Assert.AreEqual(RegistrationMode.Queue, data.Tasks[0].Mode);
        Assert.HasCount(8, data.Tasks[1].Login);
        Assert.HasCount(24, _protector.Unprotect(data.Tasks[1].ProtectedPassword!));
        Assert.AreEqual(HistoryAction.TasksImported, data.History[0].Action);
    }

    [TestMethod]
    public async Task AccountEditsRejectAliasesAndDeletionIsRecorded()
    {
        var id = await _store.SaveAccountAsync(null,
            new("first.account@gmail.com", MailProvider.Gmail, RegistrationStatus.Succeeded, PhoneCheck.Unknown, ""));
        await Assert.ThrowsExactlyAsync<InputException>(() => _store.SaveAccountAsync(null,
            new("firstaccount@googlemail.com", MailProvider.Gmail, RegistrationStatus.Succeeded, PhoneCheck.Unknown, "")));

        await _store.SaveAccountAsync(id, new("another@mail.ru", MailProvider.MailRu,
            RegistrationStatus.Succeeded, PhoneCheck.Unknown, "Изменено"));
        var account = (await _store.ReadAsync()).Accounts.Single();
        Assert.AreEqual("another@mail.ru", account.Email);
        Assert.AreEqual("Изменено", account.Note);

        await _store.DeleteAccountAsync(id);
        var data = await _store.ReadAsync();
        Assert.IsEmpty(data.Accounts);
        Assert.AreEqual(HistoryAction.AccountDeleted, data.History[0].Action);
    }

    [TestMethod]
    public async Task SnapshotRestoresTasksSettingsAndHistoryWithoutChangingSource()
    {
        var backup = Path.Combine(_folder, "backup.sqlite");
        await _store.SaveSettingsAsync(new AppSettings { LoginLength = 18, ClipboardSeconds = 45 });
        var id = await _store.AddTaskAsync(new(MailProvider.MailRu, "example777", Secret), RegistrationMode.Queue);
        await _store.CreateBackupAsync(backup);
        var original = SHA256.HashData(await File.ReadAllBytesAsync(backup));

        await _store.FinishAsync(id, new(RegistrationStatus.Succeeded, "example777@mail.ru", "", PhoneCheck.Unknown, true));
        await _store.SaveSettingsAsync(new AppSettings());
        await _store.RestoreBackupAsync(backup);

        var data = await _store.ReadAsync();
        Assert.IsEmpty(data.Accounts);
        Assert.AreEqual(id, data.Tasks.Single().Id);
        Assert.AreEqual(RegistrationStatus.Pending, data.Tasks.Single().Status);
        Assert.AreEqual(Secret, _protector.Unprotect(data.Tasks.Single().ProtectedPassword!));
        Assert.AreEqual(45, data.Settings.ClipboardSeconds);
        Assert.AreEqual(HistoryAction.BackupRestored, data.History[0].Action);
        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(backup)));
    }

    [TestMethod]
    public async Task DamagedBackupDoesNotReplaceData()
    {
        var damaged = Path.Combine(_folder, "damaged.sqlite");
        await File.WriteAllTextAsync(damaged, "This is not a database.");
        await _store.AddTaskAsync(new(MailProvider.Gmail, "example888", Secret), RegistrationMode.Queue);
        await Assert.ThrowsExactlyAsync<InputException>(() => _store.RestoreBackupAsync(damaged));
        Assert.AreEqual("example888", (await _store.ReadAsync()).Tasks.Single().Login);
    }

    [TestMethod]
    public async Task RestoreRejectsSnapshotWithoutExpectedSettingsRow()
    {
        var backup = Path.Combine(_folder, "other.sqlite");
        await _store.CreateBackupAsync(backup);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backup,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Settings SET Id = 2";
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsExactlyAsync<InputException>(() => _store.RestoreBackupAsync(backup));
        Assert.AreEqual(1, (await _store.ReadAsync()).Settings.Id);
    }

    [TestMethod]
    public async Task BackupCannotOverwriteLiveDatabaseOrSidecars()
    {
        var database = Path.Combine(_folder, "data.sqlite");
        await Assert.ThrowsExactlyAsync<InputException>(() => _store.CreateBackupAsync(database));
        await Assert.ThrowsExactlyAsync<InputException>(() => _store.CreateBackupAsync(database + "-wal"));
        Assert.AreEqual(1, (await _store.ReadAsync()).Settings.Id);
    }

    [TestMethod]
    public async Task ConcurrentDuplicateTaskHasOneWinner()
    {
        var first = Add();
        var second = Add();
        var results = await Task.WhenAll(first, second);
        Assert.AreEqual(1, results.Count(x => x));
        Assert.HasCount(1, (await _store.ReadAsync()).Tasks);

        async Task<bool> Add()
        {
            try
            {
                await _store.AddTaskAsync(new(MailProvider.Gmail, "duplicate123", Secret), RegistrationMode.Queue);
                return true;
            }
            catch (InputException)
            {
                return false;
            }
        }
    }

    [TestMethod]
    public void WindowsDpapiRoundTripsWithoutStoringPlaintext()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Тест DPAPI выполняется только в Windows.");
            return;
        }

        ISecretProtector protector = new WindowsSecretProtector();
        var ciphertext = protector.Protect(Secret);
        Assert.AreEqual(-1, ciphertext.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Secret)));
        Assert.AreEqual(Secret, protector.Unprotect(ciphertext));
        ciphertext[^1] ^= 1;
        Assert.ThrowsExactly<CryptographicException>(() => protector.Unprotect(ciphertext));
    }

    private sealed class TestProtector : ISecretProtector
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public byte[] Protect(string value)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            return [.. aes.IV, .. aes.EncryptCbc(Encoding.UTF8.GetBytes(value), aes.IV)];
        }

        public string Unprotect(byte[] value)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            return Encoding.UTF8.GetString(aes.DecryptCbc(value.AsSpan(16), value.AsSpan(0, 16)));
        }
    }
}
