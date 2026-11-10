using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// One subject of the editor (Pokémon, moves, encounters, trainers, items, rules) as the option pages see it: the
/// randomizer's groups about it (shown under Aleatorio) and the groups whose own cards it shows (Ajustes). The groups
/// are rebuilt every time the options load, so the lists are read again then.
/// </summary>
public sealed class UprSubjectViewModel : ObservableObject
{
    private readonly string[] random;
    private readonly string[] settings;

    public UprOptionsViewModel Options { get; }

    public UprSubjectViewModel(UprOptionsViewModel options, string[] random, string[] settings)
    {
        Options = options;
        this.random = random;
        this.settings = settings;
        options.PropertyChanged += OnOptionsChanged;
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UprOptionsViewModel.IsLoaded) or null or "")
        {
            OnPropertyChanged(nameof(Groups));
            OnPropertyChanged(nameof(SettingsGroups));
            OnPropertyChanged(nameof(HasRandom));
            OnPropertyChanged(nameof(ShowTitles));
            OnPropertyChanged(nameof(AllSections));
            OnPropertyChanged(nameof(HasAllSections));
            OnPropertyChanged(nameof(HasRandomShops));
            OnPropertyChanged(nameof(HasSettings));
        }
    }

    /// <summary>The randomizer's groups of this subject that have options to show.</summary>
    public IReadOnlyList<UprOptionGroupViewModel> Groups => Options.GroupsFor(random, withOptions: true);

    /// <summary>The groups whose own cards (this application's) belong to this subject.</summary>
    public IReadOnlyList<UprOptionGroupViewModel> SettingsGroups => Options.GroupsFor(settings, withOptions: false);

    public bool HasRandom => random.Length > 0;

    /// <summary>
    /// Ours that act when randomizing (the item pool: every item, Mega Stones), shown with the randomizer's options of
    /// the Items subject rather than with what is written at build (2026-11-09, user).
    /// </summary>
    public ShopExtrasViewModel? RandomShops { get; init; }

    public bool HasRandomShops => RandomShops is not null && Options.IsLoaded;

    /// <summary>A title over each group only where the subject has more than one (Encuentros, Pokémon, Movimientos).</summary>
    public bool ShowTitles => Groups.Count > 1 && Groups.Any(g => !g.HasSections);

    /// <summary>Every group of the subject has titled cards: they flow together in one set of columns.</summary>
    public bool HasAllSections => Groups.Count > 0 && Groups.All(g => g.HasSections);

    public IReadOnlyList<UprOptionSectionViewModel> AllSections => HasAllSections ? [.. Groups.SelectMany(g => g.Sections)] : [];
    public bool HasSettings => settings.Length > 0;
}
