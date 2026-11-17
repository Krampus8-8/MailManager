using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using MailManager.App.Commands;
using MailManager.App.Services;
using MailManager.Core.Interfaces;
using MailManager.Core.Models;
using MailManager.Core.Services;

namespace MailManager.App.ViewModels;

internal enum Section { Dashboard, Single, Queue, Accounts, History, Generators, Backups, Settings }

public sealed class ShellViewModel : ViewModel
{
    private PageViewModel _selectedPage;
    private bool _isBusy;
    private bool _isReady;
    private bool _isError;
    private string _message = "";
    private readonly WindowsDesktop _desktop = new();

    internal ShellViewModel(IMailStore store, ISecretProtector protector, string dataPath)
    {
        Store = store;
        Protector = protector;
        Navigation =
        [
            new("Главная", "\uE80F", new DashboardViewModel(this)),
            new("Создание почты", "\uE710", new RegistrationViewModel(this, RegistrationMode.Single)),
            new("Массовое создание", "\uE81E", new RegistrationViewModel(this, RegistrationMode.Queue)),
            new("Мои аккаунты", "\uE715", new AccountsViewModel(this)),
            new("История", "\uE823", new HistoryViewModel()),
            new("Генераторы", "\uE8D7", new GeneratorsViewModel(this)),
            new("Резервные копии", "\uE74E", new BackupsViewModel(this)),
            new("Настройки", "\uE713", new SettingsViewModel(this, dataPath))
        ];
        Navigation[0].IsSelected = true;
        _selectedPage = Navigation[0].Page;
        NavigateCommand = new UiCommand(item =>
        {
            if (item is NavigationItem navigation)
                Navigate(navigation);
        });
        DismissCommand = new UiCommand(_ => Message = "");
        RetryCommand = new AsyncUiCommand(InitializeAsync, () => !IsReady && !IsBusy);
        UpdateCommand = Action(UpdateAsync);
    }

    public IReadOnlyList<NavigationItem> Navigation { get; }
    public PageViewModel SelectedPage { get => _selectedPage; private set => Set(ref _selectedPage, value); }
    public ICommand NavigateCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand UpdateCommand { get; }
    public string VersionLabel => $"v{UpdateInstaller.CurrentVersion}";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                Notify(nameof(CanInteract));
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (Set(ref _isReady, value))
                Notify(nameof(CanInteract));
        }
    }

    public bool CanInteract => IsReady && !IsBusy;
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }
    public string Message { get => _message; private set { if (Set(ref _message, value)) Notify(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;
    internal IMailStore Store { get; }
    internal ISecretProtector Protector { get; }
    internal WindowsDesktop Desktop => _desktop;
    internal CredentialGenerator Generator { get; } = new();
    internal WorkspaceData Data { get; private set; } = new([], [], [], new());

    internal async Task InitializeAsync()
    {
        if (IsBusy || IsReady)
            return;

        await RunAsync(async () =>
        {
            await Store.InitializeAsync();
            await ReloadAsync();
            IsReady = true;
        });
    }

    internal ICommand GoTo(Section section) => new UiCommand(_ => Navigate(Navigation[(int)section]));

    internal void GoToGenerators(bool password)
    {
        var destination = Navigation[(int)Section.Generators];
        ((GeneratorsViewModel)destination.Page).ShowPasswords = password;
        Navigate(destination);
    }

    private void Navigate(NavigationItem destination)
    {
        if (!CanInteract || destination.IsSelected)
            return;

        foreach (var item in Navigation)
            item.IsSelected = item == destination;
        SelectedPage = destination.Page;
    }

    internal ICommand Action(Func<Task> action, Func<bool>? canExecute = null) =>
        new AsyncUiCommand(action, () => CanInteract && (canExecute?.Invoke() ?? true));

    internal async Task RunAsync(Func<Task> action, string? success = null, bool reload = false)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Message = "";
        try
        {
            await action();
            if (reload)
                await ReloadAsync();
            if (success is not null)
                Report(success);
        }
        catch (InputException ex)
        {
            Report(ex.Message, true);
        }
        catch (CryptographicException)
        {
            Report("Windows не смогла обработать пароль. Для паролей из копии требуется исходный профиль Windows.", true);
        }
        catch (Win32Exception)
        {
            Report("Не удалось открыть браузер. Проверьте браузер по умолчанию.", true);
        }
        catch (ExternalException)
        {
            Report("Не удалось получить доступ к буферу обмена. Повторите действие.", true);
        }
        catch (UnauthorizedAccessException)
        {
            Report("Windows отказала в доступе к файлу или папке.", true);
        }
        catch (IOException)
        {
            Report("Не удалось обработать файл. Проверьте путь и доступ к файлу.", true);
        }
        catch (Exception)
        {
            // Не выводим исключения сторонних библиотек: они могут содержать сведения о локальных данных.
            Report(IsReady ? "Операция не завершилась. Проверьте доступ к локальным данным и повторите действие."
                : "Не удалось открыть локальную базу Mail Manager. Проверьте доступ к папке данных и повторите попытку.", true);
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    internal Task Run(Action action, string? success = null) =>
        RunAsync(() => { action(); return Task.CompletedTask; }, success);

    private async Task UpdateAsync()
    {
        var restart = false;
        await RunAsync(async () =>
        {
            Report("Проверяем обновления…");
            var source = new GitHubUpdates();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            UpdateStage? stage = null;
            try
            {
                string? token = null;
                var check = await source.CheckAsync(token, UpdateInstaller.CurrentVersion, limit.Token);
                if (check.RequiresSignIn)
                {
                    token = await source.GetTokenAsync(interactive: false);
                    if (string.IsNullOrEmpty(token))
                    {
                        if (!Desktop.Confirm("Для приватного релиза нужен вход в GitHub. Открыть страницу входа?", "Обновление Mail Manager"))
                        {
                            Report("Обновление отменено.");
                            return;
                        }

                        token = await source.GetTokenAsync(interactive: true);
                        if (string.IsNullOrEmpty(token))
                            throw new InputException("Вход в GitHub не завершён. Нажмите «Обновить», чтобы повторить.");
                    }

                    check = await source.CheckAsync(token, UpdateInstaller.CurrentVersion, limit.Token);
                }

                var release = check.Release;
                if (release is null)
                {
                    Report($"Установлена актуальная версия {UpdateInstaller.CurrentVersion}.");
                    return;
                }

                var executable = Environment.ProcessPath!;
                stage = UpdateStage.Create(executable);
                Report($"Скачиваем Mail Manager {release.Version}…");
                await source.DownloadAsync(release, token, stage.Payload,
                    progress => Report($"Скачиваем Mail Manager {release.Version} · {progress} %"), limit.Token);
                Report("Устанавливаем обновление и перезапускаем приложение…");
                await UpdateInstaller.HandOffAsync(stage, executable, release);
                restart = true;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                throw new InputException("Не удалось скачать обновление. Проверьте подключение к GitHub и повторите.");
            }
            catch (Win32Exception)
            {
                throw new InputException("Windows не смогла запустить установку обновления.");
            }
            finally
            {
                if (!restart)
                    stage?.Clean();
            }
        });

        if (restart)
            Application.Current.Shutdown();
    }

    internal async Task ReloadAsync(bool restored = false)
    {
        Data = await Store.ReadAsync();
        if (restored)
        {
            Desktop.ClearSecret();
            foreach (var item in Navigation)
                item.Page.ResetAfterRestore();
        }

        foreach (var item in Navigation)
            item.Page.Update(Data);
    }

    internal void Report(string message, bool error = false)
    {
        IsError = error;
        Message = message;
    }

    internal string GenerateLogin(MailProvider provider, int length, LoginPattern pattern, string? previous = null) =>
        Generator.Login(provider, length, pattern, login =>
        {
            var key = RegistrationRules.LoginKey(provider, login);
            return login == previous ||
                Data.Accounts.Any(x => x.Provider == provider && RegistrationRules.LoginKey(provider, x.Email.Split('@', 2)[0]) == key) ||
                Data.Tasks.Any(x => x.Provider == provider && x.Status == RegistrationStatus.Pending &&
                    RegistrationRules.LoginKey(provider, x.Login) == key);
        });

    internal void CopySecret(string secret)
    {
        Desktop.CopySecret(secret, Data.Settings.ClipboardSeconds);
        Report($"Пароль скопирован. Буфер очистится через {Data.Settings.ClipboardSeconds} с, если его содержимое не изменится.");
    }

    internal void CopySecret(byte[]? encrypted)
    {
        if (encrypted is null)
            throw new InputException("Для этой записи пароль не сохранён.");
        CopySecret(Protector.Unprotect(encrypted));
    }
}
