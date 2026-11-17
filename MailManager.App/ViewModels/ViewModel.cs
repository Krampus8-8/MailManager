using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MailManager.App.Commands;
using MailManager.Core.Models;
using MailManager.Core.Services;

namespace MailManager.App.ViewModels;

public abstract class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Notify(property);
        return true;
    }

    protected void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new(property));
}

public abstract class PageViewModel(string title, string subtitle) : ViewModel
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;

    internal virtual void Update(WorkspaceData data) { }
    internal virtual void ResetAfterRestore() { }

    private protected static int ReadNumber(string value, string field) =>
        int.TryParse(value, out var result) ? result : throw new InputException($"Укажите целое число в поле «{field}».");
}

public sealed record Choice<T>(T Value, string Label);

internal static class Labels
{
    internal static IReadOnlyList<Choice<MailProvider>> Providers { get; } =
        RegistrationRules.Providers.Select(x => new Choice<MailProvider>(x.Id, x.Name)).ToArray();

    internal static IReadOnlyList<Choice<RegistrationStatus>> Statuses { get; } =
    [
        new(RegistrationStatus.Pending, "В процессе"),
        new(RegistrationStatus.Succeeded, "Успешно"),
        new(RegistrationStatus.Failed, "С ошибкой")
    ];

    internal static IReadOnlyList<Choice<PhoneCheck>> PhoneChecks { get; } =
    [
        new(PhoneCheck.Unknown, "Не указано"),
        new(PhoneCheck.NotRequested, "Телефон не запрашивали"),
        new(PhoneCheck.Verified, "Телефон подтверждён"),
        new(PhoneCheck.NotCompleted, "Телефон запрошен, подтверждение не завершено")
    ];

    internal static string Provider(MailProvider value) => Providers.FirstOrDefault(x => x.Value == value)?.Label ?? "Неизвестно";

    internal static string History(HistoryAction value) => value switch
    {
        HistoryAction.TaskAdded => "Добавлена задача",
        HistoryAction.TasksImported => "Импортированы задачи",
        HistoryAction.Registered => "Регистрация подтверждена",
        HistoryAction.RegistrationFailed => "Отмечена ошибка регистрации",
        HistoryAction.AccountAdded => "Добавлен аккаунт",
        HistoryAction.AccountEdited => "Изменён аккаунт",
        HistoryAction.AccountDeleted => "Удалён аккаунт",
        HistoryAction.BackupCreated => "Создана резервная копия",
        HistoryAction.BackupRestored => "Восстановлена резервная копия",
        HistoryAction.SettingsChanged => "Сохранены настройки",
        _ => "Неизвестное действие"
    };

    internal static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}

public sealed class NavigationItem(string name, string icon, PageViewModel page) : ViewModel
{
    private bool _isSelected;

    public string Name { get; } = name;
    public string Icon { get; } = icon;
    public PageViewModel Page { get; } = page;
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
}
