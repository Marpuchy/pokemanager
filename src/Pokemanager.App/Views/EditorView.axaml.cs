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

        // The randomizer's options are read from UPR ZX (a few seconds) the first time the ROM options are shown.
        MainTabs.SelectionChanged += async (_, e) =>
        {
            if (e.Source != MainTabs || DataContext is not EditorViewModel vm)
                return;
            if (MainTabs.SelectedItem == OptionsTab)
                await vm.Randomizer.LoadOptionsAsync();
        };

        // The mod library is hashed when its tab is first shown, for the same reason: a mod file can be hundreds of
        // megabytes, and a project that never visits the page should not pay for reading it.
        ProjectTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != ProjectTabs || DataContext is not EditorViewModel vm)
                return;
            if (ProjectTabs.SelectedItem == ModsTab)
                vm.Mods.EnsureLoaded();
            if (ProjectTabs.SelectedItem == HistoryTab)
                vm.History.Refresh();
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
