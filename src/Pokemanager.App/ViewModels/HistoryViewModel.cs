using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>A row of the history list.</summary>
public sealed class VersionItemViewModel(ProjectVersion version, string difference, bool isCurrent)
{
    public ProjectVersion Version { get; } = version;

    public string When => Version.CreatedAt.ToString("g");

    public string KindText => Version.Kind switch
    {
        VersionKind.Built => Strings.History_KindBuilt,
        VersionKind.Manual => Strings.History_KindManual,
        VersionKind.BeforeRestore => Strings.History_KindBeforeRestore,
        VersionKind.BeforeImport => Strings.History_KindBeforeImport,
        VersionKind.BeforeSaveEdit => Strings.History_KindBeforeSaveEdit,
        _ => Version.Kind.ToString(),
    };

    public string? Note => Version.Note;
    public bool HasNote => Version.Note is not null;

    public string Summary
    {
        get
        {
            string random = Version.Randomized
                ? string.Format(Strings.History_Random, Version.Seed, Version.PresetName ?? Strings.Rnd_PresetDefaults)
                : Strings.History_NotRandom;
            string edits = Version.EditCount == 1 ? Strings.Editor_EditCountOne : string.Format(Strings.Editor_EditCount, Version.EditCount);
            string rom = Version.RomPath is { } p ? " · " + Path.GetFileName(p) : "";
            string save = Version.HasSave ? " · " + Strings.History_WithSave : "";
            return $"{random} · {edits}{rom}{save}";
        }
    }

    /// <summary>What differs from the project now.</summary>
    public string Difference { get; } = difference;

    public bool IsCurrent { get; } = isCurrent;
}

/// <summary>History tab: versions of the project to go back to after an unwanted change.</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private readonly IDialogs dialogs;

    public ProjectHistory History { get; }

    public ObservableCollection<VersionItemViewModel> Versions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand), nameof(DeleteCommand))]
    public partial VersionItemViewModel? Selected { get; set; }

    public bool IsEmpty => Versions.Count == 0;

    public string FolderText => string.Format(Strings.History_Folder, History.Root);

    public HistoryViewModel(EditorViewModel editor, IDialogs dialogs, string projectPath)
    {
        this.editor = editor;
        this.dialogs = dialogs;
        History = new ProjectHistory(projectPath);
        Refresh();
    }

    public void Refresh()
    {
        string? keep = Selected?.Version.Id;
        Versions.Clear();
        var project = editor.Session.Project;
        foreach (var version in History.List())
        {
            string difference;
            bool current = false;
            try
            {
                var diff = ProjectHistory.Compare(project, History.LoadProject(version));
                current = !diff.Any;
                difference = Describe(diff);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                difference = string.Format(Strings.History_Unreadable, ex.Message);
            }
            Versions.Add(new VersionItemViewModel(version, difference, current));
        }
        Selected = Versions.FirstOrDefault(v => v.Version.Id == keep);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string Describe(VersionDifference diff)
    {
        if (!diff.Any)
            return Strings.History_SameAsNow;
        var parts = new List<string>();
        if (diff.Randomization)
            parts.Add(Strings.History_DiffRandomization);
        if (diff.Seed)
            parts.Add(Strings.History_DiffSeed);
        if (diff.Preset)
            parts.Add(Strings.History_DiffPreset);
        int edits = diff.EditsAdded + diff.EditsRemoved + diff.EditsChanged;
        if (edits > 0)
            parts.Add(string.Format(Strings.History_DiffEdits, edits));
        return string.Format(Strings.History_Differs, string.Join(", ", parts));
    }

    [RelayCommand]
    private async Task SaveVersion()
    {
        var question = new QuestionViewModel(Strings.History_SaveTitle, Strings.History_SaveMessage, Strings.History_SaveConfirm,
            Strings.Common_Cancel, [], Strings.History_NotePlaceholder);
        if (!await dialogs.AskAsync(question))
            return;
        try
        {
            History.Record(editor.Session.Project, VersionKind.Manual, question.Input, editor.Session.Project.Randomization.LastBuiltRom,
                editor.Randomizer.SavePath);
            Refresh();
            editor.SetStatus(Strings.History_Saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.History_Failed, ex.Message), error: true);
        }
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Restore()
    {
        if (Selected is not { } item)
            return;
        var restoreSave = new DialogCheck(item.Version.HasSave
            ? string.Format(Strings.History_RestoreSave, item.When)
            : Strings.History_RestoreSaveUnavailable, isChecked: false, isEnabled: item.Version.HasSave);
        var rebuild = new DialogCheck(Strings.History_RestoreRebuild, isChecked: true);
        var question = new QuestionViewModel(Strings.History_RestoreTitle,
            string.Format(Strings.History_RestoreMessage, item.When, item.Summary), Strings.History_RestoreConfirm, Strings.Common_Cancel,
            [rebuild, restoreSave]);
        if (!await dialogs.AskAsync(question))
            return;
        await editor.RestoreVersionAsync(item.Version, restoreSave.IsChecked, rebuild.IsChecked);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Delete()
    {
        if (Selected is not { } item)
            return;
        var question = new QuestionViewModel(Strings.History_DeleteTitle, string.Format(Strings.History_DeleteMessage, item.When, item.Summary),
            Strings.History_DeleteConfirm, Strings.Common_Cancel, []);
        if (!await dialogs.AskAsync(question))
            return;
        try
        {
            History.Delete(item.Version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.History_Failed, ex.Message), error: true);
        }
        Refresh();
    }
}
