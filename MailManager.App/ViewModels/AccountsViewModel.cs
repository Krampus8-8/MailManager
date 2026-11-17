using System.Windows.Input;
using MailManager.App.Commands;
using MailManager.Core.Models;

namespace MailManager.App.ViewModels;

public sealed class AccountsViewModel : PageViewModel
{
    private IReadOnlyList<AccountRow> _allAccounts = [];
    private IReadOnlyList<AccountRow> _rows = [];
    private AccountRow? _selected;
    private string _search = "";
    private int _providerFilter = -1;
    private int _statusFilter = -1;
    private string _email = "";
    private string _note = "";
    private MailProvider _provider;
    private RegistrationStatus _status = RegistrationStatus.Succeeded;
    private PhoneCheck _phone;

    internal AccountsViewModel(ShellViewModel shell)
        : base("Мои аккаунты", "Локальные записи, поиск и управление")
    {
        NewCommand = new UiCommand(_ => Clear());
        SaveCommand = shell.Action(() => shell.RunAsync(async () =>
        {
            var id = await shell.Store.SaveAccountAsync(Selected?.Id, new(Email, Provider, Status, Phone, Note));
            await shell.ReloadAsync();
            Selected = Rows.FirstOrDefault(x => x.Id == id);
        }, "Запись сохранена."));
        DeleteCommand = shell.Action(() => shell.RunAsync(async () =>
        {
            var target = Selected!;
            if (!shell.Desktop.Confirm($"Удалить локальную запись {target.Email}?", "Удаление аккаунта"))
                return;

            await shell.Store.DeleteAccountAsync(target.Id);
            await shell.ReloadAsync();
            shell.Report("Запись удалена.");
        }), () => Selected is not null);
        CopyPasswordCommand = shell.Action(() => shell.Run(() => shell.CopySecret(Selected?.Record.ProtectedPassword)),
            () => Selected?.Record.ProtectedPassword is not null);
    }

    public string Search { get => _search; set { if (Set(ref _search, value)) ApplyFilters(); } }
    public int ProviderFilter { get => _providerFilter; set { if (Set(ref _providerFilter, value)) ApplyFilters(); } }
    public int StatusFilter { get => _statusFilter; set { if (Set(ref _statusFilter, value)) ApplyFilters(); } }
    public IReadOnlyList<AccountRow> Rows { get => _rows; private set => Set(ref _rows, value); }
    public bool IsEmpty => Rows.Count == 0;
    public AccountRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
                return;

            Email = value?.Email ?? "";
            Note = value?.Note ?? "";
            Provider = value?.Record.Provider ?? MailProvider.Gmail;
            Status = value?.Status ?? RegistrationStatus.Succeeded;
            Phone = value?.Record.PhoneCheck ?? PhoneCheck.Unknown;
            Notify(nameof(EditTitle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string EditTitle => Selected is null ? "Новый аккаунт" : "Редактирование аккаунта";
    public string Email { get => _email; set => Set(ref _email, value); }
    public string Note { get => _note; set => Set(ref _note, value); }
    public MailProvider Provider { get => _provider; set => Set(ref _provider, value); }
    public RegistrationStatus Status { get => _status; set => Set(ref _status, value); }
    public PhoneCheck Phone { get => _phone; set => Set(ref _phone, value); }

    public IReadOnlyList<Choice<MailProvider>> Providers => Labels.Providers;
    public IReadOnlyList<Choice<RegistrationStatus>> Statuses => Labels.Statuses;
    public IReadOnlyList<Choice<PhoneCheck>> PhoneChecks => Labels.PhoneChecks;

    public IReadOnlyList<Choice<int>> ProviderFilters { get; } =
        [new(-1, "Все сервисы"), .. Labels.Providers.Select(x => new Choice<int>((int)x.Value, x.Label))];

    public IReadOnlyList<Choice<int>> StatusFilters { get; } =
        [new(-1, "Все статусы"), .. Labels.Statuses.Select(x => new Choice<int>((int)x.Value, x.Label))];

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CopyPasswordCommand { get; }

    internal override void Update(WorkspaceData data)
    {
        _allAccounts = data.Accounts.Select(x => new AccountRow(x)).ToArray();
        ApplyFilters();
    }

    internal override void ResetAfterRestore() => Clear();

    private void ApplyFilters()
    {
        var id = Selected?.Id;
        var term = Search.Trim();
        Rows = _allAccounts.Where(x =>
            (ProviderFilter < 0 || (int)x.Record.Provider == ProviderFilter) &&
            (StatusFilter < 0 || (int)x.Status == StatusFilter) &&
            (x.Email.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Note.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();

        if (id.HasValue)
            Selected = Rows.FirstOrDefault(x => x.Id == id);
        Notify(nameof(IsEmpty));
    }

    private void Clear()
    {
        Selected = null;
        Email = "";
        Note = "";
        Provider = MailProvider.Gmail;
        Status = RegistrationStatus.Succeeded;
        Phone = PhoneCheck.Unknown;
    }
}

public sealed class AccountRow
{
    internal AccountRow(EmailAccount record) => Record = record;
    internal EmailAccount Record { get; }
    public int Id => Record.Id;
    public string Email => Record.Email;
    public string Provider => Labels.Provider(Record.Provider);
    public RegistrationStatus Status => Record.Status;
    public DateTime Created => Labels.Local(Record.CreatedUtc);
    public string Note => Record.Note;
}
