using System.IO;
using System.Windows;
using MailManager.App.ViewModels;
using MailManager.App.Views;
using MailManager.Data.Repositories;

namespace MailManager.App;

public partial class App : Application
{
    private FileStream? _instanceLock;
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailManager");
        var database = Path.Combine(folder, "mailmanager.sqlite");

        try
        {
            Directory.CreateDirectory(folder);
            _instanceLock = new FileStream(Path.Combine(folder, "app.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            MessageBox.Show("Mail Manager уже запущен или папка данных недоступна.", "Mail Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Windows отказала в доступе к папке данных Mail Manager.", "Mail Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var protector = new WindowsSecretProtector();
        _shell = new ShellViewModel(new WorkspaceStore(database, protector), protector, database);
        MainWindow = new MainWindow { DataContext = _shell };
        MainWindow.Show();
        _ = _shell.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Desktop.ClearSecret();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
