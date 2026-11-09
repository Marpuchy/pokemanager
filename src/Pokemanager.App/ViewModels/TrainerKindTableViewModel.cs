using Pokemanager.App.Resources;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The trainer options that come in three — one for ordinary trainers, one for the important ones, one for the bosses —
/// as one table instead of nine loose controls (2026-11-09, user: "a table with the options, more tidy"): a row per
/// option, a column per kind. Rows are the randomizer's own (extra Pokémon, random held items) and this application's
/// (mega and Z-crystal from a level), each cell bound to the control it replaces, so nothing is stored twice.
/// </summary>
public sealed class TrainerKindTableViewModel(IReadOnlyList<TrainerKindRowViewModel> rows)
{
    public IReadOnlyList<TrainerKindRowViewModel> Rows { get; } = rows;
    public bool HasRows => Rows.Count > 0;

    public static string RegularHeader => Strings.Role_Ordinary;
    public static string ImportantHeader => Strings.Role_Important;
    public static string BossHeader => Strings.Role_Bosses;
}

/// <summary>One option of the table: its name, what it does, and its three cells (ordinary, important, bosses).</summary>
public sealed record TrainerKindRowViewModel(string Label, string? Tip, TrainerKindCellViewModel Regular,
    TrainerKindCellViewModel Important, TrainerKindCellViewModel Boss, bool IsOurs);

/// <summary>A cell: one of the randomizer's options (a switch or a number), or one of ours (a switch and a level).</summary>
public sealed class TrainerKindCellViewModel
{
    private TrainerKindCellViewModel() { }

    public UprOptionViewModel? Option { get; private init; }
    public TrainerItemRowViewModel? Items { get; private init; }
    public bool IsMega { get; private init; }

    public bool IsSwitch => Option is { IsBool: true };
    public bool IsNumber => Option is { IsInt: true };
    public bool IsMegaCell => Items is not null && IsMega;
    public bool IsZCell => Items is not null && !IsMega;

    public static TrainerKindCellViewModel For(UprOptionViewModel option) => new() { Option = option };
    public static TrainerKindCellViewModel Mega(TrainerItemRowViewModel row) => new() { Items = row, IsMega = true };
    public static TrainerKindCellViewModel Z(TrainerItemRowViewModel row) => new() { Items = row };
}
