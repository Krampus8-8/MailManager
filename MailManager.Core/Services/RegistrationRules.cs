using System.Net.Mail;
using System.Text.RegularExpressions;
using MailManager.Core.Models;

namespace MailManager.Core.Services;

public sealed class InputException(string message) : Exception(message);

public static partial class RegistrationRules
{
    public static IReadOnlyList<ProviderInfo> Providers { get; } =
    [
        new(MailProvider.Gmail, "Gmail", "gmail.com", new("https://accounts.google.com/signup"), ["gmail.com", "googlemail.com"]),
        new(MailProvider.MailRu, "Mail.ru", "mail.ru", new("https://account.mail.ru/signup"),
            ["mail.ru", "internet.ru", "bk.ru", "list.ru", "inbox.ru", "mail.ua"]),
        new(MailProvider.Yandex, "Яндекс", "yandex.ru", new("https://passport.yandex.ru/registration"),
            ["yandex.ru", "ya.ru", "yandex.com", "yandex.by", "yandex.kz", "yandex.uz", "yandex.com.tr"])
    ];

    public static ProviderInfo Provider(MailProvider id) =>
        Providers.FirstOrDefault(p => p.Id == id) ?? throw new InputException("Выберите почтовый сервис.");

    public static MailProvider ParseProvider(string value) => value.Trim().ToLowerInvariant() switch
    {
        "gmail" or "gmail.com" => MailProvider.Gmail,
        "mail.ru" or "mailru" => MailProvider.MailRu,
        "yandex" or "yandex.ru" or "яндекс" => MailProvider.Yandex,
        _ => throw new InputException("Укажите Gmail, Mail.ru или Яндекс.")
    };

    public static string Login(string value, MailProvider provider)
    {
        var login = value.Trim().ToLowerInvariant();
        var valid = provider switch
        {
            MailProvider.Gmail => GmailLogin().IsMatch(login) && !login.Contains(".."),
            MailProvider.MailRu => MailRuLogin().IsMatch(login),
            MailProvider.Yandex => YandexLogin().IsMatch(login) && !login.Contains(".."),
            _ => false
        };
        if (!valid)
        {
            throw new InputException("Проверьте длину логина и допустимые символы для выбранного сервиса.");
        }

        return login;
    }

    public static string LoginKey(MailProvider provider, string login) =>
        provider == MailProvider.Gmail ? login.Replace(".", "", StringComparison.Ordinal) : login;

    public static string Email(string value, MailProvider provider)
    {
        var email = value.Trim().ToLowerInvariant();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email ||
            !Provider(provider).Domains.Contains(address.Host, StringComparer.OrdinalIgnoreCase) ||
            !EmailLocalPart().IsMatch(address.User))
        {
            throw new InputException("Введите фактический адрес почты выбранного сервиса, например имя@" + Provider(provider).Domain + ".");
        }

        return email;
    }

    public static string Note(string value)
    {
        var note = value.Trim();
        if (note.Length > 500)
            throw new InputException("Примечание может содержать до 500 символов.");
        return note;
    }

    public static string? ProfileName(string? value, string field)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        if (name.Length > 100 || name.Any(char.IsControl))
            throw new InputException($"Укажите до 100 символов без переносов строки в поле «{field}».");
        return name;
    }

    public static void ValidatePassword(string password, MailProvider provider)
    {
        var minimum = provider == MailProvider.Gmail ? 8 : 12;
        if (password.Length < minimum || password.Length > 128)
            throw new InputException($"Введите или сгенерируйте пароль длиной от {minimum} до 128 символов.");
        if (provider == MailProvider.Gmail && (password[0] == ' ' || password[^1] == ' ' ||
            password.Any(character => character is < ' ' or > '~')))
        {
            throw new InputException("Для Gmail используйте латинские буквы, цифры и символы ASCII. Уберите пробелы в начале и конце пароля.");
        }
    }

    public static void ValidateStatus(RegistrationStatus status, PhoneCheck phone)
    {
        if (!Enum.IsDefined(status) || !Enum.IsDefined(phone))
            throw new InputException("Выберите статус и результат проверки телефона.");
        if (status == RegistrationStatus.Succeeded && phone == PhoneCheck.NotCompleted)
            throw new InputException("Уточните результат проверки телефона для успешной регистрации.");
    }

    public static void ValidateSettings(AppSettings settings)
    {
        if (settings.LoginLength is < 6 or > 30 || settings.PasswordLength is < 12 or > 128 ||
            settings.ClipboardSeconds is < 10 or > 120)
        {
            throw new InputException("Логин: 6–30 символов. Пароль: 12–128. Буфер обмена: 10–120 секунд.");
        }
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.]{4,28}[a-z0-9]$")]
    private static partial Regex GmailLogin();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{2,29}$")]
    private static partial Regex MailRuLogin();
    [GeneratedRegex(@"^[a-z][a-z0-9.-]{1,28}[a-z0-9]$")]
    private static partial Regex YandexLogin();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex EmailLocalPart();
}
