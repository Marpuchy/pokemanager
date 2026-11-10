using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The Encuentros tab (2026-11-09, user chose one sub-tab per kind of encounter): for each kind, the randomizer's
/// options about it and, beside them, the level percentage this application writes when the ROM is built. The
/// randomizer keeps starters, statics and trades in one group of its own; here they are three kinds.
/// </summary>
public sealed class EncountersViewModel : ObservableObject
{
    private static readonly string[] WildNames = ["WildPokemonMod", "WildPokemonRestrictionMod", "UseTimeBasedEncounters",
        "BlockWildLegendaries", "AllowWildAltFormes", "BalanceShakingGrass", "UseMinimumCatchRate", "MinimumCatchRateLevel",
        "RandomizeWildPokemonHeldItems", "BanBadRandomWildPokemonHeldItems", "WildLevelsModified", "WildLevelModifier"];
    private static readonly string[] TotemNames = ["TotemPokemonMod", "AllyPokemonMod", "AuraMod", "RandomizeTotemHeldItems",
        "AllowTotemAltFormes", "TotemLevelsModified", "TotemLevelModifier"];
    private static readonly string[] StarterNames = ["StartersMod", "AllowStarterAltFormes", "RandomizeStartersHeldItems",
        "BanBadRandomStarterHeldItems"];
    private static readonly string[] StaticNames = ["StaticPokemonMod", "AllowStaticAltFormes", "SwapStaticMegaEvos",
        "LimitMainGameLegendaries", "Limit600", "StaticLevelModified", "StaticLevelModifier", "CorrectStaticMusic"];
    private static readonly string[] TradeNames = ["InGameTradesMod", "RandomizeInGameTradesNicknames", "RandomizeInGameTradesOTs",
        "RandomizeInGameTradesIVs", "RandomizeInGameTradesItems"];

    public UprOptionsViewModel Options { get; }

    public EncountersViewModel(UprOptionsViewModel options)
    {
        Options = options;
        options.PropertyChanged += OnOptionsChanged;
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UprOptionsViewModel.IsLoaded) or null or "")
            OnPropertyChanged(string.Empty);
    }

    public IReadOnlyList<UprOptionViewModel> Wild => Options.Shown(WildNames);
    public IReadOnlyList<UprOptionViewModel> Totems => Options.Shown(TotemNames);
    public IReadOnlyList<UprOptionViewModel> Starters => Options.Shown(StarterNames);
    public IReadOnlyList<UprOptionViewModel> Statics => Options.Shown(StaticNames);
    public IReadOnlyList<UprOptionViewModel> Trades => Options.Shown(TradeNames);

    /// <summary>Totems, allies and auras exist only in Generation 7.</summary>
    public bool HasTotems => Totems.Count > 0;
}
