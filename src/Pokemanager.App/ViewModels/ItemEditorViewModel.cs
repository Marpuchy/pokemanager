using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// Options → Objetos → editing an item (2026-11-12, user: "edit the price, the name and the description"): pick one by
/// name, change what the shops charge for it, its name and its description. Name and description start from the game's
/// English text and go to every language when the ROM is built, like the move descriptions; the price is the item
/// table's. All three are ordinary edits, so they undo, show as modified and revert.
/// </summary>
public sealed partial class ItemEditorViewModel : ObservableObject
{
    private readonly EditorViewModel editor;

    public ItemEditorViewModel(EditorViewModel editor)
    {
        this.editor = editor;
        Filter();
    }

    private Model.Editing.EditorSession Session => editor.Session;

    public bool Supported => Session.Current.Items.Length > 0 && Session.Current.ItemNames.Length > 0;

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    partial void OnSearchChanged(string value) => Filter();

    public ObservableCollection<ItemChoice> Matches { get; } = [];

    [ObservableProperty]
    public partial ItemChoice? Selected { get; set; }

    partial void OnSelectedChanged(ItemChoice? value) => RefreshFields();

    private static string Fold(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    /// <summary>The items whose name (in the interface's language or as edited) contains what was typed; empty ids left out.</summary>
    private void Filter()
    {
        var names = editor.Names.Items;
        int count = Math.Min(Session.Current.Items.Length, Session.Current.ItemNames.Length);
        string wanted = Fold(Search.Trim());
        var keep = Selected;
        Matches.Clear();
        for (int id = 1; id < count; id++)
        {
            string shown = id < names.Count ? names[id] : "";
            string own = Session.Current.ItemNames[id];
            if (string.IsNullOrWhiteSpace(shown) && string.IsNullOrWhiteSpace(own) || shown is "???" or "(?)")
                continue;
            if (wanted.Length > 0 && !Fold(shown).Contains(wanted) && !Fold(own).Contains(wanted) && wanted != id.ToString(CultureInfo.InvariantCulture))
                continue;
            Matches.Add(new ItemChoice(id, $"#{id:000} {(string.IsNullOrWhiteSpace(shown) ? own : shown)}"));
            if (Matches.Count >= 400)
                break;
        }
        Selected = keep is not null && Matches.FirstOrDefault(m => m.Id == keep.Id) is { } same ? same : Matches.FirstOrDefault();
    }

    public bool HasSelection => Selected is not null;
    private int Id => Selected?.Id ?? 0;

    public string Name
    {
        get => Selected is null ? "" : Session.Get(GameTables.ItemTexts, Id, "name").GetValue<string>();
        set => Write(GameTables.ItemTexts, "name", JsonValue.Create(value ?? ""), Name != (value ?? ""));
    }

    public string Description
    {
        get => Selected is null ? "" : Session.Get(GameTables.ItemTexts, Id, GameTables.Description).GetValue<string>();
        set => Write(GameTables.ItemTexts, GameTables.Description, JsonValue.Create(value ?? ""), Description != (value ?? ""));
    }

    /// <summary>What a shop charges for one (the game keeps a tenth of it, so it goes in steps of 10).</summary>
    public decimal Price
    {
        get => Selected is null ? 0 : Session.GetInt(GameTables.Items, Id, "price");
        set => Write(GameTables.Items, "price", JsonValue.Create((int)value / 10 * 10), (int)value / 10 * 10 != (int)Price);
    }

    public string OriginalText => Selected is null ? "" : string.Format(Strings.ItemEdit_Original,
        Session.GetOriginal(GameTables.ItemTexts, Id, "name").GetValue<string>(), Session.GetOriginal(GameTables.Items, Id, "price").GetValue<int>());

    public string OriginalDescription => Selected is null ? "" : Session.GetOriginal(GameTables.ItemTexts, Id, GameTables.Description).GetValue<string>();

    public bool IsModified => Selected is not null && (Session.IsModified(GameTables.ItemTexts, Id) || Session.IsModified(GameTables.Items, Id));

    private void Write(string table, string field, JsonNode value, bool changed)
    {
        if (Selected is null || !changed)
            return;
        Session.Set(table, Id, field, value);
        OnPropertyChanged(nameof(IsModified));
        if (field == "price")
            OnPropertyChanged(nameof(Price));
    }

    [RelayCommand]
    private void Revert()
    {
        if (Selected is null)
            return;
        Session.Revert(GameTables.ItemTexts, Id);
        Session.Revert(GameTables.Items, Id);
        RefreshFields();
    }

    /// <summary>The project's changes were replaced (undo, history, a new session): show what is stored now.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Supported));
        Filter();
        RefreshFields();
    }

    private void RefreshFields()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Price));
        OnPropertyChanged(nameof(OriginalText));
        OnPropertyChanged(nameof(OriginalDescription));
        OnPropertyChanged(nameof(IsModified));
    }
}

public sealed record ItemChoice(int Id, string Label)
{
    public override string ToString() => Label;
}
