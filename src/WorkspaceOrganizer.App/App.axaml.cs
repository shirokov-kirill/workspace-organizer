using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WorkspaceOrganizer.App.ViewModels;
using WorkspaceOrganizer.App.Views;
using WorkspaceOrganizer.Core.Launching;
using WorkspaceOrganizer.Core.Storage;

namespace WorkspaceOrganizer.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(
                new JsonDataStore(JsonDataStore.DefaultPath),
                BrowserCatalog.Detect());
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            // Flush any pending debounced save.
            desktop.Exit += (_, _) => viewModel.SaveNow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
