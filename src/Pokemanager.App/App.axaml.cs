using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pokemanager.App.Services;
using Pokemanager.App.ViewModels;
using Pokemanager.App.Views;

namespace Pokemanager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var dialogs = new Dialogs(window);
            window.DataContext = new MainWindowViewModel(dialogs, AppSettings.Load());
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
