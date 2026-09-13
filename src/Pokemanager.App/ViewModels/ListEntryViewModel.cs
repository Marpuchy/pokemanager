using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.Model.Editing;

namespace Pokemanager.App.ViewModels;

/// <summary>An item of a side list (Pokémon or move).</summary>
/// <param name="icon">Sprite, decoded only when the item is shown. Null for moves.</param>
public sealed class ListEntryViewModel(EditorSession session, IReadOnlyList<string> tables, int id, string name, Func<Bitmap?>? icon = null) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Number => $"#{Id:000}";
    public bool IsModified => tables.Any(t => session.IsModified(t, Id));
    public Bitmap? Icon => icon?.Invoke();
    public bool HasIcon => icon is not null;

    /// <summary>Empty: everything. A number (with or without #): that id. Text: names containing it.</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || (int.TryParse(filter.TrimStart('#'), out int number)
            ? Id == number
            : Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

    public void Refresh() => OnPropertyChanged(nameof(IsModified));
}
