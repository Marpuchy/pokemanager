using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.Model.Difficulty;
using Pokemanager.Model.Editing;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The difficulty of the run in one choice (<see cref="DifficultyProfiles"/>): it writes over every trainer what the
/// player would otherwise set group by group in Advanced: Trainers, in one undo step.
/// </summary>
public sealed partial class DifficultyViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private readonly EditorSession session;

    public DifficultyViewModel(EditorViewModel editor, EditorSession session)
    {
        this.editor = editor;
        this.session = session;
        // Built once: this list is bound to an ItemsSource next to a two-way selection.
        Levels = [.. DifficultyProfiles.All.Select(p => new DifficultyOptionViewModel(p))];
        Selected = Levels.FirstOrDefault(o => o.Level == session.Project.Tweaks.Difficulty)
                   ?? Levels.First(o => o.Level == DifficultyLevel.Normal);
    }

    public IReadOnlyList<DifficultyOptionViewModel> Levels { get; }

    [ObservableProperty]
    public partial DifficultyOptionViewModel Selected { get; set; }

    /// <summary>What the last apply did, or the profile the project is on.</summary>
    [ObservableProperty]
    public partial string Result { get; private set; } = "";

    /// <summary>The profile the project says is applied, for the line above the list.</summary>
    public string Applied => session.Project.Tweaks.Difficulty is { } level
        ? string.Format(Strings.Difficulty_Applied, Levels.First(o => o.Level == level).Name)
        : Strings.Difficulty_AppliedNone;

    [RelayCommand]
    private void Apply()
    {
        var level = Selected.Level;
        DifficultyResult result;
        using (editor.BeginBatch(string.Format(Strings.Difficulty_Undo, Selected.Name)))
        {
            result = DifficultyProfiles.Apply(session, level);
            session.Project.Tweaks.TrainerLevelPercent = DifficultyProfiles.Of(level).TrainerLevelPercent;
            session.Project.Tweaks.Difficulty = level == DifficultyLevel.Normal ? null : level;
        }

        Result = result.DidNothing
            ? Strings.Difficulty_Nothing
            : result.Values == 0
                ? string.Format(Strings.Difficulty_PutBack, result.PutBack, result.Trainers)
                : string.Format(Strings.Difficulty_Result, result.Values, result.Trainers);
        OnPropertyChanged(nameof(Applied));
        editor.RefreshTrainerLists();
        editor.GameTweaks.Refresh(); // the trainer level percentage is one of its fields
    }

    /// <summary>The project changed under us (a profile undone with Ctrl+Z, another project opened).</summary>
    public void Refresh()
    {
        Selected = Levels.FirstOrDefault(o => o.Level == session.Project.Tweaks.Difficulty)
                   ?? Levels.First(o => o.Level == DifficultyLevel.Normal);
        OnPropertyChanged(nameof(Applied));
    }
}

/// <summary>One difficulty in the list: its name, what it is for, and what it actually writes.</summary>
public sealed class DifficultyOptionViewModel
{
    private readonly DifficultyProfile profile;

    public DifficultyOptionViewModel(DifficultyProfile profile)
    {
        this.profile = profile;
        (Name, About) = profile.Level switch
        {
            DifficultyLevel.Relaxed => (Strings.Difficulty_Relaxed, Strings.Difficulty_RelaxedAbout),
            DifficultyLevel.Normal => (Strings.Difficulty_Normal, Strings.Difficulty_NormalAbout),
            DifficultyLevel.Challenge => (Strings.Difficulty_Challenge, Strings.Difficulty_ChallengeAbout),
            _ => (Strings.Difficulty_Nightmare, Strings.Difficulty_NightmareAbout),
        };
    }

    public DifficultyLevel Level => profile.Level;
    public string Name { get; }
    public string About { get; }

    /// <summary>What it writes, spelled out so the choice is not made blind.</summary>
    public string Detail
    {
        get
        {
            if (profile.Level == DifficultyLevel.Normal)
                return Strings.Difficulty_NormalDetail;
            var lines = new List<string>
            {
                Line(Strings.Difficulty_GroupRegular, profile.Regular),
                Line(Strings.Difficulty_GroupImportant, profile.Important),
                Line(Strings.Difficulty_GroupBoss, profile.Boss),
            };
            if (profile.TrainerLevelPercent != 0)
                lines.Add(string.Format(Strings.Difficulty_Levels, profile.TrainerLevelPercent.ToString("+0;-0")));
            return string.Join("\n", lines);
        }
    }

    private static string Line(string group, DifficultyRule rule)
    {
        var parts = new List<string>();
        if (rule.Ai is { } ai)
            parts.Add(AiLevelFieldViewModel.Labels[Array.IndexOf(AiLevelFieldViewModel.Values, ai)]);
        if (rule.Ivs is { } ivs)
            parts.Add(string.Format(Strings.Difficulty_Ivs, ivs));
        if (rule.BagItems is { } bag)
            parts.Add(bag == 0 ? Strings.Difficulty_NoBag : string.Format(Strings.Difficulty_Bag, bag));
        if (rule.MegaFromLevel is not null)
            parts.Add(Strings.Difficulty_Mega);
        return $"{group}: {string.Join(" · ", parts)}";
    }
}
