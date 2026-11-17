using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using MailManager.Core.Services;

namespace MailManager.App.Services;

internal sealed class UpdateStage(string folder)
{
    private const string Prefix = ".mailmanager-update-";

    internal string Name => Path.GetFileName(folder);
    internal string Payload => Path.Combine(folder, "MailManager.update.exe");
    internal string Patch => Path.Combine(folder, "MailManager.delta");
    internal string Replacement => Path.Combine(folder, "MailManager.new.exe");
    internal string Backup => Path.Combine(folder, "MailManager.previous.exe");
    internal string Ready => Path.Combine(folder, "ready");
    internal string Started => Path.Combine(folder, "started");

    internal static UpdateStage Create(string executable)
    {
        if (!IsMailManager(executable))
            throw new InputException("Запустите автономный MailManager.exe, чтобы обновить приложение.");

        var folder = Path.Combine(Path.GetDirectoryName(executable)!, Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return new UpdateStage(folder);
    }

    internal static UpdateStage Open(string executableFolder, string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
            throw new IOException("Неверная папка обновления.");

        var folder = Path.Combine(executableFolder, name);
        if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Папка обновления недоступна.");

        return new UpdateStage(folder);
    }

    internal static bool IsMailManager(string path) =>
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
        && FileVersionInfo.GetVersionInfo(path).ProductName == "MailManager";

    internal void Clean()
    {
        try
        {
            foreach (var file in new[] { Payload, Patch, Replacement, Backup, Ready, Started })
                File.Delete(file);
            Directory.Delete(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Оставляем временные файлы, если Windows ещё держит процесс или папку открытыми.
        }
    }
}

internal static class UpdateInstaller
{
    internal const string ApplyArgument = "--apply-mailmanager-update";
    private const string CompleteArgument = "--confirm-mailmanager-update";

    internal static Version CurrentVersion { get; } = GetCurrentVersion();

    private static Version GetCurrentVersion()
    {
        var assembly = typeof(UpdateInstaller).Assembly.GetName().Version!;
        return new Version(assembly.Major, assembly.Minor, assembly.Build);
    }

    internal static async Task HandOffAsync(UpdateStage stage, string target, UpdateRelease release)
    {
        var info = FileVersionInfo.GetVersionInfo(stage.Payload);
        if (info.ProductName != "MailManager" ||
            new Version(info.ProductMajorPart, info.ProductMinorPart, info.ProductBuildPart) != release.Version)
        {
            throw new InputException("Версия файла не совпала с номером релиза GitHub.");
        }

        using var helper = Start(stage.Payload, ApplyArgument, Environment.ProcessId.ToString(),
            Path.GetFileName(target), Convert.ToHexString(release.Digest));
        if (await WaitForMarkerAsync(stage.Ready, helper, TimeSpan.FromSeconds(20)))
            return;

        if (!helper.HasExited)
        {
            helper.Kill();
            await helper.WaitForExitAsync();
        }

        throw new InputException("Не удалось подготовить замену MailManager.exe.");
    }

    internal static async Task<int> ApplyAsync(string[] arguments)
    {
        var ready = false;
        try
        {
            if (arguments.Length != 4 || !int.TryParse(arguments[1], out var originalId) || originalId <= 0 ||
                arguments[2].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                arguments[3].Length != 64 || !arguments[3].All(Uri.IsHexDigit))
            {
                return 1;
            }

            var executable = Environment.ProcessPath!;
            var folder = Path.GetDirectoryName(executable)!;
            var installFolder = Path.GetDirectoryName(folder)!;
            var stage = UpdateStage.Open(installFolder, Path.GetFileName(folder));
            var target = Path.Combine(installFolder, arguments[2]);
            using var original = Process.GetProcessById(originalId);
            if (!string.Equals(executable, stage.Payload, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(original.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase) ||
                !UpdateStage.IsMailManager(target))
            {
                return 1;
            }

            await using (var payload = File.OpenRead(stage.Payload))
            {
                if (!Convert.ToHexString(await SHA256.HashDataAsync(payload)).Equals(arguments[3], StringComparison.OrdinalIgnoreCase))
                    return 1;
            }

            File.Copy(stage.Payload, stage.Replacement);
            File.WriteAllText(stage.Ready, "");
            ready = true;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await original.WaitForExitAsync(limit.Token);
            await ReplaceAndStartAsync(stage, target);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            Win32Exception or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            if (ready)
            {
                MessageBox.Show("Не удалось установить обновление. Проверьте доступ к папке MailManager.exe и запустите приложение снова.",
                    "Mail Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return 1;
        }
    }

    private static async Task ReplaceAndStartAsync(UpdateStage stage, string target)
    {
        File.Replace(stage.Replacement, target, stage.Backup);
        Process? replacement = null;
        try
        {
            replacement = Start(target, CompleteArgument, stage.Name, Environment.ProcessId.ToString());
            if (await WaitForMarkerAsync(stage.Started, replacement, TimeSpan.FromSeconds(30)))
                return;

            if (!replacement.HasExited)
            {
                // Действующее приложение может быть просто занято запуском. Оставляем резервную копию.
                return;
            }
        }
        catch (Win32Exception)
        {
            // Ошибка создания процесса также позволяет вернуть предыдущий EXE.
        }
        finally
        {
            replacement?.Dispose();
        }

        File.Replace(stage.Backup, target, null);
        MessageBox.Show("Новая версия не запустилась. Возвращена предыдущая сборка Mail Manager.", "Mail Manager",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        using var original = Start(target);
    }

    internal static bool ConfirmStartup(string[] arguments)
    {
        if (arguments.Length != 3 || arguments[0] != CompleteArgument ||
            !int.TryParse(arguments[2], out var helperId) || helperId <= 0)
            return false;

        var executableFolder = Path.GetDirectoryName(Environment.ProcessPath!)!;
        var stage = UpdateStage.Open(executableFolder, arguments[1]);
        File.WriteAllText(stage.Started, "");
        _ = CleanWhenHelperExitsAsync(stage, helperId);
        return true;
    }

    private static async Task CleanWhenHelperExitsAsync(UpdateStage stage, int helperId)
    {
        try
        {
            using var helper = Process.GetProcessById(helperId);
            if (string.Equals(helper.MainModule?.FileName, stage.Payload, StringComparison.OrdinalIgnoreCase))
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                await helper.WaitForExitAsync(limit.Token);
            }
        }
        catch (ArgumentException)
        {
            // Помощник уже завершился.
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            return;
        }

        stage.Clean();
    }

    private static Process Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new Win32Exception("The Mail Manager process could not start.");
    }

    private static async Task<bool> WaitForMarkerAsync(string marker, Process process, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!File.Exists(marker) && !process.HasExited && watch.Elapsed < timeout)
            await Task.Delay(125);

        return File.Exists(marker);
    }
}
