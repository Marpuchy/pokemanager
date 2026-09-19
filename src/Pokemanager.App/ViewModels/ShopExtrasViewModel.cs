using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The application's own item options, shown among the randomizer's on the Items page: what the randomizer is allowed
/// to place, and what the built ROM's shops sell beyond what the game does.
/// </summary>
/// <remarks>
/// They are not all of a kind and the tooltips say so: the two pool options belong to the randomization (the ROM has to
/// be randomized again and the seed alone no longer describes the result), while the Rare Candies are a project setting
/// a rebuild applies. The shops live in a table this application has to find, so a game it cannot find it in gets a
/// disabled check box that says why.
/// </remarks>
public sealed partial class ShopExtrasViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private bool loading;

    public ShopExtrasViewModel(EditorViewModel editor)
    {
        this.editor = editor;
        Supported = GameShops.For(editor.Session.Current.Title) is not null && editor.Session.Current.Items.Length > 0;
        Load();
    }

    /// <summary>Whether the game is one whose shops this application knows how to change.</summary>
    public bool Supported { get; }

    /// <summary>On hover: what the option does, or why it cannot be used in this game.</summary>
    public string FreeRareCandiesTip => Supported
        ? Strings.Shops_FreeCandiesTip
        : string.Format(Strings.Shops_Unsupported, editor.Session.Current.Title.DisplayName());

    [ObservableProperty]
    public partial bool FreeRareCandies { get; set; }

    /// <summary>
    /// The game's Mega Stones stop counting as bad items, so the randomizer can place them wherever it places items.
    /// Part of the randomization, like <see cref="AllowAllItems"/>: the ROM has to be randomized again.
    /// </summary>
    public bool MegaStonesInPool
    {
        get => editor.Session.Project.Randomization.MegaStonesInPool;
        set
        {
            if (editor.Session.Project.Randomization.MegaStonesInPool == value)
                return;
            editor.Session.Project.Randomization.MegaStonesInPool = value;
            OnPropertyChanged();
            editor.MarkDirty(Strings.Items_MegaStonesUndo);
        }
    }

    /// <summary>"42 Mega Stones", so the option says what the game actually has instead of a promise.</summary>
    public string MegaStonesText => string.Format(Strings.Shops_MegaStonesCount, editor.Session.Current.MegaStones.Length);

    /// <summary>A game with no mega evolutions at all (or a folder imported before this version) does not offer it.</summary>
    public bool HasMegaStones => editor.Session.Current.MegaStones.Length > 0;

    /// <summary>
    /// Let the randomizer use every item of the game. Like the Mega Stones it belongs to the randomization, so changing
    /// it means the ROM has to be randomized again — the seed alone no longer describes the result.
    /// </summary>
    public bool AllowAllItems
    {
        get => editor.Session.Project.Randomization.AllowAllItems;
        set
        {
            if (editor.Session.Project.Randomization.AllowAllItems == value)
                return;
            editor.Session.Project.Randomization.AllowAllItems = value;
            OnPropertyChanged();
            editor.MarkDirty(Strings.Items_AllowAllUndo);
        }
    }

    private ShopSettings Settings => editor.Session.Project.Shops;

    private void Load()
    {
        loading = true;
        FreeRareCandies = Settings.FreeRareCandies;
        loading = false;
    }

    partial void OnFreeRareCandiesChanged(bool value)
    {
        if (loading || Settings.FreeRareCandies == value)
            return;
        Settings.FreeRareCandies = value;
        editor.MarkDirty(Strings.Shops_UndoCandies);
    }

    /// <summary>The project was replaced (undo, restore): show what it holds now.</summary>
    public void Refresh()
    {
        Load();
        OnPropertyChanged(nameof(AllowAllItems));
        OnPropertyChanged(nameof(MegaStonesInPool));
    }
}
