using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// What the built ROM's shops sell beyond what the game does: free Rare Candies, and items the game never puts on sale
/// (the Mega Stones of a game that has none, an evolution item a randomized run made necessary).
/// </summary>
/// <remarks>
/// These are not randomizer settings: they live in the project, survive a seed change and do not need randomizing
/// again. The shops are a table inside <c>code.bin</c>, so only the games <see cref="GameShops"/> can find it in are
/// offered; the rest see why not.
/// </remarks>
public sealed partial class ShopExtrasViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private bool loading;

    public ShopExtrasViewModel(EditorViewModel editor)
    {
        this.editor = editor;
        ItemNames = editor.Names.ItemChoices;
        Supported = GameShops.For(editor.Session.Current.Title) is not null && editor.Session.Current.Items.Length > 0;
        Load();
    }

    /// <summary>Whether the game is one whose shops this application knows how to change.</summary>
    public bool Supported { get; }

    public string UnsupportedText => string.Format(Strings.Shops_Unsupported, editor.Session.Current.Title.DisplayName());

    public IReadOnlyList<string> ItemNames { get; }

    /// <summary>Items added to every ordinary Poké Mart, as rows with their name.</summary>
    public ObservableCollection<ShopItemViewModel> Extra { get; } = [];

    [ObservableProperty]
    public partial int SelectedItem { get; set; } = -1;

    [ObservableProperty]
    public partial bool FreeRareCandies { get; set; }

    public string RareCandyName =>
        GameShops.RareCandy < ItemNames.Count ? ItemNames[GameShops.RareCandy] : $"#{GameShops.RareCandy}";

    private ShopSettings Settings => editor.Session.Project.Shops;

    private void Load()
    {
        loading = true;
        FreeRareCandies = Settings.FreeRareCandies;
        Extra.Clear();
        foreach (int id in Settings.ExtraItems)
            Extra.Add(new ShopItemViewModel(this, id, Name(id)));
        loading = false;
    }

    private string Name(int id) => id >= 0 && id < ItemNames.Count ? ItemNames[id] : $"#{id}";

    partial void OnFreeRareCandiesChanged(bool value)
    {
        if (loading || Settings.FreeRareCandies == value)
            return;
        Settings.FreeRareCandies = value;
        editor.MarkDirty(Strings.Shops_UndoCandies);
    }

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedItem <= 0 || Settings.ExtraItems.Contains(SelectedItem))
            return;
        Settings.ExtraItems.Add(SelectedItem);
        Extra.Add(new ShopItemViewModel(this, SelectedItem, Name(SelectedItem)));
        editor.MarkDirty(string.Format(Strings.Shops_UndoAdd, Name(SelectedItem)));
    }

    internal void Remove(ShopItemViewModel item)
    {
        if (!Settings.ExtraItems.Remove(item.Id))
            return;
        Extra.Remove(item);
        editor.MarkDirty(string.Format(Strings.Shops_UndoRemove, item.Name));
    }

    /// <summary>The project was replaced (undo, restore): show what it holds now.</summary>
    public void Refresh() => Load();
}

/// <summary>One item on the extra list, with the button that takes it off.</summary>
public sealed partial class ShopItemViewModel(ShopExtrasViewModel owner, int id, string name) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}
