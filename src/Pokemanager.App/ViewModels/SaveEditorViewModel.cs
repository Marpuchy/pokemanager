using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Projects;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A party or box slot button.</summary>
public partial class SlotViewModel(SaveEditorViewModel owner, SaveSlot slot) : ObservableObject
{
    public SaveSlot Slot { get; } = slot;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    [RelayCommand]
    private void Select() => owner.SelectSlot(Slot);
}

/// <summary>A problem line; clicking it selects the Pokémon it belongs to.</summary>
public sealed partial class ProblemViewModel(SaveEditorViewModel owner, SaveProblem problem) : ObservableObject
{
    public string Text { get; } = problem.Slot is { } s ? $"{SaveEditorViewModel.SlotLabel(s)}: {problem.Message}" : problem.Message;

    [RelayCommand]
    private void Go()
    {
        if (problem.Slot is { } slot)
            owner.SelectSlot(slot);
    }
}

/// <summary>Save file tab: a PKHeX-style editor for the emulator's save, written only on demand.</summary>
public partial class SaveEditorViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private readonly AppSettings settings;
    private SaveNames? names;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial SaveDocument? Document { get; private set; }

    public bool IsOpen => Document is not null;

    /// <summary>Why there is no document (no emulator, no save, unreadable…).</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = Strings.Save_NotLoaded;

    [ObservableProperty]
    public partial string HeaderText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public ObservableCollection<SlotViewModel> PartySlots { get; } = [];
    public ObservableCollection<SlotViewModel> BoxSlots { get; } = [];
    public ObservableCollection<string> BoxNames { get; } = [];
    public ObservableCollection<ProblemViewModel> Problems { get; } = [];

    [ObservableProperty]
    public partial int SelectedBox { get; set; }

    [ObservableProperty]
    public partial SaveSlot? SelectedSlot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPokemon))]
    public partial PokemonEditorViewModel? Pokemon { get; set; }

    public bool HasPokemon => Pokemon is not null;

    /// <summary>The selected slot is empty: offer to create a Pokémon there.</summary>
    [ObservableProperty]
    public partial bool CanCreate { get; set; }

    [ObservableProperty]
    public partial int NewSpecies { get; set; } = 1;

    [ObservableProperty]
    public partial decimal? NewLevel { get; set; } = 5;

    public IReadOnlyList<string> SpeciesChoices => names?.SpeciesChoices ?? [];

    [ObservableProperty]
    public partial SaveBagViewModel? Bag { get; set; }

    [ObservableProperty]
    public partial SaveTrainerViewModel? Trainer { get; set; }

    [ObservableProperty]
    public partial SaveDexViewModel? Dex { get; set; }

    public string ProblemsText => Problems.Count == 0 ? Strings.Save_NoProblems : string.Format(Strings.Save_ProblemsBlock, Problems.Count);
    public bool HasProblems => Problems.Count > 0;

    /// <summary>The project changed since the ROM was built: what is shown follows the project, not the ROM being played.</summary>
    public string? RomWarning => editor.ProjectDiffersFromBuiltRom ? Strings.Save_RomWarning : null;
    public bool HasRomWarning => RomWarning is not null;

    public SaveEditorViewModel(EditorViewModel editor, AppSettings settings)
    {
        this.editor = editor;
        this.settings = settings;
    }

    public static string SlotLabel(SaveSlot slot) => slot.Box is { } box
        ? string.Format(Strings.Change_Box, box + 1, slot.Slot + 1)
        : string.Format(Strings.Change_Party, slot.Slot + 1);

    // ------------------------------------------------------------------ open / reload

    /// <summary>Opens the save the first time the tab is shown.</summary>
    public void EnsureOpen()
    {
        if (Document is null)
            Open();
    }

    /// <summary>(Re)loads the emulator save from disk, discarding unwritten changes.</summary>
    public void Open()
    {
        Document = null;
        Pokemon = null;
        Bag = null;
        Trainer = null;
        Dex = null;
        IsDirty = false;

        string? path = editor.Randomizer.SavePath;
        if (path is null) { Message = Strings.Rnd_NoEmulator; return; }
        if (!File.Exists(path)) { Message = string.Format(Strings.Rnd_NoSave, editor.Dump.Title, settings.EffectiveEmulatorName); return; }

        try
        {
            var doc = SaveDocument.Open(path, editor.Session.Current);
            names = new SaveNames(editor.Names, editor.Session.Project.Language, doc.MaxSpecies);
            Document = doc;
            HeaderText = string.Format(Strings.Save_Header, settings.EffectiveEmulatorName, doc.TrainerName, path, File.GetLastWriteTime(path));
            OnPropertyChanged(nameof(SpeciesChoices));

            BoxNames.Clear();
            for (int b = 0; b < doc.BoxCount; b++)
                BoxNames.Add(string.IsNullOrWhiteSpace(doc.BoxName(b)) ? string.Format(Strings.Save_BoxN, b + 1) : doc.BoxName(b));
            PartySlots.Clear();
            for (int i = 0; i < SaveDocument.PartySize; i++)
                PartySlots.Add(new SlotViewModel(this, new SaveSlot(null, i)));
            BoxSlots.Clear();
            for (int i = 0; i < doc.BoxSlotCount; i++)
                BoxSlots.Add(new SlotViewModel(this, new SaveSlot(SelectedBox, i)));
            if (SelectedBox >= doc.BoxCount)
                SelectedBox = 0;
            RebuildBoxSlots();

            Bag = new SaveBagViewModel(this, doc, names);
            Trainer = new SaveTrainerViewModel(this, doc, names);
            Dex = new SaveDexViewModel(this, doc, names);
            RefreshSlots();
            RefreshProblems();
            SelectSlot(new SaveSlot(null, 0));
            OnPropertyChanged(nameof(RomWarning));
            OnPropertyChanged(nameof(HasRomWarning));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            Message = string.Format(Strings.Rnd_SaveUnreadable, settings.EffectiveEmulatorName, ex.Message);
        }
    }

    /// <summary>After a build or restore: the ROM data changed, so reopen unless there are unwritten edits.</summary>
    public void OnRomChanged()
    {
        if (Document is null)
            return;
        if (IsDirty)
        {
            Document.UseRom(editor.Session.Current);
            RefreshAll();
        }
        else
        {
            Open();
        }
    }

    partial void OnSelectedBoxChanged(int value) => RebuildBoxSlots();

    private void RebuildBoxSlots()
    {
        if (Document is null || SelectedBox < 0)
            return;
        BoxSlots.Clear();
        for (int i = 0; i < Document.BoxSlotCount; i++)
            BoxSlots.Add(new SlotViewModel(this, new SaveSlot(SelectedBox, i)));
        RefreshSlots();
    }

    // ------------------------------------------------------------------ selection and edits

    public void SelectSlot(SaveSlot slot)
    {
        if (Document is null)
            return;
        if (slot.Box is { } box && box != SelectedBox)
            SelectedBox = box;

        // Party slots past the last Pokémon are "the next free one".
        if (slot.IsParty && slot.Slot > Document.PartyCount)
            slot = new SaveSlot(null, Document.PartyCount);

        SelectedSlot = slot;
        bool empty = slot.IsParty ? slot.Slot >= Document.PartyCount : Document.IsEmpty(slot);
        Pokemon = empty ? null : new PokemonEditorViewModel(this, Document, names!, slot);
        CanCreate = empty && (!slot.IsParty || Document.PartyCount < SaveDocument.PartySize);
        foreach (var s in PartySlots.Concat(BoxSlots))
            s.IsSelected = s.Slot == slot;
    }

    /// <summary>Called by the Pokémon editor after every change.</summary>
    public void OnPokemonEdited(SaveSlot slot)
    {
        if (slot != SelectedSlot)
            SelectedSlot = slot;
        Touch();
    }

    public void SetStatus(string text) => editor.SetStatus(text);

    public void Touch()
    {
        IsDirty = Document?.IsDirty ?? false;
        RefreshSlots();
        RefreshProblems();
    }

    private void RefreshAll()
    {
        if (SelectedSlot is { } slot)
            SelectSlot(slot);
        Touch();
    }

    private void RefreshSlots()
    {
        if (Document is null || names is null)
            return;
        var problemSlots = Document.Validate().Where(p => p.Slot is not null).Select(p => p.Slot!.Value).ToHashSet();
        foreach (var s in PartySlots.Concat(BoxSlots))
        {
            bool empty = s.Slot.IsParty ? s.Slot.Slot >= Document.PartyCount : Document.IsEmpty(s.Slot);
            s.IsEmpty = empty;
            if (empty)
            {
                s.Name = Strings.Save_Empty;
                s.Detail = "";
            }
            else
            {
                var pk = Document.Get(s.Slot);
                s.Name = pk.IsEgg ? Strings.Save_Egg : names.SpeciesName(pk.Species);
                s.Detail = string.Format(Strings.Save_SlotDetail, Document.Level(pk), pk.IsNicknamed ? pk.Nickname : "");
            }
            s.HasProblem = problemSlots.Contains(s.Slot);
        }
    }

    private void RefreshProblems()
    {
        Problems.Clear();
        if (Document is not null)
            foreach (var p in Document.Validate())
                Problems.Add(new ProblemViewModel(this, p));
        OnPropertyChanged(nameof(ProblemsText));
        OnPropertyChanged(nameof(HasProblems));
    }

    [RelayCommand]
    private void CreatePokemon()
    {
        // Only into an empty slot: never replace a Pokémon that is already there.
        if (Document is null || SelectedSlot is not { } slot || !CanCreate || NewSpecies <= 0 || NewSpecies >= SpeciesChoices.Count)
            return;
        if (slot.IsParty ? slot.Slot < Document.PartyCount : !Document.IsEmpty(slot))
            return;
        var placed = Document.Set(slot, Document.Create((ushort)NewSpecies, (int)Math.Clamp(NewLevel ?? 5, 1, 100)));
        Touch();
        SelectSlot(placed);
    }

    [RelayCommand]
    private void DeletePokemon()
    {
        if (Document is null || SelectedSlot is not { } slot)
            return;
        if (!Document.Clear(slot))
        {
            editor.SetStatus(Strings.Save_LastPartyPokemon, error: true);
            return;
        }
        Touch();
        SelectSlot(slot);
    }

    // ------------------------------------------------------------------ write

    [RelayCommand]
    private async Task Reload()
    {
        if (IsDirty)
        {
            var question = new QuestionViewModel(Strings.Save_ReloadTitle, Strings.Save_ReloadMessage, Strings.Save_ReloadConfirm, Strings.Common_Cancel, []);
            if (!await editor.Dialogs.AskAsync(question))
                return;
        }
        Open();
        editor.SetStatus(Strings.Save_Reloaded);
    }

    [RelayCommand]
    private void Write()
    {
        if (Document is null)
            return;
        if (Problems.Count > 0)
        {
            editor.SetStatus(string.Format(Strings.Save_ProblemsBlock, Problems.Count), error: true);
            return;
        }
        if (EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName) is { Count: > 0 } running)
        {
            editor.SetStatus(string.Format(Strings.Status_CloseEmulator, string.Join(", ", running)), error: true);
            return;
        }
        if (Document.ChangedOnDisk())
        {
            editor.SetStatus(Strings.Save_ChangedOnDisk, error: true);
            return;
        }

        try
        {
            // The save as it was, in the project history, so it can be put back from there too.
            editor.History.History.Record(editor.Session.Project, VersionKind.BeforeSaveEdit, romPath: editor.Session.Project.Randomization.LastBuiltRom,
                savePath: Document.SavePath);
            editor.History.History.Prune();

            string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
            var result = Document.Write(backups);
            SaveUpdater.PruneBackups(backups, keep: 10);
            IsDirty = false;
            HeaderText = string.Format(Strings.Save_Header, settings.EffectiveEmulatorName, Document.TrainerName, Document.SavePath, File.GetLastWriteTime(Document.SavePath));
            editor.History.Refresh();
            editor.Randomizer.RefreshSave();
            editor.SetStatus(string.Format(Strings.Save_Written, result.SavePath, result.BackupPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            editor.SetStatus(string.Format(Strings.Save_WriteFailed, ex.Message), error: true);
        }
    }
}

/// <summary>A row of a bag pocket.</summary>
public sealed partial class BagItemViewModel(SaveBagViewModel owner, PocketViewModel pocket, int itemIndex, int count) : ObservableObject
{
    public PocketViewModel Pocket { get; } = pocket;

    /// <summary>Index into <see cref="PocketViewModel.ItemNames"/>.</summary>
    public int ItemIndex
    {
        get => itemIndex;
        set { if (value >= 0 && value != itemIndex) { itemIndex = value; owner.Commit(Pocket); } }
    }

    public decimal? Count
    {
        get => count;
        set { if (value is { } v) { count = (int)Math.Clamp(v, 1, Pocket.MaxCount); owner.Commit(Pocket); } }
    }

    public int MaxCount => Pocket.MaxCount;
    public int ItemId => Pocket.ItemIds[itemIndex];
    public int Amount => count;

    [RelayCommand]
    private void Remove() => owner.RemoveItem(this);
}

public sealed class PocketViewModel(InventoryPouch pouch, SaveNames names)
{
    public InventoryPouch Pouch { get; } = pouch;

    public string Name => Pouch.Type switch
    {
        InventoryType.Items => Strings.Bag_Items,
        InventoryType.KeyItems => Strings.Bag_KeyItems,
        InventoryType.TMHMs => Strings.Bag_TMs,
        InventoryType.Medicine => Strings.Bag_Medicine,
        InventoryType.Berries => Strings.Bag_Berries,
        _ => Pouch.Type.ToString(),
    };

    public IReadOnlyList<ushort> ItemIds { get; } = SaveDocument.PouchItems(pouch);
    public IReadOnlyList<string> ItemNames { get; } = SaveDocument.PouchItems(pouch).Select(i => names.ItemName(i)).ToList();
    public int MaxCount => Pouch.MaxCount;
    public int Capacity => Pouch.Items.Length;
    public System.Collections.ObjectModel.ObservableCollection<BagItemViewModel> Items { get; } = [];
}

/// <summary>Bag: pockets and their items.</summary>
public partial class SaveBagViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;
    private readonly SaveDocument doc;

    public IReadOnlyList<PocketViewModel> Pockets { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapacityText))]
    public partial PocketViewModel? SelectedPocket { get; set; }

    public string CapacityText => SelectedPocket is { } p ? string.Format(Strings.Bag_Capacity, p.Items.Count, p.Capacity) : "";

    public SaveBagViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names)
    {
        this.owner = owner;
        this.doc = doc;
        Pockets = doc.Pouches.Select(p => new PocketViewModel(p, names)).ToList();
        foreach (var pocket in Pockets)
        {
            foreach (var item in pocket.Pouch.Items.Where(i => i.Index != 0))
            {
                int index = IndexOf(pocket, item.Index);
                if (index >= 0)
                    pocket.Items.Add(new BagItemViewModel(this, pocket, index, Math.Max(1, item.Count)));
            }
        }
        SelectedPocket = Pockets.FirstOrDefault();
    }

    private static int IndexOf(PocketViewModel pocket, int item)
    {
        for (int i = 0; i < pocket.ItemIds.Count; i++)
            if (pocket.ItemIds[i] == item)
                return i;
        return -1;
    }

    public void Commit(PocketViewModel pocket)
    {
        doc.SetPouch(pocket.Pouch, pocket.Items.Select(i => (i.ItemId, i.Amount)));
        OnPropertyChanged(nameof(CapacityText));
        owner.Touch();
    }

    public void RemoveItem(BagItemViewModel item)
    {
        item.Pocket.Items.Remove(item);
        Commit(item.Pocket);
    }

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedPocket is not { } pocket || pocket.Items.Count >= pocket.Capacity || pocket.ItemIds.Count == 0)
            return;
        var used = pocket.Items.Select(i => i.ItemId).ToHashSet();
        int index = Enumerable.Range(0, pocket.ItemIds.Count).FirstOrDefault(i => !used.Contains(pocket.ItemIds[i]));
        pocket.Items.Add(new BagItemViewModel(this, pocket, index, 1));
        Commit(pocket);
    }

    [RelayCommand]
    private void SortByName()
    {
        if (SelectedPocket is not { } pocket)
            return;
        var sorted = pocket.Items.OrderBy(i => pocket.ItemNames[i.ItemIndex], StringComparer.CurrentCultureIgnoreCase).ToList();
        pocket.Items.Clear();
        foreach (var item in sorted)
            pocket.Items.Add(item);
        Commit(pocket);
    }
}

public sealed class DexEntryViewModel(SaveDexViewModel owner, SaveDocument doc, ushort species, string name) : ObservableObject
{
    public ushort Species { get; } = species;
    public string Number => $"#{Species:000}";
    public string Name { get; } = name;

    public bool Seen
    {
        get => doc.GetSeen(Species);
        set { doc.SetSeen(Species, value); owner.Changed(this); }
    }

    public bool Caught
    {
        get => doc.GetCaught(Species);
        set { doc.SetCaught(Species, value); owner.Changed(this); }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Pokédex: seen and caught per species.</summary>
public partial class SaveDexViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;
    private readonly SaveDocument doc;
    private readonly List<DexEntryViewModel> all;

    public ObservableCollection<DexEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    public string CountText => string.Format(Strings.Dex_Count, all.Count(e => e.Seen), all.Count(e => e.Caught), all.Count);

    public SaveDexViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names)
    {
        this.owner = owner;
        this.doc = doc;
        all = Enumerable.Range(1, doc.MaxSpecies).Select(s => new DexEntryViewModel(this, doc, (ushort)s, names.SpeciesName((ushort)s))).ToList();
        ApplyFilter();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        string filter = Filter.Trim();
        Entries.Clear();
        foreach (var e in all.Where(e => filter.Length == 0
                                         || (int.TryParse(filter.TrimStart('#'), out int n) ? e.Species == n : e.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))))
            Entries.Add(e);
    }

    public void Changed(DexEntryViewModel entry)
    {
        entry.Refresh();
        OnPropertyChanged(nameof(CountText));
        owner.Touch();
    }

    [RelayCommand]
    private void AllSeen()
    {
        foreach (var e in all)
            doc.SetSeen(e.Species, true);
        foreach (var e in all) e.Refresh();
        OnPropertyChanged(nameof(CountText));
        owner.Touch();
    }

    [RelayCommand]
    private void AllCaught()
    {
        foreach (var e in all)
            doc.SetCaught(e.Species, true);
        foreach (var e in all) e.Refresh();
        OnPropertyChanged(nameof(CountText));
        owner.Touch();
    }
}
