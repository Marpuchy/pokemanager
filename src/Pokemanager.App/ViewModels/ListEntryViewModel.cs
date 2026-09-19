using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Services;
using Pokemanager.Model.Editing;

namespace Pokemanager.App.ViewModels;

/// <summary>An item of a side list (Pokémon or move).</summary>
/// <param name="icon">Sprite, decoded only when the item is shown. Null for moves.</param>
/// <param name="types">Current types of the entry (the same twice for one type), shown as a colored strip.</param>
/// <param name="moveCategory">For moves: the current category (its icon is shown next to the type's).</param>
/// <param name="liveName">For entries whose name follows an edited value (a trainer's class); null keeps <paramref name="name"/>.</param>
/// <param name="strip">Colour of the left strip when the entry is not typed (trainers use it for the AI level).</param>
public sealed class ListEntryViewModel(EditorSession session, IReadOnlyList<string> tables, int id, string name, Func<Bitmap?>? icon = null,
    Func<(int, int)>? types = null, Func<int>? moveCategory = null, Func<string>? liveName = null, Func<IBrush>? strip = null) : ObservableObject
{
    public bool IsMove => moveCategory is not null;
    public Bitmap? TypeIcon => IsMove && types?.Invoke() is var (t, _) ? PkhexImages.Type(t) : null;
    public Avalonia.Media.IImage? CategoryIcon => moveCategory is { } c ? PkhexImages.Category(c()) : null;

    public int Id { get; } = id;
    public string Name => liveName?.Invoke() ?? name;
    public string Number => $"#{Id:000}";
    public bool IsModified => tables.Any(t => session.IsModified(t, Id));
    public Bitmap? Icon => icon?.Invoke();

    /// <summary>A picture that is not there yet (a trainer class being fetched) hides its slot instead of leaving a hole.</summary>
    public bool HasIcon => Icon is not null;

    /// <summary>The picture arrived after the row was drawn.</summary>
    public void RefreshIcon()
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
    }

    public IBrush TypeBrush => types?.Invoke() is var (t1, t2) ? TypeColors.Background(t1, t2) : strip?.Invoke() ?? Brushes.Transparent;

    /// <summary>Empty: everything. A number (with or without #): that id. Text: names containing it.</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || (int.TryParse(filter.TrimStart('#'), out int number)
            ? Id == number
            : Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(TypeBrush));
        OnPropertyChanged(nameof(TypeIcon));
        OnPropertyChanged(nameof(CategoryIcon));
    }
}
