using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A read-only stat row of a party Pokémon.</summary>
public sealed record StatLine(string Label, int Base, int Iv, int Ev, int Value);

/// <summary>A read-only move of a party Pokémon.</summary>
public sealed record MoveLine(string Name, string PP);

/// <summary>A Pokémon of the party in the project preview. Nothing here can be edited.</summary>
public sealed partial class PartyMemberViewModel : ObservableObject
{
    private readonly PK6 pk;
    private readonly SaveDocument doc;
    private readonly SaveNames names;
    private readonly PokemonSprites sprites;

    public SaveSlot Slot { get; }

    public PartyMemberViewModel(SaveDocument doc, SaveNames names, PokemonSprites sprites, SaveSlot slot)
    {
        this.doc = doc;
        this.names = names;
        this.sprites = sprites;
        Slot = slot;
        pk = doc.Get(slot);
    }

    public Bitmap? Icon => pk.IsEgg ? null : sprites.For(pk.Species, pk.Form, pk.Gender == 1, pk.IsShiny);
    public string Name => pk.IsEgg ? Strings.Save_Egg : pk.IsNicknamed ? pk.Nickname : names.SpeciesName(pk.Species);
    public string SpeciesName => names.SpeciesName(pk.Species);
    public bool HasNickname => pk.IsNicknamed && !pk.IsEgg;
    public string LevelText => string.Format(Strings.Preview_Level, doc.Level(pk));
    public string GenderText => pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" };
    public bool IsShiny => pk.IsShiny;
    public string ItemText => pk.HeldItem == 0 ? Strings.Preview_NoItem : names.ItemName(pk.HeldItem);
    public bool HasItem => pk.HeldItem != 0;

    // ------------------------------------------------------------------ detail

    public string AbilityText
    {
        get
        {
            string slotName = pk.AbilityNumber switch { 2 => "2", 4 => Strings.Preview_Hidden, _ => "1" };
            return $"{names.AbilityName(pk.Ability)} ({slotName})";
        }
    }

    public string NatureText => (int)pk.Nature < names.Natures.Count ? names.Natures[(int)pk.Nature] : "—";
    public string BallText => pk.Ball < names.Balls.Count ? names.Balls[pk.Ball] : "—";
    public string FriendshipText => pk.CurrentFriendship.ToString();
    public string TrainerText => $"{pk.OriginalTrainerName} ({pk.TID16:00000})";
    public string EvTotalText => string.Format(Strings.Pkm_EvTotal, pk.EV_HP + pk.EV_ATK + pk.EV_DEF + pk.EV_SPA + pk.EV_SPD + pk.EV_SPE, SaveDocument.MaxEvTotal);

    public IReadOnlyList<StatLine> Stats
    {
        get
        {
            var p = doc.Personal(pk.Species, pk.Form);
            int[] values = doc.Stats(pk); // HP, Atk, Def, Spe, SpA, SpD
            return
            [
                new(Strings.Stat_HP, p?.HP ?? 0, pk.IV_HP, pk.EV_HP, values[0]),
                new(Strings.Stat_Atk, p?.ATK ?? 0, pk.IV_ATK, pk.EV_ATK, values[1]),
                new(Strings.Stat_Def, p?.DEF ?? 0, pk.IV_DEF, pk.EV_DEF, values[2]),
                new(Strings.Stat_SpA, p?.SPA ?? 0, pk.IV_SPA, pk.EV_SPA, values[4]),
                new(Strings.Stat_SpD, p?.SPD ?? 0, pk.IV_SPD, pk.EV_SPD, values[5]),
                new(Strings.Stat_Spe, p?.SPE ?? 0, pk.IV_SPE, pk.EV_SPE, values[3]),
            ];
        }
    }

    public IReadOnlyList<MoveLine> Moves
    {
        get
        {
            (ushort Move, int PP, int Ups)[] moves =
            [
                (pk.Move1, pk.Move1_PP, pk.Move1_PPUps), (pk.Move2, pk.Move2_PP, pk.Move2_PPUps),
                (pk.Move3, pk.Move3_PP, pk.Move3_PPUps), (pk.Move4, pk.Move4_PP, pk.Move4_PPUps),
            ];
            return moves.Select(m => m.Move == 0
                    ? new MoveLine("—", "")
                    : new MoveLine(m.Move < names.Moves.Count ? names.Moves[m.Move] : $"#{m.Move}", string.Format(Strings.Pkm_PP, m.PP, doc.MaxPP(m.Move, m.Ups))))
                .ToList();
        }
    }
}

/// <summary>
/// Right side of the project list: what the ROM is and the team of its save, read only. "Manage" opens the editor.
/// </summary>
public sealed partial class ProjectPreviewViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;

    public LoadedProject Loaded { get; }

    public string Name => Path.GetFileNameWithoutExtension(Loaded.Path);

    public string GameText { get; }
    public string RomText { get; }
    public string RandomText { get; }

    /// <summary>Why there is no team to show (no emulator, no save…), or null.</summary>
    public string? SaveMessage { get; }
    public bool HasSave => SaveMessage is null;

    public string TrainerText { get; } = "";

    public ObservableCollection<PartyMemberViewModel> Party { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial PartyMemberViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>Loads everything but the bitmaps, so it can run off the UI thread.</summary>
    public ProjectPreviewViewModel(MainWindowViewModel main, LoadedProject loaded)
    {
        this.main = main;
        Loaded = loaded;
        var project = loaded.Project;
        var r = project.Randomization;

        GameText = string.Format(Strings.Preview_Game, loaded.Dump.Title, project.Edits.Count == 1
            ? Strings.Editor_EditCountOne
            : string.Format(Strings.Editor_EditCount, project.Edits.Count));
        RomText = r.LastBuiltRom is { } rom && File.Exists(rom)
            ? string.Format(Strings.Preview_Rom, Path.GetFileName(rom), File.GetLastWriteTime(rom))
            : Strings.Preview_NoRom;
        RandomText = r.Enabled
            ? string.Format(Strings.History_Random, r.Seed, r.PresetName ?? Strings.Rnd_PresetDefaults)
            : Strings.History_NotRandom;

        var settings = main.Settings;
        string? savePath = settings.EffectiveEmulatorDirectory is { } dir ? EmulatorUserFolders.SaveFile(dir, loaded.Dump.Title.TitleId()) : null;
        if (savePath is null)
        {
            SaveMessage = Strings.Rnd_NoEmulator;
            return;
        }
        if (!File.Exists(savePath))
        {
            SaveMessage = string.Format(Strings.Rnd_NoSave, loaded.Dump.Title, settings.EffectiveEmulatorName);
            return;
        }

        try
        {
            var doc = SaveDocument.Open(savePath, loaded.Session.Current);
            var gameNames = new GameNames(loaded.Dump, loaded.Session.Original);
            var names = new SaveNames(gameNames, GameTextLanguage.Current, doc.MaxSpecies);
            Sprites = PokemonSprites.Load(project, loaded.Session.Original);
            int badges = Enumerable.Range(0, 8).Count(doc.GetBadge);
            TrainerText = string.Format(Strings.Preview_Trainer, doc.TrainerName, badges, doc.Money,
                $"{doc.PlayedHours}:{doc.PlayedMinutes:00}", settings.EffectiveEmulatorName, File.GetLastWriteTime(savePath));
            for (int i = 0; i < doc.PartyCount; i++)
                Party.Add(new PartyMemberViewModel(doc, names, Sprites, new SaveSlot(null, i)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            SaveMessage = string.Format(Strings.Rnd_SaveUnreadable, settings.EffectiveEmulatorName, ex.Message);
        }
    }

    public PokemonSprites Sprites { get; } = PokemonSprites.Empty;

    [RelayCommand]
    private void Manage() => main.Manage(Loaded);

    /// <summary>Opens the editor on the save editor with the selected Pokémon.</summary>
    [RelayCommand]
    private void ManagePokemon()
    {
        if (Selected is { } member)
            main.Manage(Loaded, member.Slot);
    }
}
