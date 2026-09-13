using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>Detail of a personal entry (species or form) with its learnset.</summary>
public partial class SpeciesDetailViewModel : ObservableObject
{
    private const string P = GameTables.Personal;
    private readonly EditorSession session;
    private readonly GameNames names;

    public int Id { get; }
    public string Title { get; }

    public IReadOnlyList<IntFieldViewModel> Stats { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Types { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Abilities { get; }
    public IReadOnlyList<FieldViewModel> Breeding { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Items { get; }
    public IReadOnlyList<IntFieldViewModel> EvYield { get; }
    public IReadOnlyList<IntFieldViewModel> Other { get; }

    public ObservableCollection<LearnsetRowViewModel> Learnset { get; } = [];
    public IReadOnlyList<string> MoveNames => names.Moves;

    public int BaseStatTotal => Stats.Sum(s => (int)(s.Value ?? 0));
    public bool IsLearnsetModified => session.IsModified(GameTables.Learnsets, Id, GameTables.LevelUp);
    public bool IsModified => session.IsModified(P, Id) || session.IsModified(GameTables.Learnsets, Id);

    public SpeciesDetailViewModel(EditorSession session, GameNames names, int id)
    {
        this.session = session;
        this.names = names;
        Id = id;
        Title = $"#{id:000} {names.PersonalEntries[id]}";

        IntFieldViewModel Int(string field, string label, int max = 255) => new(session, P, id, field, label, 0, max);
        ChoiceFieldViewModel Choice(string field, string label, IReadOnlyList<string> options) => new(session, P, id, field, label, options);

        Stats =
        [
            Int("hp", Strings.Stat_HP), Int("atk", Strings.Stat_Atk), Int("def", Strings.Stat_Def),
            Int("spa", Strings.Stat_SpA), Int("spd", Strings.Stat_SpD), Int("spe", Strings.Stat_Spe),
        ];
        Types = [Choice("type1", Strings.Field_Type1, names.Types), Choice("type2", Strings.Field_Type2, names.Types)];
        Abilities =
        [
            Choice("ability1", Strings.Field_Ability1, names.Abilities),
            Choice("ability2", Strings.Field_Ability2, names.Abilities),
            Choice("abilityHidden", Strings.Field_AbilityHidden, names.Abilities),
        ];
        Breeding =
        [
            Int("catchRate", Strings.Field_CatchRate),
            Int("baseExp", Strings.Field_BaseExp, 65535),
            Choice("expGrowth", Strings.Field_ExpGrowth, GameNames.ExpGrowth),
            Int("gender", Strings.Field_Gender),
            Int("hatchCycles", Strings.Field_HatchCycles),
            Int("baseFriendship", Strings.Field_BaseFriendship),
            Choice("eggGroup1", Strings.Field_EggGroup1, GameNames.EggGroups),
            Choice("eggGroup2", Strings.Field_EggGroup2, GameNames.EggGroups),
        ];
        Items =
        [
            Choice("item1", Strings.Field_Item1, names.Items),
            Choice("item2", Strings.Field_Item2, names.Items),
            Choice("item3", Strings.Field_Item3, names.Items),
        ];
        EvYield =
        [
            Int("evHp", Strings.Ev_HP, 3), Int("evAtk", Strings.Ev_Atk, 3), Int("evDef", Strings.Ev_Def, 3),
            Int("evSpa", Strings.Ev_SpA, 3), Int("evSpd", Strings.Ev_SpD, 3), Int("evSpe", Strings.Ev_Spe, 3),
        ];
        Other = [Int("height", Strings.Field_Height, 65535), Int("weight", Strings.Field_Weight, 65535), Int("escapeRate", Strings.Field_EscapeRate)];

        foreach (var stat in Stats)
            stat.PropertyChanged += (_, _) => OnPropertyChanged(nameof(BaseStatTotal));

        LoadLearnset();
    }

    private void LoadLearnset()
    {
        Learnset.Clear();
        foreach (var pair in session.Get(GameTables.Learnsets, Id, GameTables.LevelUp).AsArray())
            Learnset.Add(new LearnsetRowViewModel(this, pair![0]!.GetValue<int>(), pair[1]!.GetValue<int>()));
        OnPropertyChanged(nameof(IsLearnsetModified));
    }

    /// <summary>Stores the whole list as a single edit and optionally reloads it sorted by level.</summary>
    internal void CommitLearnset(bool reload)
    {
        var value = new JsonArray(Learnset.Select(r => (JsonNode)new JsonArray(r.LevelValue, r.MoveIndex)).ToArray());
        session.Set(GameTables.Learnsets, Id, GameTables.LevelUp, value);
        if (reload)
            LoadLearnset();
        OnPropertyChanged(nameof(IsLearnsetModified));
        OnPropertyChanged(nameof(IsModified));
    }

    [RelayCommand]
    private void AddMove()
    {
        int level = Learnset.Count > 0 ? Learnset[^1].LevelValue : 1;
        Learnset.Add(new LearnsetRowViewModel(this, level, 1));
        CommitLearnset(reload: false);
    }

    internal void RemoveRow(LearnsetRowViewModel row)
    {
        Learnset.Remove(row);
        CommitLearnset(reload: false);
    }

    [RelayCommand]
    private void SortLearnset() => CommitLearnset(reload: true);

    [RelayCommand]
    private void Revert()
    {
        session.Revert(P, Id);
        session.Revert(GameTables.Learnsets, Id);
        foreach (var f in Stats.Concat<FieldViewModel>(Types).Concat(Abilities).Concat(Breeding).Concat(Items).Concat(EvYield).Concat(Other))
            f.Refresh();
        LoadLearnset();
        OnPropertyChanged(string.Empty);
    }
}

public partial class LearnsetRowViewModel(SpeciesDetailViewModel owner, int level, int move) : ObservableObject
{
    public int LevelValue { get; private set; } = level;
    public int MoveIndex { get; private set; } = move;
    public IReadOnlyList<string> MoveNames => owner.MoveNames;

    public decimal? Level
    {
        get => LevelValue;
        set
        {
            if (value is null || (int)value == LevelValue)
                return;
            LevelValue = Math.Clamp((int)value, 1, 100);
            OnPropertyChanged();
            owner.CommitLearnset(reload: false);
        }
    }

    public int SelectedMove
    {
        get => MoveIndex;
        set
        {
            if (value < 0 || value == MoveIndex)
                return;
            MoveIndex = value;
            OnPropertyChanged();
            owner.CommitLearnset(reload: false);
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveRow(this);
}
