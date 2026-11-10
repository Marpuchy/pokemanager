using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Save;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// Advanced: Game — what the built ROM does differently, beyond any single Pokémon or trainer. Two kinds of thing live
/// here and the page says which is which: a change to the game's executable (the shiny one), and the sweeps that are
/// ordinary edits over a whole table, so they show up in the advanced tabs, undo in one step and can be reverted.
/// </summary>
public sealed partial class GameTweaksViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private readonly EditorSession session;

    public GameTweaksViewModel(EditorViewModel editor, EditorSession session)
    {
        this.editor = editor;
        this.session = session;
        ShinySupported = Supported(out bool rolls);
        ShinyRollsSupported = rolls;
        WildAndStaticSupported = session.Current.Title.Layout().Wild.Length > 0;
        TrainerItems =
        [
            new(editor, session, Pokemanager.Model.Data.TrainerDifficulty.Boss, Strings.Role_Bosses),
            new(editor, session, Pokemanager.Model.Data.TrainerDifficulty.Important, Strings.Role_Important),
            new(editor, session, Pokemanager.Model.Data.TrainerDifficulty.Regular, Strings.Role_Ordinary),
        ];
    }

    /// <summary>Who mega evolves and who carries a Z-crystal, by kind of trainer (built once: bound to the view).</summary>
    public IReadOnlyList<TrainerItemRowViewModel> TrainerItems { get; }

    /// <summary>Z-crystals only exist in Generation 7.</summary>
    public bool HasZCrystals => session.Current.Title.Generation() == 7;

    /// <summary>The item and shop options, shown on this page: they are the ROM's too, and one place is enough.</summary>
    public ShopExtrasViewModel Shops => editor.Randomizer.Shops;

    /// <summary>The level cap card of this page (it lives on the editor, which also shows its indicator).</summary>
    public LevelCapViewModel LevelCap => editor.LevelCap;

    /// <summary>Whether this application can find the shiny branch in the game's executable.</summary>
    public bool ShinySupported { get; }

    public string ShinyTip => ShinySupported ? Strings.Game_ShinyTip : Strings.Game_ShinyUnsupported;

    /// <summary>
    /// The game's executable also has the two places where the number of identity rolls is read, so odds other than
    /// "all" and "the game's" can be written (measured in Ultra Sun / Ultra Moon).
    /// </summary>
    public bool ShinyRollsSupported { get; }

    /// <summary>
    /// The shiny odds as "one in N": 4096 is the game's own, 1 makes every Pokémon that is not shiny-locked shiny.
    /// Needs a rebuild, not a new randomization.
    /// </summary>
    public decimal ShinyOdds
    {
        get => session.Project.Tweaks.EffectiveShinyOdds ?? GameCode.GameOdds;
        set
        {
            int odds = (int)Math.Clamp(Math.Round(value), 1, GameCode.GameOdds);
            // Without the reads of the roll count only the two ends exist: everything shiny, or the game's own.
            if (!ShinyRollsSupported && odds is > 1 and < GameCode.GameOdds)
                odds = odds < GameCode.GameOdds / 2 ? 1 : GameCode.GameOdds;
            int? stored = odds >= GameCode.GameOdds ? null : odds;
            var t = session.Project.Tweaks;
            if (t.ShinyOdds == stored && !t.AlwaysShiny)
                return;
            t.ShinyOdds = stored;
            t.AlwaysShiny = false; // the old switch is now just odds of 1
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShinyOddsText));
            editor.MarkDirty(Strings.Game_ShinyUndo);
        }
    }

    /// <summary>What the odds really come to once written as rolls: "≈ 1 in 410 (10 rolls)", or the two ends.</summary>
    public string ShinyOddsText
    {
        get
        {
            int odds = session.Project.Tweaks.EffectiveShinyOdds ?? GameCode.GameOdds;
            if (odds >= GameCode.GameOdds)
                return Strings.Game_ShinyGameOdds;
            if (odds <= 1)
                return Strings.Game_ShinyAll;
            int rolls = GameCode.RollsFor(odds);
            return string.Format(Strings.Game_ShinyOddsReal, GameCode.OddsFor(rolls), rolls);
        }
    }

    [RelayCommand]
    private void SetShinyOdds(string odds) => ShinyOdds = decimal.Parse(odds, System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ levels

    // ---- what the preset would add on its own: the same levers, hidden so they are not in two places

    /// <summary>The percentage the randomizer's own option would add to the trainers, when a preset carries one.</summary>
    public int PresetTrainerLevel => Preset("TrainersLevelModified", "TrainersLevelModifier");

    public bool HasPresetTrainerLevel => PresetTrainerLevel != 0;

    public string PresetTrainerLevelText => string.Format(Strings.Options_PresetModifier, PresetTrainerLevel);


    public int PresetWildLevel => Preset("WildLevelsModified", "WildLevelModifier");

    public bool HasPresetWildLevel => PresetWildLevel != 0;

    public string PresetWildLevelText => string.Format(Strings.Options_PresetModifier, PresetWildLevel);

    /// <summary>The fixed Pokémon and the totems have one option each in the randomizer; either one shows here.</summary>
    public int PresetStaticLevel => Preset("StaticLevelModified", "StaticLevelModifier") is var s and not 0
        ? s
        : Preset("TotemLevelsModified", "TotemLevelModifier");

    public bool HasPresetStaticLevel => PresetStaticLevel != 0;

    public string PresetStaticLevelText => string.Format(Strings.Options_PresetModifier, PresetStaticLevel);

    [RelayCommand]
    private void ClearPresetTrainerLevel() => ClearPreset(("TrainersLevelModified", "TrainersLevelModifier"));

    [RelayCommand]
    private void ClearPresetWildLevel() => ClearPreset(("WildLevelsModified", "WildLevelModifier"));

    [RelayCommand]
    private void ClearPresetStaticLevel() =>
        ClearPreset(("StaticLevelModified", "StaticLevelModifier"), ("TotemLevelsModified", "TotemLevelModifier"));

    private int Preset(string toggle, string percent) => editor.Randomizer.Options.PresetLevelModifier(toggle, percent);

    private void ClearPreset(params (string Toggle, string Percent)[] options)
    {
        foreach (var (toggle, percent) in options)
            editor.Randomizer.Options.ClearLevelModifier(toggle, percent);
        RefreshPreset();
    }

    /// <summary>The preset changed (cleared here, imported, or loaded): the warnings follow it.</summary>
    public void RefreshPreset()
    {
        OnPropertyChanged(nameof(PresetTrainerLevel));
        OnPropertyChanged(nameof(HasPresetTrainerLevel));
        OnPropertyChanged(nameof(PresetTrainerLevelText));
        OnPropertyChanged(nameof(PresetWildLevel));
        OnPropertyChanged(nameof(HasPresetWildLevel));
        OnPropertyChanged(nameof(PresetWildLevelText));
        OnPropertyChanged(nameof(PresetStaticLevel));
        OnPropertyChanged(nameof(HasPresetStaticLevel));
        OnPropertyChanged(nameof(PresetStaticLevelText));
    }

    /// <summary>Wild and fixed Pokémon levels can be changed: the tables are known in Generation 7 only.</summary>
    public bool WildAndStaticSupported { get; }

    public string WildStaticTip => WildAndStaticSupported ? Strings.Tip_GameLevelsWild : Strings.Game_LevelsGen7Only;

    public decimal TrainerLevelPercent
    {
        get => (decimal)session.Project.Tweaks.TrainerLevelPercent;
        set => SetLevel(value, () => session.Project.Tweaks.TrainerLevelPercent, v => session.Project.Tweaks.TrainerLevelPercent = v);
    }

    public decimal WildLevelPercent
    {
        get => (decimal)session.Project.Tweaks.WildLevelPercent;
        set => SetLevel(value, () => session.Project.Tweaks.WildLevelPercent, v => session.Project.Tweaks.WildLevelPercent = v);
    }

    public decimal StaticLevelPercent
    {
        get => (decimal)session.Project.Tweaks.StaticLevelPercent;
        set => SetLevel(value, () => session.Project.Tweaks.StaticLevelPercent, v => session.Project.Tweaks.StaticLevelPercent = v);
    }

    /// <summary>The three starters, apart from the other fixed Pokémon (Generation 7).</summary>
    public decimal StarterLevelPercent
    {
        get => (decimal)session.Project.Tweaks.StarterLevelPercent;
        set => SetLevel(value, () => session.Project.Tweaks.StarterLevelPercent, v => session.Project.Tweaks.StarterLevelPercent = v);
    }

    /// <summary>
    /// "Global levels": one value for the whole game. Writing it sets every kind at once — trainers and bosses, wild,
    /// fixed Pokémon, totems and starters — and the rows below can still be changed one by one afterwards.
    /// </summary>
    public decimal AllLevelPercent
    {
        get
        {
            var t = session.Project.Tweaks;
            return WildAndStaticSupported
                   && t.TrainerLevelPercent == t.WildLevelPercent && t.WildLevelPercent == t.StaticLevelPercent
                   && t.StaticLevelPercent == t.StarterLevelPercent
                ? (decimal)t.TrainerLevelPercent
                : !WildAndStaticSupported ? (decimal)t.TrainerLevelPercent : 0;
        }
        set
        {
            TrainerLevelPercent = value;
            if (WildAndStaticSupported)
            {
                WildLevelPercent = value;
                StaticLevelPercent = value;
                StarterLevelPercent = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllLevelsMixed));
        }
    }

    /// <summary>The rows do not all say the same: the global value shows 0 and this says why.</summary>
    public bool AllLevelsMixed
    {
        get
        {
            var t = session.Project.Tweaks;
            return WildAndStaticSupported
                   && !(t.TrainerLevelPercent == t.WildLevelPercent && t.WildLevelPercent == t.StaticLevelPercent
                        && t.StaticLevelPercent == t.StarterLevelPercent);
        }
    }

    private void SetLevel(decimal value, Func<double> get, Action<double> set, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        double wanted = (double)Math.Round(value, 2);
        if (get() == wanted)
            return;
        set(wanted);
        OnPropertyChanged(name);
        if (name != nameof(AllLevelPercent))
        {
            OnPropertyChanged(nameof(AllLevelPercent));
            OnPropertyChanged(nameof(AllLevelsMixed));
        }
        editor.MarkDirty(Strings.Game_LevelsUndo);
        editor.SettingsChanged(); // the caps, the trainers as built and every other view of the levels follow
    }

    // ------------------------------------------------------------------ sweeps over a whole table

    [ObservableProperty]
    public partial int ExperiencePercent { get; set; } = 100;

    [ObservableProperty]
    public partial int CatchRatePercent { get; set; } = 100;

    [ObservableProperty]
    public partial int HatchPercent { get; set; } = 100;

    [ObservableProperty]
    public partial int FriendshipValue { get; set; } = 70;

    [ObservableProperty]
    public partial int MoneyPercent { get; set; } = 100;

    /// <summary>What the last sweep did ("718 values changed"), or empty before any.</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [RelayCommand]
    private void ApplyExperience() =>
        Sweep(Strings.Game_ExperienceUndo, GameTables.Personal, "baseExp", 1, ushort.MaxValue, ExperiencePercent);

    [RelayCommand]
    private void ApplyCatchRate() =>
        Sweep(Strings.Game_CatchRateUndo, GameTables.Personal, "catchRate", 1, 255, CatchRatePercent);

    [RelayCommand]
    private void ApplyHatch() =>
        Sweep(Strings.Game_HatchUndo, GameTables.Personal, "hatchCycles", 1, 255, HatchPercent);

    [RelayCommand]
    private void ApplyFriendship() =>
        Sweep(Strings.Game_FriendshipUndo, GameTables.Personal, "baseFriendship", 0, 255, null, FriendshipValue);

    [RelayCommand]
    private void ApplyMoney() =>
        Sweep(Strings.Game_MoneyUndo, GameTables.Trainers, "money", 0, 255, MoneyPercent);

    /// <summary>Back to what the game had: every edit of that field is dropped.</summary>
    [RelayCommand]
    private void Reset(string field)
    {
        string table = field == "money" ? GameTables.Trainers : GameTables.Personal;
        int changed = 0;
        using (editor.BeginBatch(Strings.Game_ResetUndo))
        {
            foreach (var edit in session.Project.Edits.All.Where(e => e.Table == table && e.Field == field).ToList())
            {
                session.SetInt(table, edit.Id, field, session.GetOriginal(table, edit.Id, field).GetValue<int>());
                changed++;
            }
        }
        Result = string.Format(Strings.Game_Result, changed);
    }

    /// <summary>
    /// The same change over every entry of a table, as ordinary edits and one undo step. A percentage multiplies what
    /// the game has (rounded, never below one when the game had one, so nothing becomes impossible by rounding); a
    /// value writes that value. Entries already holding the result are left alone, so nothing becomes an edit for free.
    /// </summary>
    private void Sweep(string label, string table, string field, int min, int max, int? percent, int? value = null)
    {
        var target = GameTables.Get(table);
        int count = target.Count(session.Current);
        int changed = 0;
        using (editor.BeginBatch(label))
        {
            for (int id = 0; id < count; id++)
            {
                int now = session.GetInt(table, id, field);
                int wanted = value ?? (now == 0 ? 0 : Math.Max(1, (int)Math.Round(now * percent!.Value / 100.0)));
                wanted = Math.Clamp(wanted, min, max);
                if (wanted == now)
                    continue;
                session.SetInt(table, id, field, wanted);
                changed++;
            }
        }
        Result = string.Format(Strings.Game_Result, changed);
    }

    /// <summary>
    /// Starts the game over: the save is copied to the backups and taken out of the emulator's folder, so the game
    /// begins a new adventure. It asks **twice** — the first time explaining what happens and offering what else can
    /// go back with it, the second as the last chance — because there is no button that brings the adventure back, only
    /// the copy it leaves behind.
    /// </summary>
    /// <remarks>
    /// Taking the save away is only half of starting over: the run's progress lives in the project, so a new adventure
    /// would otherwise begin with the lives already lost, the roulette already spun and the level cap where the last
    /// run left it. <see cref="RunReset"/> puts that back always, and the ROM's own rules only when asked (they need
    /// the ROM built again, so they are check boxes and they start unticked). All of it is one undo step.
    /// </remarks>
    [RelayCommand]
    private async Task ResetGame()
    {
        var save = editor.SaveEditor;
        save.EnsureOpen();
        if (save.Document is not { } document)
        {
            editor.SetStatus(Strings.Game_ResetNoSave, error: true);
            return;
        }
        if (EmulatorUserFolders.RunningEmulators(editor.Settings.EffectiveEmulatorName) is { Count: > 0 } running)
        {
            editor.SetStatus(string.Format(Strings.Status_CloseEmulator, string.Join(", ", running)), error: true);
            return;
        }

        var project = editor.Session.Project;
        string path = document.SavePath;
        int editCount = project.Edits.Count;
        // Ticked to begin with (user, 2026-11-03): starting over means the game as it ships, not the last run's rules.
        // Each can still be unticked to keep it.
        var edits = new DialogCheck(string.Format(Strings.Game_ResetEdits, editCount), isChecked: editCount > 0, isEnabled: editCount > 0);
        var randomization = new DialogCheck(Strings.Game_ResetRandomization, isChecked: true);
        var shops = new DialogCheck(Strings.Game_ResetShops, isChecked: true);
        var rules = new DialogCheck(Strings.Game_ResetRules, isChecked: true);
        string message = string.Format(Strings.Game_ResetMessage, document.TrainerName, path)
                         + "\n\n" + string.Format(Strings.Game_ResetRunNote, project.Locke.LivesLost, project.Locke.Spins.Count)
                         + (BaseRomWasModified(project) ? "\n\n" + Strings.Game_ResetBaseModified : "");
        var first = new QuestionViewModel(Strings.Game_ResetTitle, message, Strings.Game_ResetConfirm, Strings.Common_Cancel,
            [edits, randomization, shops, rules], checksTitle: Strings.Game_ResetRomChecks);
        if (!await editor.Dialogs.AskAsync(first))
            return;
        var second = new QuestionViewModel(Strings.Game_ResetTitle,
            string.Format(Strings.Game_ResetAgain, document.TrainerName), Strings.Game_ResetConfirmAgain, Strings.Common_Cancel, []);
        if (!await editor.Dialogs.AskAsync(second))
            return;

        var options = new RunResetOptions(edits.IsChecked, randomization.IsChecked, shops.IsChecked, rules.IsChecked);
        try
        {
            editor.History.History.Record(editor.Session.Project, VersionKind.BeforeSaveEdit,
                romPath: editor.Session.Project.Randomization.LastBuiltRom, savePath: path);
            editor.History.History.Prune();

            string backups = Path.Combine(AppSettings.BackupRoot, editor.Settings.EffectiveEmulatorName);
            string backup = document.Reset(backups);
            SaveUpdater.PruneBackups(backups, keep: 10);

            // One undo step for the whole thing, as a difficulty profile or a sweep does: the notice, the edit counts
            // and the history entry are done once, when the batch ends.
            RunResetReport report;
            using (editor.BeginBatch(Strings.Game_ResetRunUndo))
            {
                report = RunReset.Apply(editor.Session, options);
                // Nothing here goes through the session except the edits, so the project is marked by hand. The run's
                // own progress changes no byte of the ROM; the cap's step and the ROM's rules do.
                if (report.Count > 0)
                    editor.MarkDirty(Strings.Game_ResetRunUndo, affectsRom: report.AffectsRom);
            }

            // Read the folder again: there is no save there now, and the page has to say so.
            save.Open();
            editor.History.Refresh();
            editor.Randomizer.RefreshSave();
            if (report.TableEdits > 0)
                editor.ReloadLists();
            if (report.Randomization)
                editor.Randomizer.ReloadFromProject();
            if (report.Shops)
                editor.Randomizer.Shops.Refresh();
            if (report.GameRules > 0)
                Refresh();
            editor.Locke.Refresh();
            editor.LevelCap.Refresh();
            // Say how much was put back, so starting over is not done blind.
            editor.SetStatus(string.Format(Strings.Game_ResetDone, backup)
                             + (report.Count > 0 ? " " + string.Format(Strings.Game_ResetRunDone, report.Count) : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            editor.SetStatus(string.Format(Strings.Game_ResetFailed, ex.Message), error: true);
        }
    }

    /// <summary>
    /// Opens every box of the PC in the save being edited, like the save page's other unlocks: the change is in the
    /// editor until the save is written, so it can still be undone there.
    /// </summary>
    [RelayCommand]
    private void UnlockBoxes()
    {
        var save = editor.SaveEditor;
        save.EnsureOpen();
        if (save.Trainer is not { } trainer)
        {
            editor.SetStatus(Strings.Game_ResetNoSave, error: true);
            return;
        }
        trainer.UnlockAllBoxes();
        editor.SetStatus(string.Format(Strings.Trainer_Unlocked, Strings.Trainer_UnlockBoxes));
    }

    /// <summary>The project was replaced (undo, restore): show what it holds now.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ShinyOdds));
        OnPropertyChanged(nameof(ShinyOddsText));
        OnPropertyChanged(nameof(TrainerLevelPercent));
        OnPropertyChanged(nameof(WildLevelPercent));
        OnPropertyChanged(nameof(StaticLevelPercent));
        OnPropertyChanged(nameof(StarterLevelPercent));
        OnPropertyChanged(nameof(AllLevelPercent));
        OnPropertyChanged(nameof(AllLevelsMixed));
        foreach (var row in TrainerItems)
            row.Refresh();
    }

    /// <summary>
    /// The project's base ROM is not the game as it ships: it gives other starters (a ROM randomized before it was
    /// imported). What the application can put back then is only its own part — the base's own changes are in it.
    /// </summary>
    private static bool BaseRomWasModified(Project project)
    {
        var title = project.Game;
        return GameStarters.Read(new RomFsLayers(project.RomFsPath), title) is { } starters
               && !starters.SequenceEqual(GameStarters.Vanilla(title));
    }

    private bool Supported(out bool rolls)
    {
        rolls = false;
        try
        {
            string code = session.Project.CodePath(session.Mods);
            if (!File.Exists(code))
                return false;
            byte[] bytes = File.ReadAllBytes(code);
            rolls = GameCode.CanSetShinyRolls(bytes);
            return GameCode.HasShinyBranch(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// One row of the "Megas and Z-crystals" card of Opciones → Entrenadores: a kind of trainer, its mega switch and level,
/// its Z-crystal switch and level (2026-11-09, user). Writes <see cref="Pokemanager.Model.Projects.TrainerBattleItems"/>,
/// which the build applies last over every trainer.
/// </summary>
public sealed class TrainerItemRowViewModel(EditorViewModel editor, EditorSession session,
    Pokemanager.Model.Data.TrainerDifficulty kind, string name) : ObservableObject
{
    public string Name { get; } = name;

    private Pokemanager.Model.Projects.TrainerItemRule Rule =>
        (session.Project.Tweaks.TrainerItems ??= new()).For(kind);

    private void Changed(string property)
    {
        OnPropertyChanged(property);
        editor.MarkDirty(string.Format(Strings.TrainerItems_Undo, Name));
        editor.SettingsChanged(); // the open trainer and the lists show the megas and Z-crystals as built
    }

    public bool Mega
    {
        get => Rule.Mega;
        set { if (value != Rule.Mega) { Rule.Mega = value; Changed(nameof(Mega)); } }
    }

    public decimal? MegaFromLevel
    {
        get => Rule.MegaFromLevel;
        set
        {
            int level = (int)Math.Clamp(value ?? 1, 1, 100);
            if (level != Rule.MegaFromLevel) { Rule.MegaFromLevel = level; Changed(nameof(MegaFromLevel)); }
        }
    }

    public bool Z
    {
        get => Rule.Z;
        set { if (value != Rule.Z) { Rule.Z = value; Changed(nameof(Z)); } }
    }

    public decimal? ZFromLevel
    {
        get => Rule.ZFromLevel;
        set
        {
            int level = (int)Math.Clamp(value ?? 1, 1, 100);
            if (level != Rule.ZFromLevel) { Rule.ZFromLevel = level; Changed(nameof(ZFromLevel)); }
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Mega));
        OnPropertyChanged(nameof(MegaFromLevel));
        OnPropertyChanged(nameof(Z));
        OnPropertyChanged(nameof(ZFromLevel));
    }
}
