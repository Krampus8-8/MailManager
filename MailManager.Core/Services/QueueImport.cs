using Microsoft.VisualBasic.FileIO;
using MailManager.Core.Models;

namespace MailManager.Core.Services;

public static class QueueImport
{
    public const int MaximumRows = 1000;
    public const long MaximumBytes = 256 * 1024;

    public static IReadOnlyList<ImportRow> Parse(string content, bool csv, MailProvider defaultProvider)
    {
        List<ImportRow> rows = [];
        if (csv)
        {
            using var parser = new TextFieldParser(new StringReader(content));
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            var first = true;
            try
            {
                while (!parser.EndOfData)
                {
                    var line = parser.LineNumber;
                    var fields = parser.ReadFields()!;
                    if (first && fields.Length == 2 &&
                        fields[0].Equals("provider", StringComparison.OrdinalIgnoreCase) &&
                        fields[1].Equals("login", StringComparison.OrdinalIgnoreCase))
                    {
                        first = false;
                        continue;
                    }

                    first = false;
                    if (fields.Length != 2)
                        throw new InputException($"Строка {line}: ожидаются поля provider,login.");
                    Add(rows, fields[0], fields[1], defaultProvider, line);
                }
            }
            catch (MalformedLineException ex)
            {
                throw new InputException($"Строка {ex.LineNumber}: проверьте кавычки и разделители CSV.");
            }
        }
        else
        {
            using var reader = new StringReader(content);
            for (var number = 1; reader.ReadLine() is { } line; number++)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var columns = line.Split('|');
                if (columns.Length > 2)
                    throw new InputException($"Строка {number}: укажите логин или сервис|логин.");
                Add(rows, columns.Length == 2 ? columns[0] : "", columns[^1], defaultProvider, number);
            }
        }

        if (rows.Count == 0)
            throw new InputException("В файле нет задач для импорта.");
        return rows;
    }

    private static void Add(List<ImportRow> rows, string provider, string login, MailProvider defaultProvider, long line)
    {
        if (rows.Count == MaximumRows)
            throw new InputException($"Импортируйте до {MaximumRows} задач за один раз.");

        try
        {
            var service = string.IsNullOrWhiteSpace(provider) ? defaultProvider : RegistrationRules.ParseProvider(provider);
            rows.Add(new(service, string.IsNullOrWhiteSpace(login) ? "" : RegistrationRules.Login(login, service)));
        }
        catch (InputException ex)
        {
            throw new InputException($"Строка {line}: {ex.Message}");
        }
    }
}
