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

    /// <summary>
    /// How many extras the biggest ordinary Poké Mart can take, and how many the smallest one can. A shop sells a fixed
    /// number of things and one slot is always left as the game had it, so a long list does not fit everywhere — and
    /// what does not fit is not an error, it simply never shows up.
    /// </summary>
    private IReadOnlyList<int> Room =>
        GameShops.For(editor.Session.Current.Title) is { } layout
            ? [.. layout.Regular.Select(shop => Math.Max(0, layout.Sizes[shop] - 1))]
            : [];

    /// <summary>How many things are being asked for: the list, plus the Rare Candy when it is switched on.</summary>
    private int Asked => Extra.Count + (FreeRareCandies ? 1 : 0);

    /// <summary>A line saying where they will and will not appear; empty when there is nothing to say yet.</summary>
    public string FitText
    {
        get
        {
            var room = Room;
            if (!Supported || Asked == 0 || room.Count == 0)
                return "";
            int most = room.Max();
            if (Asked > most)
                return string.Format(Strings.Shops_FitOverflow, Asked, most);
            // A shop that can only take fewer than the list sells the last ones of it; the count that means something
            // is how many Poké Marts sell the whole list.
            int whole = room.Count(r => r >= Asked);
            return whole == room.Count
                ? string.Format(Strings.Shops_FitAll, Asked)
                : string.Format(Strings.Shops_FitSome, Asked, whole, room.Count);
        }
    }

    /// <summary>Whether some of them will not appear in any shop at all, which the card says in orange.</summary>
    public bool Overflows => Supported && Room.Count > 0 && Asked > Room.Max();

    private void NotifyFit()
    {
        OnPropertyChanged(nameof(FitText));
        OnPropertyChanged(nameof(Overflows));
    }

    public string UnsupportedText => string.Format(Strings.Shops_Unsupported, editor.Session.Current.Title.DisplayName());

    public IReadOnlyList<string> ItemNames { get; }

    /// <summary>Items added to every ordinary Poké Mart, as rows with their name.</summary>
    public ObservableCollection<ShopItemViewModel> Extra { get; } = [];

    [ObservableProperty]
    public partial int SelectedItem { get; set; } = -1;

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
    /// Let the randomizer use every item of the game. Unlike the rest of this card it belongs to the randomization, so
    /// changing it means the ROM has to be randomized again — the seed alone no longer describes the result.
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
        NotifyFit();
        editor.MarkDirty(Strings.Shops_UndoCandies);
    }

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedItem <= 0 || Settings.ExtraItems.Contains(SelectedItem))
            return;
        Settings.ExtraItems.Add(SelectedItem);
        Extra.Add(new ShopItemViewModel(this, SelectedItem, Name(SelectedItem)));
        NotifyFit();
        editor.MarkDirty(string.Format(Strings.Shops_UndoAdd, Name(SelectedItem)));
    }

    internal void Remove(ShopItemViewModel item)
    {
        if (!Settings.ExtraItems.Remove(item.Id))
            return;
        Extra.Remove(item);
        NotifyFit();
        editor.MarkDirty(string.Format(Strings.Shops_UndoRemove, item.Name));
    }

    /// <summary>The project was replaced (undo, restore): show what it holds now.</summary>
    public void Refresh()
    {
        Load();
        NotifyFit();
        OnPropertyChanged(nameof(AllowAllItems));
        OnPropertyChanged(nameof(MegaStonesInPool));
    }
}

/// <summary>One item on the extra list, with the button that takes it off.</summary>
public sealed partial class ShopItemViewModel(ShopExtrasViewModel owner, int id, string name) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}
