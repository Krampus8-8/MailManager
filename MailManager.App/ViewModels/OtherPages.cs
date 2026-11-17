using System.Globalization;
using System.Windows.Input;
using MailManager.App.Commands;
using MailManager.App.Services;
using MailManager.Core.Models;
using MailManager.Core.Services;

namespace MailManager.App.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    private IReadOnlyList<StatTile> _statistics = [];
    private IReadOnlyList<AccountRow> _recent = [];

    internal DashboardViewModel(ShellViewModel shell)
        : base("Главная", "Управление почтовыми аккаунтами Gmail")
    {
        OpenSingle = shell.GoTo(Section.Single);
        OpenQueue = shell.GoTo(Section.Queue);
        OpenAccounts = shell.GoTo(Section.Accounts);
        OpenPasswords = new UiCommand(_ => shell.GoToGenerators(true));
        OpenLogins = new UiCommand(_ => shell.GoToGenerators(false));
        Update(shell.Data);
    }

    public IReadOnlyList<StatTile> Statistics { get => _statistics; private set => Set(ref _statistics, value); }
    public IReadOnlyList<AccountRow> Recent { get => _recent; private set => Set(ref _recent, value); }
    public bool IsEmpty => Recent.Count == 0;
    public ICommand OpenSingle { get; }
    public ICommand OpenQueue { get; }
    public ICommand OpenAccounts { get; }
    public ICommand OpenPasswords { get; }
    public ICommand OpenLogins { get; }

    internal override void Update(WorkspaceData data)
    {
        var accounts = data.Accounts.Where(x => x.Provider == MailProvider.Gmail).ToArray();
        var tasks = data.Tasks.Where(x => x.Provider == MailProvider.Gmail).ToArray();
        Statistics =
        [
            new("Всего аккаунтов", accounts.Length, "\uE715", "#A37BFF", "#34274F"),
            new("Успешных", tasks.Count(x => x.Status == RegistrationStatus.Succeeded), "\uE73E", "#4DE69B", "#203D37"),
            new("В процессе", tasks.Count(x => x.Status == RegistrationStatus.Pending), "\uE823", "#FFBC57", "#42372F"),
            new("С ошибкой", tasks.Count(x => x.Status == RegistrationStatus.Failed), "\uE711", "#FF6A72", "#442A38")
        ];
        Recent = accounts.Take(5).Select(x => new AccountRow(x)).ToArray();
        Notify(nameof(IsEmpty));
    }
}

public sealed record StatTile(string Label, int Count, string Icon, string Color, string Tint);

public sealed class HistoryViewModel() : PageViewModel("История", "Журнал локальных операций")
{
    private IReadOnlyList<HistoryRow> _rows = [];
    public IReadOnlyList<HistoryRow> Rows { get => _rows; private set => Set(ref _rows, value); }
    public bool IsEmpty => Rows.Count == 0;

    internal override void Update(WorkspaceData data)
    {
        Rows = data.History.Select(x => new HistoryRow(Labels.Local(x.OccurredUtc), Labels.History(x.Action), x.Subject)).ToArray();
        Notify(nameof(IsEmpty));
    }
}

public sealed record HistoryRow(DateTime Date, string Action, string Subject);

public sealed class GeneratorsViewModel : PageViewModel
{
    private string _loginLength = "12";
    private string _passwordLength = "20";
    private string _login = "";
    private string _password = "";
    private bool _showPasswords;
    private int _defaultLoginLength;
    private int _defaultPasswordLength;

    internal GeneratorsViewModel(ShellViewModel shell)
        : base("Генераторы", "Создавайте логины и пароли с нужными параметрами")
    {
        ShowLoginCommand = new UiCommand(_ => ShowPasswords = false);
        ShowPasswordCommand = new UiCommand(_ => ShowPasswords = true);
        LoginCommand = shell.Action(() => shell.Run(() =>
            Login = shell.GenerateLogin(MailProvider.Gmail, ReadNumber(LoginLength, "Длина логина"), Pattern, Login)));
        PasswordCommand = shell.Action(() => shell.Run(() =>
        {
            _password = shell.Generator.Password(new(ReadNumber(PasswordLength, "Длина пароля"), Upper, Lower, Digits, Symbols));
            Notify(nameof(PasswordHint));
        }));
        CopyLoginCommand = shell.Action(() => shell.Run(() => shell.Desktop.Copy(Login), "Логин скопирован."), () => Login.Length > 0);
        CopyPasswordCommand = shell.Action(() => shell.Run(() => shell.CopySecret(_password)), () => _password.Length > 0);
    }

    public bool ShowPasswords { get => _showPasswords; set => Set(ref _showPasswords, value); }
    public LoginPattern Pattern { get; set; }
    public bool Upper { get; set; } = true;
    public bool Lower { get; set; } = true;
    public bool Digits { get; set; } = true;
    public bool Symbols { get; set; } = true;
    public string LoginLength { get => _loginLength; set => Set(ref _loginLength, value); }
    public string PasswordLength { get => _passwordLength; set => Set(ref _passwordLength, value); }
    public string Login { get => _login; private set => Set(ref _login, value); }
    public string PasswordHint => _password.Length == 0 ? "Пароль не сгенерирован" : "••••••••••••••••";
    public IReadOnlyList<Choice<LoginPattern>> Patterns { get; } =
        [new(LoginPattern.Name, "Имя и цифры"), new(LoginPattern.Numeric, "Числовая комбинация")];
    public ICommand LoginCommand { get; }
    public ICommand PasswordCommand { get; }
    public ICommand ShowLoginCommand { get; }
    public ICommand ShowPasswordCommand { get; }
    public ICommand CopyLoginCommand { get; }
    public ICommand CopyPasswordCommand { get; }

    internal override void Update(WorkspaceData data)
    {
        if (_defaultLoginLength != data.Settings.LoginLength)
            LoginLength = data.Settings.LoginLength.ToString(CultureInfo.InvariantCulture);
        if (_defaultPasswordLength != data.Settings.PasswordLength)
            PasswordLength = data.Settings.PasswordLength.ToString(CultureInfo.InvariantCulture);

        _defaultLoginLength = data.Settings.LoginLength;
        _defaultPasswordLength = data.Settings.PasswordLength;
    }

    internal override void ResetAfterRestore()
    {
        Login = "";
        _password = "";
        Notify(nameof(PasswordHint));
        _defaultLoginLength = 0;
        _defaultPasswordLength = 0;
    }
}

public sealed class BackupsViewModel : PageViewModel
{
    internal BackupsViewModel(ShellViewModel shell)
        : base("Резервные копии", "Создание и восстановление локального снимка SQLite")
    {
        CreateCommand = shell.Action(() => shell.RunAsync(async () =>
        {
            var file = WindowsDesktop.PickBackupDestination();
            if (file is null)
                return;

            await shell.Store.CreateBackupAsync(file);
            await shell.ReloadAsync();
            shell.Report("Резервная копия создана и проверена.");
        }));
        RestoreCommand = shell.Action(() => shell.RunAsync(async () =>
        {
            var file = WindowsDesktop.PickBackup();
            if (file is null || !shell.Desktop.Confirm(
                "Текущие аккаунты, задачи, история и настройки будут заменены данными из выбранной копии. Продолжить?",
                "Восстановление данных"))
            {
                return;
            }

            await shell.Store.RestoreBackupAsync(file);
            await shell.ReloadAsync(restored: true);
            shell.Report("Данные восстановлены из проверенной копии.");
        }));
    }

    public ICommand CreateCommand { get; }
    public ICommand RestoreCommand { get; }
}

public sealed class SettingsViewModel : PageViewModel
{
    private string _loginLength = "";
    private string _passwordLength = "";
    private string _clipboardSeconds = "";
    private AppSettings? _last;

    internal SettingsViewModel(ShellViewModel shell, string path)
        : base("Настройки", "Параметры генерации, буфера обмена и локального хранения")
    {
        DataPath = path;
        SaveCommand = shell.Action(() => shell.RunAsync(() => shell.Store.SaveSettingsAsync(new AppSettings
        {
            LoginLength = ReadNumber(LoginLength, "Длина логина"),
            PasswordLength = ReadNumber(PasswordLength, "Длина пароля"),
            ClipboardSeconds = ReadNumber(ClipboardSeconds, "Очистка буфера")
        }), "Настройки сохранены.", reload: true));
        ResetCommand = new UiCommand(_ => Apply(new()));
        CopyPathCommand = shell.Action(() => shell.Run(() => shell.Desktop.Copy(DataPath), "Путь скопирован."));
    }

    public string LoginLength { get => _loginLength; set => Set(ref _loginLength, value); }
    public string PasswordLength { get => _passwordLength; set => Set(ref _passwordLength, value); }
    public string ClipboardSeconds { get => _clipboardSeconds; set => Set(ref _clipboardSeconds, value); }
    public string DataPath { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CopyPathCommand { get; }

    internal override void Update(WorkspaceData data)
    {
        if (_last is null || _last.LoginLength != data.Settings.LoginLength || _last.PasswordLength != data.Settings.PasswordLength ||
            _last.ClipboardSeconds != data.Settings.ClipboardSeconds)
        {
            Apply(data.Settings);
        }
        _last = data.Settings;
    }

    internal override void ResetAfterRestore() => _last = null;

    private void Apply(AppSettings settings)
    {
        LoginLength = settings.LoginLength.ToString(CultureInfo.InvariantCulture);
        PasswordLength = settings.PasswordLength.ToString(CultureInfo.InvariantCulture);
        ClipboardSeconds = settings.ClipboardSeconds.ToString(CultureInfo.InvariantCulture);
    }
}
