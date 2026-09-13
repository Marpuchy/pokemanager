using System.Globalization;
using Avalonia;
using Pokemanager.App.Services;

namespace Pokemanager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplyLanguage(AppSettings.Load().UiLanguage);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Sets the UI language for the whole process before any window exists. Every assembly's resources follow it.
    /// English is the default; only languages with a translation are accepted.
    /// </summary>
    public static void ApplyLanguage(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(AppSettings.SupportedLanguages.Contains(language) ? language! : AppSettings.DefaultLanguage);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
