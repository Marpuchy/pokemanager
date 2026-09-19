using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Multiplayer;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The level cap by milestones (<see cref="LevelCaps"/>): the plan, the step the save is on and the cap the next ROM gets.
/// The save is watched while the project is open, so when the game saves with a new badge or crystal the next cap is
/// unlocked at once: <see cref="Model.Projects.GameSettings.LevelCap"/> changes and the "build the ROM" notice appears.
/// </summary>
public sealed partial class LevelCapViewModel : ObservableObject, IDisposable
{
    private readonly EditorViewModel editor;
    private SaveWatcher? watcher;
    private string? watchedPath;
    private IReadOnlyList<LevelCapStep> plan = [];

    public LevelCapViewModel(EditorViewModel editor)
    {
        this.editor = editor;
        Supported = LevelCaps.Supported(editor.Session.Current.Title);
    }

    /// <summary>This game has a plan (Generation 6 and Ultra Sun/Ultra Moon).</summary>
    public bool Supported { get; }

    public string EnabledTip => Supported ? Strings.Tip_LevelCapEnabled : Strings.LevelCap_Unsupported;

    public ObservableCollection<LevelCapStepViewModel> Steps { get; } = [];

    public bool Enabled
    {
        get => editor.Session.Project.Tweaks.LevelCapByMilestones;
        set
        {
            if (Enabled == value)
                return;
            editor.Session.Project.Tweaks.LevelCapByMilestones = value;
            if (!value)
                editor.Session.Project.Tweaks.LevelCap = null;
            OnPropertyChanged();
            editor.MarkDirty(Strings.LevelCap_Undo);
            Refresh();
        }
    }

    /// <summary>The cap now (null: none), and what it waits for.</summary>
    [ObservableProperty]
    public partial int? CurrentLevel { get; private set; }

    [ObservableProperty]
    public partial string CurrentText { get; private set; } = "";

    /// <summary>The short form for the editor's bar and the start screen.</summary>
    [ObservableProperty]
    public partial string BadgeText { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShowBadge { get; private set; }

    /// <summary>Recomputes the plan and the current step from the save the editor has, or the one on disk.</summary>
    public void Refresh() => Apply(CurrentSave(), fromGame: false);

    /// <summary>A step's level was edited by the player.</summary>
    internal void SetOverride(int index, int? level)
    {
        var overrides = editor.Session.Project.Tweaks.LevelCapOverrides;
        if (level is { } l && l != plan[index].Computed)
            overrides[index] = l;
        else if (!overrides.Remove(index))
            return;
        editor.MarkDirty(Strings.LevelCap_Undo);
        Refresh();
    }

    private void Apply(SaveDocument? save, bool fromGame)
    {
        plan = Supported ? LevelCaps.Plan(editor.Session) ?? [] : [];
        int current = save is null || plan.Count == 0 ? -1 : LevelCaps.Current(plan, step => Earned(save, step));
        bool noSave = save is null;

        Steps.Clear();
        for (int i = 0; i < plan.Count; i++)
            Steps.Add(new LevelCapStepViewModel(this, i, plan[i], Name(plan[i]), Icon(plan[i]),
                earned: save is not null && Earned(save, plan[i]), isCurrent: i == current));

        int? level = current >= 0 ? plan[current].Level : null;
        CurrentLevel = Enabled ? level : null;
        CurrentText = !Enabled ? Strings.LevelCap_Off
            : noSave ? Strings.LevelCap_NoSave
            : current < 0 ? Strings.LevelCap_None
            : string.Format(Strings.LevelCap_Current, level, Name(plan[current]));
        BadgeText = !Enabled || noSave ? "" : current < 0 ? Strings.LevelCap_BadgeNone : string.Format(Strings.LevelCap_Badge, level);
        ShowBadge = Enabled && !noSave;

        // What the next ROM gets. Without a save the last cap stays: better than lifting it by accident.
        var tweaks = editor.Session.Project.Tweaks;
        if (Enabled && !noSave && tweaks.LevelCap != level)
        {
            int? before = tweaks.LevelCap;
            tweaks.LevelCap = level;
            editor.MarkDirty(Strings.LevelCap_Undo);
            if (fromGame && (before is null || level is null || level > before))
                editor.SetStatus(string.Format(Strings.LevelCap_Unlocked, level is null ? Strings.LevelCap_BadgeNone : level.ToString()));
        }
        Watch();
    }

    private SaveDocument? CurrentSave()
    {
        if (editor.SaveEditor.Document is { } open)
            return open;
        return ReadFromDisk();
    }

    private SaveDocument? ReadFromDisk()
    {
        if (editor.Randomizer.SavePath is not { } path || !File.Exists(path))
            return null;
        try
        {
            return SaveDocument.Open(path, editor.Session.Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            return null;
        }
    }

    /// <summary>Watches the save while the cap is on: a save by the game may unlock the next cap.</summary>
    private void Watch()
    {
        string? path = Enabled ? editor.Randomizer.SavePath : null;
        if (path == watchedPath)
            return;
        watcher?.Dispose();
        watcher = null;
        watchedPath = path;
        if (path is null)
            return;
        watcher = new SaveWatcher(path);
        watcher.Saved += () => Dispatcher.UIThread.Post(() =>
        {
            // The game wrote the file: read it as it is now. Unwritten edits in the save editor are not the game's.
            if (ReadFromDisk() is { } saved)
                Apply(saved, fromGame: true);
        });
    }

    /// <summary>Whether the save has the milestone of a step.</summary>
    public static bool Earned(SaveDocument save, LevelCapStep step) => step.Milestone switch
    {
        LevelCapMilestone.Badge => save.GetBadge(step.Value),
        LevelCapMilestone.Crystal => save.HasItem(step.Value) || save.HasItem(step.Value + 31),
        _ => save.HallOfFame is not null,
    };

    private string Name(LevelCapStep step) => StepName(step, editor.Session.Current.Title, editor.Names.Items);

    /// <summary>What a step waits for, as the player reads it: the badge's name, the crystal's item name, the league.</summary>
    public static string StepName(LevelCapStep step, GameTitle title, IReadOnlyList<string> items) => step.Milestone switch
    {
        LevelCapMilestone.Badge => title.Milestones() is var m && step.Value < m.Count ? m[step.Value].Name : $"#{step.Value + 1}",
        LevelCapMilestone.Crystal => step.Value < items.Count ? items[step.Value] : $"#{step.Value}",
        _ => Strings.LevelCap_League,
    };

    /// <summary>The indicator for a save, or null when the cap by milestones is off or the game has no plan.</summary>
    public static string? Indicator(Model.Editing.EditorSession session, SaveDocument save, IReadOnlyList<string> items)
    {
        if (!session.Project.Tweaks.LevelCapByMilestones || LevelCaps.Plan(session) is not { Count: > 0 } plan)
            return null;
        int current = LevelCaps.Current(plan, step => Earned(save, step));
        return current < 0
            ? string.Format(Strings.LevelCap_Badge, Strings.LevelCap_BadgeNone)
            : string.Format(Strings.LevelCap_Current, plan[current].Level, StepName(plan[current], session.Current.Title, items));
    }

    private static Bitmap? Icon(LevelCapStep step) => step.Milestone == LevelCapMilestone.Crystal ? PkhexImages.Item(step.Value) : null;

    public void Dispose()
    {
        watcher?.Dispose();
        watcher = null;
        watchedPath = null;
    }
}

/// <summary>One step of the plan in the list: what it waits for, its cap (editable) and where the save is.</summary>
public sealed class LevelCapStepViewModel(LevelCapViewModel owner, int index, LevelCapStep step, string name, Bitmap? icon, bool earned, bool isCurrent)
{
    public string Name { get; } = name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
    public Bitmap? Icon { get; } = icon;
    public bool HasIcon => Icon is not null;
    public bool IsEarned { get; } = earned;
    public bool IsCurrent { get; } = isCurrent;
    public string State => IsCurrent ? Strings.LevelCap_StateCurrent : IsEarned ? Strings.LevelCap_StateEarned : "";
    public bool IsOverridden => step.Level != step.Computed;
    public string Tip => string.Format(Strings.LevelCap_StepTip, step.Computed);

    public decimal? Level
    {
        get => step.Level;
        set => owner.SetOverride(index, value is { } v ? (int)v : null);
    }
}
