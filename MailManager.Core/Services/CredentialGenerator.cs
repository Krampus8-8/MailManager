using System.Security.Cryptography;
using MailManager.Core.Models;

namespace MailManager.Core.Services;

public sealed class CredentialGenerator
{
    private const string Digits = "0123456789";
    private static readonly string[] Names =
        ["alex", "anika", "aria", "daria", "elena", "emil", "irina", "kirill", "lev", "lina", "maksim", "maria",
         "mila", "miran", "nina", "roman", "sasha", "savel", "sonya", "timur", "vera", "viktor", "yana"];

    private static readonly string[] Surnames =
        ["Belov", "Karelin", "Kovalenko", "Lebedev", "Moroz", "Orlov", "Romanenko", "Smirnov", "Sokolov", "Volkov"];
    private static readonly string[] HandleStarts =
        ["alto", "echo", "iris", "jade", "luna", "mint", "nova", "onyx", "rune", "sage", "velo", "zest"];
    private static readonly string[] HandleWords =
        ["amber", "atlas", "bloom", "cloud", "comet", "coral", "ember", "fjord", "glint", "grove", "orbit", "pearl",
         "pixel", "reef", "river", "solar", "spark", "stone", "storm", "tango", "trail", "tulip", "vivid", "wave"];

    public RegistrationName Name()
    {
        var first = Names[RandomNumberGenerator.GetInt32(Names.Length)];
        var last = Surnames[RandomNumberGenerator.GetInt32(Surnames.Length)];
        return new(char.ToUpperInvariant(first[0]) + first[1..], last);
    }

    public DateOnly SampleBirthDate(DateOnly today)
    {
        var earliest = today.AddYears(-40);
        var latest = today.AddYears(-20);
        return earliest.AddDays(RandomNumberGenerator.GetInt32(latest.DayNumber - earliest.DayNumber + 1));
    }

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
            var prefixLength = provider == MailProvider.Gmail && pattern == LoginPattern.Name
                ? WriteGmailHandle(candidate[..^Math.Min(5, length - 4)])
                : WriteLegacyPrefix(candidate, provider, pattern);
            for (var i = prefixLength; i < length; i++)
                candidate[i] = Pick(Digits);

            var login = new string(candidate);
            if (!isUsed(login))
                return login;
        }

        throw new InputException("Свободный локальный логин не найден. Увеличьте длину или смените тип.");
    }

    private static int WriteGmailHandle(Span<char> handle)
    {
        var start = HandleStarts[RandomNumberGenerator.GetInt32(HandleStarts.Length)];
        start.AsSpan().CopyTo(handle);
        var written = start.Length;
        if (handle.Length - written >= 4 && RandomNumberGenerator.GetInt32(2) == 0)
            handle[written++] = '.';

        while (written < handle.Length)
        {
            var word = HandleWords[RandomNumberGenerator.GetInt32(HandleWords.Length)];
            var count = Math.Min(word.Length, handle.Length - written);
            word.AsSpan(0, count).CopyTo(handle[written..]);
            written += count;
        }
        return written;
    }

    private static int WriteLegacyPrefix(Span<char> candidate, MailProvider provider, LoginPattern pattern)
    {
        // Яндекс требует начальную букву даже для числового варианта.
        var prefix = pattern == LoginPattern.Name
            ? Names[RandomNumberGenerator.GetInt32(Names.Length)]
            : provider == MailProvider.Yandex ? Pick("abcdefghijklmnopqrstuvwxyz").ToString() : "";
        var length = Math.Min(prefix.Length, candidate.Length - 3);
        prefix.AsSpan(0, length).CopyTo(candidate);
        return length;
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
