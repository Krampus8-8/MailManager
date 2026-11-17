using System.IO;
using System.Text;
using System.Windows.Input;
using MailManager.App.Commands;
using MailManager.App.Services;
using MailManager.Core.Models;
using MailManager.Core.Services;

namespace MailManager.App.ViewModels;

public sealed class RegistrationViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly RegistrationMode _mode;
    private IReadOnlyList<TaskRow> _rows = [];
    private TaskRow? _selectedTask;
    private MailProvider _provider;
    private string _login = "";
    private string _password = "";
    private string _email = "";
    private string _note = "";
    private PhoneCheck _phone;
    private bool _keepPassword;

    internal RegistrationViewModel(ShellViewModel shell, RegistrationMode mode)
        : base(mode == RegistrationMode.Queue ? "Массовое создание" : "Создание почты",
            mode == RegistrationMode.Queue
                ? "Очередь задач для последовательной регистрации вручную"
                : "Подготовьте логин и пароль, пройдите регистрацию и запишите результат")
    {
        _shell = shell;
        _mode = mode;

        NewCommand = new UiCommand(_ => ClearForm());
        GenerateLoginCommand = shell.Action(() => shell.Run(() =>
            Login = shell.GenerateLogin(Provider, shell.Data.Settings.LoginLength, LoginPattern.Name, Login)));
        GeneratePasswordCommand = shell.Action(() => shell.Run(() =>
        {
            _password = shell.Generator.Password(new(shell.Data.Settings.PasswordLength));
            Notify(nameof(PasswordHint));
        }));
        CopyLoginCommand = shell.Action(() => shell.Run(() =>
            shell.Desktop.Copy(RegistrationRules.Login(Login, Provider)), "Логин скопирован."));
        CopyPasswordCommand = shell.Action(() => shell.Run(() =>
        {
            if (SelectedTask is not null)
                shell.CopySecret(SelectedTask.Record.ProtectedPassword);
            else if (_password.Length > 0)
                shell.CopySecret(_password);
            else
                throw new InputException("Сначала сгенерируйте пароль.");
        }));
        SaveTaskCommand = shell.Action(() => shell.RunAsync(async () =>
        {
            await SaveDraftAsync();
            if (IsQueue)
                ClearForm();
        }, "Задача добавлена."), () => IsDraft);
        OpenSignupCommand = shell.Action(() => shell.RunAsync(async () =>
        {
            if (IsDraft)
                await SaveDraftAsync();
            shell.Desktop.Open(RegistrationRules.Provider(Provider).Signup);
        }), () => IsDraft || IsPending);
        NextCommand = new UiCommand(_ => Next(), _ => _shell.CanInteract && PendingCount > 0);
        ImportCommand = shell.Action(() => shell.RunAsync(ImportAsync));
        SuccessCommand = shell.Action(() => FinishAsync(RegistrationStatus.Succeeded), () => IsPending);
        FailureCommand = shell.Action(() => FinishAsync(RegistrationStatus.Failed), () => IsPending);
    }

    public bool IsQueue => _mode == RegistrationMode.Queue;
    public string ListTitle => IsQueue ? "Очередь задач" : "Задачи регистрации";
    public string CreateLabel => IsQueue ? "Добавить в очередь" : "Сохранить задачу";
    public IReadOnlyList<Choice<MailProvider>> Providers => Labels.Providers;
    public IReadOnlyList<Choice<PhoneCheck>> PhoneChecks => Labels.PhoneChecks;
    public IReadOnlyList<TaskRow> Rows { get => _rows; private set => Set(ref _rows, value); }
    public int PendingCount => Rows.Count(x => x.Record.Status == RegistrationStatus.Pending);
    public int CompletedCount => Rows.Count - PendingCount;
    public string Progress => $"{CompletedCount} из {Rows.Count} завершено";
    public bool IsEmpty => Rows.Count == 0;

    public TaskRow? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (!Set(ref _selectedTask, value))
                return;

            _password = "";
            if (value is not null)
            {
                Provider = value.Record.Provider;
                Login = value.Record.Login;
                Note = value.Record.Note;
                Phone = value.Record.PhoneCheck;
                Email = Login + "@" + RegistrationRules.Provider(Provider).Domain;
                KeepPassword = false;
            }
            else
            {
                Login = "";
                Email = "";
                Note = "";
                Phone = PhoneCheck.Unknown;
                KeepPassword = false;
            }
            RefreshForm();
        }
    }

    public MailProvider Provider
    {
        get => _provider;
        set
        {
            if (Set(ref _provider, value))
                Notify(nameof(DomainHint));
        }
    }

    public string Login { get => _login; set => Set(ref _login, value); }
    public string Email { get => _email; set => Set(ref _email, value); }
    public string Note { get => _note; set => Set(ref _note, value); }
    public PhoneCheck Phone { get => _phone; set => Set(ref _phone, value); }
    public bool KeepPassword { get => _keepPassword; set => Set(ref _keepPassword, value); }

    public bool IsDraft => SelectedTask is null;
    public bool IsPending => SelectedTask?.Record.Status == RegistrationStatus.Pending;
    public bool IsClosed => !IsDraft && !IsPending;
    public bool CanRegister => IsDraft || IsPending;
    public string FormTitle => IsDraft ? "Данные для регистрации" : $"Задача №{SelectedTask!.Record.Id}";
    public string DomainHint => "@" + RegistrationRules.Provider(Provider).Domain;
    public string PasswordHint => (SelectedTask?.Record.ProtectedPassword is not null || _password.Length > 0)
        ? "••••••••••••••••" : "Сгенерируйте пароль";

    public ICommand NewCommand { get; }
    public ICommand GenerateLoginCommand { get; }
    public ICommand GeneratePasswordCommand { get; }
    public ICommand CopyLoginCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand SaveTaskCommand { get; }
    public ICommand OpenSignupCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand SuccessCommand { get; }
    public ICommand FailureCommand { get; }

    internal override void Update(WorkspaceData data)
    {
        var id = SelectedTask?.Record.Id;
        Rows = data.Tasks.Where(x => x.Mode == _mode).Select(x => new TaskRow(x)).ToArray();
        if (id is int number)
            SelectedTask = Rows.FirstOrDefault(x => x.Record.Id == number);

        Notify(nameof(PendingCount));
        Notify(nameof(CompletedCount));
        Notify(nameof(Progress));
        Notify(nameof(IsEmpty));
        CommandManager.InvalidateRequerySuggested();
    }

    internal override void ResetAfterRestore() => ClearForm();

    private void Next()
    {
        var current = SelectedTask?.Record.Id ?? 0;
        var next = Rows.FirstOrDefault(x => x.Record.Id > current && x.Record.Status == RegistrationStatus.Pending)
            ?? Rows.FirstOrDefault(x => x.Record.Status == RegistrationStatus.Pending);
        SelectedTask = next;
    }

    private void ClearForm()
    {
        SelectedTask = null;
        Login = "";
        _password = "";
        Email = "";
        Note = "";
        Phone = PhoneCheck.Unknown;
        KeepPassword = false;
        RefreshForm();
    }

    private void RefreshForm()
    {
        Notify(nameof(IsDraft));
        Notify(nameof(IsPending));
        Notify(nameof(IsClosed));
        Notify(nameof(CanRegister));
        Notify(nameof(FormTitle));
        Notify(nameof(PasswordHint));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveDraftAsync()
    {
        var id = await _shell.Store.AddTaskAsync(new(Provider, Login, _password), _mode);
        await _shell.ReloadAsync();
        SelectedTask = Rows.Single(x => x.Record.Id == id);
    }

    private Task FinishAsync(RegistrationStatus status) => _shell.RunAsync(async () =>
    {
        await _shell.Store.FinishAsync(SelectedTask!.Record.Id, new(status, Email, Note, Phone, KeepPassword));
    }, status == RegistrationStatus.Succeeded ? "Аккаунт добавлен." : "Ошибка регистрации зафиксирована.", reload: true);

    private async Task ImportAsync()
    {
        var file = WindowsDesktop.PickImport();
        if (file is null)
            return;

        await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (source.Length > QueueImport.MaximumBytes)
            throw new InputException("Файл импорта должен быть не больше 256 КБ.");

        var bytes = new byte[(int)source.Length];
        await source.ReadExactlyAsync(bytes);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            throw new InputException("Сохраните список в кодировке UTF-8.");
        }

        var rows = QueueImport.Parse(text, Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase), Provider);
        var count = await _shell.Store.ImportAsync(rows);
        await _shell.ReloadAsync();
        _shell.Report($"Импортировано задач: {count}.");
    }
}

public sealed class TaskRow
{
    internal TaskRow(RegistrationTask record) => Record = record;
    internal RegistrationTask Record { get; }
    public string Login => Record.Login;
    public string Provider => Labels.Provider(Record.Provider);
    public RegistrationStatus Status => Record.Status;
    public string Note => Record.Note;
    public string Phone => Labels.PhoneChecks.FirstOrDefault(x => x.Value == Record.PhoneCheck)?.Label ?? "Не указано";
}
