using Avalonia.Controls;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

public partial class RandomizerView : UserControl
{
    public RandomizerView()
    {
        InitializeComponent();
        // Options are read from UPR (takes a few seconds) only when their tab is opened.
        Tabs.SelectionChanged += async (_, _) =>
        {
            if (Tabs.SelectedIndex == 1 && DataContext is RandomizerViewModel vm)
                await vm.LoadOptionsAsync();
        };
    }
}
