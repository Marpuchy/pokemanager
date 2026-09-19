using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Data;
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
        ShinySupported = Supported();
    }

    /// <summary>Whether this application can find the shiny branch in the game's executable.</summary>
    public bool ShinySupported { get; }

    public string ShinyTip => ShinySupported ? Strings.Game_ShinyTip : Strings.Game_ShinyUnsupported;

    /// <summary>Every Pokémon that is not shiny-locked comes out shiny. Needs a rebuild, not a new randomization.</summary>
    public bool AlwaysShiny
    {
        get => session.Project.Tweaks.AlwaysShiny;
        set
        {
            if (session.Project.Tweaks.AlwaysShiny == value)
                return;
            session.Project.Tweaks.AlwaysShiny = value;
            OnPropertyChanged();
            editor.MarkDirty(Strings.Game_ShinyUndo);
        }
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
    /// begins a new adventure. It asks **twice** — the first time explaining what happens, the second as the last
    /// chance — because there is no button that brings the adventure back, only the copy it leaves behind.
    /// </summary>
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

        string path = document.SavePath;
        var first = new QuestionViewModel(Strings.Game_ResetTitle,
            string.Format(Strings.Game_ResetMessage, document.TrainerName, path), Strings.Game_ResetConfirm, Strings.Common_Cancel, []);
        if (!await editor.Dialogs.AskAsync(first))
            return;
        var second = new QuestionViewModel(Strings.Game_ResetTitle,
            string.Format(Strings.Game_ResetAgain, document.TrainerName), Strings.Game_ResetConfirmAgain, Strings.Common_Cancel, []);
        if (!await editor.Dialogs.AskAsync(second))
            return;

        try
        {
            editor.History.History.Record(editor.Session.Project, VersionKind.BeforeSaveEdit,
                romPath: editor.Session.Project.Randomization.LastBuiltRom, savePath: path);
            editor.History.History.Prune();

            string backups = Path.Combine(AppSettings.BackupRoot, editor.Settings.EffectiveEmulatorName);
            string backup = document.Reset(backups);
            SaveUpdater.PruneBackups(backups, keep: 10);

            // Read the folder again: there is no save there now, and the page has to say so.
            save.Open();
            editor.History.Refresh();
            editor.Randomizer.RefreshSave();
            editor.SetStatus(string.Format(Strings.Game_ResetDone, backup));
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
    public void Refresh() => OnPropertyChanged(nameof(AlwaysShiny));

    private bool Supported()
    {
        try
        {
            string code = Path.Combine(session.Project.DumpDirectory, "exefs", "code.bin");
            return File.Exists(code) && GameCode.HasShinyBranch(File.ReadAllBytes(code));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
