using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MailManager.App.ViewModels;

namespace MailManager.App.Views;

public partial class RegistrationView : UserControl
{
    private bool _syncingPassword;

    public RegistrationView()
    {
        InitializeComponent();
        DataContextChanged += OnRegistrationChanged;
    }

    private void OnRegistrationChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.OldValue is RegistrationViewModel oldModel)
            PropertyChangedEventManager.RemoveHandler(oldModel, OnPasswordUpdated, nameof(RegistrationViewModel.DraftPassword));
        if (args.NewValue is RegistrationViewModel model)
            PropertyChangedEventManager.AddHandler(model, OnPasswordUpdated, nameof(RegistrationViewModel.DraftPassword));

        RefreshPasswordBox();
    }

    private void OnPasswordUpdated(object? sender, PropertyChangedEventArgs args) => RefreshPasswordBox();

    private void RefreshPasswordBox()
    {
        var password = (DataContext as RegistrationViewModel)?.DraftPassword ?? "";
        if (DraftPasswordBox.Password == password)
            return;

        _syncingPassword = true;
        try
        {
            DraftPasswordBox.Password = password;
        }
        finally
        {
            _syncingPassword = false;
        }
    }

    private void DraftPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (!_syncingPassword && DataContext is RegistrationViewModel model)
            model.DraftPassword = DraftPasswordBox.Password;
    }
}
