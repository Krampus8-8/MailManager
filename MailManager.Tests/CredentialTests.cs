using MailManager.Core.Models;
using MailManager.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MailManager.Tests;

[TestClass]
public sealed class CredentialTests
{
    private readonly CredentialGenerator _generator = new();

    [TestMethod]
    public void PasswordIncludesEveryEnabledGroup()
    {
        var password = _generator.Password(new(32));

        Assert.HasCount(32, password);
        Assert.IsTrue(password.Any(char.IsAsciiLetterUpper));
        Assert.IsTrue(password.Any(char.IsAsciiLetterLower));
        Assert.IsTrue(password.Any(char.IsAsciiDigit));
        Assert.IsTrue(password.Any(ch => !char.IsAsciiLetterOrDigit(ch)));
    }

    [TestMethod]
    public void PasswordExcludesDisabledGroups()
    {
        var password = _generator.Password(new(12, Upper: false, Lower: false, Symbols: false));
        Assert.IsTrue(password.All(char.IsAsciiDigit));
        Assert.ThrowsExactly<InputException>(() => _generator.Password(new(12, false, false, false, false)));
        Assert.ThrowsExactly<InputException>(() => _generator.Password(new(11)));
    }

    [TestMethod]
    [DataRow(MailProvider.Gmail)]
    [DataRow(MailProvider.MailRu)]
    [DataRow(MailProvider.Yandex)]
    public void LoginsRespectProviderAndSkipExistingValues(MailProvider provider)
    {
        var attempts = 0;
        var login = _generator.Login(provider, 6, LoginPattern.Numeric, _ => attempts++ == 0);

        Assert.HasCount(6, login);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(login, RegistrationRules.Login(login, provider));
        Assert.IsTrue(login.Skip(provider == MailProvider.Yandex ? 1 : 0).All(char.IsAsciiDigit));
        Assert.ThrowsExactly<InputException>(() => _generator.Login(provider, 8, LoginPattern.Name, _ => true));
    }

    [TestMethod]
    public void GmailWordLoginsUseTheRequestedLength()
    {
        for (var length = 6; length <= 30; length++)
        {
            var login = _generator.Login(MailProvider.Gmail, length, LoginPattern.Name, _ => false);
            Assert.HasCount(length, login);
            Assert.AreEqual(login, RegistrationRules.Login(login, MailProvider.Gmail));
            Assert.IsTrue(login[^2..].All(char.IsAsciiDigit));
            Assert.IsTrue(login[..^2].All(ch => char.IsAsciiLetterLower(ch) || ch == '.'));
            Assert.IsTrue(char.IsAsciiLetterLower(login[^3]));
        }
    }

    [TestMethod]
    public void SampleBirthDateRespectsTheRangeOnLeapDay()
    {
        var today = new DateOnly(2024, 2, 29);
        var birthDate = _generator.SampleBirthDate(today);
        Assert.IsTrue(birthDate >= new DateOnly(1984, 2, 29) && birthDate <= new DateOnly(2004, 2, 29));
    }

    [TestMethod]
    public void EmailIsNormalizedAndMustMatchProvider()
    {
        Assert.AreEqual("alex.name@gmail.com", RegistrationRules.Email(" Alex.Name@GMAIL.COM ", MailProvider.Gmail));
        Assert.AreEqual("alexname", RegistrationRules.LoginKey(MailProvider.Gmail, "alex.name"));
        Assert.AreEqual("alex@ya.ru", RegistrationRules.Email("alex@ya.ru", MailProvider.Yandex));
        Assert.ThrowsExactly<InputException>(() => RegistrationRules.Email("Alex <alex@ya.ru>", MailProvider.Yandex));
        Assert.ThrowsExactly<InputException>(() => RegistrationRules.Email("alex@gmail.com", MailProvider.MailRu));
    }

    [TestMethod]
    public void CsvSupportsHeaderQuotesAndServiceAliases()
    {
        var rows = QueueImport.Parse("provider,login\r\nGmail,\"example123\"\r\nЯндекс,\r\n,ilya24", true, MailProvider.MailRu);

        Assert.HasCount(3, rows);
        Assert.AreEqual(new ImportRow(MailProvider.Gmail, "example123"), rows[0]);
        Assert.AreEqual(new ImportRow(MailProvider.Yandex, ""), rows[1]);
        Assert.AreEqual(new ImportRow(MailProvider.MailRu, "ilya24"), rows[2]);
    }

    [TestMethod]
    public void TxtUsesDefaultProviderAndRejectsUnexpectedColumns()
    {
        var rows = QueueImport.Parse("maria58\nmail.ru|ilya21\nЯндекс|", false, MailProvider.Gmail);

        Assert.HasCount(3, rows);
        Assert.AreEqual(MailProvider.Gmail, rows[0].Provider);
        Assert.AreEqual(MailProvider.MailRu, rows[1].Provider);
        Assert.AreEqual("", rows[2].Login);
        Assert.ThrowsExactly<InputException>(() => QueueImport.Parse("Gmail,example123,secret", true, MailProvider.Gmail));
        Assert.ThrowsExactly<InputException>(() => QueueImport.Parse("Gmail|example123|secret", false, MailProvider.Gmail));
        Assert.ThrowsExactly<InputException>(() => QueueImport.Parse("provider,login\nGmail,\"unfinished", true, MailProvider.Gmail));
    }
}
