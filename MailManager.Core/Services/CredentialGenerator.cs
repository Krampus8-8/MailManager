using System.Security.Cryptography;
using MailManager.Core.Models;

namespace MailManager.Core.Services;

public sealed class CredentialGenerator
{
    private const string Digits = "0123456789";
    private static readonly string[] Names =
        ["alex", "anika", "aria", "daria", "elena", "emil", "irina", "kirill", "lev", "lina", "maksim", "maria",
         "mila", "miran", "nina", "roman", "sasha", "savel", "sonya", "timur", "vera", "viktor", "yana"];

    public string Password(PasswordOptions options)
    {
        if (options.Length is < 12 or > 128)
            throw new InputException("Длина пароля должна быть от 12 до 128 символов.");

        List<string> groups = [];
        if (options.Upper) groups.Add("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        if (options.Lower) groups.Add("abcdefghijklmnopqrstuvwxyz");
        if (options.Digits) groups.Add(Digits);
        if (options.Symbols) groups.Add("!@#$%&*+-=?_");
        if (groups.Count == 0)
            throw new InputException("Выберите хотя бы один набор символов.");

        var alphabet = string.Concat(groups);
        Span<char> result = stackalloc char[options.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = Pick(i < groups.Count ? groups[i] : alphabet);

        RandomNumberGenerator.Shuffle(result);
        return new string(result);
    }

    public string Login(MailProvider provider, int length, LoginPattern pattern, Func<string, bool> isUsed)
    {
        RegistrationRules.Provider(provider);
        if (length is < 6 or > 30)
            throw new InputException("Длина логина должна быть от 6 до 30 символов.");
        if (!Enum.IsDefined(pattern))
            throw new InputException("Выберите тип логина.");

        Span<char> candidate = stackalloc char[length];
        for (var attempt = 0; attempt < 128; attempt++)
        {
            // Яндекс требует начальную букву даже для числового варианта.
            var prefix = pattern == LoginPattern.Name
                ? Names[RandomNumberGenerator.GetInt32(Names.Length)]
                : provider == MailProvider.Yandex ? Pick("abcdefghijklmnopqrstuvwxyz").ToString() : "";
            var prefixLength = Math.Min(prefix.Length, length - 3);
            prefix.AsSpan(0, prefixLength).CopyTo(candidate);
            for (var i = prefixLength; i < candidate.Length; i++)
                candidate[i] = Pick(Digits);

            var login = new string(candidate);
            if (!isUsed(login))
                return login;
        }

        throw new InputException("Свободный локальный логин не найден. Увеличьте длину или смените тип.");
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
