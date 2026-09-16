using Avalonia.Controls;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        // The save is read when its tab is first shown.
        MainTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != MainTabs || DataContext is not EditorViewModel vm)
                return;
            if (MainTabs.SelectedItem == SaveTab)
                vm.SaveEditor.EnsureOpen();
        };

        // Opened from the project preview on a Pokémon of the save: go straight to it.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EditorViewModel { PendingSaveSlot: { } slot } vm)
            {
                vm.PendingSaveSlot = null;
                MainTabs.SelectedItem = SaveTab;
                vm.SaveEditor.EnsureOpen();
                vm.SaveEditor.SelectSlot(slot);
            }
        };
    }
}
