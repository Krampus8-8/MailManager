using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using MailManager.App.ViewModels;

namespace MailManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void ToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void ResetPageScroll(object sender, DataTransferEventArgs e) => PageScroller.ScrollToTop();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var expanded = WindowState == WindowState.Maximized;
        MaximizeButton.Content = expanded ? "\uE923" : "\uE922";
        var label = expanded ? "Восстановить размер" : "Развернуть";
        MaximizeButton.ToolTip = label;
        AutomationProperties.SetName(MaximizeButton, label);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is ShellViewModel { IsBusy: true })
            e.Cancel = true;
        base.OnClosing(e);
    }
}
