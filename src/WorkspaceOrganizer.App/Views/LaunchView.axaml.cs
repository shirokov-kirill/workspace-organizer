using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WorkspaceOrganizer.App.ViewModels;

namespace WorkspaceOrganizer.App.Views;

public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();
    }

    async void OnBrowseExecutable(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { SelectedApp: { } app } || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the app's executable",
            AllowMultiple = false,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            app.Path = path;
    }
}
