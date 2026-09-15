using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Pokemanager.App.Services;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

/// <summary>Pokémon Showdown's battle screen for one battle of the room (see <see cref="BattleViewModel"/>).</summary>
public partial class BattleWindow : Window
{
    private BattleViewModel? viewModel;

    public BattleWindow()
    {
        InitializeComponent();
        WebView.EnvironmentRequested += (_, e) =>
        {
            // The browser profile (cache of Showdown's scripts and sprites) goes with Pokemanager's data, not next to the app.
            if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                windows.UserDataFolder = Path.Combine(AppSettings.DataRoot, "webview");
        };
        WebView.WebMessageReceived += (_, e) =>
        {
            if (e.Body is { } body)
                viewModel?.OnPageMessage(body);
        };
        DataContextChanged += (_, _) => Attach(DataContext as BattleViewModel);
        Closed += (_, _) =>
        {
            if (viewModel is { } vm)
            {
                vm.ScriptRequested -= RunScript;
                vm.ActivateRequested -= Activate;
                vm.OnClosed();
            }
            viewModel = null;
        };
    }

    private void Attach(BattleViewModel? vm)
    {
        if (ReferenceEquals(vm, viewModel))
            return;
        if (viewModel is { } old)
        {
            old.ScriptRequested -= RunScript;
            old.ActivateRequested -= Activate;
        }
        viewModel = vm;
        if (vm is null)
            return;
        vm.ScriptRequested += RunScript;
        vm.ActivateRequested += Activate;
        WebView.Source = BattleViewModel.PageUri;
    }

    private void RunScript(string script) => Dispatcher.UIThread.Post(async () =>
    {
        try
        {
            await WebView.InvokeScript(script);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The page is reloading or the window is closing: it asks for everything again when it is ready.
        }
    });
}
