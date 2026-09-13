using Avalonia.Controls;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

public partial class RandomizerView : UserControl
{
    public RandomizerView()
    {
        InitializeComponent();
        // Las opciones se leen de UPR (tarda unos segundos) solo cuando se abre su pestaña.
        Tabs.SelectionChanged += async (_, _) =>
        {
            if (Tabs.SelectedIndex == 1 && DataContext is RandomizerViewModel vm)
                await vm.LoadOptionsAsync();
        };
    }
}
