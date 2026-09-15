using Avalonia.Controls;

namespace Pokemanager.App.Controls;

/// <summary>A box shown as PKHeX shows it; its data context is an <see cref="ViewModels.IBoxBrowser"/>.</summary>
public partial class BoxBrowser : UserControl
{
    public BoxBrowser() => InitializeComponent();
}
