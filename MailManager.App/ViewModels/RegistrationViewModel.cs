using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private string _login = "";
    private string _firstName = "";
    private string _lastName = "";
    private string _birthDate = "";
    private string _gender = "";
    private string _phoneNumber = "";
    private string _password = "";
    private string _email = "";
    private string _note = "";
    private PhoneCheck _phone;
    private bool _keepPassword;

    internal RegistrationViewModel(ShellViewModel shell, RegistrationMode mode)
        : base(mode == RegistrationMode.Queue ? "Массовое создание" : "Создание почты",
            mode == RegistrationMode.Queue
                ? "Очередь задач для последовательной регистрации вручную"
                : "Подготовьте данные, пройдите регистрацию и запишите результат")
    {
        _shell = shell;
        _mode = mode;

        NewCommand = new UiCommand(_ => ClearForm());
        GenerateLoginCommand = shell.Action(() => shell.Run(() =>
            Login = shell.GenerateLogin(Provider, shell.Data.Settings.LoginLength, LoginPattern.Name, Login)));
        GeneratePasswordCommand = shell.Action(() => shell.Run(() =>
            DraftPassword = shell.Generator.Password(new(shell.Data.Settings.PasswordLength))));
        GenerateBirthDateCommand = shell.Action(() => shell.Run(() => BirthDate = GenerateBirthDate()), () => CanRegister);
        GenerateProfileCommand = shell.Action(() => shell.Run(() =>
        {
            var name = shell.Generator.Name();
            var login = shell.GenerateLogin(Provider, shell.Data.Settings.LoginLength, LoginPattern.Name, Login);
            var password = shell.Generator.Password(new(shell.Data.Settings.PasswordLength));
            FirstName = name.FirstName;
            LastName = name.LastName;
            Login = login;
            DraftPassword = password;
            if (string.IsNullOrWhiteSpace(BirthDate))
                BirthDate = GenerateBirthDate();
        }), () => IsDraft);
        CopyFirstNameCommand = shell.Action(() => CopyName(FirstName, "Имя"), () => !string.IsNullOrWhiteSpace(FirstName));
        CopyLastNameCommand = shell.Action(() => CopyName(LastName, "Фамилия"), () => !string.IsNullOrWhiteSpace(LastName));
        CopyLoginCommand = shell.Action(() => shell.Run(() =>
            shell.Desktop.Copy(RegistrationRules.Login(Login, Provider)), "Логин скопирован."));
        CopyPasswordCommand = shell.Action(() => shell.Run(() => shell.CopySecret(GetPassword())));
        ShowPasswordCommand = shell.Action(() => shell.Run(() => shell.Desktop.ShowPassword(GetPassword())),
            () => CanRegister && (DraftPassword.Length > 0 || SelectedTask?.Record.ProtectedPassword is not null));
        CopyBrowserProfileCommand = shell.Action(() => shell.Run(() => CopyBrowserProfile()), () => CanRegister);
        CopyLegacyBrowserProfileCommand = shell.Action(() => shell.Run(() => CopyBrowserProfile(legacy: true)), () => CanRegister);
        CopyExtensionGuideCommand = shell.Action(() => shell.Run(
            () => shell.Desktop.Copy(ExtensionGuideTitle + Environment.NewLine + ExtensionGuide),
            "Инструкция скопирована."), () => CanRegister);
        SaveGmailExtensionCommand = shell.Action(() => shell.Run(() =>
        {
            var destination = WindowsDesktop.PickExtensionDestination();
            if (destination is null)
                return;

            GmailExtensionArchive.Save(destination);
            shell.Report("ZIP сохранён. Распакуйте его и выберите папку GmailExtension в меню расширений браузера.");
        }), () => CanRegister);
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
    public IReadOnlyList<Choice<PhoneCheck>> PhoneChecks => Labels.PhoneChecks;
    public IReadOnlyList<Choice<string>> Genders { get; } =
        [new("", "Не заполнять"), new("female", "Женский"), new("male", "Мужской"), new("undisclosed", "Предпочитаю не указывать")];
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

            DraftPassword = "";
            (BirthDate, Gender, PhoneNumber) = ("", "", "");
            if (value is not null)
            {
                Login = value.Record.Login;
                FirstName = value.Record.FirstName ?? "";
                LastName = value.Record.LastName ?? "";
                Note = value.Record.Note;
                Phone = value.Record.PhoneCheck;
                Email = Login + "@" + RegistrationRules.Provider(Provider).Domain;
                KeepPassword = !IsQueue && value.Record.Status == RegistrationStatus.Pending;
            }
            else
            {
                Login = "";
                FirstName = "";
                LastName = "";
                Email = "";
                Note = "";
                Phone = PhoneCheck.Unknown;
                KeepPassword = false;
            }
            RefreshForm();
        }
    }

    private const MailProvider Provider = MailProvider.Gmail;

    public string Login { get => _login; set => Set(ref _login, value); }
    public string FirstName { get => _firstName; set => Set(ref _firstName, value); }
    public string LastName { get => _lastName; set => Set(ref _lastName, value); }
    public string BirthDate { get => _birthDate; set => Set(ref _birthDate, value); }
    public string Gender { get => _gender; set => Set(ref _gender, value); }
    public string PhoneNumber { get => _phoneNumber; set => Set(ref _phoneNumber, value); }
    public string Email { get => _email; set => Set(ref _email, value); }
    public string Note { get => _note; set => Set(ref _note, value); }
    public PhoneCheck Phone { get => _phone; set => Set(ref _phone, value); }
    public bool KeepPassword { get => _keepPassword; set => Set(ref _keepPassword, value); }
    internal string DraftPassword { get => _password; set => Set(ref _password, value); }

    public bool IsDraft => SelectedTask is null;
    public bool IsPending => SelectedTask?.Record.Status == RegistrationStatus.Pending;
    public bool IsClosed => !IsDraft && !IsPending;
    public bool CanRegister => IsDraft || IsPending;
    public bool CanEnterPassword => CanRegister;
    public string FormTitle => IsDraft ? "Данные для регистрации" : $"Задача №{SelectedTask!.Record.Id}";
    public string DomainHint => "@" + RegistrationRules.Provider(Provider).Domain;
    public string PasswordLabel => IsPending ? "Другой пароль (если изменили на сайте)" : "Пароль";

    public ICommand NewCommand { get; }
    public ICommand GenerateLoginCommand { get; }
    public ICommand GeneratePasswordCommand { get; }
    public ICommand GenerateBirthDateCommand { get; }
    public ICommand GenerateProfileCommand { get; }
    public ICommand CopyFirstNameCommand { get; }
    public ICommand CopyLastNameCommand { get; }
    public ICommand CopyLoginCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand ShowPasswordCommand { get; }
    public ICommand CopyBrowserProfileCommand { get; }
    public ICommand CopyLegacyBrowserProfileCommand { get; }
    public ICommand CopyExtensionGuideCommand { get; }
    public ICommand SaveGmailExtensionCommand { get; }

    public string ExtensionGuideTitle => "Как установить расширение";

    public string ExtensionGuide => """
        1. Распакуйте GmailExtension.zip.
        2. В адресной строке браузера откройте chrome://extensions в Chrome или edge://extensions в Edge.
        3. Включите «Режим разработчика» и нажмите «Загрузить распакованное расширение».
        4. Выберите папку GmailExtension, внутри которой лежит manifest.json, и нажмите «Выбор папки».

        Чтобы обновить расширение, замените файлы в установленной папке GmailExtension и нажмите значок перезагрузки на карточке в Chrome или Edge.
        Вставьте данные в боковую панель и нажимайте «Заполнить текущий шаг». Код из SMS введите на сайте Google.
        """;
    public ICommand SaveTaskCommand { get; }
    public ICommand OpenSignupCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand SuccessCommand { get; }
    public ICommand FailureCommand { get; }

    internal override void Update(WorkspaceData data)
    {
        var previous = SelectedTask;
        var entered = (Email, Note, Phone, KeepPassword, DraftPassword, BirthDate, Gender, PhoneNumber);
        Rows = data.Tasks.Where(x => x.Mode == _mode && x.Provider == Provider).Select(x => new TaskRow(x)).ToArray();
        if (previous is not null)
        {
            SelectedTask = Rows.FirstOrDefault(x => x.Record.Id == previous.Record.Id);
            if (previous.Record.Status == RegistrationStatus.Pending && IsPending)
                (Email, Note, Phone, KeepPassword, DraftPassword, BirthDate, Gender, PhoneNumber) = entered;
        }

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
        FirstName = "";
        LastName = "";
        (BirthDate, Gender, PhoneNumber) = ("", "", "");
        DraftPassword = "";
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
        Notify(nameof(CanEnterPassword));
        Notify(nameof(PasswordLabel));
        Notify(nameof(FormTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveDraftAsync()
    {
        var profile = (BirthDate, Gender, PhoneNumber);
        var id = await _shell.Store.AddTaskAsync(new(Provider, Login, DraftPassword, FirstName, LastName), _mode);
        await _shell.ReloadAsync();
        SelectedTask = Rows.Single(x => x.Record.Id == id);
        (BirthDate, Gender, PhoneNumber) = profile;
    }

    private Task FinishAsync(RegistrationStatus status) => _shell.RunAsync(async () =>
    {
        await _shell.Store.FinishAsync(SelectedTask!.Record.Id, new(status, Email, Note, Phone, KeepPassword, DraftPassword));
    }, status == RegistrationStatus.Succeeded ? "Аккаунт добавлен." : "Ошибка регистрации зафиксирована.", reload: true);

    private Task CopyName(string value, string field) => _shell.Run(() =>
        _shell.Desktop.Copy(RegistrationRules.ProfileName(value, field) ?? throw new InputException($"Заполните поле «{field}».")),
        "Скопировано.");

    private string GenerateBirthDate() => _shell.Generator.SampleBirthDate(DateOnly.FromDateTime(DateTime.Today))
        .ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private string GetPassword()
    {
        if (DraftPassword.Length > 0)
            return DraftPassword;
        if (SelectedTask?.Record.ProtectedPassword is { } protectedPassword)
            return _shell.Protector.Unprotect(protectedPassword);

        throw new InputException("Введите или сгенерируйте пароль.");
    }

    private void CopyBrowserProfile(bool legacy = false)
    {
        var password = GetPassword();
        RegistrationRules.ValidatePassword(password, MailProvider.Gmail);
        var credentials = new
        {
            firstName = RegistrationRules.ProfileName(FirstName, "Имя") ?? "",
            lastName = RegistrationRules.ProfileName(LastName, "Фамилия") ?? "",
            login = RegistrationRules.Login(Login, MailProvider.Gmail),
            password
        };
        if (legacy)
        {
            _shell.Desktop.CopySecret("MailManager-Gmail/1:" + JsonSerializer.Serialize(credentials), _shell.Data.Settings.ClipboardSeconds);
            _shell.Report("Имя, фамилия, логин и пароль скопированы для расширения 1.1. Вставьте их в боковую панель.");
            return;
        }

        DateOnly? birthday = null;
        if (!string.IsNullOrWhiteSpace(BirthDate))
        {
            if (!DateOnly.TryParseExact(BirthDate.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || date.Year < 1900 || date > DateOnly.FromDateTime(DateTime.Today))
                throw new InputException("Введите действительную дату рождения в формате ДД.ММ.ГГГГ.");
            birthday = date;
        }

        var phoneNumber = Regex.Replace(PhoneNumber, @"[\s()-]", "");
        if (phoneNumber.Length > 0 && !Regex.IsMatch(phoneNumber, @"\A\+[1-9][0-9]{6,14}\z"))
            throw new InputException("Введите свой номер в международном формате с плюсом и кодом страны.");

        var profile = JsonSerializer.Serialize(new
        {
            credentials.firstName,
            credentials.lastName,
            credentials.login,
            credentials.password,
            birthDate = birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            gender = Gender,
            phoneNumber
        });
        _shell.Desktop.CopySecret("MailManager-Gmail/2:" + profile, _shell.Data.Settings.ClipboardSeconds);
        _shell.Report("Данные скопированы. Вставьте их в боковую панель Mail Manager на странице регистрации Gmail.");
    }

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
        if (rows.Any(x => x.Provider != Provider))
            throw new InputException("Можно импортировать только задачи Gmail.");
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
