using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace MailManager.App.Services;

internal sealed class WindowsDesktop
{
    private readonly SecretClipboard _secrets = new();

    internal void Open(Uri signup) => Process.Start(new ProcessStartInfo(signup.AbsoluteUri) { UseShellExecute = true });

    internal bool Confirm(string message, string title) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    internal static string? PickImport() => new OpenFileDialog
    {
        Filter = "Списки задач (*.txt;*.csv)|*.txt;*.csv"
    } is { } dialog && dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;

    internal static string? PickBackup() => new OpenFileDialog
    {
        Filter = "Резервная копия (*.sqlite)|*.sqlite|Все файлы (*.*)|*.*"
    } is { } dialog && dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;

    internal static string? PickBackupDestination() => new SaveFileDialog
    {
        Filter = "Резервная копия SQLite (*.sqlite)|*.sqlite",
        FileName = $"MailManager-{DateTime.Now:yyyy-MM-dd-HHmmss}.sqlite",
        AddExtension = true,
        OverwritePrompt = true
    } is { } dialog && dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;

    internal void Copy(string value) => Clipboard.SetText(value);

    internal void CopySecret(string value, int seconds) => _secrets.Copy(value, seconds);

    internal void ClearSecret() => _secrets.Clear();
}
