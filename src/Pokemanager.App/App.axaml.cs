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
        // The user's own mods (Documents\Pokemanager\Mods): read before any project opens, so one that has a mod of
        // theirs on finds it. Whatever goes wrong here is reported by the Mods tab, which scans again.
        Pokemanager.Model.Mods.UserMods.Root = System.IO.Path.Combine(AppSettings.DocumentsRoot, "Mods");
        try
        {
            Pokemanager.Model.Mods.UserMods.Scan();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();
            AppTheme.Configure(settings.DarkMode, settings.Background);
            var window = new MainWindow();
            var dialogs = new Dialogs(window);
            var main = new MainWindowViewModel(dialogs, settings);
            window.DataContext = main;
            window.Closing += (_, _) => main.Room.CloseForExit(); // tells the room and closes the router port
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
