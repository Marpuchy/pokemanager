using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// One trainer: the record (AI, bag, class, battle type, money) and the team. What each generation actually stores is
/// not hidden — the Generation 6 IVs byte and the Generation 7 IVs, EVs and nature are shown only where they exist.
/// </summary>
public sealed partial class TrainerDetailViewModel : ObservableObject
{
    private const string T = GameTables.Trainers;
    private readonly EditorViewModel editor;
    private readonly EditorSession session;
    private readonly GameNames names;
    private readonly PokemonSprites sprites;

    public int Id { get; }
    public int Generation { get; }
    public bool IsGen6 => Generation == 6;
    public bool IsGen7 => Generation == 7;

    /// <summary>A record the application could not parse: shown, but not editable.</summary>
    public bool IsUnreadable { get; }

    public string Number => $"#{Id:000}";
    public string Title => names.TrainerLabel(Id, session.GetInt(T, Id, "class"));

    /// <summary>Bits 0-2 as one list; see <see cref="AiLevelFieldViewModel"/> for why it is not three check boxes.</summary>
    public AiLevelFieldViewModel AiLevel { get; }

    /// <summary>The bits that are not the level: "uses items", and in Generation 7 the four the game added.</summary>
    public IReadOnlyList<BitFieldViewModel> AiFlags { get; }
    public IReadOnlyList<FieldViewModel> Record { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Bag { get; }
    public IntFieldViewModel Count { get; }
    public ObservableCollection<TrainerMemberViewModel> Members { get; } = [];

    public TrainerDetailViewModel(EditorViewModel editor, EditorSession session, GameNames names, PokemonSprites sprites, int id)
    {
        this.editor = editor;
        this.session = session;
        this.names = names;
        this.sprites = sprites;
        Id = id;
        Generation = session.Current.Title.Generation();
        IsUnreadable = id < session.Current.Trainers.Length && session.Current.Trainers[id].Unreadable;

        // Bits 0-2 are the level and go in one list. Of the rest, Generation 6 only ever uses bit 7 (measured on the
        // real dump, docs/game-ai.md), so the four Generation 7 bits are not offered where they do nothing. Bit 3
        // (doubles) is left out on purpose: the record's battle type already says whether the fight is a double one.
        AiLevel = new AiLevelFieldViewModel(session, T, id, "ai", Strings.Ai_Level);
        int[] bits = IsGen6 ? [7] : [4, 5, 6, 7];
        AiFlags = [.. bits.Select(b => new BitFieldViewModel(session, T, id, "ai", AiName(b), b, Strings.Ai_Tip))];

        Record =
        [
            new ChoiceFieldViewModel(session, T, id, "class", Strings.Trainer_Class, names.TrainerClasses),
            new ChoiceFieldViewModel(session, T, id, "battleType", Strings.Trainer_BattleType, BattleTypes),
            new IntFieldViewModel(session, T, id, "money", Strings.Trainer_MoneyRate, 0, 255),
        ];
        Bag =
        [
            .. Enumerable.Range(1, 4).Select(n => new ChoiceFieldViewModel(session, T, id, $"item{n}", string.Format(Strings.Trainer_BagSlot, n), names.ItemChoices)),
        ];
        Count = new IntFieldViewModel(session, T, id, "count", Strings.Trainer_TeamSize, 0, 6);
        Count.PropertyChanged += (_, _) => BuildTeam();

        // Generation 6 decides per trainer whether the entries carry their own moves and a held item; in Generation 7
        // they always do, so the check boxes would do nothing.
        Gen6Flags = IsGen6
            ?
            [
                new BoolFieldViewModel(session, T, id, "customMoves", Strings.Trainer_CustomMoves),
                new BoolFieldViewModel(session, T, id, "heldItems", Strings.Trainer_HeldItems),
                new BoolFieldViewModel(session, T, id, "flag", Strings.Trainer_Healer),
            ]
            : [new BoolFieldViewModel(session, T, id, "flag", Strings.Trainer_Flag)];

        foreach (var field in AiFlags.Cast<FieldViewModel>().Append(AiLevel).Concat(Record))
            field.PropertyChanged += (_, _) => NotifyHeader();
        foreach (var field in Gen6Flags)
            field.PropertyChanged += (_, _) =>
            {
                NotifyHeader();
                foreach (var member in Members)
                    member.RefreshFlags();
            };

        BuildTeam();
    }

    public IReadOnlyList<BoolFieldViewModel> Gen6Flags { get; }

    /// <summary>The same order the record stores; Generation 7 calls them Singles, Doubles and Multi.</summary>
    private static IReadOnlyList<string> BattleTypes =>
        [Strings.Trainer_Single, Strings.Trainer_Double, Strings.Trainer_Multi, Strings.Trainer_BattleType3];

    /// <summary>Only the bits that are not the level reach here; bits 0-2 are <see cref="AiLevel"/>.</summary>
    private static string AiName(int bit) => bit switch
    {
        4 => Strings.Ai_NoWhiteout,
        5 => Strings.Ai_BattleRoyal,
        6 => Strings.Ai_PokeChange,
        _ => Strings.Ai_UseItem,
    };

    // ------------------------------------------------------------------ header

    private int Ai => session.GetInt(T, Id, "ai");

    public string AiText => string.Format(Strings.Ai_Value, Ai, AiFlagNames());

    private string AiFlagNames()
    {
        var set = new List<string>();
        if (AiLevel.SelectedIndex >= 0)
            set.Add(AiLevelFieldViewModel.Labels[AiLevel.SelectedIndex]);
        set.AddRange(AiFlags.Where(f => f.IsSet).Select(f => f.Label));
        return set.Count == 0 ? Strings.Ai_None : string.Join(" + ", set);
    }

    /// <summary>
    /// Grey with no flags, blue with the basic one, amber with two, red with the three the game gives its gym leaders.
    /// A rough "how hard is this trainer meant to be", not a claim about what the flags do.
    /// </summary>
    public IBrush AiBrush => AiColor(Ai);

    /// <inheritdoc cref="AiBrush"/>
    public static IBrush AiColor(int ai) => new SolidColorBrush(Color.Parse((ai & 0x07) switch
    {
        0 => "#8C888C",
        1 => "#3A7BD5",
        3 or 5 => "#E8A33D",
        7 => "#C92112",
        _ => "#6B4FA8",
    }));

    public string TeamText => string.Format(Strings.Trainer_TeamCount, Count.Value ?? 0);

    private void NotifyHeader()
    {
        foreach (string property in new[] { nameof(AiText), nameof(AiBrush), nameof(Title), nameof(IsModified), nameof(TeamText) })
            OnPropertyChanged(property);
    }

    // ------------------------------------------------------------------ team

    private void BuildTeam()
    {
        int count = Math.Clamp((int)(Count.Value ?? 0), 0, 6);
        while (Members.Count > count)
            Members.RemoveAt(Members.Count - 1);
        while (Members.Count < count)
        {
            var member = new TrainerMemberViewModel(session, names, sprites, Id, Members.Count, Generation);
            member.Changed += NotifyHeader;
            Members.Add(member);
        }
        NotifyHeader();
    }

    // ------------------------------------------------------------------ presets and revert

    /// <summary>Every team member to the highest IVs the game can store: 255 in Generation 6, 31 each in Generation 7.</summary>
    [RelayCommand]
    private void MaxIvs()
    {
        using (editor.BeginBatch(string.Format(Strings.Trainer_MaxIvsUndo, Title)))
        {
            foreach (var member in Members)
                member.SetMaxIvs();
        }
        foreach (var member in Members)
            member.RefreshAll();
        NotifyHeader();
    }

    public bool IsModified => session.IsModified(T, Id);

    [RelayCommand]
    private void Revert()
    {
        session.Revert(T, Id);
        foreach (var field in AiFlags.Cast<FieldViewModel>().Append(AiLevel).Concat(Record).Concat(Bag).Concat(Gen6Flags).Append(Count))
            field.Refresh();
        BuildTeam();
        foreach (var member in Members)
            member.RefreshAll();
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>One Pokémon of a trainer's team, addressed as <c>p1.</c> … <c>p6.</c> in the trainer table.</summary>
public sealed class TrainerMemberViewModel : ObservableObject
{
    private const string T = GameTables.Trainers;
    private readonly EditorSession session;
    private readonly GameNames names;
    private readonly PokemonSprites sprites;
    private readonly int trainer;
    private readonly string prefix;

    /// <summary>Raised when something that the trainer's header shows has changed.</summary>
    public event Action? Changed;

    public int Slot { get; }
    public int Generation { get; }
    public bool IsGen6 => Generation == 6;
    public bool IsGen7 => Generation == 7;

    public string Header => string.Format(Strings.Trainer_Slot, Slot + 1);

    /// <summary>
    /// Generation 6 trainers whose entries do not carry their own moves: what is written here is stored but the game
    /// fills the moves from the learnset instead, so the warning says so rather than letting the user edit into a void.
    /// </summary>
    public bool MovesIgnored => session.GetInt(T, trainer, "customMoves") == 0;

    /// <summary>Same for the held item.</summary>
    public bool ItemIgnored => session.GetInt(T, trainer, "heldItems") == 0;

    public ChoiceFieldViewModel Species { get; }
    public IntFieldViewModel Form { get; }
    public IntFieldViewModel Level { get; }
    public ChoiceFieldViewModel Ability { get; }
    public ChoiceFieldViewModel Gender { get; }
    public ChoiceFieldViewModel Item { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Moves { get; }

    /// <summary>Generation 6: the single byte the game turns into the six IVs (0–255).</summary>
    public IntFieldViewModel IvByte { get; }

    /// <summary>Generation 7: the six IVs and the six EVs, plus the nature.</summary>
    public IReadOnlyList<IntFieldViewModel> Ivs { get; }
    public IReadOnlyList<IntFieldViewModel> Evs { get; }
    public ChoiceFieldViewModel Nature { get; }

    public TrainerMemberViewModel(EditorSession session, GameNames names, PokemonSprites sprites, int trainer, int slot, int generation)
    {
        this.session = session;
        this.names = names;
        this.sprites = sprites;
        this.trainer = trainer;
        Slot = slot;
        Generation = generation;
        prefix = $"p{slot + 1}.";

        Species = Choice("species", Strings.Trainer_Species, names.Species);
        Form = Int("form", Strings.Trainer_Form, 0, 30);
        Level = Int("level", Strings.Trainer_Level, 1, 100);
        Ability = Choice("ability", Strings.Trainer_Ability, AbilitySlots);
        Gender = Choice("gender", Strings.Trainer_Gender, Genders);
        Item = Choice("item", Strings.Trainer_Item, names.ItemChoices);
        Moves = [.. Enumerable.Range(1, 4).Select(n => Choice($"move{n}", string.Format(Strings.Trainer_Move, n), names.MoveChoices))];
        IvByte = Int("ivs", Strings.Trainer_IvByte, 0, 255);
        string[] stats = ["Hp", "Atk", "Def", "Spa", "Spd", "Spe"];
        Ivs = [.. stats.Select((s, i) => Int("iv" + s, StatLabels[i], 0, 31))];
        Evs = [.. stats.Select((s, i) => Int("ev" + s, StatLabels[i], 0, 252))];
        Nature = Choice("nature", Strings.Trainer_Nature, names.Natures);

        Species.PropertyChanged += (_, _) => Refresh();
        Form.PropertyChanged += (_, _) => Refresh();
        Level.PropertyChanged += (_, _) => Refresh();
        IvByte.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IvByteText));
    }

    private ChoiceFieldViewModel Choice(string field, string label, IReadOnlyList<string> options) =>
        new(session, T, trainer, prefix + field, label, options);

    private IntFieldViewModel Int(string field, string label, int min, int max) =>
        new(session, T, trainer, prefix + field, label, min, max);

    /// <summary>pk3DS's reading: 0 lets the game pick between the first two, then ability 1, ability 2 and the hidden one.</summary>
    private static IReadOnlyList<string> AbilitySlots =>
        [Strings.Trainer_AbilityAny, Strings.Trainer_Ability1, Strings.Trainer_Ability2, Strings.Trainer_AbilityHidden];

    private static IReadOnlyList<string> Genders => [Strings.Trainer_GenderAuto, Strings.Trainer_GenderMale, Strings.Trainer_GenderFemale, Strings.Trainer_GenderAuto];

    private static IReadOnlyList<string> StatLabels =>
        [Strings.Stat_HP, Strings.Stat_Atk, Strings.Stat_Def, Strings.Stat_SpA, Strings.Stat_SpD, Strings.Stat_Spe];

    // ------------------------------------------------------------------ header of the card

    private int SpeciesId => session.GetInt(T, trainer, prefix + "species");

    public string SpeciesName => SpeciesId >= 0 && SpeciesId < names.Species.Count ? names.Species[SpeciesId] : "—";
    public string LevelText => string.Format(Strings.Trainer_LevelShort, session.GetInt(T, trainer, prefix + "level"));
    public Bitmap? Icon => sprites.For(SpeciesId, session.GetInt(T, trainer, prefix + "form"));

    public IBrush TypeBackground => Types is var (t1, t2) ? TypeColors.Background(t1, t2) : Brushes.Transparent;
    public IBrush TypeForeground => Types is var (t, _) ? TypeColors.Foreground(t) : Brushes.Black;

    /// <summary>The types of the species as the ROM has them now, so a randomized game shows its own.</summary>
    private (int, int) Types
    {
        get
        {
            var personal = session.Current.Personal;
            int index = SpeciesId >= 0 && SpeciesId < personal.Length ? SpeciesId : 0;
            var entry = personal[index];
            return (entry.Types[0], entry.Types[1]);
        }
    }

    /// <summary>"150 ≈ IVs 18": the byte as pk3DS reads it (value / 8), so the number means something to the user.</summary>
    public string IvByteText => string.Format(Strings.Trainer_IvByteHint, session.GetInt(T, trainer, prefix + "ivs") / 8);

    /// <summary>Which ability the chosen slot is for this species, with the ROM's current abilities.</summary>
    public string AbilityHint
    {
        get
        {
            var personal = session.Current.Personal;
            if (SpeciesId <= 0 || SpeciesId >= personal.Length)
                return "";
            int[] abilities = personal[SpeciesId].Abilities;
            int slot = session.GetInt(T, trainer, prefix + "ability");
            int ability = slot switch { 1 => abilities[0], 2 => abilities[1], 3 => abilities[2], _ => abilities[0] };
            string name = ability >= 0 && ability < names.Abilities.Count ? names.Abilities[ability] : "?";
            if (slot != 0)
                return name;
            // Species whose two slots hold the same ability: saying it twice reads like a mistake.
            string first = names.Abilities[abilities[0]], second = names.Abilities[abilities[1]];
            return first == second ? first : string.Format(Strings.Trainer_AbilityAnyHint, first, second);
        }
    }

    private void Refresh()
    {
        foreach (string property in new[] { nameof(SpeciesName), nameof(LevelText), nameof(Icon), nameof(TypeBackground), nameof(TypeForeground), nameof(AbilityHint) })
            OnPropertyChanged(property);
        Changed?.Invoke();
    }

    /// <summary>The best IVs the game can store for this member: the byte at 255 in Generation 6, 31 each in Generation 7.</summary>
    public void SetMaxIvs()
    {
        if (IsGen6)
        {
            IvByte.Value = 255;
            return;
        }
        foreach (var iv in Ivs)
            iv.Value = 31;
    }

    /// <summary>The trainer's "own moves" / "held items" flags changed: the warnings follow them.</summary>
    public void RefreshFlags()
    {
        OnPropertyChanged(nameof(MovesIgnored));
        OnPropertyChanged(nameof(ItemIgnored));
    }

    public void RefreshAll()
    {
        foreach (var field in new FieldViewModel[] { Species, Form, Level, Ability, Gender, Item, IvByte, Nature }.Concat(Moves).Concat(Ivs).Concat(Evs))
            field.Refresh();
        OnPropertyChanged(string.Empty);
    }
}
