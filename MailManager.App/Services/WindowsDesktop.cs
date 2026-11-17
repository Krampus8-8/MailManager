using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace MailManager.App.Services;

internal sealed class WindowsDesktop
{
    private readonly SecretClipboard _secrets = new();

    internal void Open(Uri signup) => Process.Start(new ProcessStartInfo(signup.AbsoluteUri) { UseShellExecute = true });

    internal bool Confirm(string message, string title) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    internal void ShowPassword(string password)
    {
        var owner = Application.Current.MainWindow;
        var close = new Button { Content = "Закрыть", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var value = new TextBlock
        {
            Text = password,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        };
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(value);
        content.Children.Add(close);
        var dialog = new Window
        {
            Title = "Пароль",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 410,
            Background = (Brush)owner.FindResource("Canvas"),
            Foreground = (Brush)owner.FindResource("Text"),
            Content = content
        };
        dialog.Closed += (_, _) => value.Text = "";
        dialog.ShowDialog();
    }

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

    internal static string? PickExtensionDestination() => new SaveFileDialog
    {
        Title = "Сохранить расширение для Chrome и Edge",
        Filter = "ZIP-архив (*.zip)|*.zip",
        FileName = "GmailExtension.zip",
        AddExtension = true,
        OverwritePrompt = true
    } is { } dialog && dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;

    internal void Copy(string value) => Clipboard.SetText(value);

    internal void CopySecret(string value, int seconds) => _secrets.Copy(value, seconds);

    internal void ClearSecret() => _secrets.Clear();
}
