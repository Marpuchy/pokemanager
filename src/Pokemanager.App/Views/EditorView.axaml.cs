using Avalonia.Controls;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        // The save is read when its tab is first shown; history differences are recomputed when it is shown.
        MainTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != MainTabs || DataContext is not EditorViewModel vm)
                return;
            if (MainTabs.SelectedItem == SaveTab)
                vm.SaveEditor.EnsureOpen();
            else if (MainTabs.SelectedItem == HistoryTab)
                vm.History.Refresh();
        };
    }
}
