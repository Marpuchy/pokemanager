using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// A group of trainers to edit in one go. The groups are the game's own trainer classes (Leader, Youngster, Elite
/// Four…), not tiers we invented, plus "every trainer" at the top.
/// </summary>
public sealed class TrainerCategoryViewModel(EditorSession session, string name, IReadOnlyList<int> trainers)
    : ObservableObject
{
    /// <summary>
    /// The class name as the game writes it. Several class entries share a name (the game has more than one "Pokémon
    /// Trainer" and more than one "Team Flare"), and they are one category here: the player reads the name, not the id.
    /// </summary>
    public string Name { get; } = name;

    public IReadOnlyList<int> Trainers { get; } = trainers;

    public string CountText => string.Format(Strings.Trainer_CategoryCount, Trainers.Count);

    /// <summary>"AI 0x07 · levels 25-59", so a class can be told from another without opening it.</summary>
    public string Summary
    {
        get
        {
            var ai = Trainers.Select(id => session.GetInt(GameTables.Trainers, id, "ai")).Distinct().Order().ToList();
            var levels = Trainers
                .SelectMany(id => Enumerable.Range(1, 6).Select(slot => session.GetInt(GameTables.Trainers, id, $"p{slot}.level")))
                .Where(l => l > 0)
                .ToList();
            string aiText = ai.Count == 1 ? $"0x{ai[0]:X2}" : $"0x{ai[0]:X2}–0x{ai[^1]:X2}";
            return levels.Count == 0
                ? string.Format(Strings.Trainer_CategorySummaryNoLevels, aiText)
                : string.Format(Strings.Trainer_CategorySummary, aiText, levels.Min(), levels.Max());
        }
    }

    public bool IsModified => Trainers.Any(id => session.IsModified(GameTables.Trainers, id));

    public bool Matches(string filter) => filter.Length == 0 || Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsModified));
    }
}

/// <summary>
/// What can be applied to a whole category at once — the settings that otherwise have to be repeated trainer by
/// trainer. Every apply is a single undo step, and every value goes through the edit layer like a hand edit.
/// </summary>
public sealed partial class TrainerBulkViewModel(EditorViewModel editor, EditorSession session) : ObservableObject
{
    private const string T = GameTables.Trainers;

    [ObservableProperty]
    public partial TrainerCategoryViewModel? Category { get; set; }

    /// <summary>Index into <see cref="AiLevelFieldViewModel.Values"/>; nothing is applied until the button is pressed.</summary>
    [ObservableProperty]
    public partial int AiLevel { get; set; } = -1;

    [ObservableProperty]
    public partial int BattleType { get; set; } = -1;

    [ObservableProperty]
    public partial decimal? Ivs { get; set; } = 255;

    [ObservableProperty]
    public partial int LevelPercent { get; set; } = 100;

    /// <summary>What the last apply did, so a bulk change is never silent.</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

    public IReadOnlyList<string> AiLevels => AiLevelFieldViewModel.Labels;

    /// <summary>Kept as one instance for the same reason as <see cref="AiLevelFieldViewModel.Labels"/>.</summary>
    public IReadOnlyList<string> BattleTypes { get; } =
        [Strings.Trainer_Single, Strings.Trainer_Double, Strings.Trainer_Multi, Strings.Trainer_BattleType3];

    public bool IsGen6 => session.Current.Title.Generation() == 6;

    [RelayCommand]
    private void ApplyAi()
    {
        if (Category is not { } category || (uint)AiLevel >= AiLevelFieldViewModel.Values.Length)
            return;
        int level = AiLevelFieldViewModel.Values[AiLevel];
        Apply(category, Strings.Trainer_BulkAi, id =>
        {
            int current = session.GetInt(T, id, "ai");
            int wanted = (current & ~0x07) | level;
            if (current == wanted)
                return 0;
            session.SetInt(T, id, "ai", wanted);
            return 1;
        });
    }

    [RelayCommand]
    private void ApplyBattleType()
    {
        if (Category is not { } category || (uint)BattleType >= 4)
            return;
        Apply(category, Strings.Trainer_BulkBattleType, id => Set(id, "battleType", BattleType));
    }

    [RelayCommand]
    private void ApplyIvs()
    {
        if (Category is not { } category)
            return;
        int value = (int)Math.Clamp(Ivs ?? 0, 0, 255);
        Apply(category, Strings.Trainer_BulkIvs, id => SetTeamIvs(id, value));
    }

    /// <summary>The highest the game can store: the byte at 255 in Generation 6, 31 in each of the six in Generation 7.</summary>
    [RelayCommand]
    private void MaxIvs()
    {
        if (Category is not { } category)
            return;
        Ivs = 255;
        Apply(category, Strings.Trainer_BulkMaxIvs, id => SetTeamIvs(id, 255));
    }

    [RelayCommand]
    private void ApplyLevels()
    {
        if (Category is not { } category || LevelPercent is < 10 or > 300)
            return;
        Apply(category, Strings.Trainer_BulkLevels, id =>
        {
            int changed = 0;
            for (int slot = 1; slot <= 6; slot++)
            {
                int level = session.GetInt(T, id, $"p{slot}.level");
                if (level <= 0)
                    continue;
                changed += Set(id, $"p{slot}.level", Math.Clamp((int)Math.Round(level * LevelPercent / 100.0), 1, 100));
            }
            return changed;
        });
    }

    private int SetTeamIvs(int id, int value)
    {
        int changed = 0;
        for (int slot = 1; slot <= 6; slot++)
        {
            if (session.GetInt(T, id, $"p{slot}.species") <= 0 && session.GetInt(T, id, $"p{slot}.level") <= 0)
                continue;
            if (IsGen6)
            {
                changed += Set(id, $"p{slot}.ivs", value);
                continue;
            }
            foreach (string stat in new[] { "Hp", "Atk", "Def", "Spa", "Spd", "Spe" })
                changed += Set(id, $"p{slot}.iv{stat}", Math.Min(value, 31));
        }
        return changed;
    }

    private int Set(int id, string field, int value)
    {
        if (session.GetInt(T, id, field) == value)
            return 0;
        session.SetInt(T, id, field, value);
        return 1;
    }

    /// <summary>One undo step for the whole category, and a line saying how many trainers and values actually moved.</summary>
    private void Apply(TrainerCategoryViewModel category, string what, Func<int, int> apply)
    {
        int trainers = 0, values = 0;
        using (editor.BeginBatch($"{category.Name} · {what}"))
        {
            foreach (int id in category.Trainers)
            {
                int changed = apply(id);
                values += changed;
                if (changed > 0)
                    trainers++;
            }
        }
        Result = values == 0
            ? string.Format(Strings.Trainer_BulkNothing, what)
            : string.Format(Strings.Trainer_BulkResult, what, values, trainers);
        category.Refresh();
        editor.RefreshTrainerLists();
    }
}
